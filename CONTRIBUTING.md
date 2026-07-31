# Contributing to Duetto

Thanks for your interest in improving Duetto! Contributions of all kinds are
welcome — bug reports, features, docs, and fixes.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned via `global.json`).

## Build, run, test

```sh
dotnet build src/Duetto/Duetto.csproj                 # build the app
dotnet run   --project src/Duetto                     # run it
dotnet test  tests/Duetto.Tests/Duetto.Tests.csproj   # run the full test suite
```

Preview the per-OS window chrome without switching machines:

```sh
dotnet run --project src/Duetto -- --chrome win|mac|gnome
```

## Project layout

- `src/Duetto` — the Avalonia desktop app (views, view-models, `Program`/`App`).
- `src/Duetto.Core` — platform-agnostic core: filesystem/remote providers,
  operations (trash, transfer), and persisted state stores.
- `tests/Duetto.Tests` — xUnit tests, including headless Avalonia UI tests.
- `scripts/` — packaging (`publish-all.sh`, `make-app-bundle.sh`, `make-dmg.sh`)
  and screenshot capture (`scripts/screenshots/`).
- `docs/` — screenshots and reference docs.
- `plans/` and `docs/superpowers/specs/` — design specs and implementation plans.

## Guidelines

- **Tests first.** New behavior and bug fixes come with tests; UI-level behavior
  is covered by headless Avalonia tests. Keep the suite green
  (`dotnet test`).
- **Match the surrounding code** — naming, comment density, and idioms.
- Keep core logic in `Duetto.Core` (no Avalonia dependency) so it stays unit
  testable; the app project holds only the UI wiring.

## Releases

Releases are automated: pushing a `v*` tag (e.g. `v1.1.0`) builds the
self-contained artifacts for all platforms and publishes a GitHub Release. The
version is derived from the tag. Update `CHANGELOG.md` in the same change.

## License

By contributing, you agree that your contributions are licensed under the
project's [MIT License](LICENSE).
