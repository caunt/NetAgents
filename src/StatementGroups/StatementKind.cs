namespace NetAgents.Analyzers.StatementGroups;

/// <summary>
/// Names the group a statement joins when blank lines split a statement list.
/// </summary>
internal enum StatementKind
{
    /// <summary>
    /// A statement that stands alone: control flow, a jump, a <c>using</c> declaration, a declaration spanning
    /// lines, or any statement no group below covers.
    /// </summary>
    Alone,

    /// <summary>
    /// A local declaration written on one line.
    /// </summary>
    Declaration,

    /// <summary>
    /// A local declaration written on one line whose variables take the results of awaits, as in
    /// <c>string text = await reader.ReadToEndAsync();</c>.
    /// </summary>
    AwaitedDeclaration,

    /// <summary>
    /// An assignment, a compound assignment, or an increment or decrement.
    /// </summary>
    Assignment,

    /// <summary>
    /// An assignment or compound assignment that stores the result of an await, as in
    /// <c>text = await reader.ReadToEndAsync();</c>.
    /// </summary>
    AwaitedAssignment,

    /// <summary>
    /// A <c>+=</c> or <c>-=</c> that adds or removes an event handler.
    /// </summary>
    EventSubscription,

    /// <summary>
    /// A call or object creation written as a statement and not awaited.
    /// </summary>
    Call,

    /// <summary>
    /// A statement that awaits a task or other awaitable.
    /// </summary>
    Await,

    /// <summary>
    /// A static <c>ThrowIf</c> call on <see cref="System.ArgumentException"/> or a type deriving from it, such as
    /// <c>ArgumentNullException.ThrowIfNull(value)</c>.
    /// </summary>
    ArgumentCheck,
}
