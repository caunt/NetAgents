using NetAgents.Analyzers.Ordering;
using NetAgents.Analyzers.Tests.Infrastructure;

namespace NetAgents.Analyzers.Tests.Ordering;

/// <summary>
/// Covers the order members take by mutability and initialization, and the blank lines placed between their groups.
/// </summary>
public sealed class MemberGroupingTests
{
    /// <summary>
    /// Verifies organized members are accepted, including a type written on one line, whose members share a line and
    /// keep it, members kept below the comment they were written under even when their names sort earlier, a
    /// commented member staying first in its section, and initializers left in written order because one of them
    /// reads another member, by name or through a method of the type.
    /// </summary>
    /// <param name="source">A type whose members are already in order and grouped.</param>
    [Theory]
    [InlineData("public sealed class ExampleType { private int _first; private int _second; public void Run() { } }")]
    [InlineData("public sealed class ExampleType\n{\n    private readonly int _first;\n    private readonly int _second;\n\n    private int _count;\n\n    public void Run() { }\n}\n")]
    [InlineData("public sealed class ExampleType\n{\n    private int _zebra;\n\n    // Totals kept apart from the counter above.\n    private int _alpha;\n    private int _beta;\n}\n")]
    [InlineData("public sealed class ExampleType\n{\n    // Totals, headed by the grand one.\n    private int _zeta;\n    private int _alpha;\n}\n")]
    [InlineData("public sealed class ExampleType\n{\n    private static readonly int Zebra = 1;\n    private static readonly int Alpha = Zebra + 1;\n}\n")]
    [InlineData("public sealed class ExampleType\n{\n    private readonly int _zebra = Next();\n    private readonly int _alpha = Next();\n    private readonly int _middle = 3;\n\n"
        + "    private static int Next() => 1;\n}\n")]
    public async Task AcceptsOrganizedMembers(string source)
    {
        Assert.Empty(await AnalyzerTestHarness.Analyze(new MemberOrderingAnalyzer(), source));
    }

    /// <summary>
    /// Verifies the class from SupercellProxy that the earlier fix left interleaved: members fixed at construction
    /// sort before settable ones, initializers that only read constructor parameters move freely, a single
    /// initializer that runs code has nothing to keep its order with, and blank lines sit only between groups.
    /// </summary>
    [Fact]
    public async Task GroupsFixedStateBeforeSettableState()
    {
        const string source = """
            public sealed class Encryption(int with, byte[] localPrivateKey, byte[] sessionKey, byte[] remotePublicKey)
            {
                public int With { get; init; } = with;

                public byte[] LocalPrivateKey { get; init; } = localPrivateKey;

                public int? ReceiveNonce { get; set; }
                public byte[] LocalPublicKey { get; init; } = System.Array.Empty<byte>().Concat(localPrivateKey).ToArray();
                public int? SendNonce { get; set; }
                public byte[] RemotePublicKey { get; set; } = remotePublicKey;
                public byte[] SharedKey { get; set; } = [];

                public byte[] SessionKey { get; init; } = sessionKey;
            }
            """;

        const string expected = """
            public sealed class Encryption(int with, byte[] localPrivateKey, byte[] sessionKey, byte[] remotePublicKey)
            {
                public byte[] LocalPrivateKey { get; init; } = localPrivateKey;
                public byte[] LocalPublicKey { get; init; } = System.Array.Empty<byte>().Concat(localPrivateKey).ToArray();
                public byte[] SessionKey { get; init; } = sessionKey;
                public int With { get; init; } = with;

                public int? ReceiveNonce { get; set; }
                public byte[] RemotePublicKey { get; set; } = remotePublicKey;
                public int? SendNonce { get; set; }
                public byte[] SharedKey { get; set; } = [];
            }
            """;

        string fixedSource = await CodeFixTestHarness.FixMemberOrdering("using System.Linq;\n\n" + source, fixAll: true);

        Assert.Equal("using System.Linq;\n\n" + expected, fixedSource);
    }

