using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Derail.Analyzers.Tests;

public class OptionInitializationAnalyzerTests
{
    // [| and |] mark the spans that must report DRL001.
    [Theory]
    [InlineData("static Option<int> TypedDefault() => [|default(Option<int>)|];")]
    [InlineData("static Option<int> TargetTypedDefault() => [|default|];")]
    [InlineData("static Option<int> Parameterless() => [|new Option<int>()|];")]
    [InlineData("static Option<int> TargetTypedNew() => [|new()|];")]
    [InlineData("static Option<string>[] Several() => [[|default|], Option.None, [|new()|]];")]
    public async Task Explicitly_uninitialized_options_are_errors(string member)
    {
        var (source, expected) = Unmark(member);
        var diagnostics = await Diagnose(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "DRL001"));
        Assert.Equal(expected, diagnostics.Where(d => d.Id == "DRL001").Select(d => d.Location.SourceSpan));
        Assert.All(diagnostics.Where(d => d.Id == "DRL001"), d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("default(Option<int>)")]
    [InlineData("new Option<int>()")]
    public async Task Optional_parameter_diagnostic_recommends_an_overload(string initializer)
    {
        var (source, expected) = Unmark($"static void M(Option<int> value = [|{initializer}|]) {{ }}");
        var diagnostic = Assert.Single((await Diagnose(source)).Where(d => d.Severity == DiagnosticSeverity.Error));

        Assert.Equal("DRL001", diagnostic.Id);
        Assert.Equal(Assert.Single(expected), diagnostic.Location.SourceSpan);
        Assert.Contains("overload that passes Option.None", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("static void M() => M(Option.None); static void M(Option<int> value) { }")]
    [InlineData("static Option<int>[] Cases() => [Option.Some(42), Option.None, new(new Some<int>(1)), new(new None())];")]
    [InlineData("static Option<int>? NoNullableValue() => default;")]
    [InlineData("static Option<int>[] ArrayStorage() => new Option<int>[2];")]
    [InlineData("static Other.Option<int> Unrelated() => default;")]
    public async Task Initialized_options_and_other_types_compile_cleanly(string member)
    {
        var diagnostics = await Diagnose(member);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error || d.Id == "DRL001"));
    }

    private static (string Member, TextSpan[] Spans) Unmark(string marked)
    {
        var spans = new List<TextSpan>();
        var member = marked;
        for (var start = member.IndexOf("[|", StringComparison.Ordinal); start >= 0; start = member.IndexOf("[|", StringComparison.Ordinal))
        {
            var end = member.IndexOf("|]", start, StringComparison.Ordinal);
            spans.Add(TextSpan.FromBounds(Prefix.Length + start, Prefix.Length + end - 2));
            member = string.Concat(member.AsSpan(0, start), member.AsSpan(start + 2, end - start - 2), member.AsSpan(end + 2));
        }
        return (member, [.. spans]);
    }

    private const string Prefix = "using Derail;\nstatic class Options\n{\n    ";
    private const string Suffix = "\n}\nnamespace Other { readonly struct Option<T>; }\n";

    private static async Task<ImmutableArray<Diagnostic>> Diagnose(string member)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "Consumer",
            [CSharpSyntaxTree.ParseText(Prefix + member + Suffix, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return await compilation
            .WithAnalyzers([new OptionInitializationAnalyzer()])
            .GetAllDiagnosticsAsync(TestContext.Current.CancellationToken);
    }
}
