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

        [TestCleanup]
        public void TestCleanup()
        {
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
            var ent = secsy.NewEntity(Components.TestComp1, Components.TestComp2, Components.TestComp3);
            ent.Has(Components.TestComp2).ShouldBeTrue();
            ent.IsAlive.ShouldBeTrue();
            ent.Remove();
            ent.IsAlive.ShouldBeFalse();
            //GenerateDefault(100);

            secsy.NewEntity(Components.TestComp1Copy);
        }

        [TestMethod]
        public void NewEntity100_000()
        {
            secsy.Clear();
            var ents = GenerateDefault(100_000);
            ents.Length.ShouldBe(100_000);

            Console.WriteLine($"{secsy.Count}/{secsy.Capacity}");
        }

        [TestMethod]
        public void NewEntity500_000()
        {
            secsy.Clear();
            var ents = GenerateDefault(500_000);
            ents.Length.ShouldBe(500_000);
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
            Components.TestComp2.SetValue(ent, new TestComp2 { Value = 3 }).Get(ent).Value.ShouldBe(3);
            strComp.SetValue(ent, "Hello World!!!!").Get(ent).ShouldBe("Hello World!!!!");
            strComp.SetValue(ent, "Goodbye!").Get(ent).ShouldBe("Goodbye!");

            var comps = secsy.GetComponents(ent.ID);
            secsy.GetComponentValue(ent.ID, comps[0]).ShouldNotBeNull();
        }

        [TestMethod]
        public void ComponentAdd()
        {
            ref var ent = ref secsy.NewEntity(Components.TestComp1, Components.TestComp2);
            var id = ent.ID;
            Assert.ThrowsException<DuplicateComponentException>(() => secsy.AddComponent(id, Components.TestComp2));
        }


        [TestMethod]
        public void IterateFilter()
        {
            int amount = 5000;
            var ents = GenerateDefault(amount);
            ents.Length.ShouldBe(amount);
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
        }

        [TestMethod]
        public void FiltersMany()
        {
            secsy.Clear();
            secsy.Count.ShouldBe(0);
            int amount = 10000;
            var groupA = GenerateDefault(amount);
            var groupB = Generate(amount, Components.TestComp1, Components.TestComp3);
            Console.WriteLine($"{secsy.Count}/{secsy.Capacity}");
            secsy.Count.ShouldBe(amount * 2);

            var filterA = new Filter().With(Components.TestComp1, Components.TestComp2).Without(Components.TestComp3);
            var filterB = new Filter().With(Components.TestComp3);
            groupA.Where(filterA.Apply).Count().ShouldBe(amount);
            groupA.Where(filterA.Apply).Count().ShouldBe(amount);
            groupB.Where(filterA.Apply).Count().ShouldBe(0);
            groupB.Where(filterB.Apply).Count().ShouldBe(groupB.Length);

            var filter = new Filter().With(Components.TestComp1, Components.TestComp2);//.Without(Components.TestComp3);
            filter.ApplyMany(groupA).ShouldBeTrue();
            var excFilter = new Filter().Without(Components.TestComp1);
            excFilter.ApplyMany(groupA).ShouldBeFalse();
            var bothFilter = new Filter().With(Components.TestComp1).Without(Components.TestComp5);
            bothFilter.ApplyMany(groupA).ShouldBeFalse();

        }

        [TestMethod]
        public void EachOp()
        {
            secsy.Clear();
            int amount = 10000;
            var ents = GenerateDefault(amount);
            //Generate(amount, Components.TestComp1, Components.TestComp3);

            var filter = new Filter().With(Components.TestComp1, Components.TestComp2);//.Without(Components.TestComp3);
            filter.ApplyMany(ents).ShouldBeTrue();

            int simCount = 0;
            int count = secsy.Each(filter, (ref EntityId ent) =>
            {
                simCount++;
            });

            simCount.ShouldBe(count);
            Console.WriteLine($"{simCount}");
        }

        private EntityId[] Generate(int amount, params IComponentId[] comps)
        {
            return secsy.NewEntities(amount, comps);
        }

        private EntityId[] GenerateDefault(int amount)
        {
            return secsy.NewEntities(amount, Components.TestComp1, Components.TestComp2);
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
