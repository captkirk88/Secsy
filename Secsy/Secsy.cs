using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace ECS
{
    public delegate void EachEntity(ref EntityId entId);
    public delegate void EachComponent<T>(ComponentId<T> compId);

    /// <summary>
    /// Very fast entity component system.  So secsy..
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

        /// <summary>
        /// Very secsy
        /// </summary>
        public Secsy()
        {
            entComponents = [];
            entityIds = [];
            entsInactive = new();
        }

        /// <summary>
        /// Total length of internal array
        /// </summary>
        public int Capacity
        {
            get
            {
                lock (_lock) return entityIds.Length;
            }
        }

        /// <summary>
        /// Amount of alive entities
        /// </summary>
        public int Count
        {
            get
            {
                int count = 0;
                lock (_lock)
                {
                    for (int i = 0, x = entityIds.Length; i < x; i++)
                    {
                        if (entityIds[i].IsAlive) count++;
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// Gets all alive entities
        /// </summary>
        /// <returns></returns>
        public List<EntityId> All()
        {
            lock (_lock)
            {
                List<EntityId> result = new(entityIds.Length);
                for (int i = 0, x = entityIds.Length; i < x; i++)
                {
                    var ent = entityIds[i];
                    if (ent.IsAlive) result.Add(ent);
                }
                return result;
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
        /// <param name="amount">Amount of entities to create</param>
        /// <param name="comps"></param>
        /// <returns></returns>
        public EntityId[] NewEntities(int amount, params IComponentId[] comps)
        {
            if (amount < 0) return [];
            IncreaseCapacity(amount);
            ConcurrentBag<EntityId> result = [];
            Parallel.For(0, amount, c =>
            {
                ref var entId = ref NewEntity(comps);
                result.Add(entId);
            });
            return [.. result];
        }

        internal void IncreaseCapacity(int capSize)
        {
            lock (_lock)
            {
                var len = entityIds.Length;
                var diff = ((len + capSize) - len);
                if (diff > 0)
                {
                    var newSize = len + diff;
                    Array.Resize(ref entityIds, newSize);
                    Array.Resize(ref entComponents, newSize);
                }
            }
        }

        internal ref EntityId NewEnt()
        {
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
                    nextEntId = nextFreeEntId;
                }
            }

            entId = new(nextEntId, this);
            lock (_lock)
            {
                if (nextEntId >= entityIds.Length)
                {
                    IncreaseCapacity(nextEntId + 100);
                }

                entityIds[nextEntId] = entId;

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
            if ((ent.compIds & compId.id) != 0) throw new DuplicateComponentException(compId);

            lock (_lock)
            {
                int len = entComponents[entId].Length;
                if (len < compId.id) Array.Resize(ref entComponents[entId], len + compId.id);

                entComponents[entId][compId.id] = compId.DefaultValue;
                ent.compIds |= compId.id;
            }
        }

        /// <summary>
        /// Sets component value
        /// </summary>
        /// <param name="entId"></param>
        /// <param name="comp"></param>
        /// <param name="newValue"></param>
        /// <exception cref="InvalidEntityIdException"></exception>
        public void SetComponentValue(long entId, IComponentId comp, object newValue)
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

            lock (_lock)
            {
                try
                {
                    entComponents[entId][comp.Id] = newValue;
                }
                catch (Exception)
                {
                    throw new ComponentNotFoundException(ent, comp);
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
            try
            {
                return (T?)entComponents[entId][compId.id];
            }
            catch (Exception)
            {
                throw new ComponentNotFoundException(ent, compId);
            }
            return default;
        }

        /// <summary>
        /// Get the component value from <paramref name="entId"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entId"></param>
        /// <param name="comp"></param>
        /// <returns></returns>
        /// <exception cref="InvalidEntityIdException"></exception>
        public object? GetComponentValue(long entId, IComponentId comp)
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
            var compId = comp.Id;
            if ((ent.compIds & compId) != 0)
                return entComponents[entId][compId];
            return default;
        }


        /// <summary>
        /// Remove component from <paramref name="entId"/>
        /// </summary>
        /// <param name="entId"></param>
        /// <param name="comp"></param>
        /// <returns></returns>
        /// <exception cref="InvalidEntityIdException"></exception>
        public bool RemoveComponent(long entId, IComponentId comp)
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

            var compId = comp.Id;
            if ((ent.compIds & compId) != 0)
            {
                lock (_lock)
                {
                    entComponents[entId][compId] = null;
                    entityIds[entId].compIds &= ~compId;
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
            var entComps = entComponents;
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
                for (int i = 0, x = componentIds.Length; i < x; i++)
                {
                    var comp = componentIds[i];
                    if (comp != null && (ent.compIds & comp.Id) != 0)
                    {
                        var compVal = entComps[ent.id][comp.Id];

                        if (compVal != null) comps.Add(componentIds[i]);
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
            EntityId ent;
            try
            {
                ent = entityIds[entId];
            }
            catch (Exception)
            {
                throw new InvalidEntityIdException(entId);
            }

            lock (_lock)
            {
                ent.compIds = 0;
                Array.Clear(entComponents[ent.id]);
            }
            entsInactive.Enqueue(entId);
        }

        /// <summary>
        /// Iterates through each entity that was <paramref name="filter"/>ed
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="fn"></param>
        /// <returns>Amount of entities that were processed</returns>
        public int Each(IFilter filter, EachEntity fn)
        {
            if (filter == null) return 0;
            var e = Filter(filter);
            int count = 0;

            while (e.MoveNext())
            {
                fn(ref entityIds[e.Current]);
                count++;
            }
            return count;
        }

        /// <summary>
        /// Filters entities with <paramref name="filter"/>
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        public IEnumerator<long> Filter(IFilter filter)
        {
            if (filter == null) return Enumerable.Empty<long>().GetEnumerator();
            ConcurrentBag<long> ents = [];
            Parallel.ForEach(entityIds, filterBody);
            //Parallel.For(0, entityIds.Length, filterBody);
            return ents.GetEnumerator();
            void filterBody(EntityId ent)
            {
                //ref EntityId ent = ref entityIds[i];
                if (ent.compIds == 0 || ent.id == 0) return;
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
        /// <summary>
        /// Component's unique id
        /// </summary>
        int Id { get; }

        /// <summary>
        /// Default value to set
        /// </summary>
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

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public readonly int Id => id;

        object? IComponentId.DefaultValue => value;

        internal readonly T? value;

        /// <summary>
        /// <inheritdoc cref="IComponentId.DefaultValue"/>
        /// </summary>
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
        /// Adds the component of type <typeparamref name="T"/> for <paramref name="ent"/>
        /// </summary>
        /// <param name="ent"></param>
        /// <returns>This for chaining</returns>
        public ComponentId<T> Add(ref EntityId ent)
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
            try
            {
                ent.man.entComponents[ent.id][id] = newValue;
            }
            catch (Exception)
            {
                throw new ComponentNotFoundException(ent, this);
            }
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

        /// <summary>
        /// Removes the <see cref="ComponentId{T}"/> from <paramref name="ent"/>
        /// </summary>
        /// <param name="ent"></param>
        /// <returns></returns>
        public ComponentId<T> Remove(ref EntityId ent)
        {
            ent.man.RemoveComponent(ent.id, this);
            ent.compIds &= ~id;
            return this;
        }
    }

    /// <summary>
    /// Entity
    /// </summary>
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

        /// <summary>
        /// Returns entity id
        /// </summary>
        public readonly int ID => id;

        internal int compIds;

        /// <summary>
        /// Returns if this entity is alive with components assigned to it
        /// </summary>
        public readonly bool IsAlive => compIds != 0;

        /// <summary>
        /// Checks if this entity has <paramref name="componentId"/>
        /// </summary>
        /// <param name="componentId"></param>
        /// <returns></returns>
        public readonly bool Has(IComponentId componentId)
        {
            return componentId != null && (compIds & componentId.Id) == componentId.Id;
        }

        /// <summary>
        /// Gets all the components for this entity
        /// </summary>
        /// <returns></returns>
        public readonly IComponentId[] GetComponents()
        {
            return man.GetComponents(id);
        }

        /// <summary>
        /// Removes this entity
        /// </summary>
        public void Remove()
        {
            man.Remove(this.id);
            compIds = 0;
        }

        /// <summary>
        /// Returns a string
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"Entity[{id}]";
        }
    }

    /// <summary>
    /// Filter
    /// </summary>
    public interface IFilter
    {
        /// <summary>
        /// Applies the filter criteria to the <paramref name="entity"/>
        /// </summary>
        /// <param name="entity">A referenced <see cref="EntityId"/></param>
        /// <returns><c>true</c> to keep, <c>false</c> to ignore</returns>
        bool Apply(EntityId entity);
    }

    /// <summary>
    /// Common usage filter
    /// </summary>
    public class Filter : IFilter
    {
        internal int incIds, excIds;


        /// <summary>
        /// Filter entities that <b>DO</b> have components specified in <paramref name="compIds"/>.
        /// </summary>
        /// <param name="compIds"></param>
        /// <returns></returns>
        public Filter With(params IComponentId[] compIds)
        {
            for (int i = 0, x = compIds.Length; i < x; i++)
            {
                if (compIds[i] != null)
                {
                    incIds |= compIds[i].Id;
                    excIds &= ~compIds[i].Id; // remove the component id from the excluded if it is in there
                }
            }
            return this;
        }

        /// <summary>
        /// Filter entities that <b>DO NOT</b> have components specified in <paramref name="compIds"/>.
        /// </summary>
        /// <param name="compIds"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Resets the filter making it filter nothing.
        /// </summary>
        /// <returns></returns>
        public Filter Reset()
        {
            incIds = 0;
            excIds = 0;
            return this;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public bool Apply(EntityId entity)
        {
            bool incPass = incIds > 0 && (entity.compIds & incIds) != 0;
            bool excPass = excIds > 0 && (entity.compIds & excIds) != 0;
            return incPass == true && excPass == false;
        }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        public bool ApplyMany(params EntityId[] ents)
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
        {
            bool result = true;
            for (int i = 0, x = ents.Length; i < x; i++)
            {
                result &= Apply(ents[i]);
            }
            return result;
        }
    }




    [Serializable]
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
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

    public class ComponentNotFoundException : SecsyException
    {
        public ComponentNotFoundException()
        {
        }

        public ComponentNotFoundException(EntityId ent, IComponentId component) : base(ent, component) { }
    }

    public class InvalidEntityIdException : SecsyException
    {
        public InvalidEntityIdException() { }

        public InvalidEntityIdException(long entId) : base(entId.ToString())
        {
        }
    }

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}