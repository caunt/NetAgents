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

**Builds can change your source files.** Every rewritten file is named in the build log, including when the build then fails on a violation no fix can repair. Review those changes before committing. Some violations require a manual fix; IDE quick fixes and `dotnet format` are also available.

## 📏 Formatting rules

- **Conditions:** keep conditions on one line, with at most **128 characters**. Extract longer expressions into separate variables. Ternary conditions follow this rule; their `?` and `:` branches can span multiple lines.
- **Arguments and parameters:** keep lists on one line when their normalized contents fit within **128 characters**, excluding the surrounding delimiters. Longer lists put each item and the closing delimiter on separate lines. This applies to calls and declarations, including constructors and records.
- **Layout:** use consistent spacing, blank lines between methods and around control flow and multiline declarations, braces based on body layout, and a final newline.
- **Clarity:** name literal arguments, pass every other argument positionally, and remove unused imports.

## 🗂️ Member order

Members are sorted by kind, visibility, const/static/instance, readonly, then name. Nested classes follow methods. Overloads with matching sorting keys retain their relative order.

Sorting preserves initializer order and storage order in structs and types with explicit layout. Types containing preprocessor directives retain their member order.

## ✅ Code standards

| Area | What to expect |
| --- | --- |
| 🧩 Type safety | Nullable analysis is required. No boxing, `object`/`dynamic` values, or untyped collections. |
| 🏗️ Design | Return dedicated named records, classes, or structs instead of tuple result types, including wrapped tuple results. |
| 🏷️ Naming and structure | Descriptive names, one top-level type per matching file, files under 1,000 lines, and at most 16 authored C# files per directory. |
| 🛡️ Reliability | Consume return values. No discarded results, empty catches, null-forgiving operators, or unconditional loops. |
| ⚡ Concurrency | Use asynchronous composition or Nito.AsyncEx for coordination. No `lock`, framework synchronization primitives, or blocking waits. |
| 📖 Documentation | XML comments are required for public APIs. |

Existing editor and project settings must agree with the shared rules. See the [complete rule reference](docs/engineering-rules.md) for details.

---

[📦 NuGet package](https://www.nuget.org/packages/NetAgents.Analyzers) · [📖 Rule reference](docs/engineering-rules.md) · [⚖️ MIT license](LICENSE)
