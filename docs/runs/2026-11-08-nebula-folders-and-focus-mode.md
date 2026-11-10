# Nebula folder icons and Search-only interface

## Intended result

Continue the approved folder-icon design, remove sound, and add a toggle that hides the interface except Search. This builds on the sole Spatial Explorer interface already delivered on `feature/nebulux-my-files` at `e7d75e56e1893c3b395b92d8d72c1add4eb4870b`.

## Changes and decisions

- Nebula uses blue, violet, teal and amber vector folders, document-shaped files, a larger blue focus folder, and subtle cached gradient glow. Labels, edge endpoints, Hybrid markers, Search diamonds and selection/focus cues follow the glyph envelope. Every admitted name, real containment edge and 44-DIP effective target remains.
- The interface button or `Ctrl+H` hides ordinary chrome and panels while leaving Search and the restore button visible. Selection, pan, zoom, query and pending provider work survive the toggle. Active microphone feedback and loading cancellation remain reachable. Explicit list, Details and Voice actions restore controls. Escape in Search still cancels active recording; otherwise it clears Search without removing the editor in hidden mode.
- Sound controls and all Desktop playback dispatch are removed. The old preference key stays compatible but normalizes false. Voice capture remains independent. No selected roots, queries or new interface preference are persisted.
- Static geometry and two eight-entry gradient caches preserve the total 576-entry resource-cache bound by reducing the solid-brush cache to 176. ScenePadding is unchanged, avoiding a view jump when hiding controls.
- Version advances to `1.2.0-nebula.2`; existing installer identity and sole Spatial Explorer shortcut remain.

## Validation and confidence

Verified locally: Release build with zero warnings/errors; 103 headless tests passed; nine actual Skia software renders covered Nebula, Search-only, compact 820×520, dense scenes, zoom-out, large text, Light, and selection/Search cues. The ordinary suite passed 319 tests; seven named-pipe tests are blocked by the local execution environment denying Unix sockets. Changed C# whitespace and `git diff --check` passed. Full solution formatting also requires denied local named pipes and is delegated to the existing Windows/Ubuntu CI.

Independent UX/performance and adversarial reviews examined source and software renders. Corrected findings included glyph-corner clearance, microphone cancellation from Search, hidden empty/welcome panels, common list restoration, delayed connection failure restoration, and Search placeholder contrast. Hosted source qualification and exact installer provenance are tracked in PR #1; this report is the implementation checkpoint before that run.

Not verified: interactive installation or upgrade on the owner's Windows computer, native DPI/snap/screen-reader behavior, GPU frame time, real microphone capture, or a live companion host. Software renders use synthetic folders and are not screenshots of the owner's files.

## Retrospective and documentation

The prior branch had the sole interface completed; the icon render had not been implemented. Circle-based glyph geometry needed coordinated replacement across drawing, obstacles and connectors. Shell visibility required centralized state because asynchronous session updates can reopen panels. These observations are captured in focused regressions and current contracts, with independent review; no new global rule was added.

Updated README, architecture and its dependency diagram, interaction contract, packaging notes and ADR 0004. Historical reports and upstream release notes remain historical. Changes remain on the existing feature branch and PR; main is not merged. No protocol, provider-authority or persistence schema is changed.

Status: Partial. The source implementation and local verification are complete. Automatic approval review rejected the GitHub source-tree upload, including after account ownership and prior task authorization were checked. No feature ref, main branch or PR metadata was changed, and no new Windows installer was built. Explicit approval of the source upload is the remaining gate to hosted qualification and installer delivery; native owner acceptance remains separate.
