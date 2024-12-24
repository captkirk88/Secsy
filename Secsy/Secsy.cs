using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;

namespace ECS
{
    public delegate void EachEntity(ref EntityId entId);

    /// <summary>
    /// Very fast entity component system.  So secsy..
    /// <para>Max entities = Array.<seealso cref="Array.MaxLength"/></para>
    /// </summary>
    public class Secsy
    {
        internal static IComponentId[] componentIds;
        internal static BitFlags128 componentIdsComposition;
        internal EntityId[] entityIds;
        internal object?[][] entComponents;
        internal ConcurrentQueue<long> entsInactive;

        static int nextCompId;
        internal long nextFreeEntId;

        private readonly object _lock = new();

        static Secsy()
        {
            componentIds = new IComponentId[128];
            componentIdsComposition = new();
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
            nextFreeEntId = 0;
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
            ComponentId<T> newCompId;
            lock (componentIds)
            {
                if (nextCompId == componentIds.Length) throw new MaxComponentsReachedException();

                newCompId = new(nextCompId, defaultValue);
                componentIds[nextCompId] = newCompId;
                componentIdsComposition.SetFlag(newCompId.id);
            }

            nextCompId += 1;

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
                var entComponentsLocal = entComponents[entId.id];
                for (int i = 0; i < comps.Length; i++)
                {
                    var comp = comps[i] ?? throw new ArgumentNullException($"comps[{i}]");
                    var compId = comp.Id;

                    if (componentIdsComposition.HasFlag(compId) == false)
                        throw new UnregisteredComponentException(comp);

                    entId.compIdsMask.SetFlag(compId);

                    lock (_lock)
                    {
                        entComponentsLocal[compId] = comp.DefaultValue;
                    }
                }
            }
            return ref entId;
        }


        /// <summary>
        /// Create many entities at once
        /// </summary>
        /// <param name="amount">Amount of entities to create</param>
        /// <param name="comps"></param>
        /// <returns></returns>
        public EntityId[] NewEntities(int amount, params IComponentId[] comps)
        {
            if (amount < 0) return [];
            IncreaseCapacity(amount);
            ConcurrentBag<EntityId> result = [];
            var chunkSize = amount > 4 ? amount / 4 : 1;
            var chunks = amount / chunkSize;
            var remain = amount % chunkSize;
            for (int i = 0; i < chunks; i++)
            {
                Parallel.For(0, chunkSize, createEntity);
            }

            if (remain > 0)
            {
                Parallel.For(0, remain, createEntity);
            }
            return [.. result];

            void createEntity(int c)
            {
                result.Add(NewEntity(comps));
            }
        }

        internal void IncreaseCapacity(long capSize)
        {
            lock (_lock)
            {
                var len = entityIds.LongLength;
                //var diff = ((len + capSize) - len); // thought about leaving this here to see how long it takes for someone to point out the redundant math.  Was a fun little "gotcha"
                if (capSize > 0)
                {
                    var newSize = (int)(len + capSize);
                    Array.Resize(ref entityIds, newSize);
                    Array.Resize(ref entComponents, newSize);
                }
            }
        }

