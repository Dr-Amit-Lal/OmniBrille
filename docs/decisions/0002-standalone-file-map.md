# ADR 0002: Separate saved standalone file map

Status: Superseded for application startup by [ADR 0004](0004-spatial-only-nebula.md). The separate workspace implementation remains dormant and its data is preserved.

## Decision

Use a new MyFilesWindow/MyFilesSession for an accessible multi-folder map. Retain MainWindow/ExplorerSession for the original advanced explorer and connected handoffs. Persist only explicitly chosen local workspace metadata in a separate versioned store.

## Reason

ExplorerSession has a single provider and focused neighborhood. Multiple expanded roots and remembered local paths would complicate its connected authority and privacy contracts. A dedicated local workspace can reuse the existing filesystem provider and safe activation service while leaving opaque connected IDs outside persistence.

## Consequences

The custom startup and workspace persistence differ from upstream v1.2.0. Documentation and screenshots must distinguish these experiences. Additional ordinary-path validation is necessary because restored expansions may bypass the original UI's non-navigable reparse-point checks. Native controls provide a simpler keyboard and automation surface; the existing animated radial renderer remains in Advanced explorer.
