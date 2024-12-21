using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis.Diagnostics;
using Secsy.Benchmark;

namespace Secsy.Testing
{
    [MemoryDiagnoser]
    public class Benchmarks
    {
        Secsy secsy = new();


        [BenchmarkDotNet.Attributes.IterationSetup]
        public void PerSetup()
        {

        }

        [BenchmarkDotNet.Attributes.IterationCleanup]
        public void PerCleanup()
        {
            secsy.Clear();
        }

        [BenchmarkDotNet.Attributes.GlobalCleanup]
        public void Cleanup()
        {
            secsy.Clear();
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
        public void SystemWithOneComponent()
        {
            secsy.Each(new Filter().With(Components.TestComp1, Components.TestComp2), eachEnt);

            void eachEnt(ref EntityId ent)
            {

            }
        }

        [Benchmark]
        public void SystemWithTwoComponents()
        {
            secsy.Each(new Filter().With(Components.TestComp1, Components.TestComp2), eachEnt);

            void eachEnt(ref EntityId ent)
            {
                var comp1 = Components.TestComp1.Get(ent);
                comp1.Value = 2;
                Components.TestComp1.SetValue(ent, comp1);
            }
        }

        [Benchmark]
        public void SystemWithThreeComponents()
        {
            secsy.Each(new Filter().With(Components.TestComp1, Components.TestComp2, Components.TestComp3), eachEnt);

            void eachEnt(ref EntityId ent)
            {
                var comp1 = Components.TestComp1.Get(ent);
                comp1.Value = 2;
                Components.TestComp1.SetValue(ent, comp1);
            }
        }

        [Benchmark]
        public void SystemTwoComponentsMultipleComposition()
        {
            secsy.Each(new Filter().With(Components.TestComp1, Components.TestComp2), eachEnt);

            void eachEnt(ref EntityId ent)
            {
                var comp1 = Components.TestComp1.Get(ent);
                comp1.Value = 2;
                Components.TestComp1.SetValue(ent, comp1);
                Components.TestComp2.Remove(ref ent);
            }
        }

    }
}
