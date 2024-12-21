﻿using System.Diagnostics;
using System.Security.Cryptography;
using Shouldly;

namespace Secsy.Testing
{
    [TestClass]
    public class Test1
    {
        static Secsy secsy = new();

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            // This method is called once for the test class, before any tests of the class are run.
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            // This method is called once for the test class, after all tests of the class are run.
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
        }

        [TestMethod]
        public void NewEntityMany()
        {
            Generate(100_000);
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
        }

        [TestMethod]
        public void ComponentAdd()
        {
            ref var ent = ref secsy.NewEntity(Components.TestComp1, Components.TestComp2);
            secsy.AddComponent(ent.ID, Components.TestComp2);
        }


        [TestMethod]
        public void IterateFilter()
        {
            int amount = 500;
            Generate(amount);
            var enumerator = secsy.Filter(new Filter().With(Components.TestComp1));
            while (enumerator.MoveNext())
            {
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
            int amount = 10000;
            var ents = Generate(amount);
            ents.Length.ShouldBe(amount);
            var includeResult = secsy.Filter(new Filter().With(Components.TestComp1));

            var excludeResult = secsy.Filter(new Filter().Without(Components.TestComp1, Components.TestComp2));

            var includeExcludeResult = secsy.Filter(new Filter().With(Components.TestComp1, Components.TestComp2).Without(Components.TestComp3));

            var allResult = secsy.Filter(new Filter().With(Components.TestComp1, Components.TestComp2, Components.TestComp3));

        }

        [TestMethod]
        public void EachOp()
        {
            int amount = 10000;
            var ents = Generate(amount);

            void eachEnt(ref EntityId ent)
            {

            }
        }

        private EntityId[] Generate(int amount)
        {
            return secsy.NewEntities(amount, Components.TestComp1, Components.TestComp2, Components.TestComp3);
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