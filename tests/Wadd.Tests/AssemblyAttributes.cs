using System.Runtime.CompilerServices;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

internal static class TestAssemblyInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SQLitePCL.Batteries_V2.Init();
    }
}
