# ✨ NetAgents

[![NuGet](https://img.shields.io/nuget/v/NetAgents.Analyzers.svg)](https://www.nuget.org/packages/NetAgents.Analyzers)

**Consistent C# standards, enforced on every build.**

Keep formatting, naming, and code quality consistent across your .NET projects. NetAgents automatically formats your source files and applies available code fixes when you build.

## 📦 Install

```bash
dotnet add package NetAgents.Analyzers
```

Requires the **.NET 10 SDK or newer**. Your projects can target older .NET versions.

Set `PrivateAssets="all"` on the package reference to keep the dependency private to your project. Shared rules apply automatically.

## 🛠️ Build and format

```bash
dotnet build
```

On each build, NetAgents:

1. Formats your source files and applies available code fixes automatically.
2. Checks the updated code against the shared rules.
3. Reports any remaining violations as build errors for you to resolve.

**Builds can change your source files.** Every rewritten file is named in the build log, including when the build then fails on a violation no fix can repair. A build that would leave behind a violation it introduced changes nothing at all and says so instead. Review those changes before committing. Some violations require a manual fix; IDE quick fixes and `dotnet format` are also available.

Unchanged builds reuse completed formatting after checking a content fingerprint of the sources, dependencies, tools, and configuration. Policy checks and compiler enforcement remain enabled. Profile a build with `dotnet-trace`; `dotnet clean` discards the record when a full run is wanted.

## 📏 Formatting rules

- **Conditions:** keep conditions on one line, with at most **128 characters**. Extract longer expressions into separate variables. Ternary conditions follow this rule; their `?` and `:` branches can span multiple lines.
- **Arguments and parameters:** keep lists, collection expressions, and initializers on one line when their normalized contents fit within **128 characters**, excluding the surrounding delimiters. Longer lists keep the first item beside the opening delimiter, put each later item on its own line one level deeper (one more for a list nested on the same line as another that continues), and close right after the last item; so does a list holding a lambda with several statements or a statement that holds others, such as `if` or `try`, one statement per line. This applies to calls and declarations, including constructors and records. Collection expressions and initializers of two or more elements that do not fit put one element per line.
- **Member chains:** keep a chain of up to four links (field, property, event, or method accesses) on one line when it fits within **128 characters**. A longer chain, or one that does not fit, keeps the first link on the receiver's line and puts every later link on its own line one level deeper.
- **Layout:** use consistent spacing, blank lines between methods and around control flow and multiline declarations, statements grouped by kind with one blank line between groups and none inside them, a statement that fits within 128 characters written on one line, awaited values named before their members are read, a statement written over several lines standing alone, `when` on the line of the `catch`, `case`, or switch arm it guards, groups of at most four statements split by comments that say what each part is for, braces based on body layout, every brace and statement of a statement block on its own line, an unbraced body on its own line below its header, and a final newline.
- **Clarity:** name literal and collection expression arguments, pass every other argument positionally, and remove unused imports. Merge a `try` that only adds a `finally` into the `try` it wraps, write strings joined with `+` on one line as one string, compare with string literals as `value is "literal"`, and assign lambdas and method groups directly instead of through a delegate constructor.

## 🗂️ Member order

Members are sorted by kind, visibility, const/static/instance, members fixed at construction before settable ones, constants by type, then name. Nested classes follow methods. Overloads with matching sorting keys retain their relative order.

Blank lines follow the same groups: single-line fields, properties, and events of one kind and visibility sit together, one blank line separates groups, and methods and documented or multiline members stand alone. A comment line starts a new group and keeps the members written under it together, and a group of more than four members needs comments saying what each part is for.

Sorting keeps the order of initializers that run code or read other members, and the field and property order of structs, explicit layouts, and serialized types. Types containing preprocessor directives retain their member order. A member wrapped at `=` or `=>` that fits within 128 characters is joined onto one line.

## ✅ Code standards

| Area | What to expect |
| --- | --- |
| 🧩 Type safety | Nullable analysis is required. No boxing, `object`/`dynamic` values, or untyped collections. Numeric casts such as `(int)value` are written with `CreateChecked`, `CreateSaturating`, or `CreateTruncating`, and the build rewrites existing ones; implicit conversions are left alone. |
| 🏗️ Design | Return dedicated named records, classes, or structs instead of tuple result types, including wrapped tuple results. Lambdas with one inferred parameter drop the optional parentheses. |
| 🏷️ Naming and structure | Descriptive names and no placeholder locals such as `result`, `flag`, or `isConditionMet`, method names that do not repeat their first parameter, imported type names instead of qualified ones, one top-level type per matching file, files under 1,000 lines not counting comment lines, and at most 16 authored C# files per directory. |
| 📝 Documentation | Hand-written documentation that tells a reader what the name cannot: every public and protected member documents each parameter, type parameter, result, value, and exception it throws, with `<remarks>` and `<example>` code where usage is not obvious. Tags fit the member, say different things, end their sentences, write `null`, `true`, and `false` as `<see langword="…"/>`, and open property and constructor summaries the .NET way. One `<summary>` per documentation comment. |
| 🛡️ Reliability | Consume return values. No discarded results, empty catches, null-forgiving operators, or unconditional loops. Required rules cannot be suppressed, by pragma, attribute, configuration, or project setting. |
| ⚡ Concurrency | Use asynchronous composition or Nito.AsyncEx for coordination. No `lock`, framework synchronization primitives, or blocking waits. |
| 📖 Documentation | XML comments are required for public APIs. |

C# written in Razor components and views (`@code`, `@functions`, and `@{ }` blocks, and event-handler lambdas) meets the type-safety, descriptive-name, reliability, concurrency, and tuple-result rules too. Formatting rules and fixes apply to `.cs` files.

Existing editor and project settings must agree with the shared rules. See the [complete rule reference](docs/engineering-rules.md) for details.

---

[📦 NuGet package](https://www.nuget.org/packages/NetAgents.Analyzers) · [📖 Rule reference](docs/engineering-rules.md) · [⚖️ MIT license](LICENSE)
