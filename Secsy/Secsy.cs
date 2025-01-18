using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;
using EID = ulong;

namespace ECS
{
    /// <summary>
    /// Delegate for <see cref="Secsy.Each(IFilter, EachEntity)"/>
    /// </summary>
    /// <param name="entId"></param>
    public delegate void EachEntity(ref EntityId entId);

    /// <summary>
    /// Very fast entity component system.  So secsy..
    /// <para>Max entities = Array.<seealso cref="Array.MaxLength"/></para>
    /// </summary>
    public class Secsy
    {
        internal static IComponentId[] componentIds;
        internal static IBitFlags componentIdsComposition;
        internal EntityId[] entityIds;
        internal object?[][] entComponents;
        internal ConcurrentQueue<EID> entsInactive;

        static int nextCompId;
        internal EID nextFreeEntId;

        private readonly object _lock = new();

        static Secsy()
        {
            componentIds = new IComponentId[SecsyConfig.maxCompIds];
            componentIdsComposition = SecsyConfig.Current();
            nextCompId = 0;
        }

        /// <summary>
        /// Very secsy
        /// </summary>
        public Secsy()
        {
            entComponents = new object?[100][];
            entityIds = new EntityId[100];
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
                lock (_lock)
                {
                    return Array.FindAll(entityIds, amIAlive).Length;
                }
                static bool amIAlive(EntityId ent)
                {
                    return ent.IsAlive;
                }
            }
        }

