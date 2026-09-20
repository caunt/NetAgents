# Purpose

NetAgents is an opinionated C#/.NET NuGet package that makes shared coding standards mandatory for both human-written and agent-generated code. It automatically formats sources and applies available code fixes during builds, then reports remaining violations as build errors. Strict rules, source changes during builds, and rejection of conflicting settings or suppression of protected diagnostics are intentional product behavior, not defects. Treat [the engineering rules](docs/engineering-rules.md) as the policy reference. Fix implementation bugs and false positives without weakening that policy; do not relax rules, downgrade errors, disable enforcement, or add bypasses unless the user explicitly requests a policy change.

# Commits

- Use Conventional Commits: `type(scope): <gitmoji> imperative summary`. Scope is required.
- Match Gitmoji to intent: ✨ feat, 🐛 fix, ♻️ refactor, 📝 docs, ✅ test, 📦 build, 👷 ci, 🔧 chore.
- Mark breaking changes with `!` and a `BREAKING CHANGE:` footer.
