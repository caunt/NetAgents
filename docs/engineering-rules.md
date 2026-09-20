# Engineering rules

The package enforces shared C# style, build settings, and engineering rules.

Builds automatically apply the available source formatting and code fixes before compilation. A packaged task hosts Roslyn inside MSBuild using the current project's source files, references, language version, conditional symbols, analyzer configuration, and additional files. It does not launch a shell, `Exec` task, `dotnet format`, or any other child process. The task and its Roslyn dependencies ship with the package; standard C# code-fix providers come from the building SDK.

The task applies supported project-wide fixes (or individual fixes when necessary) and repeats until stable. Generated files and design-time IDE builds are excluded. Unchanged source files keep their timestamps. Formatting of shared source directories is serialized to protect parallel and multi-target builds, and each target uses its own conditional symbols. Rules without a fix still fail compilation; fixes that add/remove documents or require project-system/UI operations are not applied automatically. Unresolved-symbol compiler errors (`CS0103`, `CS0117`, `CS0120`, `CS0234`, `CS0246`, `CS0426`, `CS1061`, `CS7036`) are never fixed automatically, because the available fixes would declare API the author never wrote; they remain ordinary compiler errors, including the cases a missing `using` directive would resolve. A code-fix provider that cannot run — for example one built against a different Roslyn version than the hosting SDK — is skipped instead of failing the build, and any diagnostic it leaves behind still fails compilation. An action offered for a `hidden` or `info` suggestion is dropped the same way when applying it would introduce compiler errors, so an SDK suggestion cannot break a build that has nothing else wrong with it; a compiler-breaking action still fails the build when the diagnostic it repairs is a warning or an error. Formatting also adds time to the build and can apply style changes beyond whitespace.

Formatting runs on every build that is not a design-time IDE build, and there is no property that turns it off. This repository uses the same task, with an isolated copy of the locally built task to allow it to rebuild itself. On the first clean checkout, the task project must bootstrap before the formatter becomes available.

## Diagnostics

Every custom diagnostic is an error and is marked non-configurable.

| Diagnostic | Meaning |
| --- | --- |
| NETAGENTS0001 | Reject implicit and explicit boxing conversions, including nullable values, enums, interface conversions, and unconstrained generics. |
| NETAGENTS0002 | Reject explicit `object`/`dynamic` type uses, untyped arrays/generic collections, and untyped values returned by calls, fields, properties, indexing, and await. Overrides and explicit interface implementations keep the prescribed parameter and result types of the signature they inherit; their bodies are still checked. |
| NETAGENTS0003 | Reject C# `lock` statements. |
| NETAGENTS0004 | Reject catch blocks containing no executable statements, including comment-only blocks. |
| NETAGENTS0005 | Reject single-letter declarations, uppercase acronyms, and known abbreviated identifier words. Standard `I…` and `T…` prefixes and the .NET `Async` naming convention (for example, `ReadAsync`) are supported. |
| NETAGENTS0006 | Require one top-level type per file with the matching case-sensitive file name. Appropriate nested types are allowed. |
| NETAGENTS0007 | Reject the postfix null-forgiving operator. |
| NETAGENTS0008 | Reject literal-true `while`/`do` conditions and conditionless or literal-true `for` loops. |
| NETAGENTS0009 | Require parameter names for inline literal/default arguments in methods, constructors, delegates, and indexers, including signed/parenthesized literals, and omit names the rule does not require. For expanded `params`, pass a named collection. Attributes use their separate language syntax. Includes an automatic code fix and Fix all support. |
| NETAGENTS0010 | Require authored source files to contain fewer than 1,000 lines. The required final newline terminates the last line instead of starting another one. |
| NETAGENTS0011 | Reject type names matching containing namespace segments or feature directories below the project root. |
| NETAGENTS0012 | Reject framework synchronization types and blocking task/thread waits, including aliases, static imports, and explicit awaiter `GetResult` calls. |
| NETAGENTS0013 | Reject changes to the effective shared configuration and diagnostic severities. |
| NETAGENTS0014 | Reject incompatible project settings. This diagnostic comes from the package's MSBuild target. |
| NETAGENTS0015 | Require return values to be consumed. Reject ignored non-void calls, ignored awaited results, and assignments to `_`, including named underscore symbols. Reject deconstruction discards, including `foreach` and `await foreach`. This includes conditional calls, expression-bodied members, callbacks, and `for` clauses. |
| NETAGENTS0016 | Require blank lines before and after control-flow statements and multiline local declarations when another statement is adjacent. Includes an automatic code fix and Fix all support. |
| NETAGENTS0017 | Omit optional braces around one single-line body statement; require braces for multiline bodies and keep conditional chains consistent. Includes an automatic code fix and Fix all support. |
| NETAGENTS0018 | Allow at most 16 authored C# files directly in each directory per project. Organize larger directories into subdirectories with narrower responsibilities. |
| NETAGENTS0019 | Require single-line conditions of at most 128 characters in `if`/`else if`, `while`, `do`, `for`, ternary expressions, catch filters, and switch `when` guards. Extract longer conditions into a separate variable declaration. |
| NETAGENTS0020 | Keep argument and parameter lists on one line when their compact contents are at most 128 characters; otherwise put each item and the closing delimiter on separate lines. Includes an automatic fix and Fix All support. |
| NETAGENTS0021 | Require a blank line between adjacent method declarations, including constructors, destructors, and operators. Includes an automatic fix and Fix All support. |
| NETAGENTS0022 | Order type members by kind, visibility, const/static/instance, readonly, then ordinal name. Includes an automatic fix and Fix All support. |
| NETAGENTS0023 | Forbid blank lines directly after an opening brace or before a closing brace, in every block: type bodies, method and statement blocks, accessor lists, namespaces, enums, switch bodies, initializers, lambdas, and local functions. Includes an automatic fix and Fix All support. |

