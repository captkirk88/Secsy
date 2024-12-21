using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Secsy
{
    public delegate void EachEntity(ref EntityId entId);
    public delegate void EachComponent<T>(ComponentId<T> compId);

    /// <summary>
    /// Sexxy entity component system in one file.  High performance.  So sexy..
    /// <para>Max entities = Array.<seealso cref="Array.MaxLength"/></para>
    /// </summary>
    public class Secsy
    {
        internal static IComponentId[] componentIds;
        internal static int componentIdsComposition;
        internal EntityId[] entityIds;
        internal object?[][] entComponents;
        internal ConcurrentQueue<int> entsInactive;

        static ushort nextCompId;
        internal int nextFreeEntId = 0;

        private readonly object _lock = new();

        static Secsy()
        {
            componentIds = [];
            nextCompId = 0;
        }
        public Secsy()
        {
            entComponents = [];
            entityIds = [];
            entsInactive = new();
        }

        public int Count
        {
            get
            {
                int count = 0;
                for (int i = 0, x = entityIds.Length; i < x; i++)
                {
                    if (entityIds[i].IsAlive) count++;
                }
                return count;
            }
        }

        public ReadOnlySpan<EntityId> All()
        {
            lock (_lock)
            {
                return new(entityIds);
            }
        }
        /// <summary>
        /// Create a new component id.
        /// </summary>
        /// <typeparam name="T">Must be a struct type</typeparam>
        /// <param name="defaultValue">Default value of <typeparamref name="T"/></param>
        /// <returns></returns>
        public static ComponentId<T> NewComponentId<T>(T? defaultValue = default)
        {
            // find existing ComponentId and return that instead.. nice try.
            ComponentId<T> newCompId = new(nextCompId, defaultValue);
            lock (componentIds)
            {
                if (nextCompId >= componentIds.Length)
                    Array.Resize(ref componentIds, componentIds.Length + 10);
                componentIds[newCompId.id] = newCompId;
            }
            nextCompId++;
            componentIdsComposition |= newCompId.id;
            return newCompId;
        }

        /// <summary>
        /// Create a new <see cref="EntityId"/>
        /// </summary>
        /// <param name="comps"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="UnregisteredComponentException"></exception>
        public ref EntityId NewEntity(params IComponentId[] comps)
        {
            ref EntityId entId = ref NewEnt();
            if (comps.Length > 0)
            {
                entId.compIds = 0;
                for (int i = 0, x = comps.Length - 1; i <= x; i++)
                {
                    if (comps[i] == null) throw new ArgumentNullException($"comps[{i}]");
                    var compId = comps[i].Id;

                    if ((componentIdsComposition & compId) != compId) throw new UnregisteredComponentException(comps[i]);

                    entId.compIds |= compId;

                    lock (_lock)
                    {
                        // Use the Component.Id, which is it's index in componentIds array, to add to ent's components
                        entComponents[entId.id][compId] = comps[i].DefaultValue;
                    }
                }
            }
            return ref entId;
        }

        /// <summary>
        /// Create many entities
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="comps"></param>
        /// <returns></returns>
        public EntityId[] NewEntities(int amount, params IComponentId[] comps)
        {
            if (amount < 0) return [];
            int first = 0;
            ConcurrentBag<EntityId> result = [];
            Parallel.For(0, amount, c =>
            {
                var entId = NewEntity(comps);
                result.Add(entId);
            });
            return [.. result];
        }

        internal ref EntityId NewEnt()
        {
            int bufferSize = 100;
            EntityId entId;
            int nextEntId = 0;
            if (entsInactive.IsEmpty == false)
            {
                entsInactive.TryDequeue(out nextEntId);
            }
            if (nextEntId == 0)
            {
                lock (_lock)
                {
                    nextFreeEntId++;
                }
                nextEntId = nextFreeEntId;
            }

            entId = new(nextEntId, this);
            lock (_lock)
            {
                if (nextEntId >= entityIds.Length)
                    Array.Resize(ref entityIds, entityIds.Length + bufferSize);
                entityIds[nextEntId] = entId;

                if (nextEntId >= entComponents.Length)
                {
                    Array.Resize(ref entComponents, entComponents.Length + bufferSize);
                }
                if (entComponents[nextEntId] == null || entComponents[nextEntId].Length == 0)
                    entComponents[nextEntId] = new object?[componentIds.Length];
                else Array.Clear(entComponents[nextEntId]);
                return ref entityIds[nextEntId];
            }
        }

        // TODO create batch way of making many entities

        /// <summary>
        /// Get the <see cref="EntityId"/> from a raw id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="InvalidEntityIdException"></exception>
        public ref EntityId Get(long id)
        {
            if (id == 0) return ref EntityId.Null;
            try
            {
                return ref entityIds[id];
            }
            catch (Exception)
            {
                throw new InvalidEntityIdException(id);
            }
        }

        /// <summary>
        /// Add component to <paramref name="entId"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entId"></param>
        /// <param name="compId"></param>
        public void AddComponent<T>(long entId, ComponentId<T> compId)
        {
            if (entId == 0) return;
            ref var ent = ref Get(entId);
            if ((ent.compIds & compId.id) == compId.id) throw new DuplicateComponentException(compId);

            lock (_lock)
            {
                int len = entComponents[entId].Length;
                if (len < compId.id) Array.Resize(ref entComponents[entId], len + compId.id);

                Span<object?> comps = new(entComponents[entId]);
                comps[compId.id] = compId.DefaultValue;
                ent.compIds |= compId.id;
            }
        }

        /// <summary>
        /// Sets component value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entId"></param>
        /// <param name="compId"></param>
        /// <param name="newValue"></param>
        /// <exception cref="InvalidEntityIdException"></exception>
        public void SetComponentValue<T>(long entId, ComponentId<T> compId, T newValue)
        {
            EntityId ent;
            try
            {
                ent = entityIds[entId];
            }
            catch (Exception)
            {
                throw new InvalidEntityIdException(entId);
            }
            if ((ent.compIds & compId.id) == compId.id)
            {
                lock (_lock)
                {
                    entComponents[entId][compId.id] = newValue;
                }
            }
        }

        /// <summary>
        /// Get the component value of type <typeparamref name="T"/> from <paramref name="entId"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entId"></param>
        /// <param name="compId"></param>
        /// <returns></returns>
        /// <exception cref="InvalidEntityIdException"></exception>
        public T? GetComponentValue<T>(long entId, ComponentId<T> compId)
        {
            if (entId == 0) return default;
            EntityId ent;
            try
            {
                ent = entityIds[entId];
            }
            catch (Exception)
            {
                throw new InvalidEntityIdException(entId);
            }
            if ((ent.compIds & compId.id) == compId.id)
                return (T?)entComponents[entId][compId.id];
            return default;
        }

        /// <summary>
        /// Remove component from <paramref name="entId"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entId"></param>
        /// <param name="compId"></param>
        /// <returns></returns>
        /// <exception cref="InvalidEntityIdException"></exception>
        public bool RemoveComponent<T>(long entId, ComponentId<T> compId)
        {
            if (entId == 0) return true;
            EntityId ent;
            try
            {
                ent = entityIds[entId];
            }
            catch (Exception)
            {
                throw new InvalidEntityIdException(entId);
            }
            if ((ent.compIds & compId.id) == compId.id)
            {
                lock (_lock)
                {
                    entComponents[entId][compId.id] = null;
                    entityIds[entId].compIds &= ~compId.id;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets all the components associated to the entity id
        /// </summary>
        /// <param name="entId"></param>
        /// <returns></returns>
        /// <exception cref="InvalidEntityIdException"></exception>
        public IComponentId[] GetComponents(long entId)
        {
            if (entId == 0) return [];
            EntityId ent;
            try
            {
                ent = entityIds[entId];
            }
            catch (Exception)
            {
                throw new InvalidEntityIdException(entId);
            }
            if (ent.compIds > 0)
            {
                List<IComponentId> comps = [];
                lock (_lock)
                {
                    for (int i = 0, x = componentIds.Length; i < x; i++)
                    {
                        var compVal = entComponents[componentIds[i].Id];
                        if (compVal != default) comps.Add(componentIds[i]);
                    }
                }
                return [.. comps];
            }
            return [];
        }

        /// <summary>
        /// Sets entity with <paramref name="entId"/> to be inactive.  It will be reused later if a new entity is created.
        /// </summary>
        /// <param name="entId"></param>
        public void Remove(int entId)
        {
            if (entId <= 0) return; // default(EntityId) is automatically considered inactive.
            lock (_lock)
            {
                ref var ent = ref entityIds[entId];
                ent.compIds = 0;
                // Just in case called on default(EntityId)
            }
            entsInactive.Enqueue(entId);
        }

        public void Each(Filter filter, EachEntity fn)
        {
            var result = Filter(filter);
            while (result.MoveNext())
            {
                fn(ref entityIds[result.Current]);
            }
        }

        public IEnumerator<long> Filter(Filter filter)
        {
            if (filter == null) return Enumerable.Empty<long>().GetEnumerator();
            ConcurrentBag<long> ents = [];
            Parallel.For(0, entityIds.Length, filterBody);
            return ents.GetEnumerator();
            void filterBody(int i, ParallelLoopState state)
            {
                EntityId ent = entityIds[i];
                if (ent.compIds == 0) return;
                if (filter.Apply(ent))
                    ents.Add(ent.id);
            }
        }


        public void Clear()
        {
            entsInactive.Clear();
            Array.Clear(entityIds);
            Array.Clear(entComponents);
        }
    }

    public interface IComponentId
    {
        int Id { get; }

        object? DefaultValue { get; }

        string ToString()
        {
            return $"Component[{Id}]";
        }
    }


    public readonly struct ComponentId<T> : IComponentId
    {
        internal ComponentId(int id, T? defaultValue)
        {
            this.id = id;
            this.value = defaultValue;
        }
        internal readonly int id;

        public readonly int Id => id;

        object? IComponentId.DefaultValue => value;

        internal readonly T? value;

        public T? DefaultValue => value;

        /// <summary>
        /// Gets the component value of type <typeparamref name="T"/> for <paramref name="ent"/>
        /// </summary>
        /// <param name="ent"></param>
        /// <returns></returns>
        public T? Get(EntityId ent)
        {
            return ent.man.GetComponentValue(ent.id, this);
        }

        /// <summary>
        /// Sets the component value of type <typeparamref name="T"/> for <paramref name="ent"/>
        /// </summary>
        /// <param name="ent"></param>
        /// <returns>This for chaining</returns>
        public ComponentId<T> Set(ref EntityId ent)
        {
            ent.man.AddComponent(ent.id, this);
            ent.compIds |= id;
            return this;
        }

        /// <summary>
        /// Sets the value of this component for <paramref name="ent"/> to <paramref name="newValue"/> if this <paramref name="ent"/> has this component
        /// </summary>
        /// <param name="ent"></param>
        /// <param name="newValue"></param>
        /// <returns></returns>
        public ComponentId<T> SetValue(EntityId ent, T newValue)
        {
            if ((ent.compIds & id) == id)
                ent.man.entComponents[ent.id][id] = newValue;
            return this;
        }

        /// <summary>
        /// Checks if <paramref name="ent"/> has this component assigned to it
        /// </summary>
        /// <param name="ent"></param>
        /// <returns><see langword="true" /> if <paramref name="ent"/> has this component, <see langword="false"/> otherwise</returns>
        public bool Has(ref EntityId ent)
        {
            return (ent.compIds & id) == id;
        }

        public ComponentId<T> Remove(ref EntityId ent)
        {
            ent.man.RemoveComponent(ent.id, this);
            ent.compIds &= ~id;
            return this;
        }
    }

    // TODO evaluate using ref struct

    public struct EntityId
    {
        internal static EntityId Null = default;

        internal EntityId(int id, Secsy man)
        {
            this.id = id;
            this.man = man;
        }
        internal Secsy man;
        internal int id;

        public readonly int ID => id;

        internal int compIds;

        public readonly bool IsAlive => compIds != 0;

        public readonly bool Has(IComponentId componentId)
        {
            return componentId != null && (compIds & componentId.Id) == componentId.Id;
        }

        public readonly IComponentId[] GetComponents()
        {
            return man.GetComponents(id);
        }

        public void Remove()
        {
            man.Remove(this.id);
            compIds = 0;
        }

        public override string ToString()
        {
            return $"Entity[{id}]";
        }
    }

    public interface IFilter
    {
        /// <summary>
        /// Applies the filter criteria to the <paramref name="entity"/>
        /// </summary>
        /// <param name="entity">A referenced <see cref="EntityId"/></param>
        /// <returns><c>true</c> to keep, <c>false</c> to ignore</returns>
        bool Apply(EntityId entity);
    }

    public class Filter : IFilter
    {
        internal int incIds, excIds;

        public Filter With(params IComponentId[] compIds)
        {
            for (int i = 0, x = compIds.Length; i < x; i++)
            {
                if (compIds[i] != null)
                    incIds |= compIds[i].Id;
            }
            return this;
        }

        public Filter Without(params IComponentId[] compIds)
        {
            for (int i = 0, x = compIds.Length; i < x; i++)
            {
                if (compIds[i] != null)
                {
                    var id = compIds[i].Id;
                    incIds &= ~id; // remove the component id from the included if it is in there
                    excIds |= id;
                }
            }
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Apply(EntityId entity)
        {
            bool incPass = incIds > 0 && (entity.compIds & incIds) == incIds;
            bool excPass = excIds > 0 && (entity.compIds & excIds) != 0;
            return incPass == true && excPass == false;
        }
    }




    [Serializable]
    public class SecsyException : Exception
    {
        public SecsyException() { }
        public SecsyException(string message) : base(message) { }
        public SecsyException(EntityId ent) : base(ent.ToString()) { }
        public SecsyException(EntityId ent, IComponentId component) : base($"{ent} => {component}") { }
        public SecsyException(string message, Exception inner) : base(message, inner) { }
    }

    public class DuplicateComponentException : SecsyException
    {
        public DuplicateComponentException()
        {
        }

        public DuplicateComponentException(IComponentId component) : base($"{component.GetType().FullName}") { }
    }

    public class UnregisteredComponentException : SecsyException
    {
        public UnregisteredComponentException()
        {
        }

        public UnregisteredComponentException(IComponentId component) : base($"{component.GetType().FullName}") { }
    }

    public class InvalidEntityIdException : SecsyException
    {
        public InvalidEntityIdException() { }

        public InvalidEntityIdException(long entId) : base(entId.ToString())
        {
        }
    }
}