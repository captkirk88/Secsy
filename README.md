# Secsy

[![NuGet version (Secsy)](https://img.shields.io/nuget/v/SoftCircuits.Silk.svg?style=flat-square)](https://www.nuget.org/packages/Secsy/)

It's so sexy that you'll consider C# as the fastest programming language out there (joke).  Oh and all done in a single file called Secsy.cs.. so sexy.

See BenchmarkDotNet results at the bottom.  Jokes aside, with component access, I believe it outperforms competitors.

> [!NOTE]
> This ECS requires you to do things a little differently, which is why it is so fast.

> [!NOTE]
> Feedback and correcting me is sexy too.


After reviewing a lot of ECS libraries out there.  To name a few..
- [Arch](https://github.com/genaray/Arch)
- [DefaultEcs](https://github.com/Doraku/DefaultEcs)
- [Friflo.Engine.ECS](https://github.com/friflo/Friflo.Json.Fliox/blob/main/Engine/README.md)
- [MonoGame.Extended](https://github.com/craftworkgames/MonoGame.Extended)

So ugly it hurts my programming attraction..
- [Entitas](https://github.com/sschmid/Entitas)

They all seemed less sexy.

#

## Why?
I branched out to other programming languages to see how they do it in their respective environments.  Rust, Java, Google Go.  One in Google Go intrigued me.

- [donburi](https://github.com/yohamta/donburi) by @yohamta

Donburi's approach to Component types was purposeful for the way Google Go is and very very fast.  But, I thought, wouldn't this work in C# too?  Well, it does.  Not in the same exact way.  Still sexy.

#

## First Steps -> Components
Components must be defined in a static class wrapping them for easy access. A ComponentId type wraps the component type into a struct that allows you to access the Entity component data from anywhere in your code.
> [!NOTE]
> Max components is determined by what BitFlags instance you use, BitFlags256 being 256 components per entity.
```csharp
public static class C // I would keep the static class name short so you can type it out easy
{
  public static ComponentId<string> MyTag = Secsy.NewComponentId<string>(); // Tags can be whatever type your want, you'll see why.
  public static ComponentId<float> X = Secsy.NewComponentId<float>(); // 
  public static ComponentId<float> Y = Secsy.NewComponentId<float>();
  public static ComponentId<MyDataStruct> Data = Secsy.NewComponentId<MyDataStruct>();
  public static ComponentId<MyDataStruct> MoreData = Secsy.NewComponentId<MyDataStruct>(); // Perfectly valid and sexy
}
```
The ComponentId type, the most important type, has multiple methods which can be called anywhere in your code.
```csharp
T? Get(EntityId ent)
ComponentId<T> Add(ref EntityId ent)
ComponentId<T> SetValue(EntityId ent, T newValue)
bool Has(ref EntityId ent)
ComponentId<T> Remove(ref EntityId ent)
```
#

## Initialize the Sexy
Secsy class (or think of it as "World", other ECS frameworks use that to define a "World".. which never made sense to me, is it a World/Planet/another dimension? Someone made sense calling it a EntityStore.  Good job.)
```csharp
ECS.Secsy secsy = new ECS.Secsy(); // That is it.
```
> [!NOTE]
> All ComponentIds are shared between instances of Secsy.
Secsy has the following methods.
```csharp
// as noted before in this readme
static ComponentId<T> NewComponentId<T>(T? defaultValue = default)

// use your static C fields for this. Example below.
ref EntityId NewEntity(params IComponentId[] comps) 

// if you need to make a bunch of entities
EntityId[] NewEntities(int amount, params IComponentId[] comps) 

// get the EntityId from raw id
ref EntityId Get(ulong id) 

// standard component operations
void AddComponent<T>(ulong entId, ComponentId<T> compId)
void SetComponentValue(ulong entId, IComponentId compId, object newValue)
T? GetComponentValue<T>(ulong entId, ComponentId<T> compId)
object? GetComponentValue(ulong entId, IComponentId compId)
bool RemoveComponent<T>(ulong entId, ComponentId<T> compId)

// note: if your component uses a default value, it's ignored.  TODO!
IComponentId[] GetComponents(ulong entId) 

// bad name, actually it makes the entity inactive by clearing the component ids
// resuses that entity id for a new entity
void Remove(ulong entId) 

// Filter entities and perform operations on each
void Each(Filter filter, EachEntity fn) 

// same but for enumeration
IEnumerator<ulong> Filter(Filter filter) 

// clears everything except registered ComponentIds
void Clear() 
```

#

### Example
```csharp
public static class C // I would keep the static class name short so you can type it out easy
{
  public static ComponentId<string> MyTag = Secsy.NewComponentId<string>("Helloooooo World!"); // Tags can be whatever type your want, you'll see why.
  public static ComponentId<float> X = Secsy.NewComponentId<float>(132f); 
  public static ComponentId<float> Y = Secsy.NewComponentId<float>(); // Default of float is 0.0f
  public static ComponentId<MyDataStruct> Data = Secsy.NewComponentId<MyDataStruct>();
  public static ComponentId<MyDataStruct> MoreData = Secsy.NewComponentId<MyDataStruct>(); // Perfectly valid and sexy
}

var ent = secsy.NewEntity(C.MyData,C.MoreData,C.MyTag,C.X,C.Y);

C.X.SetValue(ent, 0.2f);

C.MyData.SetValue(ent,new MyDataStruct{Value=123});

ref EntityId entId = ref secsy.Get(ent.ID)
bool isRemovedOrDidnotExist = secsy.RemoveComponent(entId,C.MoreData);

int amountOfEntitiesVisited = secsy.Each(new Filter().With(C.X), eachEnt);

void eachEnt(ref EntityId ent){
  C.X.SetValue(ent,C.X.Get(ent)+0.2f);
}

// or use Filter method
IEnumerator<long> enumerator = secsy.Filter(new Filter().With(C.X));
while (enumerator.MoveNext()){
	ref var ent = secsy.Get(enumerator.Current);
}
```

#
#

## BENCHMARK 100,000 ENTITIES!

| Method                        | EntityCount | Mean [ms] | Error [ms] | StdDev [ms] | Gen0       | Gen1       | Gen2     | Allocated [KB] |
|------------------------------ |------------ |----------:|-----------:|------------:|-----------:|-----------:|---------:|---------------:|
| CreateEntitiesWith6Components | 100000      |  572.6 ms |   419.3 ms |    149.5 ms | 20500.0000 | 10500.0000 | 500.0000 |   210163.99 KB |

| Method                                 | EntityCount | Mean [ms]  | Error [ms] | StdDev [ms] | Gen0      | Gen1     | Allocated [KB] |
| -------------------------------------- | ----------- | ----------:| ----------:| -----------:| ---------:| --------:| --------------:|
| SystemWithOneComponent                 | 100000      | 65.589 ms  | 5.8163 ms  | 2.5825 ms   | 1000.0000 | 888.8889 | 16270.68 KB    |
| SystemWithTwoComponents                | 100000      | 100.764 ms | 25.5404 ms | 11.3401 ms  | 1666.6667 | 666.6667 | 12306.5 KB     |
| SystemWithThreeComponents              | 100000      | 116.403 ms | 16.8400 ms | 6.0053 ms   | 2500.0000 | 750.0000 | 17103.09 KB    |
| SystemTwoComponentsMultipleComposition | 100000      | 1.234 ms   | 0.2037 ms  | 0.0905 ms   | 11.7188   | -        | 26.58 KB       |

> [!NOTE]
> 1000μs = 0.001ms
> 
> Memory increases with more components.  This is expected.  The more components you have, the more memory you will use.  This is a trade off for speed.  If you need to save memory, you can use a different ECS library.  This is for speed.

> [!NOTE]
> Note to self: Abandon C# and use Zig + Google Go.  C# is feature rich but in the programming world, all programming languages can become feature rich with enough work.