Condition length counts source characters, including spaces and comments, between condition parentheses (excluding the parentheses). For `for`, only the condition between semicolons is checked; for ternaries, only the condition expression is checked (`?` and `:` branches may span multiple lines); for `when` guards, the text after `when` through the condition is checked. Newlines immediately inside condition parentheses are also forbidden. Extraction is manual: preserve evaluation frequency and short-circuit behavior, especially in loops.

Argument layout covers method and delegate calls, constructors (including target-typed `new`, `base`, and `this` initializers), attributes, element access, and declaration parameters for methods, constructors, primary constructors, records, delegates, operators, indexers, local functions, and anonymous functions. Generic type argument lists and collection initializers are not included. The shared 128-character limit counts the normalized single-line contents, including commas and spaces, but excludes delimiters, the method/type name, and indentation. A single item longer than the limit still gets its own line; this rule does not split literals or expressions.

Multiline literals and comments that cannot safely fit on one line retain their contents and use expanded layout even below the limit. Lists containing preprocessor directives are left unchanged. Conditions still independently satisfy `NETAGENTS0019`; extract a call into a variable if its expanded arguments would make a condition multiline.

Use **Fix argument and parameter layout** or run:

```bash
dotnet format analyzers --diagnostics NETAGENTS0020
```

Argument names converge on one shape: an inline literal or `default` argument carries its
parameter name, and every other argument is positional. A name is only reported as unnecessary
when dropping it provably keeps the same binding — each argument already sits in its own
parameter slot, nothing is reordered, and no optional parameter is skipped — so
`Execute(first, third: third)` keeps its name. The fix rewrites a whole argument list at once,
re-resolves the call against the compiler before accepting the rewrite, and leaves line breaks,
indentation, and comments alone. Expanded `params` arguments become one named collection, so
`Log(1, 2)` becomes `Log(values: [1, 2])`; the normal form is untouched, because `Log(default)`
passes a null array rather than a one-element one. Shapes with no safe rewrite stay reported and
unfixed: `dynamic` calls, `__arglist`, function pointers, argument lists containing preprocessor
directives, a `params` collapse that would drop a comment or sits inside an expression tree, and
any rewrite that would bind to a different overload. Longer names can push a call past the
`NETAGENTS0019` condition limit, which has no fix and needs the variable extraction described
above. Run:

```bash
dotnet format analyzers --diagnostics NETAGENTS0009
```

Directory limits count each distinct `.cs` file included in the project once,
including linked source files in their physical directories. Generated files
are excluded, and each subdirectory is counted separately. The seventeenth
authored file causes a build error that names the directory and its file count.
Split oversized directories by feature or responsibility to resolve it.

Statement spacing covers `if`, loops, `switch`, `try`, `using` (including
declarations), `return`, `throw`, `break`, `continue`, and `yield`. Multiline
local declarations include chained calls, switch expressions, conditional
expressions, and object initializers. Single-line declarations can remain
grouped. Block edges stay unpadded, and unbraced bodies stay attached to their
controlling statement. Comments and existing line endings are preserved. Use the
editor's **Insert blank line between statements** quick fix or run:

```bash
dotnet format analyzers --diagnostics NETAGENTS0016
```

`NETAGENTS0023` enforces unpadded block edges everywhere rather than merely
declining to add padding: a blank line directly after `{` or directly before `}`
is an error in every block, including type bodies, method and statement blocks,
accessor lists, namespaces, enums, switch bodies, object and collection
initializers, lambdas, and local functions. Blank lines inside comments and
string literals are untouched, blocks containing preprocessor directives are
skipped, and existing line endings are preserved because the fix only deletes.
Run:

```bash
dotnet format analyzers --diagnostics NETAGENTS0023
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
from renaming their prescribed member names, and `NETAGENTS0002` likewise
accepts the `object` parameters and results those signatures prescribe. Referencing existing framework
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

## Member separation and ordering

Adjacent methods require at least one blank line, including expression-bodied and interface methods.
Comments, XML documentation, and attributes stay attached to their declaration. Existing blank lines
and newline styles are preserved. This rule does not add padding inside method bodies.

Member ordering follows StyleCop's kind order: fields, constructors, finalizers, events, enums,
interfaces, properties, indexers, operators, methods, structs, classes, and delegates. Records follow
their class or struct kind. Within each kind, order by public, internal, protected internal,
protected, private protected, then private; next by const, static, then instance; next readonly
before writable; finally by case-sensitive ordinal name. Overloads with equal keys retain their
relative order. Each partial declaration is ordered independently.

Automatic sorting preserves the relative execution order of nonconstant field, event, and property
initializers, taking precedence over the ordinary ordering keys. Storage members also retain their
relative order in structs and types annotated with StructLayout. Types containing preprocessor
directives are excluded from ordering so region, conditional compilation, nullable, and warning
boundaries remain intact. Comments and attributes move with their member.
