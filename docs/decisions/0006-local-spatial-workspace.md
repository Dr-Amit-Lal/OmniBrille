# ADR 0006: One local spatial workspace

Status: Accepted for `1.2.0-nebula.4`; supersedes the live behavior of ADRs 0001–0005 and the former standalone-file-map decision where they conflict.

## Context

The owner chose a focused local folder workspace with several saved roots, independent pins and Search path previews. Keeping dormant voice, connection modes and a second application/session model made the product boundary and release payload misleading.

## Decision

Use one `MainWindow` and `SpatialWorkspaceSession`. Save up to 8 explicitly chosen roots, 12 pins and the current authorized location in `spatial-workspace.json`. Migrate only older explicit root/pin records when the new file is absent; do not infer recommended roots. Retain legacy stores/models only for that migration.

Show roots as separate bounded clusters. Back/Up follows containing folders toward Workspace. Pins inside roots use the deepest containing root; outside pins create a temporary exact-folder scope without changing monitored roots. Search spans chosen roots only. Hover/keyboard result preview is temporary and reversible. File-result activation opens the file; Show in map reveals its containing folder without opening it.

Monitor chosen roots while the app is open and use coalesced/periodic bounded refresh. Do not maintain a content index, background service or cloud account connection. Keep theme and reduced motion/effects; interface visibility is transient.

Delete My Files UI/session, old ExplorerSession, voice/capture/parser/runtime, sound, diagnostics, OmniSorSe/handoff/protocol and Context/Hybrid implementation/tests/dependencies. Keep reusable structural models, provider boundaries, renderer helpers and their tests. Remove audio assets from installer/release manifests; clean only known obsolete install assets with reparse-safe exact paths.

## Consequences

The persistent privacy boundary now includes chosen root/pin paths and current location. Saved missing drives remain recoverable. Generation checks bind asynchronous work to the current workspace and preserve newer pin/root edits. Search previews and scene membership share the 64-node cap.

The manifest moves to schema 6 with local-workspace identity. Native Windows upgrade/cloud hydration/screen-reader evidence must be gathered for the new product; earlier voice/protocol or software-render results do not transfer. Historical guides/reports remain clearly scoped to their original versions.

## Evidence

- [Session](../../src/OmniBrille.Desktop/Presentation/SpatialWorkspaceSession.cs)
- [Workspace model](../../src/OmniBrille.Core/SpatialWorkspace.cs) and [store](../../src/OmniBrille.Infrastructure/JsonSpatialWorkspaceStore.cs)
- [Current architecture](../architecture.md), [interaction contract](../interaction-state-contract.md), [packaging](../PACKAGING.md)

## Nebula.5 visual refinement (effects superseded below)

Nebula.5 changed Search preview to mark only an existing visible node or nearest ancestor within the same root, without expanding topology. It removed hover enlargement, floating positions, route glows and particles. Thin straight edges were clipped around glyphs and labels; only local icon glow and label background responded to hover. Overview roots opened directly without a synthetic Open folder node. State schema, path authorization and explicit file activation were unchanged.

## Nebula.6 effect restoration

The owner clarified that the layout cleanup should preserve the original motion and hover effects. Restore `GraphMotionPolicy` gentle floating, a smooth hover lens bounded to 1.16× with local neighbor response, and original folder glow/shine and hover/selection rings. Navigation transitions apply only when the displayed scene changes. Keep the compact base layout and glyphs, thin neutral straight connections, nearest-visible-ancestor Search markers and direct root navigation. Reserve local ring space for labels and connector clipping; do not restore extra hover routes, particles, preview branches or the synthetic Open folder node. Reduced motion disables nonessential movement. This correction changes presentation only; session, persistence and authority contracts remain unchanged.

## Nebula.7 search indicators and file activation

The owner clarified that search indicators should follow the existing connections. Moving points now travel only on visible matching edge segments, and a hovered or selected result emphasizes its visible ancestor path. Their glow clears glyph and label exclusions; reduced motion leaves them stationary. No branches or extra connecting lines are added. Double-click or Enter on a file result opens it through the same guarded activation service as a graph file; a separate Show in map action retains location navigation. This supersedes the nebula.6 decision to remove particles while preserving its clean connections.

## Nebula.8 edge tab

When the running workspace is minimized or inactive, a small topmost, non-taskbar tab sits at the left edge of its display's working area. Clicking it restores the existing main window and its previous window state. It closes with the main window and does not add autostart, persisted settings, reserved desktop space or another workspace session.