    /// <summary>
    /// Verifies initializers whose order is observable keep it, two that run code and the fields and properties of a
    /// type that serializers read, while the members around them still sort.
    /// </summary>
    /// <param name="members">The members as written.</param>
    /// <param name="expected">The members after the fix.</param>
    [Theory]
    [InlineData("    private readonly System.Guid _zebra = System.Guid.NewGuid();\n    private readonly System.Guid _alpha = System.Guid.NewGuid();\n    private readonly System.Guid _middle = default;",
        "    private readonly System.Guid _middle = default;\n    private readonly System.Guid _zebra = System.Guid.NewGuid();\n    private readonly System.Guid _alpha = System.Guid.NewGuid();")]
    [InlineData("    [System.Text.Json.Serialization.JsonPropertyName(\"zebra\")]\n    public int Zebra { get; set; }\n    public int Alpha { get; set; }\n    public void Run() { }",
        "    [System.Text.Json.Serialization.JsonPropertyName(\"zebra\")]\n    public int Zebra { get; set; }\n\n    public int Alpha { get; set; }\n\n    public void Run() { }")]
    public async Task KeepsObservableOrder(string members, string expected)
    {
        string source = $"public sealed class ExampleType\n{{\n{members}\n}}\n";

        Assert.Equal($"public sealed class ExampleType\n{{\n{expected}\n}}\n", await CodeFixTestHarness.FixMemberOrdering(source, fixAll: true));
    }

    /// <summary>
    /// Verifies blank lines follow groups: none between single-line members of one bucket, one between buckets, one
    /// above a comment line, and one around a member with documentation, attributes, or more than one line, while the
    /// space after the opening brace is left to NETAGENTS0023. Constants group by type, so a number never sits between
    /// two messages, while other fields keep name order whatever their types.
    /// </summary>
    /// <param name="members">The members as written, already in order.</param>
    /// <param name="expected">The members after the fix.</param>
    [Theory]
    [InlineData("    private readonly int _first;\n\n\n    private readonly int _second;\n    private int _count;",
        "    private readonly int _first;\n    private readonly int _second;\n\n    private int _count;")]
    [InlineData("    private int _first;\n    // Counted separately from the first.\n\n    private int _second;",
        "    private int _first;\n\n    // Counted separately from the first.\n    private int _second;")]
    [InlineData("    private int _first;\n    /// <summary>The second value.</summary>\n    private int _second;\n    private int _third;",
        "    private int _first;\n\n    /// <summary>The second value.</summary>\n    private int _second;\n\n    private int _third;")]
    [InlineData("    private int _first;\n    private int _second =\n        1 +\n        2;\n    private int _third;",
        "    private int _first;\n\n    private int _second =\n        1 +\n        2;\n\n    private int _third;")]
    [InlineData("    private int _first;\n    private int _second;\n    private void Run() { }\n    private void Stop() { }",
        "    private int _first;\n    private int _second;\n\n    private void Run() { }\n\n    private void Stop() { }")]
    [InlineData("    private const string EncryptionNotSetMessage = \"a\";\n    private const int PromonPadSize = 508;\n    private const string ReceiveNonceNotSetMessage = \"b\";",
        "    private const int PromonPadSize = 508;\n\n    private const string EncryptionNotSetMessage = \"a\";\n    private const string ReceiveNonceNotSetMessage = \"b\";")]
    [InlineData("    private const uint Mask = 1;\n    private const int Limit = 2;\n    private const uint Call = 3;",
        "    private const int Limit = 2;\n\n    private const uint Call = 3;\n    private const uint Mask = 1;")]
    [InlineData("    private readonly string _beta = \"b\";\n    private readonly int _alpha = 1;",
        "    private readonly int _alpha = 1;\n    private readonly string _beta = \"b\";")]
    public async Task SeparatesGroupsWithOneBlankLine(string members, string expected)
    {
        string source = $"public sealed class ExampleType\n{{\n{members}\n}}\n";

        Assert.Equal($"public sealed class ExampleType\n{{\n{expected}\n}}\n", await CodeFixTestHarness.FixMemberOrdering(source, fixAll: true));
    }
}
