using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetAgents.Analyzers.Documentation;

// A documented declaration CS1591 covers, with the symbol it declares and its comment's elements.
internal sealed class DocumentedMember(SyntaxNode declaration, ISymbol symbol, List<XmlNodeSyntax> elements)
{
    public SyntaxNode Declaration { get; } = declaration;

    public List<XmlNodeSyntax> Elements { get; } = elements;

    public ISymbol Symbol { get; } = symbol;
}
