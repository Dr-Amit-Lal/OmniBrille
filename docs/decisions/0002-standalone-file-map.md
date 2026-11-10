# ADR 0002: Separate saved standalone file map

Status: Superseded for the current product by [ADR 0006](0006-local-spatial-workspace.md). Original decision and rationale follow.

## Decision

Use a new MyFilesWindow/MyFilesSession for an accessible multi-folder map. Retain MainWindow/ExplorerSession for the original advanced explorer and connected handoffs. Persist only explicitly chosen local workspace metadata in a separate versioned store.

## Reason

ExplorerSession has a single provider and focused neighborhood. Multiple expanded roots and remembered local paths would complicate its connected authority and privacy contracts. A dedicated local workspace can reuse the existing filesystem provider and safe activation service while leaving opaque connected IDs outside persistence.

## Consequences

The custom startup and workspace persistence differ from upstream v1.2.0. Documentation and screenshots must distinguish these experiences. Additional ordinary-path validation is necessary because restored expansions may bypass the original UI's non-navigable reparse-point checks. Native controls provide a simpler keyboard and automation surface; the existing animated radial renderer remains in Advanced explorer.
