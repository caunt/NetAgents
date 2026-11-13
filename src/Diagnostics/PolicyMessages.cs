namespace NetAgents.Analyzers.Diagnostics;

/// <summary>
/// Wording shared by the messages of rules an author has to satisfy by hand.
/// </summary>
internal static class PolicyMessages
{
    // The compiler ignores a pragma naming a policy rule, and NETAGENTS0043 rejects suppression attributes. Saying so up
    // front keeps an agent from spending its next attempt on a suppression instead of on the code.
    public const string ChangeTheCode = " Change the code itself; a pragma or suppression attribute for this rule fails the build.";
}
