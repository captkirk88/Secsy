using System.Diagnostics;
using System.Security.Cryptography;
using Shouldly;
using System.Linq;
using System.Diagnostics.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ECS.Testing
{
    [TestClass]
    public class Test1
    {
        static Secsy secsy = new();

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {

        }

        [ClassCleanup]
        public static void ClassCleanup()
        {

        }

        [TestInitialize]
        public void TestInit()
        {
            GenerateDefault(1);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            Console.WriteLine($"{secsy.Count}/{secsy.Capacity}");
            secsy.Clear();
        }


        [TestMethod]
        public void NewComponentId()
        {
            Secsy.NewComponentId<TestComp1>();
            Secsy.NewComponentId<TestComp2>();
            Secsy.NewComponentId<TestComp3>();
            Secsy.NewComponentId<TestComp4>();
            Secsy.NewComponentId<TestComp5>();
            Secsy.NewComponentId<TestComp6>();
            Secsy.NewComponentId<TestComp7>();
            Secsy.NewComponentId<TestComp8>();
            Secsy.NewComponentId<TestComp9>();
        }

        [TestMethod]
        public void NewEntity()
        {
            ref var ent = ref secsy.NewEntity(Components.TestComp1, Components.TestComp2, Components.TestComp3);
            ent.Has(Components.TestComp2).ShouldBeTrue();
            ent.IsAlive.ShouldBeTrue();
            ent.Remove();
            ent.IsAlive.ShouldBeFalse();
            //GenerateDefault(100);

            secsy.NewEntity(Components.TestComp1Copy);
        }


        [TestMethod]
        public void NewEntity_100_000()
        {
            var count = 100_000;
            secsy.Clear();
            GenerateDefault(count);
            secsy.Count.ShouldBe(count);

        }

        [TestMethod]
        public void NewEntity_500_000()
        {
            var count = 500_000;
            secsy.Clear();
            GenerateDefault(count);
            secsy.Count.ShouldBe(count);
        }

        [TestMethod]
        public void NewEntity_OneMillion()
        {
            var count = 1_000_000;
            secsy.Clear();
            GenerateDefault(count);
            secsy.Count.ShouldBe(count);
        }

        [TestMethod]
        public void ComponentDups()
        {
            Assert.AreNotEqual(Components.TestComp1, Components.TestComp1Copy);
            Assert.AreNotEqual(Components.TestComp2, Components.TestComp2Copy);
            Assert.AreNotEqual(Components.TestComp3, Components.TestComp3Copy);
        }

        [TestMethod]
        public void ComponentGet()
        {
            var strComp = Secsy.NewComponentId<string>("");
            ref var ent = ref secsy.NewEntity(Components.TestComp1, Components.TestComp2, strComp);
            var comp2 = Components.TestComp2.SetValue(ent, new TestComp2 { Value = 3 });
            var comp2Val = comp2.Get(ent);
            comp2Val.Value.ShouldBe(3);
            comp2.SetValue(ent, comp2Val);
            strComp.SetValue(ent, "Hello World!!!!").Get(ent).ShouldBe("Hello World!!!!");
            strComp.SetValue(ent, "Goodbye!").Get(ent).ShouldBe("Goodbye!");

            var comps = secsy.GetComponents(ent.ID);
            secsy.GetComponentValue(ent.ID, comps[0]).ShouldNotBeNull();
        }

        [TestMethod]
        public void ComponentAdd()
        {
            secsy.Clear();
            ref var ent = ref secsy.NewEntity(Components.TestComp1);
            var id = ent.ID;
            secsy.AddComponent(id, Components.TestComp2);
            Components.TestComp3.Has(ref ent).ShouldBeFalse();
            secsy.AddComponent(id, Components.TestComp3);
            Assert.ThrowsException<DuplicateComponentException>(() => secsy.AddComponent(id, Components.TestComp3));
            Components.TestComp3.Has(ref ent).ShouldBeTrue();
        }

        [TestMethod]
        public void ComponentRemove()
        {
            ref var ent = ref secsy.NewEntity(Components.TestComp1, Components.TestComp2);
            var id = ent.ID;
            secsy.RemoveComponent(id, Components.TestComp3).ShouldBeTrue();
            secsy.RemoveComponent(id, Components.TestComp2).ShouldBeTrue();
            Components.TestComp2.Has(ref ent).ShouldBeFalse();
        }


        [TestMethod]
        public void IterateFilter()
        {
            secsy.Clear();
            int amount = 5000;
            GenerateDefault(amount);
            secsy.Count.ShouldBe(amount);
            var enumerator = secsy.Filter(new Filter().With(Components.TestComp1));
            Stopwatch timeout = Stopwatch.StartNew();
            while (enumerator.MoveNext())
            {
                if (timeout.Elapsed > TimeSpan.FromSeconds(50)) break;
                ref var ent = ref secsy.Get(enumerator.Current);
                Components.TestComp1.Remove(ref ent).Has(ref ent).ShouldBeFalse();
            }
        }

        // TODO TEST other component operations

        [TestMethod]
        public void Filters()
        {
            ref var ent = ref secsy.NewEntity(Components.TestComp1, Components.TestComp2);
            ent.Has(Components.TestComp2).ShouldBeTrue();
            new Filter().Without(Components.TestComp2).Apply(ent).ShouldBeFalse();
            new Filter().With(Components.TestComp2).Apply(ent).ShouldBeTrue();
            new Filter().With(Components.TestComp1).Without(Components.TestComp2).Apply(ent).ShouldBeFalse();
        }

        [TestMethod]
        public void EachOp()
        {
            secsy.Clear();
            int amount = 100000;
            GenerateDefault(amount);
            //Generate(amount, Components.TestComp1, Components.TestComp3);

            var filter = new Filter().With(Components.TestComp1, Components.TestComp2);//.Without(Components.TestComp3);

            int simCount = 0;
            int count = secsy.Each(filter, (ref EntityId ent) =>
            {
                simCount++;
            });

            simCount.ShouldBe(count);
            Console.WriteLine($"{simCount}");
        }

        [TestMethod]
        public void SystemTwoComponentsMultipleComposition()
        {
            secsy.Clear();
            GenerateDefault(100_000);
            int amount = secsy.Each(new Filter().With(Components.TestComp1, Components.TestComp2).Without(Components.TestComp3, Components.TestComp4, Components.TestComp5, Components.TestComp6), eachEnt);
            Console.WriteLine($"{amount}");
            void eachEnt(ref EntityId ent)
            {
                var comp1 = Components.TestComp1.Get(ent);
                comp1.Value++;
                Components.TestComp1.SetValue(ent, comp1);
                Components.TestComp2.Remove(ref ent);
            }
        }

        private void Generate(int amount, params IComponentId[] comps)
        {
            secsy.NewEntities(amount, comps);
        }

        private void GenerateDefault(int amount)
        {
            secsy.NewEntities(amount, Components.TestComp1, Components.TestComp2);
        }
    }


    public static class Components
    {
        public static ComponentId<TestComp1> TestComp1 = Secsy.NewComponentId<TestComp1>();
        public static ComponentId<TestComp2> TestComp2 = Secsy.NewComponentId<TestComp2>();
        public static ComponentId<TestComp3> TestComp3 = Secsy.NewComponentId<TestComp3>();
        public static ComponentId<TestComp4> TestComp4 = Secsy.NewComponentId<TestComp4>();
        public static ComponentId<TestComp5> TestComp5 = Secsy.NewComponentId<TestComp5>();
        public static ComponentId<TestComp6> TestComp6 = Secsy.NewComponentId<TestComp6>();
        public static ComponentId<TestComp7> TestComp7 = Secsy.NewComponentId<TestComp7>();

        public static ComponentId<TestComp1> TestComp1Copy = Secsy.NewComponentId<TestComp1>();
        public static ComponentId<TestComp2> TestComp2Copy = Secsy.NewComponentId<TestComp2>();
        public static ComponentId<TestComp3> TestComp3Copy = Secsy.NewComponentId<TestComp3>();
    }

    public struct TestComp1
    {
        public int Value;
    }
    public struct TestComp2
    {
        public int Value;
    }
    public struct TestComp3
    {
        public int Value;
    }

    public struct TestComp4
    {
        public int Value;
    }
    public struct TestComp5
    {
        public int Value;
    }
    public struct TestComp6
    {
        public int Value;
    }
    public struct TestComp7
    {
        public int Value;
    }
    public struct TestComp8
    {
        public int Value;
    }
    public struct TestComp9
    {
        public int Value;
    }
}
