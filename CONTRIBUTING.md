# Contributing to OmniBrille

Start with [AGENTS.md](AGENTS.md) and the [engineering authority map](docs/engineering/README.md). Current source and tests define behavior; retained run reports explain historical evidence. Before editing, inspect branch/HEAD/tracking, worktrees, active Git operations, and existing changes. Preserve work that predates the task.

## Build and run

Install the .NET SDK selected by [global.json](global.json), then run from the repository root:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet restore .\OmniBrille.sln
dotnet build .\OmniBrille.sln --configuration Release --no-restore
dotnet run --project .\src\OmniBrille.Desktop --configuration Release --no-build
```

Spatial Explorer starts with an empty workspace on first use and restores saved explicit roots/pins on later launches. Use `--root` to add an explicit root. Choose non-private demo content when reviewing UI changes or capturing evidence. See the [Windows packaging workflow](docs/PACKAGING.md) for installed-artifact checks.

Windows x64 is the distributed runtime target. Ubuntu CI validates build/test behavior; it is not interactive Linux qualification. macOS runtime is unverified. Follow the [compatibility matrix](COMPATIBILITY.md) when making platform claims.

## Change and review

1. Classify the affected behavior using the [risk guide](docs/engineering/risk-and-validation.md), then load the relevant architecture and tests. Small documentation edits do not require a broad architectural investigation.
2. Keep reads inside explicitly chosen roots or pin scopes. Preserve bounded scenes, stale-work rejection, transient preview restoration, accessible projections and local saved-data boundaries.
3. Add focused regression coverage for a changed failure or behavior contract, and run the [appropriate validation](docs/testing.md). Do not claim hardware, native rendering, screen-reader, or native cloud-folder results from fakes/headless tests.
4. Update affected current docs, terms, diagrams, and decisions. Preserve historical reports. Substantial runs include an [owner report and reviewed retrospective](docs/engineering/learning-and-reports.md).
5. Describe the user-visible problem, resulting behavior, validation, and material remaining uncertainty in the pull request. Another developer should understand the change without the conversation that produced it.

## Releases and feedback

[Directory.Build.props](Directory.Build.props) owns product version metadata. Dependency versions are independent pins. The [release checklist](RELEASE_CHECKLIST.md) and [packaging guide](docs/PACKAGING.md) define clean-tree qualification, exact-artifact hashes, signing disclosure, normal integration, and immutable tags/assets. Generated artifacts stay below ignored `artifacts/`; do not commit installers, local user content, audio, or secrets.

For a bug report, include the release version, Windows version, reproduction steps, expected and actual behavior. Review screenshots/settings before sharing and use synthetic folder names. The previous diagnostics/voice/connection UI is removed. See [security and privacy](docs/SECURITY-PRIVACY.md).
