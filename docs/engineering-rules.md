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
| NETAGENTS0015 | Require return values to be consumed. Reject ignored non-void calls, ignored awaited results, and assignments to `_`, including tuple deconstruction. This includes conditional calls, expression-bodied members, callbacks, and `for` clauses. |

Check, return, pass, or store and use every returned value. For example, use
`if (values.TryGetValue(key, out var value))` to handle success and failure;
calling `TryGetValue` alone or assigning its result to `_` fails the build.
Storing a result without reading it also fails the build (`IDE0059`).
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
