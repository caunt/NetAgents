# Engineering rules

The package enforces shared C# style, build settings, and engineering rules.

## Diagnostics

Every custom diagnostic is an error and is marked non-configurable.

| Diagnostic | Meaning |
| --- | --- |
| NETAGENTS0001 | Reject implicit and explicit boxing conversions, including nullable values, enums, interface conversions, and unconstrained generics. |
| NETAGENTS0002 | Reject explicit `object`/`dynamic` type uses, untyped arrays/generic collections, and untyped values returned by calls, fields, properties, indexing, and await. |
| NETAGENTS0003 | Reject C# `lock` statements. |
| NETAGENTS0004 | Reject catch blocks containing no executable statements, including comment-only blocks. |
| NETAGENTS0005 | Reject single-letter declarations, uppercase acronyms, and known abbreviated identifier words. Standard `I…` and `T…` prefixes are supported. |
| NETAGENTS0006 | Require one top-level type per file with the matching case-sensitive file name. Appropriate nested types are allowed. |
| NETAGENTS0007 | Reject the postfix null-forgiving operator. |
| NETAGENTS0008 | Reject literal-true `while`/`do` conditions and conditionless or literal-true `for` loops. |
| NETAGENTS0009 | Require parameter names for inline literal/default arguments in methods, constructors, delegates, and indexers, including signed/parenthesized literals. For expanded `params`, pass a named collection. Attributes use separate language syntax. |
| NETAGENTS0010 | Require authored source files to contain fewer than 1,000 lines. |
| NETAGENTS0011 | Reject type names matching containing namespace segments or feature directories below the project root. |
| NETAGENTS0012 | Reject framework synchronization types and blocking task/thread waits, including aliases, static imports, and explicit awaiter `GetResult` calls. |
| NETAGENTS0013 | Reject changes to the effective shared configuration and diagnostic severities. |
| NETAGENTS0014 | Reject incompatible project settings. This diagnostic comes from the package's MSBuild target. |
| NETAGENTS0015 | Require return values to be consumed. Reject ignored non-void calls, ignored awaited results, and assignments to `_`, including named underscore symbols. Reject deconstruction discards, including `foreach` and `await foreach`. This includes conditional calls, expression-bodied members, callbacks, and `for` clauses. |
| NETAGENTS0016 | Require blank lines before and after control-flow statements and multiline local declarations when another statement is adjacent. Includes an automatic code fix and Fix all support. |
| NETAGENTS0017 | Omit optional braces around one single-line body statement; require braces for multiline bodies and keep conditional chains consistent. Includes an automatic code fix and Fix all support. |
| NETAGENTS0018 | Allow at most 16 authored C# files directly in each directory per project. Organize larger directories into subdirectories with narrower responsibilities. |
| NETAGENTS0019 | Require single-line conditions of at most 128 characters in `if`/`else if`, `while`, `do`, `for`, ternary expressions, catch filters, and switch `when` guards. Extract longer conditions into a separate variable declaration. |

Condition length counts source characters, including spaces and comments, between condition parentheses (excluding the parentheses). For `for`, only the condition between semicolons is checked; for ternaries, only the condition expression is checked (`?` and `:` branches may span multiple lines); for `when` guards, the text after `when` through the condition is checked. Newlines immediately inside condition parentheses are also forbidden. Extraction is manual: preserve evaluation frequency and short-circuit behavior, especially in loops.

Directory limits count each distinct `.cs` file included in the project once,
including linked source files in their physical directories. Generated files
are excluded, and each subdirectory is counted separately. The seventeenth
authored file causes a build error that names the directory and its file count.
Split oversized directories by feature or responsibility to resolve it.

Statement spacing covers `if`, loops, `switch`, `try`, `using` (including
declarations), `return`, `throw`, `break`, `continue`, and `yield`. Multiline
local declarations include chained calls, switch expressions, conditional
expressions, and object initializers. Single-line declarations can remain
grouped. Block edges stay unpadded: no blank line is added after `{` or before
`}`, and unbraced bodies stay attached to their controlling statement.
Comments and existing line endings are preserved. Use the editor's
**Insert blank line between statements** quick fix or run:

```bash
dotnet format analyzers --diagnostics NETAGENTS0016
```

