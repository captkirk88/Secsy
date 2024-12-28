using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace ECS.Testing
{
    [BenchmarkCategory("Systems")]
    [MemoryDiagnoser]
    [MinIterationCount(3)]
    [MaxIterationCount(7)]
    [WarmupCount(5)]
    public class SystemsBenchmark
    {
        private Secsy secsy = new();
        [GlobalSetup]
        public void Init()
        {
            secsy.NewEntities(100_000, Components.TestComp1, Components.TestComp2, Components.TestComp3);
        }

        [Params(100_000)]
        public int EntityCount { get; set; }


        [Benchmark]
        public void SystemWithOneComponent()
        {
            var e = secsy.Filter(new Filter().With(Components.TestComp1));
            while (e.MoveNext())
            {
                ref var ent = ref secsy.Get(e.Current);
                var val = Components.TestComp1.Get(ent);
                val.Value++;
                Components.TestComp1.SetValue(ent, val);
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
                var val = Components.TestComp1.Get(ent);
                val.Value++;
                Components.TestComp1.SetValue(ent, val);
                var val2 = Components.TestComp2.Get(ent);
                val2.Value++;
                Components.TestComp2.SetValue(ent, val2);
            }
        }

        [Benchmark]
        public void SystemWithThreeComponents()
        {
            var e = secsy.Filter(new Filter().With(Components.TestComp1, Components.TestComp2, Components.TestComp3));
            while (e.MoveNext())
            {
                ref var ent = ref secsy.Get(e.Current);
                var val = Components.TestComp1.Get(ent);
                val.Value++;
                Components.TestComp1.SetValue(ent, val);
                var val2 = Components.TestComp2.Get(ent);
                val2.Value++;
                Components.TestComp2.SetValue(ent, val2);
                var val3 = Components.TestComp2.Get(ent);
                val3.Value++;
                Components.TestComp2.SetValue(ent, val3);
            }
        }

        [Benchmark]
        public void SystemTwoComponentsMultipleComposition()
        {
            int amount = secsy.Each(new Filter().With(Components.TestComp1, Components.TestComp2).Without(Components.TestComp3, Components.TestComp4, Components.TestComp5), eachEnt);

            void eachEnt(ref EntityId ent)
            {
                var val = Components.TestComp1.Get(ent);
                val.Value++;
                Components.TestComp1.SetValue(ent, val);
                var val2 = Components.TestComp2.Get(ent);
                val2.Value++;
                Components.TestComp2.SetValue(ent, val2);
                var val3 = Components.TestComp2.Get(ent);
                val3.Value++;
                Components.TestComp2.SetValue(ent, val3);
            }
        }
    }
}
