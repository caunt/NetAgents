using System;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Concurrency;

/// <summary>
/// Rejects framework synchronization primitives and blocking waits.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FrameworkSynchronizationAnalyzer() : PolicyAnalyzer(Rule)
{
    /// <summary>
    /// Identifies the diagnostic emitted by this analyzer.
    /// </summary>
    public const string RuleIdentifier = "NETAGENTS0012";

    private static readonly DiagnosticDescriptor Rule = new(
        RuleIdentifier,
        title: "Framework synchronization and blocking waits are forbidden",
        messageFormat: "Prefer immutable state, atomics, concurrent collections, channels, or Nito.AsyncEx; await asynchronous work",
        category: "Concurrency",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    private static readonly ImmutableHashSet<string> ForbiddenTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        items: [        "Lock", "Monitor", "Semaphore", "SemaphoreSlim", "Mutex", "ReaderWriterLock", "ReaderWriterLockSlim",
        "SpinLock", "SpinWait", "AutoResetEvent", "ManualResetEvent", "ManualResetEventSlim", "EventWaitHandle",
        "WaitHandle", "CountdownEvent", "Barrier"]);

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeTypeName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
        context.RegisterOperationAction(AnalyzeMember,
            OperationKind.Invocation, OperationKind.PropertyReference, OperationKind.MethodReference,
            OperationKind.ObjectCreation);
    }

    private static bool IsForbiddenType(INamedTypeSymbol? type)
    {
        return type is not null
            && type.ContainingNamespace.ToDisplayString() == "System.Threading"
            && ForbiddenTypes.Contains(type.Name);
    }

    private static void AnalyzeTypeName(SyntaxNodeAnalysisContext context)
    {
        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;

        if (IsForbiddenType(symbol as INamedTypeSymbol))
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
    }

    private static void AnalyzeMember(OperationAnalysisContext context)
    {
        ISymbol? member = context.Operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IPropertyReferenceOperation property => property.Property,
            IMethodReferenceOperation method => method.Method,
            IObjectCreationOperation creation => creation.Constructor,
            _ => null,
        };

        INamedTypeSymbol? containingType = member?.ContainingType;

        if (member is null || containingType is null)
            return;

        string containingNamespace = containingType.ContainingNamespace.ToDisplayString();

        bool isThreadSleep = containingNamespace == "System.Threading"
            && containingType.Name == "Thread" && member.Name == "Sleep";

        bool isBlockingTask = containingNamespace == "System.Threading.Tasks"
            && containingType.Name is "Task" or "ValueTask"
            && member.Name is "Result" or "Wait" or "WaitAll" or "WaitAny";

        bool isBlockingAwaiter = containingNamespace == "System.Runtime.CompilerServices"
            && containingType.Name.EndsWith(value: "Awaiter", StringComparison.Ordinal) && member.Name == "GetResult";

        if (IsForbiddenType(containingType) || isThreadSleep || isBlockingTask || isBlockingAwaiter)
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation()));
    }
}
