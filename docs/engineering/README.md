# Engineering start here

Use current source and observable behavior before tests, Git history, build configuration, current prose, historical reports and inference. `1.2.0-nebula.6` is one local spatial workspace. Retired voice/protocol/Context/My Files guides are historical, not implementation instructions.

## Authority map

| Need | Current authority |
| --- | --- |
| Product and install | [README](../../README.md) |
| Ownership, state, search/preview and refresh | [Architecture](../architecture.md) |
| Terms and action parity | [Glossary](../glossary.md), [interaction contract](../interaction-state-contract.md) |
| Local path and saved-data boundaries | [Security/privacy](../SECURITY-PRIVACY.md), provider/guard/store source |
| Packaging, support, release evidence | [Packaging](../PACKAGING.md), [compatibility](../../COMPATIBILITY.md), [checklist](../../RELEASE_CHECKLIST.md) |
| Native dependency provenance | [DNG-free SkiaSharp](../native-skia.md) |
| Architectural rationale | [ADR 0006](../decisions/0006-local-spatial-workspace.md), [decision index](../decisions/README.md) |
| Risk and checks | [Risk/validation](risk-and-validation.md), [testing](../testing.md) |
| Retrospectives and historical evidence | [Learning/reports](learning-and-reports.md), [history/lessons](history-and-lessons.md), [run index](../runs/README.md) |

## Load only relevant context

| Change | Read and validate |
| --- | --- |
| Session/navigation/search/preview | `SpatialWorkspaceSession`, current state sections and session/headless tests; cancellation/generation and root-removal races |
| Persistence/root/pin behavior | `SpatialWorkspace`, `JsonSpatialWorkspaceStore`, retained legacy migration stores, migration/pin/path tests |
| Filesystem/cloud paths | `FileSystemExplorerProvider`, `FileMapPathGuard`, cloud-entry classifier and activation policy; native platform limitations |
| UI/keyboard/accessibility | `MainWindow`, pin partial, graph automation and `WorkspaceWindowTests`; resulting state and full action path |
| Layout/renderer | `WorkspaceGraphLayout`, `GraphSceneControl`, shared geometry/policies and renderer tests; visible labels, caps and caches |
| Package/release | Installer/build/workflow scripts, notices/native pins and package/release tests; exact artifact lifecycle |
| Documentation | Affected current contract, link/fence check and historical scoping; do not rewrite run evidence |

## Repository map

- `src/OmniBrille.Core`: local workspace/legacy migration models, generic structural graph contracts, deterministic layouts/policies and visual preferences.
- `src/OmniBrille.Infrastructure`: local provider, root/cloud-entry guards, stores and explicit file activation.
- `src/OmniBrille.Desktop`: one shell, authoritative `SpatialWorkspaceSession`, renderer/input/automation and watcher lifecycle.
- `tests/OmniBrille.Tests`: workspace/session, provider/path, migration, structural and release contracts.
- `tests/OmniBrille.HeadlessTests`: current workspace UI/renderer interaction tests.

The former protocol project, old session/window, voice/audio and Context/Hybrid implementations are removed. Their retained historical guides and source links at prior commits are evidence only.

## Change flow

Establish branch/HEAD/worktree and preserve unrelated changes. Define the observable contract, classify risk, assign one implementation owner per file, implement and run proportional checks. Review high-risk changes independently. Update affected contracts/diagrams/decisions and report exact verification limits. A substantial run follows [learning and reports](learning-and-reports.md); no prior report substitutes for current evidence.
