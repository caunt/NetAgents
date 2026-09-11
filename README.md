# NetAgents

[![Publish analyzers](https://github.com/caunt/NetAgents/actions/workflows/publish-analyzers.yml/badge.svg)](https://github.com/caunt/NetAgents/actions/workflows/publish-analyzers.yml)
[![NuGet](https://img.shields.io/nuget/v/NetAgents.Analyzers.svg)](https://www.nuget.org/packages/NetAgents.Analyzers)

**One NuGet package that enforces shared .NET engineering rules.**

`NetAgents.Analyzers` supplies thirteen C# analyzers, an embedded global editor
configuration, and automatic build integration. It carries the reusable rules
and C# preferences from the original engineering policy without application or
domain dependencies. This repository contains only the analyzer package, its
tests, and release tooling.

## Install

From a consuming project directory:

```bash
dotnet add package NetAgents.Analyzers
```

Then set `PrivateAssets="all"` on the generated `PackageReference` to keep
build tooling private to that project. Pin the resolved version. For central
package management, put the version in `Directory.Packages.props` and omit it
from the individual reference.

Requires a Roslyn 5.0+ compiler/IDE host, normally the .NET 10 SDK or newer.
Consumer target frameworks may be older; the analyzer itself targets
`netstandard2.0` and never runs inside your application.

No `.editorconfig` copy, analyzer project reference, or manual import is needed.
NuGet imports the configuration and build policy automatically. Run `dotnet
build` to enforce the rules and `dotnet format` to apply supported formatting
fixes.

## Enforced policy

- No boxing, `object`/`dynamic` values, or untyped collections.
- Full descriptive names, one top-level type per matching file, and fewer than
  1,000 lines per authored source file.
- No empty catch blocks, null-forgiving operators, `lock` statements, or
  unconditional loops.
- No framework synchronization primitives or synchronous waits. Prefer
  immutable state, atomics, concurrent collections, channels, and Nito.AsyncEx
  when coordination is necessary. Nito.AsyncEx is not a runtime dependency of
  this package; add it to an application only when needed.
- Named literal arguments, distinct type/namespace names, nullable analysis,
  the complete built-in .NET analyzer set, and warnings treated as errors.
- Shared C# formatting and naming preferences, with build-blocking style
  diagnostics. See the [rule guide](docs/engineering-rules.md) for exact coverage.

## Configuration enforcement

`NetAgents.globalconfig` is both embedded in the analyzer assembly and included
under `buildTransitive/` in the package. Its `is_global = true` declaration and
high `global_level` apply the defaults to every consumer source file.

A normal `.editorconfig` has higher precedence than a global configuration.
`NETAGENTS0013` compares each source file's effective options and diagnostic
severities with the embedded policy and rejects conflicting overrides.
`NETAGENTS0014` rejects project settings that disable required analysis,
nullability, or errors. The custom analyzer diagnostics are non-configurable,
including through `#pragma` directives.

A project owner can always uninstall the package or deliberately remove its
build assets. This enforces the policy while the package is installed and its
assets are loaded; it cannot control a build system that excludes it.

## Development

```bash
dotnet restore NetAgents.slnx
dotnet build NetAgents.slnx --configuration Release --no-restore
dotnet test NetAgents.slnx --configuration Release --no-build --no-restore
dotnet format NetAgents.slnx --verify-no-changes --no-restore
dotnet pack src/NetAgents.Analyzers.csproj --configuration Release --output artifacts/packages
```

Every normal build compiles the analyzer and then rebuilds it with that analyzer
enabled. The test project also references it as an analyzer. Self-analysis is
part of the build, with no separate workflow command needed.

The .NET tests create a clean consumer with a private NuGet cache and verify
package layout, successful compilation, rejection of boxing and formatting
defects, and rejection of configuration and suppression attempts. Set
`NETAGENTS_PACKAGE_PATH` to test a specific package artifact; otherwise the tests
pack the current build. The publishing workflow tests the exact package it
publishes. Tests also cover individual analyzer behavior and generated-code
exclusions. Negative C# snippets in test strings intentionally violate policy.

## Publishing and versioning

The trusted publishing workflow is **`publish-analyzers.yml`**, under
`.github/workflows/`. It runs on pushes to `main` and manual dispatches, validates
and packs one artifact, then uses `NuGet/login` to exchange the GitHub identity
for a short-lived publishing credential. No stored NuGet publishing key is used.

Trusted publisher settings:

| Setting | Value |
| --- | --- |
| Repository owner | `caunt` |
| Repository | `NetAgents` |
| Workflow file | `publish-analyzers.yml` |
| Environment | Leave empty; the workflow declares no environment |
| NuGet username | `caunt` by default, or repository variable `NUGET_USER` |
| Package | `NetAgents.Analyzers` |

Versions use `year.month.day.bucket`, with a two-digit UTC year and
`bucket = 1000 + floor(secondsSinceMidnight * 9000 / 86400)`.
For example, `26 09 11 12 00 00` produces `26.9.11.5500`.
The implementation is in [build/Versioning.props](build/Versioning.props).
The workflow captures the timestamp once for the entire build and publishes
the exact artifact that passed consumer verification. The bucket has roughly
9.6-second resolution; duplicate versions are skipped on push.

MIT licensed.
