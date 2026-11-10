# Risk and validation

Risk follows observable behavior, persistence and authority—not diff size. Current product: one local spatial workspace.

| Risk | Areas | Focused evidence |
| --- | --- | --- |
| High: session/concurrency | Navigation, root/pin authority, search/hover, refresh, removal/disposal | Late-result rejection; cancel/replace; preserve newer pins/roots; preview rollback; removed-scope safety |
| High: path/privacy | Local/cloud-entry classification, file opening, migration/schema | Root escape, native-case rules, symlink/junction/unknown tags, unavailable drive, malformed/bounded JSON, explicit migration |
| High: renderer/UX/accessibility | Cluster layout, scene bounds, labels, hover, graph automation, shell reservation | Small/dense/narrow scenes, budget/cache bounds, keyboard/action-result parity, reduced settings |
| High: packaging | Installer cleanup, contents, signing, native provenance | Exact payload, notices/hash/signing metadata, synthetic cleanup and real artifact lifecycle |
| Normal | Isolated deterministic helper, ordinary visual preferences | Focused contract tests, Release analyzers and impacted suite |
| Low | Typo/history-only docs or nonbehavioral metadata | Diff/link/fence checks; targeted tests only if an executable assertion changed |

Raise risk when a change crosses owners, alters persisted state, changes recovery behavior, or expands a claim without evidence. The owner coordinates relevant architecture, UX/performance/privacy and implementation roles; high-risk work also receives independent adversarial review. Keep one owner per overlapping file and do not delegate redundant rediscovery.

## Required checks

Low-risk documentation: documentation link/fence check and `git diff --check`.

Normal changes: focused meaningful tests, formatting, analyzer-enabled Release build and affected ordinary/headless tests; update current docs.

High-risk changes: all Normal checks plus full automated suites and focused failure/race fixtures. Add native or artifact validation appropriate to the boundary. Report manual/environment checks not performed.

```powershell
dotnet restore .\OmniBrille.sln
.\build\Test-EngineeringDocs.ps1
dotnet format .\OmniBrille.sln --verify-no-changes --no-restore
dotnet build .\OmniBrille.sln --configuration Release --no-restore
dotnet test .\OmniBrille.sln --configuration Release --no-build --no-restore
```

Use `build/verify-release.ps1` for release-relevant work. Native Skia changes additionally require the [pinned provenance procedure](../native-skia.md); incidental rebuilds are not acceptable.

## Current regression anchors

- [SpatialWorkspaceSession](../../src/OmniBrille.Desktop/Presentation/SpatialWorkspaceSession.cs): generations, authorized root-qualified identities, hierarchy, transient preview and pin/root mutation.
- [JsonSpatialWorkspaceStore](../../src/OmniBrille.Infrastructure/JsonSpatialWorkspaceStore.cs): bounded normalization/serialization, explicit legacy migration and failed-save reporting.
- [FileSystemExplorerProvider](../../src/OmniBrille.Infrastructure/FileSystemExplorerProvider.cs), [FileMapPathGuard](../../src/OmniBrille.Infrastructure/FileMapPathGuard.cs), and [activation](../../src/OmniBrille.Infrastructure/ShellFileActivationService.cs): scoped reads, cloud/redirection distinction and deliberate opening.
- [WorkspaceWindowTests](../../tests/OmniBrille.HeadlessTests/WorkspaceWindowTests.cs) and [WorkspaceRendererTests](../../tests/OmniBrille.HeadlessTests/WorkspaceRendererTests.cs): actual shell/graph action paths and bounded projections.
- [Ordinary tests](../../tests/OmniBrille.Tests): retained structural layout, identity, provider, migration, cache and release checks.

Historical connected/voice regressions explain earlier decisions in [history/lessons](history-and-lessons.md); their removed tests are not current acceptance gates. Do not invent blanket frame-time/coverage gates without evidence that they are reliable.

## Evidence limits

Compilation/headless render does not establish native layout, motion comfort, GPU/DPI, screen-reader behavior or OneDrive hydration. Synthetic installer cleanup is not a full installed-version upgrade. Linux CI does not qualify interactive Linux or macOS. A configured workflow and a historical report do not establish that current checks passed. Reports must distinguish Verified, Not verified and Inferred.
