# NetAgents

[![NuGet](https://img.shields.io/nuget/v/NetAgents.Analyzers.svg)](https://www.nuget.org/packages/NetAgents.Analyzers)

**Consistent C# standards, enforced on every build.**

`NetAgents.Analyzers` keeps formatting, naming, and code quality consistent across your .NET projects.

## 📦 Install

```bash
dotnet add package NetAgents.Analyzers
```

Requires the **.NET 10 SDK or newer**. Your projects can target older .NET versions.

Set `PrivateAssets="all"` on the package reference to keep the analyzer dependency private to your project. Shared rules apply automatically.

## ✅ What it checks

- **Type safety** — no boxing, `object`/`dynamic` values, or untyped collections; nullable analysis is required.
- **Naming and structure** — descriptive names, one top-level type per matching file, files under 1,000 lines, and at most 16 authored C# files per directory.
- **Reliability** — consume return values; no discarded results, empty catches, null-forgiving operators, or unconditional loops.
- **Concurrency** — no `lock`, framework synchronization primitives, or blocking waits. Use asynchronous composition or Nito.AsyncEx when coordination is needed.
- **Style** — consistent formatting, blank lines around control flow and multiline declarations, braces based on body layout, single-line conditions up to 128 characters, argument and parameter lists wrapped at the same limit, a final newline, and named literal arguments. Includes automatic spacing, brace, and argument layout fixes.
- **Documentation** — XML comments for public APIs; missing comments and unused imports fail the build.

Violations fail the build. Existing editor and project settings must agree with the shared rules.

[Documentation](https://github.com/caunt/NetAgents/blob/main/docs/engineering-rules.md)

## 🛠️ Everyday use

```bash
dotnet format
dotnet build
```

`dotnet format` applies supported fixes. `dotnet build` reports any remaining violations.

---

[NuGet package](https://www.nuget.org/packages/NetAgents.Analyzers) · [MIT license](https://github.com/caunt/NetAgents/blob/main/LICENSE)
