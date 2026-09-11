# Agent instructions

## Scope

This repository builds one NuGet package, `NetAgents.Analyzers`. Keep it
independent of application and domain projects. It is a compiler tool, so it
has no application server or background process lifecycle.

## Engineering

- Never introduce boxing or `object`/`dynamic`-typed authored values, parameters,
  returns, collections, or casts. Negative source strings in analyzer tests
  intentionally exercise prohibited code.
- Use full descriptive names without abbreviations, acronyms, or single letters.
  Standard interface and generic parameter prefixes are allowed.
- Store each top-level type in its own matching `.cs` file. Use narrowly scoped
  feature/subsystem directories. Do not introduce nesting to evade this rule.
- Keep catches meaningful; handle, log, or explicitly rethrow exceptions.
- Prefer immutable, concurrent, atomic, partitioned, or message-passing designs.
  Do not use `lock` or framework synchronization primitives. Use Nito.AsyncEx
  if asynchronous coordination is unavoidable.
- Keep services reusable and replaceable through abstractions and dependency
  injection when a service is needed.
- Keep each source file below 1,000 lines. Name inline literal arguments. Do not
  introduce null-forgiving operators or unconditional loops.

## Package policy

- Keep all custom analyzers in one assembly and one NuGet package.
- Keep `Configuration/NetAgents.globalconfig` both embedded in the assembly and
  packaged under `buildTransitive/`. The embedded copy is the authoritative
  configuration contract.
- Add rule tests for new semantic behavior and test real package consumption
  when packaging or configuration enforcement changes.
- Avoid blanket analyzer suppressions. Account for compiler-host compatibility:
  analyzers target `netstandard2.0` and use Roslyn 5.0 APIs.
- Keep editor defaults in sync with the global configuration. Architectural
  rules that cannot be statically proven belong in the rule guide.

## Verification

Run a locked restore, Release build, tests, `dotnet format`, package creation,
and `scripts/verify-package.py` against the resulting artifact. Keep package
lock files committed. Do not add application templates or sample servers.

## Releases

Preserve the EgressPool timestamp version formula in `build/Versioning.props`.
Use only GitHub trusted publishing in `publish-analyzers.yml`; never add a
stored NuGet publishing key. Release only the artifact that passed validation.
