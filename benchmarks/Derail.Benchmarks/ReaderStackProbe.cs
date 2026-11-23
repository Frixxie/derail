using System.Runtime.CompilerServices;

namespace Derail.Benchmarks;

internal static class ReaderStackProbe
{
    // Sample the CLR's stack reserve at the innermost delegate, before unwinding the chain.
    // This is a separate CLI mode, never part of xUnit or a BenchmarkDotNet workload.
    public static void Run(string shape)
    {
        const int stackBytes = 1024 * 1024;
        var thread = new Thread(() =>
        {
            var reader = new Reader<int, bool>(_ => RuntimeHelpers.TryEnsureSufficientExecutionStack());
            var result = new ReaderResult<int, bool, int>(_ => Result.Ok<bool, int>(RuntimeHelpers.TryEnsureSufficientExecutionStack()));
            for (var depth = 1; depth <= 16_384; depth++)
            {
                switch (shape)
                {
                    case "map": reader = reader.Map(static x => x); break;
                    case "bind": reader = reader.Bind(Reader.Pure<int, bool>); break;
                    case "result-map": result = result.Map(static x => x); break;
                    case "result-bind": result = result.Bind(ReaderResult.Pure<int, bool, int>); break;
                    default: throw new ArgumentException("Expected map, bind, result-map or result-bind.");
                }
                if (depth % 64 != 0) continue;
                var enough = shape is "map" or "bind" ? reader.Run(0) : result.Run(0).GetValueOrDefault(false);
                if (!enough)
                {
                    Console.WriteLine($"{shape}: first low-reserve sample at {depth} steps (requested stack: {stackBytes} B)");
                    return;
                }
            }
            Console.WriteLine($"{shape}: reserve still available at the probe cap of 16384 steps");
        }, stackBytes);
        thread.Start();
        thread.Join();
    }
}
