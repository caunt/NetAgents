using System;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NetAgents.Analyzers.Diagnostics;

// Hands a rule that checks Razor code its syntax node and operation callbacks, run in generated code only
// on what a Razor file's author wrote. A callback over a whole tree, block, symbol, or compilation would
// report on code Razor writes itself, so such a rule cannot register one.
internal sealed class RazorAnalysisContext(AnalysisContext context) : AnalysisContext
{
    internal const string Unconfined = "A rule that checks Razor code registers syntax node and operation actions only, "
        + "because no other callback can be kept to the code a Razor file's author wrote.";

    public override void RegisterCodeBlockAction(Action<CodeBlockAnalysisContext> action)
    {
        throw new NotSupportedException(Unconfined);
    }

    public override void RegisterCodeBlockStartAction<TLanguageKindEnum>(Action<CodeBlockStartAnalysisContext<TLanguageKindEnum>> action)
    {
        throw new NotSupportedException(Unconfined);
    }

    public override void RegisterCompilationAction(Action<CompilationAnalysisContext> action)
    {
        throw new NotSupportedException(Unconfined);
    }

    public override void RegisterCompilationStartAction(Action<CompilationStartAnalysisContext> action)
    {
        context.RegisterCompilationStartAction(start => action(new RazorCompilationStartAnalysisContext(start)));
    }

    public override void RegisterOperationAction(Action<OperationAnalysisContext> action, ImmutableArray<OperationKind> operationKinds)
    {
        context.RegisterOperationAction(RazorSource.Confine(action), operationKinds);
    }

    public override void RegisterSemanticModelAction(Action<SemanticModelAnalysisContext> action)
    {
        throw new NotSupportedException(Unconfined);
    }

    public override void RegisterSymbolAction(Action<SymbolAnalysisContext> action, ImmutableArray<SymbolKind> symbolKinds)
    {
        throw new NotSupportedException(Unconfined);
    }

    public override void RegisterSyntaxNodeAction<TLanguageKindEnum>(Action<SyntaxNodeAnalysisContext> action, ImmutableArray<TLanguageKindEnum> syntaxKinds)
    {
        context.RegisterSyntaxNodeAction(RazorSource.Confine(action), syntaxKinds);
    }

    public override void RegisterSyntaxTreeAction(Action<SyntaxTreeAnalysisContext> action)
    {
        throw new NotSupportedException(Unconfined);
    }
}
