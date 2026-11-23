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
        "Initialize '{0}' with Option.Some(value) or Option.None",
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

                ctx.ReportDiagnostic(Diagnostic.Create(Rule, operation.Syntax.GetLocation(), operation.Type));
            }, OperationKind.DefaultValue, OperationKind.ObjectCreation);
        });
    }
}
