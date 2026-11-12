using Microsoft.CodeAnalysis;

namespace NetAgents.Analyzers.TypeSafety;

// What a narrowing compound assignment needs to be written as a plain assignment: the type its result is
// narrowed to, the factory that keeps the narrowing's behavior, whether its value is widened for the
// operator, and whether the target can be evaluated a second time.
internal sealed class CompoundNarrowing(ITypeSymbol target, string factory, bool widensValue, bool rewritable)
{
    public string Factory { get; } = factory;

    public bool Rewritable { get; } = rewritable;

    public ITypeSymbol Target { get; } = target;

    public bool WidensValue { get; } = widensValue;
}