        internal ref EntityId NewEnt()
        {
            long nextEntId = 0;
            if (!entsInactive.IsEmpty)
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

            EntityId entId = new(nextEntId, this);
            lock (_lock)
            {
                if (nextEntId >= entityIds.Length)
                {
                    IncreaseCapacity(nextEntId + 1L);
                }

                entityIds[nextEntId] = entId;

                if (entComponents[nextEntId] == null || entComponents[nextEntId].Length == 0)
                {
                    entComponents[nextEntId] = new object?[componentIds.Length];
                }
                else
                {
                    Array.Clear(entComponents[nextEntId]);
                }
            }

            return ref entityIds[nextEntId];
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
            if (id == 0) throw new InvalidEntityIdException(id);
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
            if (entId == 0) throw new InvalidEntityIdException(entId);
            ref var ent = ref Get(entId);
            if (ent.Has(compId)) throw new DuplicateComponentException(compId);

            lock (_lock)
            {
                var components = entComponents[entId];
                if (components.Length <= compId.id)
                {
                    Array.Resize(ref components, compId.id + 1);
                    entComponents[entId] = components;
                }

                components[compId.id] = compId.DefaultValue;
                ent.compIdsMask.SetFlag(compId.id);
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
            if (entId == 0) throw new InvalidEntityIdException(entId);
            ArgumentNullException.ThrowIfNull(comp, nameof(comp));
            if (comp.DefaultValue?.GetType() == newValue.GetType())
            {
                throw new ComponentTypeConflictException(comp, newValue);
            }
            ref EntityId ent = ref Get(entId);

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
            if (entId == 0) throw new InvalidEntityIdException(entId);
            ref EntityId ent = ref Get(entId);
            try
            {
                var value = entComponents[entId][compId.id];
                if (value is T good) return good;
                throw new ComponentTypeConflictException(compId, value);
            }
            catch (ComponentTypeConflictException) { throw; }
            catch (Exception)
            {
                throw new ComponentNotFoundException(ent, compId);
            }
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
            if (entId == 0) throw new InvalidEntityIdException(entId);
            ArgumentNullException.ThrowIfNull(comp, nameof(comp));
            ref EntityId ent = ref Get(entId);

            try
            {
                return entComponents[entId][comp.Id];
            }
            catch (Exception)
            {
                throw new ComponentNotFoundException(ent, comp);
            }
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
            if (entId == 0) return true; // true because entId 0 should never have any components
            ArgumentNullException.ThrowIfNull(comp, nameof(comp));
            ref EntityId ent = ref Get(entId);

            var compId = comp.Id;
            lock (_lock)
            {
                ref var comps = ref entComponents[entId];
                try
                {
                    comps[compId] = null;
                }
                catch (Exception)
                {
                    throw new ComponentNotFoundException(ent, comp);
                }
                ent.compIdsMask.ClearFlag(compId);
            }
            return ent.compIdsMask.HasFlag(compId) == false;
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
            ref EntityId ent = ref Get(entId);

            if (ent.compIdsMask.IsEmpty == false)
            {
                var entComps = entComponents;
                List<IComponentId> comps = [];
                lock (componentIds)
                {
                    for (int i = 0, x = componentIds.Length; i < x; i++)
                    {
                        var comp = componentIds[i];
                        if (comp != null && ent.compIdsMask.HasFlag(comp.Id))
                        {
                            comps.Add(comp);
                        }
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
        public void Remove(long entId)
        {
            ref EntityId ent = ref Get(entId);

            lock (_lock)
            {
                ent.compIdsMask = BitFlags128.Empty;
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
            // yes we are doing this
            // rationale: chunking the entityIds into ArraySegments and then parallelizing makes the operation more efficient and faster for huge amounts of entities
            // the overhead of creating the ArraySegments is negligible compared to the performance gain
            // the parallelization is also negligible overhead compared to the performance gain
            var totalEntities = entityIds.Length;
            var processorCount = Environment.ProcessorCount;
            var chunkMaxSize = Math.Max(1, totalEntities / processorCount);
            var chunks = totalEntities / chunkMaxSize;
            var remain = totalEntities % chunkMaxSize;
            var offset = 0;
            for (int i = 0; i < chunks; i++)
            {
                var seg = new ArraySegment<EntityId>(entityIds, offset, chunkMaxSize);
                offset += chunkMaxSize;
                Parallel.ForEach(seg, filterBody);
            }
            if (remain > 0)
            {
                var seg = new ArraySegment<EntityId>(entityIds, offset, remain);
                Parallel.ForEach(seg, filterBody);
            }
            return ents.GetEnumerator();

            void filterBody(EntityId ent)
            {
                //ref EntityId ent = ref entityIds[i];
                if (ent.compIdsMask.IsEmpty || ent.id == 0) return;
                if (filter.Apply(ent))
                    ents.Add(ent.id);
            }
        }

        /// <summary>
        /// Clears all entities and components
        /// </summary>
        public void Clear()
        {
            entsInactive.Clear();
            Array.Clear(entityIds);
            Array.Clear(entComponents);
        }
    }

    /// <summary>
    /// Represents a component identifier.
    /// </summary>
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

        /// <summary>
        /// Generates a string representation of this component's id
        /// </summary>
        /// <returns></returns>
        string? ToString()
        {
            return $"Component[{Id}]";
        }
    }


    /// <summary>
    /// Represents a component identifier with a specific type.
    /// </summary>
    /// <typeparam name="T">The type of the component.</typeparam>
    public readonly struct ComponentId<T> : IComponentId
    {
        internal ComponentId(int id, T? defaultValue)
        {
            this.id = id;
            this.value = defaultValue;
        }
        internal readonly int id;

        /// <summary>
        /// Gets the unique identifier of the component.
        /// </summary>
        public readonly int Id => id;

        object? IComponentId.DefaultValue => value;

        internal readonly T? value;

        /// <summary>
        /// Gets the default value of the component.
        /// </summary>
        public T? DefaultValue => value;

        /// <summary>
        /// Gets the component value of type <typeparamref name="T"/> for the specified entity.
        /// </summary>
        /// <param name="ent">The entity to get the component value from.</param>
        /// <returns>The component value.</returns>
        public T? Get(EntityId ent)
        {
            return ent.man.GetComponentValue(ent.id, this);
        }

        /// <summary>
        /// Adds the component of type <typeparamref name="T"/> to the specified entity.
        /// </summary>
        /// <param name="ent">The entity to add the component to.</param>
        /// <returns>This component identifier for chaining.</returns>
        public ComponentId<T> Add(ref EntityId ent)
        {
            ent.man.AddComponent(ent.id, this);
            ent.compIdsMask.SetFlag(id);
            return this;
        }

        /// <summary>
        /// Sets the value of this component for the specified entity to the new value.
        /// </summary>
        /// <param name="ent">The entity to set the component value for.</param>
        /// <param name="newValue">The new value to set.</param>
        /// <returns>This component identifier for chaining.</returns>
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
        /// Checks if the specified entity has this component assigned to it.
        /// </summary>
        /// <param name="ent">The entity to check.</param>
        /// <returns><see langword="true"/> if the entity has this component, <see langword="false"/> otherwise.</returns>
        public bool Has(ref EntityId ent)
        {
            return ent.compIdsMask.HasFlag(id);
        }

        /// <summary>
        /// Removes this component from the specified entity.
        /// </summary>
        /// <param name="ent">The entity to remove the component from.</param>
        /// <returns>This component identifier for chaining.</returns>
        public ComponentId<T> Remove(ref EntityId ent)
        {
            ent.man.RemoveComponent(ent.id, this);
            ent.compIdsMask.ClearFlag(id);
            return this;
        }
    }

    /// <summary>
    /// Entity
    /// </summary>
    public struct EntityId
    {
        internal static EntityId Null = default;

        internal EntityId(long id, Secsy man)
        {
            this.id = id;
            this.man = man;
            compIdsMask = new();
        }
        internal Secsy man;
        internal long id;

        /// <summary>
        /// Returns entity id
        /// </summary>
        public readonly long ID => id;

        internal BitFlags128 compIdsMask;


        /// <summary>
        /// Returns if this entity is alive with components assigned to it
        /// </summary>
        public readonly bool IsAlive => compIdsMask.IsEmpty == false;

        /// <summary>
        /// Checks if this entity has <paramref name="componentId"/>
        /// </summary>
        /// <param name="componentId"></param>
        /// <returns></returns>
        public readonly bool Has(IComponentId componentId)
        {
            return componentId != null && compIdsMask.HasFlag(componentId.Id);
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
            compIdsMask = BitFlags128.Empty;
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
        internal BitFlags128 incIds = new(), excIds = new();


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
                    var id = compIds[i].Id;
                    incIds.SetFlag(id);
                    excIds.ClearFlag(id);
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
                    incIds.ClearFlag(id);
                    excIds.SetFlag(id);
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
            incIds.Clear();
            excIds.Clear();
            return this;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public bool Apply(EntityId entity)
        {
            bool incPass = incIds.HasAnyFlag(entity.compIdsMask);
            bool excPass = !excIds.HasAnyFlag(entity.compIdsMask);
            return incPass == true && excPass == true;
        }
    }


#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public struct BitFlags128
    {
        public static readonly BitFlags128 Empty = new();

        private long flags1;
        private long flags2;

        public void SetFlag(int flagIndex)
        {
            if (flagIndex < 0 || flagIndex >= 128)
                throw new ArgumentOutOfRangeException(nameof(flagIndex));

            if (flagIndex < 64)
                flags1 |= 1L << flagIndex;
            else
                flags2 |= 1L << (flagIndex - 64);
        }

        public void ClearFlag(int flagIndex)
        {
            if (flagIndex < 0 || flagIndex >= 128)
                throw new ArgumentOutOfRangeException(nameof(flagIndex));

            if (flagIndex < 64)
                flags1 &= ~(1L << flagIndex);
            else
                flags2 &= ~(1L << (flagIndex - 64));
        }

        public bool HasFlag(int flagIndex)
        {
            if (flagIndex < 0 || flagIndex >= 128)
                throw new ArgumentOutOfRangeException(nameof(flagIndex));

            if (flagIndex < 64)
                return (flags1 & (1L << flagIndex)) != 0;
            else
                return (flags2 & (1L << (flagIndex - 64))) != 0;
        }

        public bool HasAnyFlag(BitFlags128 mask)
        {
            return (flags1 & mask.flags1) != 0 || (flags2 & mask.flags2) != 0;
        }


        public void Clear()
        {
            flags1 = 0;
            flags2 = 0;
        }
        public bool IsEmpty
        {
            get
            {
                return flags1 == 0 && flags2 == 0;
            }
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

    public class MaxComponentsReachedException : SecsyException
    {
        public MaxComponentsReachedException() : base("Max components reached")
        {
        }
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

    public class ComponentTypeConflictException : SecsyException
    {
        public ComponentTypeConflictException()
        {
        }

        public ComponentTypeConflictException(IComponentId component, object? value) : base($"expected value of type {component.GetType().FullName}, got {value?.GetType()}") { }
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