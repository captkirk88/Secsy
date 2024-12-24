using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ECS.Testing
{
    [MemoryDiagnoser]
    public class Benchmarks
    {
        Secsy secsy = new();

        [GlobalSetup]
        public void Init()
        {
            secsy.NewEntities(100_000, Components.TestComp1, Components.TestComp2, Components.TestComp3);
        }

        [BenchmarkDotNet.Attributes.IterationSetup]
        public void PerSetup()
        {

        }


        [Benchmark]
        public void NewComponentId()
        {
            Secsy.NewComponentId<TestComp1>();
        }

        [Benchmark]
        public void CreateEntityWithOneComponent()
        {
            secsy.NewEntity(Components.TestComp1);
        }


        [Benchmark]
        public void CreateEntityWithTwoComponent()
        {
            secsy.NewEntity(Components.TestComp1, Components.TestComp2);
        }


        [Benchmark]
        public void CreateEntityWithThreeComponent()
        {
            secsy.NewEntity(Components.TestComp1, Components.TestComp2, Components.TestComp3);
        }

        [Benchmark]
        public void CreateEntityWithFourComponent()
        {
            secsy.NewEntity(Components.TestComp1, Components.TestComp2, Components.TestComp3, Components.TestComp4);
        }

        [Benchmark]
        public void CreateEntityWithFiveComponent()
        {
            secsy.NewEntity(Components.TestComp1, Components.TestComp2, Components.TestComp3, Components.TestComp4, Components.TestComp5);
        }

        [Benchmark]
        public void CreateEntityWithSixComponent()
        {
            secsy.NewEntity(Components.TestComp1, Components.TestComp2, Components.TestComp3, Components.TestComp4, Components.TestComp5, Components.TestComp6);
        }

        [Benchmark]
        public void SystemWithOneComponent()
        {
            var e = secsy.Filter(new Filter().With(Components.TestComp1, Components.TestComp2));
            while (e.MoveNext())
            {
                ref var ent = ref secsy.Get(e.Current);
            }
            Console.WriteLine($"{secsy.Count}");
        }

        [Benchmark]
        public void SystemWithTwoComponents()
        {
            var e = secsy.Filter(new Filter().With(Components.TestComp1, Components.TestComp2));
            while (e.MoveNext())
            {
                ref var ent = ref secsy.Get(e.Current);
                var comp1 = Components.TestComp1.Get(ent);
                comp1.Value = 2;
                Components.TestComp1.SetValue(ent, comp1);
            }
        }

        [Benchmark]
        public void SystemWithThreeComponents()
        {
            var e = secsy.Filter(new Filter().With(Components.TestComp1, Components.TestComp2, Components.TestComp3));
            while (e.MoveNext())
            {
                ref var ent = ref secsy.Get(e.Current);
                var comp1 = Components.TestComp1.Get(ent);
                comp1.Value = 2;
                Components.TestComp1.SetValue(ent, comp1);
            }
        }

        [Benchmark]
        public void SystemTwoComponentsMultipleComposition()
        {
            int amount = secsy.Each(new Filter().With(Components.TestComp1, Components.TestComp2).Without(Components.TestComp3, Components.TestComp4, Components.TestComp5), eachEnt);

            static void eachEnt(ref EntityId ent)
            {
                var comp1 = Components.TestComp1.Get(ent);
                comp1.Value = 2;
                Components.TestComp1.SetValue(ent, comp1);
                Components.TestComp2.Remove(ref ent);
            }
        }

    }
}
