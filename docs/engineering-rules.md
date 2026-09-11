# Engineering rules

The package generalizes the C# editor preferences, build settings, custom
analyzer rules, and written engineering requirements from the supplied policy.
It contains no application, deployment, radio, or receiver behavior.

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

The complete configuration is
[`src/NetAgents.Analyzers/Configuration/NetAgents.globalconfig`](../src/NetAgents.Analyzers/Configuration/NetAgents.globalconfig).
It preserves the original C# preferences: four-space indentation, explicit
types, file-scoped namespaces, System imports first, the original brace and
wrapping styles, and conventional interface, generic parameter, field, and
member naming. The type-parameter naming selector is corrected to the actual
EditorConfig `type_parameter` symbol kind. Custom diagnostic identifiers are
renamed for this independent package. Style and formatting diagnostics are
raised to errors so the preferences participate in consumer builds.

The global configuration cannot impose XML/JSON/Markdown editor indentation:
those files are not C# compiler inputs. Non-C# editing preferences remain in
this repository's `.editorconfig` for contributors.

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
