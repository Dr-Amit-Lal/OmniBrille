# My Files custom fork, 2026-11-07

## Objective and outcome

Create a simple, readable standalone folder map with remembered locations and one or two visible levels initially. The custom entry point now offers a home overview, expanding branch cards, larger text, selection and explicit file opening, Current Work pins, bounded filename search and reveal, refresh, and local workspace persistence. The original explorer and OmniSorSe handoff path remain available.

This is source version `1.2.0-myfiles.1`. It is not an installed or signed Windows release. Software-rendered implementation screenshots use synthetic folders and are committed under `docs/assets/screenshots/my-files`.

## Decisions

- Keep MyFilesSession separate from ExplorerSession: multiple saved local roots do not enter connected authority or opaque-ID persistence.
- Reuse the existing provider and activation service, with an additional ordinary-path check including the chosen root before read/open.
- Use native Avalonia buttons for map nodes and a deterministic layout. No force simulation, manual node positioning, file moves or automatic semantic relations are introduced.
- Keep workspace data in a separate versioned JSON file. Store only selected paths, aliases, expanded branches, folder pages, pins and text size. Theme remains in visual preferences.
- Use the packaged Inter font explicitly so rendering does not depend on a system font installation.

## Verified

- .NET SDK 9.0.316, .NET runtime 8.0.33, Linux execution environment.
- Restore and Release compilation with no compiler/analyzer warnings or errors after fixes. Single-node MSBuild/build-server-disabled commands were used because the environment restricts local sockets.
- Focused ordinary tests: 26 passed, covering workspace normalization/persistence, path/root/reparse boundaries, saved later pages, Back restoration, stale requests, root removal, search, selection and file activation.
- Ordinary suite excluding NamedPipeExplorerProtocolClientTests: 313 passed. The full run established seven tests in that class fail with local SocketException Permission denied; three other tests in the ten-test class passed.
- Custom My Files headless tests: 8 passed with both ordinary headless and actual Skia software rendering. Coverage includes native automation invocation, focus, selection/open separation, pin persistence, minimum window/large text, Home and screenshots.
- Full headless suite: 79 of80 passed on one run. The existing renderer timing comparison `SearchHighlight_ReducedEffectsLowersDecorativeRenderCost` failed once and passed on focused recheck. The upstream timing assertion was not weakened.
- Engineering documentation validation passed: 54 Markdown files, 218 repository-relative links, and 4 Mermaid diagrams.
- Changed C# files passed standalone whitespace-format verification. Full solution `dotnet format` could not load its named-pipe build host in this environment; build analyzers still ran normally.
- Independent source review identified and verified fixes for stale reveal/selection, page restoration, page-cache growth, pager focus, card heights, Settings background actions and Back semantics.

## Not verified

Native Windows installation, actual file-association launching, high DPI, Narrator, GPU rendering, physical input behavior, voice hardware, a live OmniSorSe host and a signed/distributed installer. The software screenshots are not a substitute for those checks. No claim of accessibility certification is made.

## Retrospective

The original single-focus session was not suitable for multiple open roots; a separate local workspace avoided invasive connected-state changes. Restored paths needed stronger validation than ordinary UI navigation because they could bypass non-navigable reparse entries. A late result can be rejected by the model yet still cause stale view-continuation effects; both session and window generations were checked. Explicit selection now invalidates a pending reveal. Native-control rebuilds require deliberate focus restoration. Saved expanded branches require page state as well as folder paths.

These lessons are represented by focused regression tests and the new ADR. No new global agent rules or skills were introduced. One implementation owner handled each area, with separate architecture, UX/test, and adversarial review roles.

## Documentation and repository state

Updated README, architecture, security/privacy, packaging, contributing guidance, and decision index. Added My Files behavior/build guide, ADR0002, screenshot evidence and this report. Existing protocol/advanced interaction contracts remain scoped to the retained explorer. Original upstream history, MIT attribution and third-party notices are preserved.

Baseline: `nishdel/OmniBrille` commit `f9f909ce42d87a26067c1481d322710f867d14a5`, clean before work. Target fork: `Dr-Amit-Lal/OmniBrille`. Work branch: `feature/simple-file-map`. No pre-existing user changes were overwritten.

Status: Complete with follow-up. The custom source is ready for review and native Windows acceptance. Preview and build instructions identify the custom fork rather than directing users to the unchanged upstream installer.
