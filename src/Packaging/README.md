# NetAgents.Analyzers

**Consistent C# standards, enforced on every build.**

## 📦 Install

```bash
dotnet add package NetAgents.Analyzers
```

Requires the **.NET 10 SDK or newer**; projects can target older .NET versions. Set `PrivateAssets="all"` on the package reference. Shared rules apply automatically.

## ✅ What it checks

- Strong types without boxing or `object`/`dynamic` values.
- Descriptive names and one top-level type per matching file.
- Consistent formatting with a final newline, named literal arguments, and nullable analysis.
- XML comments for public APIs, with missing comments and unused imports treated as errors.
- No empty catches, null-forgiving operators, unconditional loops, locks, or blocking waits.

Violations and conflicting editor or project settings fail the build.

## 🛠️ Everyday use

```bash
dotnet format
dotnet build
```

Apply supported fixes with `dotnet format`, then build to check for remaining violations.

[Documentation](https://github.com/caunt/NetAgents/blob/main/docs/engineering-rules.md)
