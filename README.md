# Secsy

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
ComponentId<T> Set(ref EntityId ent)
ComponentId<T> SetValue(EntityId ent, T newValue)
bool Has(ref EntityId ent)
ComponentId<T> Remove(ref EntityId ent)
```


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
void SetComponentValue<T>(long entId, ComponentId<T> compId, T newValue)
T? GetComponentValue<T>(long entId, ComponentId<T> compId)
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



## BENCHMARK!
| Method                                 | Mean [μs] | Error [μs] | StdDev [μs] | Allocated [KB] |
|--------------------------------------- |----------:|-----------:|------------:|---------------:|
| NewComponentId                         |  3.871 μs |  0.5966 μs |   1.7403 μs |        1.38 KB |
| CreateEntityWithOneComponent           |  2.304 μs |  0.2177 μs |   0.6175 μs |        0.57 KB |
| CreateEntityWithTwoComponent           |  2.881 μs |  0.2456 μs |   0.6887 μs |        0.63 KB |
| CreateEntityWithThreeComponent         |  2.784 μs |  0.2320 μs |   0.6428 μs |        0.68 KB |
| SystemWithOneComponent                 |  8.616 μs |  1.0690 μs |   3.1519 μs |        0.92 KB |
| SystemWithTwoComponents                |  4.669 μs |  0.4839 μs |   1.3166 μs |        0.92 KB |
| SystemWithThreeComponents              |  4.354 μs |  0.5213 μs |   1.4704 μs |        0.95 KB |
| SystemTwoComponentsMultipleComposition |  9.227 μs |  1.0958 μs |   3.1965 μs |        0.92 KB |
