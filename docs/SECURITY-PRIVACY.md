# Security and privacy posture

`1.2.0-nebula.4` is a local-only folder workspace. It contains no voice capture/runtime, sound output, OmniSorSe transport, Context/Hybrid mode, telemetry, upload service, background service, auto-start task, updater or destructive file operation.

## Local access and saved data

Users explicitly choose up to 8 workspace roots and 12 independent folder pins. Only those locations authorize reads. Search spans chosen roots; an outside pin permits temporary browsing under that exact folder without expanding monitored/search roots. Parent navigation stops at the authorized boundary and returns to the workspace.

`spatial-workspace.json` stores paths, labels, pin order and current authorized location under `%LOCALAPPDATA%\OmniBrille`. These paths may reveal private folder names; the file is local profile data, not anonymous telemetry. Existing explicit roots/pins migrate once if the new file is absent. Missing drives remain saved. No recommended location is silently added and no user content is moved.

Queries, results, transient hover paths, file contents and graph view transforms are not persisted. `visual-preferences.json` retains only theme and reduced motion/effects. Old audio/voice/diagnostics values are ignored and scrubbed from loaded preferences. Legacy workspace files remain unchanged for recovery.

## Reads, refresh and cloud folders

The filesystem provider reads directory metadata and performs bounded name/path search. It does not analyze document contents or build a persistent whole-computer index. While open, the app watches chosen roots recursively and periodically requests bounded refresh of the displayed scene and active search. Watchers are disposed on close; saved pins alone do not add watch scopes.

Paths are normalized and checked against their authorized root. Symbolic links, junctions and unsafe reparse traversal are rejected. Only the Windows Cloud Files tags `IO_REPARSE_TAG_CLOUD` and `CLOUD_1` through `CLOUD_F` are recognized as cloud placeholders. Tag inspection opens metadata without following the reparse point or requesting file data/recall. Unknown tags, query failures and non-Windows reparse points fail closed. Root and ancestor checks still apply; native availability and hydration behavior remain platform-dependent. No cloud account/API integration exists. Explicit file opening may cause the operating system/cloud provider to hydrate content; installed OneDrive acceptance is not claimed.

## Explicit file opening

Selecting or previewing a file does not launch it. Open file, deliberate graph file activation or keyboard invocation passes a revalidated ordinary-file path to the operating system. Search-result activation first opens the containing folder and selects the file.

`ShellFileActivationService` rejects out-of-root/unsafe paths and blocked executable, script, installer, shortcut and URL-like file types. It constructs no shell command from query text. Opening a permitted document invokes its associated application; subsequent application behavior is outside this explorer's control.

## Installer and sharing boundaries

The current-user installer owns only application files under `%LOCALAPPDATA%\Programs\OmniBrille`. Known obsolete audio/protocol files are removed by an exact allowlist with reparse checks; unknown files and local workspace settings are retained. Release gates reject retired payloads, source/test artifacts, logs/databases, keys/raw audio and developer paths, and preserve exact native-asset/license provenance.

Preview installers are unsigned. Verify matching checksums/manifests; a checksum is integrity evidence, not publisher authentication. The optional signing path requires an external certificate and verifies its result.

The old Copy safe diagnostics UI is removed. When reporting a problem, share version, environment and reproducible steps with synthetic paths. Review screenshots/settings before sharing because visible filenames and saved root/pin paths are personal data. The app sends nothing automatically.