        /// <summary>
        /// Gets all alive entities
        /// </summary>
        /// <returns></returns>
        public IEnumerable<EntityId> All()
        {
            lock (_lock)
            {
                return Array.FindAll(entityIds, amIAlive);
            }
            static bool amIAlive(EntityId ent)
            {
                return ent.IsAlive;
            }
        }
        /// <summary>
        /// Create a new component id.
        /// </summary>
        /// <typeparam name="T">Must be a struct type</typeparam>
        /// <param name="defaultValue">Default value of <typeparamref name="T"/></param>
        /// <returns></returns>
        /// <exception cref="BadComponentValueException">Thrown when <paramref name="defaultValue"/> is a bad type for components</exception>
        /// <exception cref="MaxComponentsReachedException">Max allowed component ids reached</exception>
        public static ComponentId<T> NewComponentId<T>(T? defaultValue = default)
        {
            if (defaultValue is IComponentId or EntityId) throw new BadComponentValueException(defaultValue);
            ComponentId<T> newComp;
            lock (componentIds)
            {
                if (nextCompId == componentIds.Length) throw new MaxComponentsReachedException();

                newComp = new(nextCompId, defaultValue);
                componentIds[nextCompId] = newComp;
                componentIdsComposition.SetFlag(newComp.id);
            }

            nextCompId += 1;

            return newComp;
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
                Span<IComponentId> compsLocal = comps;
                Span<object?> entComponentsLocal = entComponents[entId.id];
                for (int i = 0, x = compsLocal.Length; i < x; i++)
                {
                    var comp = compsLocal[i] ?? throw new ArgumentNullException($"comps[{i}]");
                    var compId = comp.Id;

                    if (componentIdsComposition.HasFlag(compId) == false)
                        throw new UnregisteredComponentException(comp);

                    entId.compIdsMask.SetFlag(compId);

                    entComponentsLocal[compId] = comp.DefaultValue;
                }
            }
            return ref entId;
        }


        /// <summary>
        /// Create many entities at once
        /// </summary>
        /// <param name="amount">Amount of entities to create</param>
        /// <param name="comps"></param>
        public void NewEntities(int amount, params IComponentId[] comps)
        {
            if (amount <= 0) return;
            IncreaseCapacity(amount);

            int processorCount = Environment.ProcessorCount;

            Span<IComponentId> compsLocal = comps;

            for (int j = 0; j < amount; j++)
            {
                ref EntityId entId = ref NewEnt();
                if (comps.Length > 0)
                {
                    Span<object?> entComponentsLocal = entComponents[entId.id];
                    for (int i = 0, x = compsLocal.Length; i < x; i++)
                    {
                        var comp = compsLocal[i] ?? throw new ArgumentNullException($"comps[{i}]");
                        var compId = comp.Id;

                        if (componentIdsComposition.HasFlag(compId) == false)
                            throw new UnregisteredComponentException(comp);

                        entId.compIdsMask.SetFlag(compId);

                        entComponentsLocal[compId] = comp.DefaultValue;
                    }
                }
            }
        }

        internal void IncreaseCapacity(long capSize)
        {
            lock (_lock)
            {
                var len = entityIds.LongLength;
                var newSize = (int)(len + Math.Max(capSize, 100));

                Array.Resize(ref entityIds, newSize);
                Array.Resize(ref entComponents, newSize);
            }
        }

        internal ref EntityId NewEnt()
        {
            EID nextEntId = 0;
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
                if ((long)nextEntId >= entityIds.Length)
                {
                    IncreaseCapacity((long)nextEntId);
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

        /// <summary>
        /// Get the <see cref="EntityId"/> from a raw id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="InvalidEntityIdException"></exception>
        public ref EntityId Get(EID id)
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
        /// <param name="comp"></param>
        /// <exception cref="InvalidEntityIdException" />
        /// <exception cref="DuplicateComponentException" />
        public void AddComponent<T>(EID entId, ComponentId<T> comp)
        {
            if (entId == 0) throw new InvalidEntityIdException(entId);
            ref var ent = ref Get(entId);
            if (ent.Has(comp)) throw new DuplicateComponentException(comp);

            lock (_lock)
            {
                var components = entComponents[entId];
                if (components.Length <= comp.id)
                {
                    Array.Resize(ref components, comp.id + 1);
                }

                components[comp.id] = comp.DefaultValue;
                ent.compIdsMask.SetFlag(comp.id);
                entComponents[entId] = components;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="ent"></param>
        /// <param name="comps"></param>
        /// <exception cref="DuplicateComponentException"><paramref name="ent"/> already has <paramref name="comp"/></exception>
        /// <exception cref="ArgumentNullException"><paramref name="comp"/> is null</exception>
        private void AddComponentIds(ref EntityId ent, params IComponentId[] comps)
        {
            ref var components = ref entComponents[ent.id];
            for (int i = 0, x = comps.Length; i < x; i++)
            {
                var comp = comps[i];
                if (ent.Has(comp)) throw new DuplicateComponentException(comp);
                ArgumentNullException.ThrowIfNull(comp, nameof(comp));

                ent.compIdsMask.SetFlag(comp.Id);
                if (components.Length <= comp.Id)
                {
                    Array.Resize(ref components, comp.Id + 1);
                }
                lock (_lock)
                {
                    components[comp.Id] = comp.DefaultValue;
                    entComponents[ent.id] = components;
                }
            }

        }

        /// <summary>
        /// Sets component value
        /// </summary>
        /// <param name="entId"></param>
        /// <param name="comp"></param>
        /// <param name="newValue"></param>
        /// <exception cref="InvalidEntityIdException"><paramref name="entId"/> is 0</exception>
        /// <exception cref="ComponentNotFoundException"><paramref name="entId"/> does not have <paramref name="comp"/></exception>
        /// <exception cref="ArgumentNullException"><paramref name="comp"/> is null</exception>
        public void SetComponentValue(EID entId, IComponentId comp, object? newValue)
        {
            if (entId == 0) throw new InvalidEntityIdException(entId);
            ArgumentNullException.ThrowIfNull(comp, nameof(comp));

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
        /// <param name="comp"></param>
        /// <returns></returns>
        /// <exception cref="InvalidEntityIdException"><paramref name="entId"/> is 0</exception>
        /// <exception cref="ComponentNotFoundException"><paramref name="entId"/> does not have <paramref name="comp"/></exception>
        /// <exception cref="ComponentTypeConflictException">component value is not <typeparamref name="T"/></exception>
        /// <exception cref="ArgumentNullException"><paramref name="comp"/> is null</exception>
        public T GetComponentValue<T>(EID entId, ComponentId<T> comp)
        {
            if (entId == 0) throw new InvalidEntityIdException(entId);
            ArgumentNullException.ThrowIfNull(comp, nameof(comp));

            ref EntityId ent = ref Get(entId);
            lock (_lock)
            {
                try
                {
                    ref var value = ref entComponents[entId][comp.id];
                    if (value == null) throw new NullReferenceException($"{comp} for {ent} has no value");
                    if (value is T good)
                        return good;
                    else throw new ComponentTypeConflictException(comp, value);
                }
                catch (ComponentTypeConflictException) { throw; }
                catch (Exception)
                {
                    throw new ComponentNotFoundException(ent, comp);
                }
            }
        }

        /// <summary>
        /// Get the component value from <paramref name="entId"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entId"></param>
        /// <param name="comp"></param>
        /// <returns></returns>
        /// <exception cref="InvalidEntityIdException"><paramref name="entId"/> is 0</exception>
        /// <exception cref="ComponentNotFoundException"><paramref name="entId"/> does not have <paramref name="comp"/></exception>
        /// <exception cref="ComponentTypeConflictException">component value is not <typeparamref name="T"/></exception>
        /// <exception cref="ArgumentNullException"><paramref name="comp"/> is null</exception>
        public ref object? GetComponentValue(EID entId, IComponentId comp)
        {
            if (entId == 0) throw new InvalidEntityIdException(entId);
            ArgumentNullException.ThrowIfNull(comp, nameof(comp));

            ref EntityId ent = ref Get(entId);
            lock (_lock)
            {
                try
                {
                    return ref entComponents[entId][comp.Id];
                }
                catch (ComponentTypeConflictException) { throw; }
                catch (Exception)
                {
                    throw new ComponentNotFoundException(ent, comp);
                }
            }
        }


        /// <summary>
        /// Remove component from <paramref name="entId"/>
        /// </summary>
        /// <param name="entId"></param>
        /// <param name="comp"></param>
        /// <returns></returns>
        /// <exception cref="ComponentNotFoundException"><paramref name="entId"/> does not have <paramref name="comp"/></exception>
        /// <exception cref="ArgumentNullException"><paramref name="comp"/> is null</exception>
        public bool RemoveComponent(EID entId, IComponentId comp)
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
        /// <exception cref="InvalidEntityIdException">thrown from <see cref="Get(long)"/></exception>
        public IComponentId[] GetComponents(EID entId)
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
        public void Remove(EID entId)
        {
            ref EntityId ent = ref Get(entId);

            lock (_lock)
            {
                ent.compIdsMask = SecsyConfig.Current();
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
        /// <exception cref="AggregateException">exceptions thrown by <paramref name="fn"/></exception>
        /// <exception cref="ArgumentNullException"><paramref name="filter"/> is null</exception>
        public int Each(IFilter filter, EachEntity fn)
        {
            ArgumentNullException.ThrowIfNull(filter, nameof(filter));
            List<Exception> exceptions = [];
            var e = Filter(filter);
            int count = 0;

            while (e.MoveNext())
            {
                EID eId = e.Current;
                if (eId == 0) continue;
                count++;
                try
                {
                    fn(ref Get(eId));
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    if (exceptions.Count > 5)
                    {
                        exceptions.Add(new OverflowException("Too many exceptions"));
                        break;
                    }
                }
            }

            if (exceptions.Count > 0) throw new AggregateException(exceptions);
            return count;
        }

        /// <summary>
        /// Filters entities with <paramref name="filter"/>
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"><paramref name="filter"/> is null</exception>
        public IEnumerator<EID> Filter(IFilter filter)
        {
            ArgumentNullException.ThrowIfNull(filter, nameof(filter));
            ConcurrentBag<EID> ents = [];
            var entIds = entityIds;
            var totalEntities = entIds.Length;
            var processorCount = Environment.ProcessorCount;
            var parallelForOpts = new ParallelOptions { MaxDegreeOfParallelism = processorCount };
            var chunkMaxSize = Math.Max(1, totalEntities / processorCount);
            var chunks = totalEntities / chunkMaxSize;
            var remain = totalEntities % chunkMaxSize;
            var offset = 0;
            for (int i = 0; i < chunks; i++)
            {
                var seg = new ArraySegment<EntityId>(entIds, offset, chunkMaxSize);
                offset += chunkMaxSize;
                Parallel.ForEach(seg, parallelForOpts, filterBody);
            }
            if (remain > 0)
            {
                var seg = new ArraySegment<EntityId>(entIds, offset, remain);
                Parallel.ForEach(seg, parallelForOpts, filterBody);
            }
            return ents.GetEnumerator();

            void filterBody(EntityId ent)
            {
                if (ent.IsAlive == false) return;
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
            Array.Resize(ref entityIds, 100);
            Array.Clear(entComponents);
            Array.Resize(ref entComponents, 100);
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
            this.defaultValue = defaultValue;
        }
        internal readonly int id;

        /// <summary>
        /// Gets the unique identifier of the component.
        /// </summary>
        public readonly int Id => id;

        object? IComponentId.DefaultValue => defaultValue;

        internal readonly T? defaultValue;

        /// <summary>
        /// Gets the default value of the component.
        /// </summary>
        public T? DefaultValue => defaultValue;

        /// <summary>
        /// Gets the component value of type <typeparamref name="T"/> for the specified entity.
        /// </summary>
        /// <param name="ent">The entity to get the component value from.</param>
        /// <returns>The component value.</returns>
        public T Get(EntityId ent)
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
        public ComponentId<T> SetValue(EntityId ent, T? newValue)
        {
            try
            {
                ent.man.SetComponentValue(ent.id, this, newValue);
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

        /// <summary>
        /// Generates a string representation of this component's id
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"Component[{typeof(T).Name}]";
        }

    }

    /// <summary>
    /// Entity
    /// </summary>
    public struct EntityId
    {
        internal static EntityId Null = new();

        internal EntityId(EID id, Secsy man)
        {
            this.id = id;
            this.man = man;
            compIdsMask = SecsyConfig.Current();
        }
        internal readonly Secsy man;
        internal readonly EID id;

        /// <summary>
        /// Returns entity id
        /// </summary>
        public readonly EID ID => id;

        internal IBitFlags compIdsMask;

        /// <summary>
        /// Returns the component id mask for this entity
        /// </summary>
        public readonly IBitFlags ComponentIdMask => new ReadOnlyBitFlags(compIdsMask);

        /// <summary>
        /// Returns if this entity is alive with components assigned to it
        /// </summary>
        public readonly bool IsAlive => id > 0 && compIdsMask != null && compIdsMask.IsEmpty == false;

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
            compIdsMask.Clear();
        }

        /// <summary>
        /// Returns a string
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"Entity[{id}]";
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool operator ==(EntityId a, EntityId b)
        {
            return a.id == b.id && a.compIdsMask == b.compIdsMask;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool operator !=(EntityId a, EntityId b)
        {
            return a.id != b.id || a.compIdsMask != b.compIdsMask;
        }

        /// <inheritdoc/>
        public readonly override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is EntityId yes && this == yes;
        }

        /// <inheritdoc/>
        public readonly override int GetHashCode()
        {
            return HashCode.Combine(id, compIdsMask);
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
        internal IBitFlags incIds, excIds;
        /// <summary>
        /// Creates a new filter
        /// </summary>
        public Filter()
        {
            incIds = SecsyConfig.Current();
            excIds = SecsyConfig.Current();
        }

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
            return incPass && excPass;
        }
    }

    #region BITFLAGS


    /// <summary>
    /// Interface for bit flags
    /// </summary>
    public interface IBitFlags
    {
        /// <summary>
        /// Sets the flag
        /// </summary>
        /// <param name="flagIndex"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        void SetFlag(int flagIndex);

        /// <summary>
        /// Checks if the flag is set
        /// </summary>
        /// <param name="flagIndex"></param>
        /// <returns></returns>
        bool HasFlag(int flagIndex);

        /// <summary>
        /// Checks if any of the flags are set
        /// </summary>
        /// <param name="mask"></param>
        /// <returns></returns>
        bool HasAnyFlag(IBitFlags mask);

        /// <summary>
        /// Checks if all of the flags are set
        /// </summary>
        /// <param name="mask"></param>
        /// <returns></returns>
        bool HasAllFlags(IBitFlags mask);

        /// <summary>
        /// Clears the flag
        /// </summary>
        /// <param name="flagIndex"></param>
        void ClearFlag(int flagIndex);

        /// <summary>
        /// Gets the flags.
        /// </summary>
        ulong Flags { get; }

        /// <summary>
        /// Clears the flags
        /// </summary>
        void Clear();

        /// <summary>
        /// Checks if the flags are empty
        /// </summary>
        bool IsEmpty { get; }

        /// <inheritdoc/>
        int GetHashCode()
        {
            return Flags.GetHashCode();
        }

        /// <inheritdoc/>
        bool Equals(object? obj)
        {
            return obj is IBitFlags other && this == other;
        }
    }

    /// <summary>
    /// 128 bit flags or 128 components per entity
    /// </summary>
    public struct BitFlags128 : IBitFlags
    {
        /// <summary>
        /// Empty flags
        /// </summary>
        public static readonly BitFlags128 Empty = default;

        private ulong flags1;
        private ulong flags2;

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public readonly ulong Flags => flags1 | flags2;

        /// <summary>
        /// Sets the flag
        /// </summary>
        /// <param name="flagIndex"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFlag(int flagIndex)
        {

            if (flagIndex < 0 || flagIndex >= 128)
                throw new ArgumentOutOfRangeException(nameof(flagIndex));

            if (flagIndex < 64)
                flags1 |= 1UL << flagIndex;
            else
                flags2 |= 1UL << (flagIndex - 64);
        }

        /// <summary>
        /// Clears the flag
        /// </summary>
        /// <param name="flagIndex"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearFlag(int flagIndex)
        {
            if (flagIndex < 0 || flagIndex >= 128)
                throw new ArgumentOutOfRangeException(nameof(flagIndex));

            if (flagIndex < 64)
                flags1 &= ~(1UL << flagIndex);
            else
                flags2 &= ~(1UL << (flagIndex - 64));
        }

        /// <summary>
        /// Checks if the flag is set
        /// </summary>
        /// <param name="flagIndex"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool HasFlag(int flagIndex)
        {
            if (flagIndex < 0 || flagIndex >= 128)
                throw new ArgumentOutOfRangeException(nameof(flagIndex));

            if (flagIndex < 64)
                return (flags1 & (1UL << flagIndex)) != 0;
            else
                return (flags2 & (1UL << (flagIndex - 64))) != 0;
        }

        /// <summary>
        /// Checks if any of the flags are set
        /// </summary>
        /// <param name="mask"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool HasAnyFlag(IBitFlags mask)
        {
            var maskFlags = mask.Flags;
            return (flags1 & maskFlags) != 0 || (flags2 & maskFlags) != 0;
        }

        /// <summary>
        /// Checks if all of the flags are set
        /// </summary>
        /// <param name="mask"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool HasAllFlags(IBitFlags mask)
        {
            var maskFlag = mask.Flags;
            return (flags1 & maskFlag) == maskFlag && (flags2 & maskFlag) == maskFlag;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool operator ==(BitFlags128 a, IBitFlags b)
        {
            var bMask = b.Flags;
            return a.flags1 == bMask && a.flags2 == bMask;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool operator !=(BitFlags128 a, IBitFlags b)
        {
            var bMask = b.Flags;
            return a.flags1 != bMask || a.flags2 != bMask;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object? obj)
        {
            if (obj is IBitFlags other)
            {
                return this == other;
            }
            return false;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(flags1, flags2);
        }

        /// <summary>
        /// Clears all flags
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            flags1 = 0;
            flags2 = 0;
        }

        /// <summary>
        /// Checks if the flags are empty
        /// </summary>
        public readonly bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return flags1 == 0 && flags2 == 0;
            }
        }
    }


    /// <summary>
    /// 256 bit flags or 256 components per entity
    /// </summary>
    public struct BitFlags256 : IBitFlags
    {
        // This was generated by Github Copilot.  Yep, I was too lazy to write another instance out.

        /// <summary>
        /// Empty flags
        /// </summary>
        public static readonly BitFlags256 Empty = default;

        private ulong flags1;
        private ulong flags2;
        private ulong flags3;
        private ulong flags4;

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public readonly ulong Flags => flags1 | flags2 | flags3 | flags4;

        /// <summary>
        /// Sets the flag
        /// </summary>
        /// <param name="flagIndex"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFlag(int flagIndex)
        {
            if (flagIndex < 0 || flagIndex >= 256)
                throw new ArgumentOutOfRangeException(nameof(flagIndex));

            if (flagIndex < 64)
                flags1 |= 1UL << flagIndex;
            else if (flagIndex < 128)
                flags2 |= 1UL << (flagIndex - 64);
            else if (flagIndex < 192)
                flags3 |= 1UL << (flagIndex - 128);
            else
                flags4 |= 1UL << (flagIndex - 192);
        }

        /// <summary>
        /// Clears the flag
        /// </summary>
        /// <param name="flagIndex"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearFlag(int flagIndex)
        {
            if (flagIndex < 0 || flagIndex >= 256)
                throw new ArgumentOutOfRangeException(nameof(flagIndex));

            if (flagIndex < 64)
                flags1 &= ~(1UL << flagIndex);
            else if (flagIndex < 128)
                flags2 &= ~(1UL << (flagIndex - 64));
            else if (flagIndex < 192)
                flags3 &= ~(1UL << (flagIndex - 128));
            else
                flags4 &= ~(1UL << (flagIndex - 192));
        }

        /// <summary>
        /// Checks if the flag is set
        /// </summary>
        /// <param name="flagIndex"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool HasFlag(int flagIndex)
        {
            if (flagIndex < 0 || flagIndex >= 256)
                throw new ArgumentOutOfRangeException(nameof(flagIndex));

            if (flagIndex < 64)
                return (flags1 & (1UL << flagIndex)) != 0;
            else if (flagIndex < 128)
                return (flags2 & (1UL << (flagIndex - 64))) != 0;
            else if (flagIndex < 192)
                return (flags3 & (1UL << (flagIndex - 128))) != 0;
            else
                return (flags4 & (1UL << (flagIndex - 192))) != 0;
        }

        /// <summary>
        /// Checks if any of the flags are set
        /// </summary>
        /// <param name="mask"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool HasAnyFlag(IBitFlags mask)
        {
            var maskFlags = mask.Flags;
            return (flags1 & maskFlags) != 0 || (flags2 & maskFlags) != 0 || (flags3 & maskFlags) != 0 || (flags4 & maskFlags) != 0;
        }

        /// <summary>
        /// Checks if all of the flags are set
        /// </summary>
        /// <param name="mask"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool HasAllFlags(IBitFlags mask)
        {
            var maskFlag = mask.Flags;
            return (flags1 & maskFlag) == maskFlag && (flags2 & maskFlag) == maskFlag && (flags3 & maskFlag) == maskFlag && (flags4 & maskFlag) == maskFlag;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool operator ==(BitFlags256 a, IBitFlags b)
        {
            var bMask = b.Flags;
            return a.flags1 == bMask && a.flags2 == bMask && a.flags3 == bMask && a.flags4 == bMask;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool operator !=(BitFlags256 a, IBitFlags b)
        {
            var bMask = b.Flags;
            return a.flags1 != bMask || a.flags2 != bMask || a.flags3 != bMask || a.flags4 != bMask;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object? obj)
        {
            if (obj is IBitFlags other)
            {
                return this == other;
            }
            return false;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(flags1, flags2, flags3, flags4);
        }

        /// <summary>
        /// Clears all flags
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            flags1 = 0;
            flags2 = 0;
            flags3 = 0;
            flags4 = 0;
        }

        /// <summary>
        /// Checks if the flags are empty
        /// </summary>
        public readonly bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return flags1 == 0 && flags2 == 0 && flags3 == 0 && flags4 == 0;
            }
        }
    }


    public readonly struct ReadOnlyBitFlags : IBitFlags
    {
        private readonly IBitFlags _bitFlags;

        public ReadOnlyBitFlags(IBitFlags bitFlags)
        {
            _bitFlags = bitFlags ?? throw new ArgumentNullException(nameof(bitFlags));
        }

        public ulong Flags => _bitFlags.Flags;

        public bool IsEmpty => _bitFlags.IsEmpty;

        public void SetFlag(int flagIndex)
        {
            throw new InvalidOperationException("ReadOnlyBitFlags cannot modify flags.");
        }

        public void ClearFlag(int flagIndex)
        {
            throw new InvalidOperationException("ReadOnlyBitFlags cannot modify flags.");
        }

        public bool HasFlag(int flagIndex)
        {
            return _bitFlags.HasFlag(flagIndex);
        }

        public bool HasAnyFlag(IBitFlags mask)
        {
            return _bitFlags.HasAnyFlag(mask);
        }

        public bool HasAllFlags(IBitFlags mask)
        {
            return _bitFlags.HasAllFlags(mask);
        }

        public void Clear()
        {
            throw new InvalidOperationException("ReadOnlyBitFlags cannot modify flags.");
        }
    }
    #endregion BITFLAGS


    /// </summary>
    public static class SecsyConfig
    {
        private static Type _bitFlagsType = typeof(BitFlags256);
        internal static int maxCompIds = 128;

        /// <summary>
        /// Sets the <see cref="IBitFlags"/> type to be used.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IBitFlags"/> to use.</typeparam>
        public static void ConfigBitFlagsType<T>() where T : IBitFlags, new()
        {
            _bitFlagsType = typeof(T);
        }

        /// <summary>
        /// Sets the <see cref="IBitFlags"/> type to be used.
        /// </summary>
        public static void ConfigBitFlagsType(Type bitFlagsType)
        {
            if (bitFlagsType == null)
                throw new ArgumentNullException(nameof(bitFlagsType));
            if (bitFlagsType.GetInterface(nameof(IBitFlags)) == null)
                throw new ArgumentException("Type must implement IBitFlags", nameof(bitFlagsType));
            _bitFlagsType = bitFlagsType;
        }

        /// <summary>
        /// Sets the maximum amount of <see cref="ComponentId{T}"/>s that can be created.
        /// </summary>
        /// <param name="maxComponentIds"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static void ConfigMaxComponentIds(int maxComponentIds)
        {
            if (maxComponentIds < 1)
                throw new ArgumentOutOfRangeException(nameof(maxComponentIds), "Max component ids must be greater than 0");
            maxCompIds = Math.Max(128, maxComponentIds);
        }

        /// <summary>
        /// Creates and returns a new instance of the currently configured <see cref="IBitFlags"/> type.
        /// </summary>
        /// <returns>A new instance of the <see cref="IBitFlags"/> type.</returns>
        public static IBitFlags Current()
        {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8603 // Possible null reference return.
            return (IBitFlags)Activator.CreateInstance(_bitFlagsType);
#pragma warning restore CS8603 // Possible null reference return.
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
        }
    }

    #region EXCEPTIONS

    [Serializable]
    public class SecsyException : Exception
    {
        public SecsyException() { }
        public SecsyException(string message) : base(message) { }
        public SecsyException(EntityId ent) : base(ent.ToString()) { }
        public SecsyException(EntityId ent, IComponentId component) : base($"{ent} => {component}") { }

        public SecsyException(EntityId ent, IComponentId component, Exception inner) : base($"{ent} => {component}", inner) { }
        public SecsyException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Thrown when the maximum amount of <see cref="ComponentId{T}"/>s is reached
    /// </summary>
    public class MaxComponentsReachedException : SecsyException
    {
        public MaxComponentsReachedException() : base("Max components reached")
        {
        }
    }

    /// <summary>
    /// Thrown when a component with the same id is already assigned to an entity.
    /// <para>Avoid this by either checking if the entity already has the component before adding it or by creating a new component id that
    /// stores the same type of value.</para>
    /// </summary>
    public class DuplicateComponentException : SecsyException
    {

        public DuplicateComponentException()
        {
        }

        public DuplicateComponentException(IComponentId component) : base($"{component.GetType().FullName}") { }
    }

    /// <summary>
    /// Thrown when a component is not registered
    /// </summary>
    public class UnregisteredComponentException : SecsyException
    {
        public UnregisteredComponentException()
        {
        }

        public UnregisteredComponentException(IComponentId component) : base($"{component.GetType().FullName}") { }
    }

    /// <summary>
    /// Thrown when a component type conflict occurs
    /// </summary>
    public class ComponentTypeConflictException : SecsyException
    {
        public ComponentTypeConflictException()
        {
        }

        public ComponentTypeConflictException(IComponentId component, object? expected) : base($"expected value of type {component.GetType().FullName}, got {expected?.GetType()}") { }
    }

    public class BadComponentValueException : SecsyException
    {
        public BadComponentValueException()
        {
        }

        public BadComponentValueException(object value) : base($"component value cannot be {value.GetType().FullName}") { }
    }

    /// <summary>
    /// Thrown when a component is not found
    /// </summary>

    public class ComponentNotFoundException : SecsyException
    {
        public ComponentNotFoundException()
        {
        }

        public ComponentNotFoundException(EntityId ent, IComponentId component) : base(ent, component) { }
        public ComponentNotFoundException(EntityId ent, IComponentId component, Exception inner) : base(ent, component) { }
    }

    /// <summary>
    /// Thrown when an entity id is invalid
    /// </summary>
    public class InvalidEntityIdException : SecsyException
    {
        public InvalidEntityIdException() { }

        public InvalidEntityIdException(EID entId) : base(entId.ToString())
        {
        }
    }

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    #endregion EXCEPTIONS
}