Brace style depends on the **body**, even when a loop header spans several
lines. Conditions must independently satisfy `NETAGENTS0019`. This covers `if`/`else`, `for`, `foreach` (including
`await foreach`), `while`, `do`, `using` (including `await using`), `fixed`, and
`case`/`default` bodies. A case with multiple statements also requires a block.
If any branch in an `if`/`else if`/`else` chain needs braces, every branch keeps
or gains them. Required language blocks, scope-dependent blocks, directives,
and braces protecting an `else` binding are preserved. Case wrapping preserves
variables and local functions shared with other sections.

Use **Fix body braces** or run the following to add missing braces and remove
unnecessary ones:

```bash
dotnet format analyzers --diagnostics NETAGENTS0017
```

Check, return, pass, or store and use every returned value. For example, use
`if (values.TryGetValue(key, out var value))` to handle success and failure;
calling `TryGetValue` alone or assigning its result to `_` fails the build.
Storing a result without reading it also fails the build (`IDE0059`).
Every `_ = expression` is rejected, including constants, properties, object
creation, and awaited results. Naming a variable, parameter, field, or property
`_` does not bypass the assignment rule. Underscore variable initializers and
compound assignments are rejected too. Consume all deconstructed values,
including those bound by `foreach` and `await foreach`.
Calls returning `void` and awaited operations with no result (`Task` or
`ValueTask`) are allowed. Awaited `Task<T>` and `ValueTask<T>` results must be
consumed. Fluent and assertion APIs follow the same rule. An `out _` argument
does not discard a method's return value and remains allowed when that return
value is consumed.

Framework synchronization bans cover `Lock`, `Monitor`, `Semaphore`,
`SemaphoreSlim`, `Mutex`, reader/writer locks, spin locks/waits, reset events,
wait handles, `CountdownEvent`, and `Barrier`. `Interlocked`, `Volatile`,
cancellation, concurrent collections, and channels remain available.

The naming vocabulary includes `args`, `cfg`, `ctx`, `config`, `dto`, `dsp`,
`http`, `id`, `json`, `msg`, `sdr`, `sql`, `tmp`, `url`, and other common shortened
words; see `Naming/DescriptiveNameAnalyzer.cs` for the complete list.
Framework-owned overrides and explicit interface implementations are exempt
from renaming their prescribed member names. Referencing existing framework
names does not require renaming framework APIs.

## Shared editor configuration

The shared C# preferences come from Microsoft's built-in
`dotnet new editorconfig` defaults for the SDK used to build each package
release. Updating the package picks up refreshed defaults. They apply globally
and automatically, with style and formatting violations treated as errors.
Every source file must end with a newline.
The template's generic type-parameter naming selector is corrected to
`type_parameter` so the conventional `T` prefix applies to type parameters.

Consumer editor and project settings must agree with the shared policy.
All .NET and code style analysis is enabled at the latest SDK level, with
warnings treated as errors during builds. XML documentation is generated;
public APIs require XML comments (`CS1591`), and unused imports and assignments
are errors (`IDE0005`, `IDE0059`). Disabling required analysis, documentation,
or severity settings fails the build with `NETAGENTS0014`.
Global analyzer configuration applies to C# compiler inputs; it does not
control XML, JSON, or Markdown editor formatting.

## Architectural review

Compiler analysis does not decide whether a service boundary is appropriate,
a nested type is justified, or a directory has a coherent responsibility.
Consumers must also follow these engineering requirements:

- Design services around replaceable abstractions and register them with
  standard `IServiceCollection` extension methods.
- Use full descriptive words even when an abbreviation is outside the analyzer
  vocabulary; language analysis cannot prove that every name is meaningful.
- Keep directories narrowly owned by a feature or subsystem.
- Prefer immutable, concurrent, atomic, partitioned, or message-passing designs.
- Handle exceptions meaningfully; syntactically nonempty catches still need
  review.

The analyzers inspect authored C# source. Generated code and implementation
inside referenced assemblies are excluded. Conversion analysis detects boxing
represented in Roslyn operations; it is not an allocation profiler or an
inspection of every lowered intermediate-language instruction. Framework
signatures are not rewritten. Do not pass value types to an untyped framework
API even if a particular implicit boxing path is outside static coverage.
