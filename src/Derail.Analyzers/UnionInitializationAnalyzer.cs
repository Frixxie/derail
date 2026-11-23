using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Derail.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnionInitializationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        "DRL001", "Initialize a Derail union with a case",
        "'{0}' is uninitialized. {1}.",
        "Correctness", DiagnosticSeverity.Error, isEnabledByDefault: true);

    // Metadata name, how to initialize a value, and what an overload should pass instead of a parameter default.
    private static readonly (string MetadataName, string Cases, string OverloadArgument)[] Unions =
    [
        ("Derail.Option`1", "Option.Some(value) or Option.None", "Option.None"),
        ("Derail.Result`2", "Result.Ok(value) or Result.Error(error)", "an initialized result"),
        ("Derail.Validation`2", "Validation.Valid(value) or Validation.Invalid(error)", "an initialized validation"),
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var guidance = Unions
                .Select(union => (Type: start.Compilation.GetTypeByMetadataName(union.MetadataName), union.Cases, union.OverloadArgument))
                .Where(union => union.Type?.ContainingAssembly.Name == "Derail")
                .ToImmutableDictionary(
                    union => (INamedTypeSymbol)union.Type!,
                    union => (union.Cases, union.OverloadArgument),
                    SymbolEqualityComparer.Default);
            if (guidance.IsEmpty) return;

            start.RegisterOperationAction(ctx =>
            {
                var operation = ctx.Operation;
                if (operation.IsImplicit || operation.Type?.OriginalDefinition is not INamedTypeSymbol type
                    || !guidance.TryGetValue(type, out var union)) return;
                if (operation is IObjectCreationOperation { Arguments.Length: > 0 }) return;

                var parent = operation.Parent;
                while (parent is IConversionOperation) parent = parent.Parent;
                var message = parent is IParameterInitializerOperation
                    ? $"Remove the parameter default and add an overload that passes {union.OverloadArgument}"
                    : $"Initialize it with {union.Cases}";
                ctx.ReportDiagnostic(Diagnostic.Create(Rule, operation.Syntax.GetLocation(), operation.Type, message));
            }, OperationKind.DefaultValue, OperationKind.ObjectCreation);
        });
    }
}
