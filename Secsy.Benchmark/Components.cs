using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ECS.Testing
{

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
