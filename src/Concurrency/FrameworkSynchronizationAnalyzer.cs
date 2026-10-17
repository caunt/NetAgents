using System;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

using NetAgents.Analyzers.Diagnostics;

namespace NetAgents.Analyzers.Concurrency;

/// <summary>
/// Rejects framework synchronization primitives and blocking waits.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FrameworkSynchronizationAnalyzer() : PolicyAnalyzer(Rule, analyzesRazorCode: true)
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
        customTags: [WellKnownDiagnosticTags.NotConfigurable]
    );

    private static readonly ImmutableHashSet<string> ForbiddenTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        items: [        "Lock", "Monitor", "Semaphore", "SemaphoreSlim", "Mutex", "ReaderWriterLock", "ReaderWriterLockSlim",
        "SpinLock", "SpinWait", "AutoResetEvent", "ManualResetEvent", "ManualResetEventSlim", "EventWaitHandle",
        "WaitHandle", "CountdownEvent", "Barrier"]
    );

    private static readonly ImmutableArray<string> BlockingMembers = ["Sleep", "Result", "Wait", "WaitAll", "WaitAny", "GetResult"];

    private static readonly ImmutableArray<string> CompilerServices = ["CompilerServices", "Runtime", "System"];

    private static readonly ImmutableArray<string> Tasks = ["Tasks", "Threading", "System"];

    private static readonly ImmutableArray<string> Threading = ["Threading", "System"];

    /// <inheritdoc />
    protected override void RegisterAnalysisActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeTypeName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
        context.RegisterOperationAction(
            AnalyzeMember,
            OperationKind.Invocation,
            OperationKind.PropertyReference,
            OperationKind.MethodReference,
            OperationKind.ObjectCreation
        );
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

        // Every invocation and property reference in a project reaches this, so the names are
        // compared before any namespace is examined: each case below needs one of them to match.
        if (!Contains(BlockingMembers, member.Name) && !ForbiddenTypes.Contains(containingType.Name))
            return;

        INamespaceSymbol containingNamespace = containingType.ContainingNamespace;

        bool isThreadSleep = containingType.Name == "Thread" && member.Name == "Sleep" && IsNamespace(containingNamespace, Threading);

        bool isBlockingTask = containingType.Name is "Task" or "ValueTask"
            && member.Name is "Result" or "Wait" or "WaitAll" or "WaitAny"
            && IsNamespace(containingNamespace, Tasks);

        bool isBlockingAwaiter = member.Name == "GetResult"
            && containingType.Name.EndsWith(value: "Awaiter", StringComparison.Ordinal)
            && IsNamespace(containingNamespace, CompilerServices);

        if (IsForbiddenType(containingType) || isThreadSleep || isBlockingTask || isBlockingAwaiter)
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation()));
    }

    private static void AnalyzeTypeName(SyntaxNodeAnalysisContext context)
    {
        SimpleNameSyntax name = (SimpleNameSyntax)context.Node;

        // Binding every identifier in a project to ask about sixteen type names is the expensive way
        // round, and the written name has to match one of them before the symbol is worth resolving.
        if (!ForbiddenTypes.Contains(name.Identifier.ValueText))
            return;

        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;

        if (IsForbiddenType(symbol as INamedTypeSymbol))
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
    }

    private static bool Contains(ImmutableArray<string> names, string name)
    {
        foreach (string candidate in names)
        {
            if (string.Equals(candidate, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsForbiddenType(INamedTypeSymbol? type)
    {
        return type is not null && ForbiddenTypes.Contains(type.Name) && IsNamespace(type.ContainingNamespace, Threading);
    }

    // Comparing the segments outward costs nothing, where rendering the namespace to a string
    // allocates one for every member this analyzer looks at.
    private static bool IsNamespace(INamespaceSymbol? symbol, ImmutableArray<string> segments)
    {
        foreach (string segment in segments)
        {
            if (symbol is null || !string.Equals(symbol.Name, segment, StringComparison.Ordinal))
                return false;

            symbol = symbol.ContainingNamespace;
        }

        return symbol is not null && symbol.IsGlobalNamespace;
    }
}
