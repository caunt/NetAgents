# NetAgents.Analyzers

One package containing thirteen C# analyzers, an embedded global editor
configuration, and automatic build policy enforcement. All custom diagnostics
are errors and non-configurable. The analyzer assembly has no consumer runtime
dependencies.

```bash
dotnet add package NetAgents.Analyzers
```

Set `PrivateAssets="all"` on the generated package reference. Requires a Roslyn
5.0+ compiler/IDE host, normally the .NET 10 SDK or newer; consumer target
frameworks may be older.

The package automatically imports `NetAgents.globalconfig` as a global analyzer
configuration. It enforces strongly typed code without boxing, descriptive
names, source-file structure, named literal arguments, nullable analysis,
meaningful catch blocks, asynchronous concurrency, and shared C# style.
Consumer configuration conflicts and project settings that disable the policy
fail the build. Run `dotnet format` to apply supported formatting fixes.

See the [repository](https://github.com/caunt/NetAgents) for installation details
and the [complete diagnostic and coverage guide](https://github.com/caunt/NetAgents/blob/main/docs/engineering-rules.md).
