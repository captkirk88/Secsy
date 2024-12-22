# Secsy

[![NuGet version (Secsy)](https://img.shields.io/nuget/v/SoftCircuits.Silk.svg?style=flat-square)](https://www.nuget.org/packages/Secsy/)

It's so sexy that you'll never see sexy the same again.  Oh and all done in a single file called Secsy.cs.. so sexy.

See BenchmarkDotNet results at the bottom.

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

### Why?
I branched out to other programming languages to see how they do it.  Rust, Java, Google Go.  One in Google Go intrigued me.

- [donburi](https://github.com/yohamta/donburi) by @yohamta

Donburi's approach to Component types was purposeful for the way Google Go is.  But, I thought, wouldn't this work in C# too?  Well, it does.  Not in the same exact way.  Still sexy.

#

## First Steps -> Components
Components must be defined in a static class wrapping them for easy access. A ComponentId type wraps the component type into a struct that allows you to access the Entity component data from anywhere in your code.
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
Secsy class (or think of it as "World" which other ECS frameworks which never made sense to me, is it a World/Planet/another dimension? Someone made sense calling it a EntityStore.  Good job.)
```csharp
Secsy secsy = new Secsy(); // That is it.
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
ref EntityId Get(long id) 

// standard component operations
void AddComponent<T>(long entId, ComponentId<T> compId)
void SetComponentValue(long entId, IComponentId compId, object newValue)
T? GetComponentValue<T>(long entId, ComponentId<T> compId)
object? GetComponentValue(long entId, IComponentId compId)
bool RemoveComponent<T>(long entId, ComponentId<T> compId)

// note: if your component uses a default value, it's ignored.  TODO!
IComponentId[] GetComponents(long entId) 

// bad name, actually it makes the entity inactive by clearing the component ids
// resuses that entity id for a new entity
void Remove(int entId) 

// Filter entities and perform operations on each
void Each(Filter filter, EachEntity fn) 

// same but for enumeration
IEnumerator<long> Filter(Filter filter) 

// clears everything except registered ComponentIds
void Clear() 
```

#

### Example
```csharp
public static class C // I would keep the static class name short so you can type it out easy
{
  public static ComponentId<string> MyTag = Secsy.NewComponentId<string>(); // Tags can be whatever type your want, you'll see why.
  public static ComponentId<float> X = Secsy.NewComponentId<float>(); // 
  public static ComponentId<float> Y = Secsy.NewComponentId<float>();
  public static ComponentId<MyDataStruct> Data = Secsy.NewComponentId<MyDataStruct>();
  public static ComponentId<MyDataStruct> MoreData = Secsy.NewComponentId<MyDataStruct>(); // Perfectly valid and sexy
}

var ent = secsy.NewEntity(C.MyData,C.MoreData,C.MyTag,C.X,C.Y);
C.X.SetValue(ent, 0.2f);
C.MyData.SetValue(ent,new MyDataStruct{Value=123});
long entId = secsy.Get(ent.ID)
secsy.RemoveComponent(entId,C.MoreData);

secsy.Each(new Filter().With(C.X, eachEnt);

void eachEnt(ref EntityId ent){
  C.X.SetValue(ent,C.X.Get(ent)+0.2f);
}
```

#
#

## BENCHMARK! 100,000 Entities
| Method     | Mean [μs]   | Error [μs] | StdDev [μs]  | Median [μs] | Allocated [KB] |
| -------------------------------------- | -----------:| ----------:| ------------:| -----------:| --------------:|
| NewComponentId                         | 13.22 μs    | 0.660 μs   | 1.863 μs     | 13.15 μs    | 0.7 KB         |
| CreateEntityWithOneComponent           | 15.01 μs    | 0.916 μs   | 2.508 μs     | 14.30 μs    | 0.85 KB        |
| CreateEntityWithTwoComponent           | 15.38 μs    | 0.718 μs   | 2.015 μs     | 15.00 μs    | 0.91 KB        |
| CreateEntityWithThreeComponent         | 15.53 μs    | 0.703 μs   | 1.923 μs     | 15.05 μs    | 0.96 KB        |
| SystemWithOneComponent                 | 2,337.17 μs | 143.222 μs | 379.806 μs   | 2,446.20 μs | 3473.98 KB     |
| SystemWithTwoComponents                | 9,173.90 μs | 328.073 μs | 936.012 μs   | 8,866.45 μs | 5305.96 KB     |
| SystemWithThreeComponents              | 9,399.42 μs | 394.294 μs | 1,143.920 μs | 9,138.35 μs | 5946.06 KB     |
| SystemTwoComponentsMultipleComposition | 727.30 μs   | 40.669 μs  | 116.030 μs   | 712.70 μs   | 4.25 KB        |
> [!NOTE]
> 1000μs = 0.001ms