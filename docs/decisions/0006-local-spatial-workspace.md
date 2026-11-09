# ADR 0006: One local spatial workspace

Status: Accepted for `1.2.0-nebula.4`; supersedes the live behavior of ADRs 0001–0005 and the former standalone-file-map decision where they conflict.

## Context

The owner chose a focused local folder workspace with several saved roots, independent pins and Search path previews. Keeping dormant voice, connection modes and a second application/session model made the product boundary and release payload misleading.

## Decision

Use one `MainWindow` and `SpatialWorkspaceSession`. Save up to 8 explicitly chosen roots, 12 pins and the current authorized location in `spatial-workspace.json`. Migrate only older explicit root/pin records when the new file is absent; do not infer recommended roots. Retain legacy stores/models only for that migration.

Show roots as separate bounded clusters. Back/Up follows containing folders toward Workspace. Pins inside roots use the deepest containing root; outside pins create a temporary exact-folder scope without changing monitored roots. Search spans chosen roots only. Hover/keyboard result preview is temporary and reversible; activating a file result reveals its containing folder and selects it before explicit file opening.

Monitor chosen roots while the app is open and use coalesced/periodic bounded refresh. Do not maintain a content index, background service or cloud account connection. Keep theme and reduced motion/effects; interface visibility is transient.

Delete My Files UI/session, old ExplorerSession, voice/capture/parser/runtime, sound, diagnostics, OmniSorSe/handoff/protocol and Context/Hybrid implementation/tests/dependencies. Keep reusable structural models, provider boundaries, renderer helpers and their tests. Remove audio assets from installer/release manifests; clean only known obsolete install assets with reparse-safe exact paths.

## Consequences

The persistent privacy boundary now includes chosen root/pin paths and current location. Saved missing drives remain recoverable. Generation checks bind asynchronous work to the current workspace and preserve newer pin/root edits. Search previews and scene membership share the 64-node cap.

The manifest moves to schema 6 with local-workspace identity. Native Windows upgrade/cloud hydration/screen-reader evidence must be gathered for the new product; earlier voice/protocol or software-render results do not transfer. Historical guides/reports remain clearly scoped to their original versions.

## Evidence

- [Session](../../src/OmniBrille.Desktop/Presentation/SpatialWorkspaceSession.cs)
- [Workspace model](../../src/OmniBrille.Core/SpatialWorkspace.cs) and [store](../../src/OmniBrille.Infrastructure/JsonSpatialWorkspaceStore.cs)
- [Current architecture](../architecture.md), [interaction contract](../interaction-state-contract.md), [packaging](../PACKAGING.md)

## Nebula.5 visual refinement

Search preview now marks only an existing visible node or nearest ancestor within the same root. It never expands topology. The renderer removes hover enlargement, floating positions, route glows and particles. Thin straight edges are clipped around glyphs and labels; only the local icon glow and label background respond to hover. Overview roots open directly without a synthetic Open folder node. State schema, path authorization and explicit file activation are unchanged.
