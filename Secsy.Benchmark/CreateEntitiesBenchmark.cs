using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using SecsyECS = ECS.Secsy;

namespace ECS.Testing
{
    [MemoryDiagnoser]
    public class CreateEntitiesBenchmark
    {
        SecsyECS secsy = new();

        [Params(10_000, 100_000, 500_000)]
        public int Size { get; set; }

        [Benchmark(Baseline = true)]
        public void Create()
        {
            secsy.NewEntities(Size, Components.TestComp1, Components.TestComp2, Components.TestComp3, Components.TestComp4, Components.TestComp5, Components.TestComp6);
        }
    }
}
