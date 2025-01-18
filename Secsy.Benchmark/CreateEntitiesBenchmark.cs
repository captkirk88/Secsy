using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using SecsyECS = ECS.Secsy;

namespace ECS.Testing
{
    [BenchmarkCategory("CreateEntities")]
    [MemoryDiagnoser]
    [MinIterationCount(3)]
    [MaxIterationCount(7)]
    [WarmupCount(5)]
    public class CreateEntitiesBenchmark
    {
        SecsyECS secsy = new();

        [Params(100_000)]
        public int EntityCount { get; set; }

        [Benchmark]
        public void CreateEntitiesWithSixComponents()
        {
            secsy.NewEntities(EntityCount, Components.TestComp1, Components.TestComp2, Components.TestComp3, Components.TestComp4, Components.TestComp5, Components.TestComp6);
        }
    }
}
