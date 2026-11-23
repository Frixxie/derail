using BenchmarkDotNet.Attributes;

namespace Derail.Benchmarks;

[MemoryDiagnoser]
public class UnionConstructionBenchmarks
{
    private int _number = 42;
    private string _text = "sample";

    // Store to fields so the constructed wrapper escapes the timed method.
    private Option<int> _optionInt;
    private Option<string> _optionText;
    private Result<int, int> _resultInt;
    private Result<string, string> _resultText;
    private int? _nullableInt;
    private string? _nullableText;

    [Benchmark] public void NullableIntPresent() => _nullableInt = _number;
    [Benchmark] public void NullableIntAbsent() => _nullableInt = null;
    [Benchmark] public void NullableTextPresent() => _nullableText = _text;
    [Benchmark] public void NullableTextAbsent() => _nullableText = null;
    [Benchmark] public void SomeInt() => _optionInt = Option.Some(_number);
    [Benchmark] public void NoneInt() => _optionInt = Option.None;
    [Benchmark] public void SomeText() => _optionText = Option.Some(_text);
    [Benchmark] public void NoneText() => _optionText = Option.None;
    [Benchmark] public void OkInt() => _resultInt = Result.Ok<int, int>(_number);
    [Benchmark] public void ErrorInt() => _resultInt = Result.Error<int, int>(_number);
    [Benchmark] public void OkText() => _resultText = Result.Ok<string, string>(_text);
    [Benchmark] public void ErrorText() => _resultText = Result.Error<string, string>(_text);

    [GlobalSetup]
    public void Verify()
    {
        NullableIntPresent(); NullableTextPresent(); SomeInt(); SomeText(); OkInt(); OkText();
        if (_optionInt is not Some<int>(42) || _optionText is not Some<string>("sample") ||
            _resultInt is not Ok<int>(42) || _resultText is not Ok<string>("sample") || _nullableInt != 42 || _nullableText != "sample")
            throw new InvalidOperationException("Incorrect present case");
        NullableIntAbsent(); NullableTextAbsent(); NoneInt(); NoneText(); ErrorInt(); ErrorText();
        if (!_optionInt.IsNone || !_optionText.IsNone || _resultInt is not Error<int>(42) ||
            _resultText is not Error<string>("sample") || _nullableInt.HasValue || _nullableText is not null)
            throw new InvalidOperationException("Incorrect absent or error case");
    }
}
