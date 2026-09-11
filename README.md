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
- **Naming and structure** — descriptive names, one top-level type per matching file, and files under 1,000 lines.
- **Reliability** — no empty catches, null-forgiving operators, or unconditional loops.
- **Concurrency** — no `lock`, framework synchronization primitives, or blocking waits. Use asynchronous composition or Nito.AsyncEx when coordination is needed.
- **Style** — consistent formatting, a final newline, named literal arguments, and warnings treated as errors.
- **Documentation** — XML comments for public APIs; missing comments and unused imports fail the build.

Violations fail the build. Existing editor and project settings must agree with the shared rules.

[Documentation](docs/engineering-rules.md)

## 🛠️ Everyday use

```bash
dotnet format
dotnet build
```

`dotnet format` applies supported fixes. `dotnet build` reports any remaining violations.

---

[NuGet package](https://www.nuget.org/packages/NetAgents.Analyzers) · [MIT license](LICENSE)
