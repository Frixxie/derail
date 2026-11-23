using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Derail.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OptionInitializationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        "DRL001", "Initialize Option with a case",
        "'{0}' is uninitialized. {1}.",
        "Correctness", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var option = start.Compilation.GetTypeByMetadataName("Derail.Option`1");
            if (option?.ContainingAssembly.Name != "Derail") return;

            start.RegisterOperationAction(ctx =>
            {
                var operation = ctx.Operation;
                if (operation.IsImplicit || !SymbolEqualityComparer.Default.Equals(operation.Type?.OriginalDefinition, option)) return;
                if (operation is IObjectCreationOperation { Arguments.Length: > 0 }) return;

                var parent = operation.Parent;
                while (parent is IConversionOperation) parent = parent.Parent;
                var guidance = parent is IParameterInitializerOperation
                    ? "Remove the parameter default and add an overload that passes Option.None"
                    : "Initialize it with Option.Some(value) or Option.None";
                ctx.ReportDiagnostic(Diagnostic.Create(Rule, operation.Syntax.GetLocation(), operation.Type, guidance));
            }, OperationKind.DefaultValue, OperationKind.ObjectCreation);
        });
    }
}
