# Architecture decision records

ADRs explain consequential, durable choices. They do not replace [`architecture.md`](../architecture.md), which describes how OmniBrille works now.

Use an ADR when a choice changes subsystem authority, state ownership, dependency direction, protocol/persistence/public contracts, renderer/layout strategy, or another costly-to-reverse boundary that history is likely to reopen. Do not write ADRs for ordinary implementation details or every bug fix.

Historical decisions reconstructed after the fact must say **Reconstructed from repository evidence** and cite the supporting commits/source/tests. When a decision changes, add or update its status and link the replacement; do not rewrite history as if the original context never existed.

| ADR | Status | Decision |
| --- | --- | --- |
| [0006](0006-local-spatial-workspace.md) | Accepted | One local saved spatial workspace; remove retired runtime/modes |
| [0005](0005-pinned-folders.md) | Superseded by 0006 | Earlier standalone pin sidebar |
| [0004](0004-spatial-only-nebula.md) | Superseded by 0006 | Earlier sole spatial interface and hide-controls behavior |
| [0003](0003-bounded-descendant-previews.md) | Superseded by 0006 | Earlier bounded descendant previews |
| [0002](0002-bounded-deterministic-scenes.md) | Superseded by 0006 | Earlier Structure/Context/Hybrid scenes |
| [Standalone file map](0002-standalone-file-map.md) | Superseded by 0006 | Earlier My Files session/window |
| [0001](0001-separate-provider-authorities.md) | Superseded by 0006 | Earlier local/connected provider separation |

## Minimal template

```markdown
# ADR NNNN: Title

Status: Proposed | Accepted | Superseded by ADR NNNN

Reconstructed from repository evidence. <!-- only when applicable -->

## Context
## Decision
## Reasoning
## Consequences
## Rejected alternatives
## Evidence
```
