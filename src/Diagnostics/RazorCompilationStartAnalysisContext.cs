using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.Analyzers.Diagnostics;

// The compilation-scoped counterpart of RazorAnalysisContext, for a rule that prepares state once per
// compilation before registering the callbacks that use it.
[SuppressMessage(
    "MicrosoftCodeAnalysisPerformance",
    "RS1012",
    Justification = "The start context is held to forward the registrations a rule makes on this wrapper, which registers nothing itself."
)]
internal sealed class RazorCompilationStartAnalysisContext(CompilationStartAnalysisContext context)
    : CompilationStartAnalysisContext(context.Compilation, context.Options, context.CancellationToken)
{
    public override void RegisterCodeBlockAction(Action<CodeBlockAnalysisContext> action)
    {
        throw new NotSupportedException(RazorAnalysisContext.Unconfined);
    }

    public override void RegisterCodeBlockStartAction<TLanguageKindEnum>(Action<CodeBlockStartAnalysisContext<TLanguageKindEnum>> action)
    {
        throw new NotSupportedException(RazorAnalysisContext.Unconfined);
    }

    public override void RegisterCompilationEndAction(Action<CompilationAnalysisContext> action)
    {
        throw new NotSupportedException(RazorAnalysisContext.Unconfined);
    }

    public override void RegisterOperationAction(Action<OperationAnalysisContext> action, ImmutableArray<OperationKind> operationKinds)
    {
        context.RegisterOperationAction(RazorSource.Confine(action), operationKinds);
    }

    public override void RegisterSemanticModelAction(Action<SemanticModelAnalysisContext> action)
    {
        throw new NotSupportedException(RazorAnalysisContext.Unconfined);
    }

    public override void RegisterSymbolAction(Action<SymbolAnalysisContext> action, ImmutableArray<SymbolKind> symbolKinds)
    {
        throw new NotSupportedException(RazorAnalysisContext.Unconfined);
    }

    public override void RegisterSyntaxNodeAction<TLanguageKindEnum>(Action<SyntaxNodeAnalysisContext> action, ImmutableArray<TLanguageKindEnum> syntaxKinds)
    {
        context.RegisterSyntaxNodeAction(RazorSource.Confine(action), syntaxKinds);
    }

    public override void RegisterSyntaxTreeAction(Action<SyntaxTreeAnalysisContext> action)
    {
        throw new NotSupportedException(RazorAnalysisContext.Unconfined);
    }
}
