using BenchmarkDotNet.Running;
using Derail.Benchmarks;

if (args is ["--stack-probe", var shape])
{
    ReaderStackProbe.Run(shape);
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
