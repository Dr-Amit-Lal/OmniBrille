# Testing and acceptance

Current target: local spatial workspace `1.2.0-nebula.4`. Configured checks are not proof of a passing run. Record the exact commit/artifact and observed result; preserve earlier reports with their original scope.

## Automated source checks

Use the SDK in `global.json`:

```powershell
dotnet restore .\OmniBrille.sln
.\build\Test-EngineeringDocs.ps1
dotnet format .\OmniBrille.sln --verify-no-changes --no-restore
dotnet build .\OmniBrille.sln --configuration Release --no-restore
dotnet test .\OmniBrille.sln --configuration Release --no-build --no-restore
```

Ordinary tests cover normalized workspace/migration/pins/preferences, native path boundaries, bounded provider search, session navigation/preview races, structural models/layouts/caches and package metadata. Headless tests cover the current workspace shell, root/pin actions, Search previews and file activation, graph layout/automation and rendering bounds. Removed voice/protocol/Context/Hybrid/MyFiles tests no longer qualify active capabilities.

For release work, use `build/verify-release.ps1` and the Windows workflows for dependency audit, packaging, prohibited payloads, native SkiaSharp/license integrity, signature/hash metadata and exact-artifact lifecycle. The shared Windows installer lifecycle script runs in standard CI and the private-preview artifact-only job, including exact retired-file cleanup, unknown-file preservation and retained workspace/preferences. Its fixture is synthetic; report full in-place upgrade separately.

## Manual acceptance

Use non-private fixture folders. Record installer SHA-256, Windows version, display/text scaling and actions/results. Exercise:

- several chosen roots, overlapping roots, unavailable/remounted drives and relaunch restoration;
- pin add/reorder/remove, outside-root excursions and return to Workspace;
- parent-based Back/Up, paging, file selection/Open file and blocked file types;
- search across chosen roots, hover/keyboard chain previews, clear/close, stale-result races and refresh;
- watcher updates for create/rename/delete, watcher failure recovery, and changes during active Search;
- Nebula/Light, narrow windows, hide/restore, reduced motion/effects, pan/zoom and dense labels;
- real UIA/Narrator/NVDA, DPI/text scaling, high contrast, window controls and GPU behavior;
- native cloud folders and hydration, including locally available and unavailable files;
- fresh install, previous-version upgrade with preserved settings, known obsolete asset cleanup and uninstall.

Source/headless tests and software screenshots do not certify installed Windows behavior, native cloud hydration, GPU performance or screen readers. Linux/macOS interactive support requires its own evidence. Voice, audio and live OmniSorSe checks belong only to historical releases. See the [interaction contract](interaction-state-contract.md), [compatibility](../COMPATIBILITY.md) and [release checklist](../RELEASE_CHECKLIST.md).
