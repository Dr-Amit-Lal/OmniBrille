# Windows packaging

Current source version: **`1.2.0-nebula.5`**. One per-user Windows x64 installer opens the local Spatial Explorer. Upstream v1.2.0 artifacts and earlier hosted results do not qualify these bytes.

## Package and installer identity

The pinned Inno Setup 6.7.3 build installs at `%LOCALAPPDATA%\Programs\OmniBrille`, keeps the stable application ID, and creates one **OmniBrille Spatial Explorer** Start Menu shortcut and one uninstall registration. It requests no administrator rights. The application is self-contained, non-trimmed and multi-file; no separate .NET runtime is required.

`Directory.Build.props` is authoritative: semantic/informational version `1.2.0-nebula.5`, assembly/file version `1.2.0.0`. The installer fallback matches it. Launch accepts `--root` and `--theme`; no handoff/runtime audio capability is packaged.

```powershell
.\build\Package-Windows.ps1 -BootstrapInnoSetup
```

The output set uses the stem `OmniBrille-1.2.0-nebula.5-win-x64-setup`: `.exe`, `.exe.sha256`, `-manifest.json`, `-dependencies.json`, and `-release-notes.md`. Keep all files from the same build together. The generated notes bind the exact installer and incorporate [current release guidance](release-notes.md).

## Contents and provenance

Schema **6** records `productMode: local-workspace`, installer identity/hash/size/signature, source commit/URL, build time, runtime/deployment, installed notice binding and native SkiaSharp provenance. It no longer claims an Explorer Protocol or voice bundle. The dependency document is a sanitized dependency graph, not an exact packaged-file inventory or a formal SBOM.

The package retains the project MIT license, Avalonia/font/runtime notices and the reviewed DNG-free SkiaSharp 3.119.4 native asset. [Native Skia provenance](native-skia.md) defines its source pins, package/DLL hashes, unsigned status and exclusion checks. Do not regenerate or replace that asset as an incidental packaging change.

Voice binaries/models, NAudio and OmniSorSe protocol binaries are absent. Publish/release/lifecycle gates reject retired payloads as well as source/test files, debug symbols, databases, logs, keys and raw audio. The installed app has no service, autostart entry, updater, file association or telemetry component.

## Upgrade and uninstall

Upgrade removes the obsolete My Files shortcut. A fixed allowlist removes known retired Voice runtime/model/manifest/license files, NAudio DLLs and the protocol DLL. Each retired path and its parents through the install directory are checked for reparse points before deletion. Only empty retired directories are removed; unknown content is preserved. No recursive wildcard cleanup targets user data or saved settings.

Workspace/preferences remain below `%LOCALAPPDATA%\OmniBrille`, outside install ownership, and survive upgrade/uninstall. On launch the new store can migrate explicitly saved roots/pins from older settings. The old JSON files remain intact. Removing a pin/root or uninstalling never deletes its folder.

The shared `build/Test-WindowsInstallerLifecycle.ps1` runs after packaging in the standard Windows CI job and in the private-preview artifact-only job. It validates installer/manifest/hash/native-notice identity, seeds known retired assets for cleanup, checks unknown-file and workspace/preference preservation, launches Spatial Explorer, closes/relaunches it, and uninstalls. These seeded files are clearly labeled a **synthetic fixture**. This is not a full previous-version upgrade test. Native in-place upgrade, reparse/unknown-content behavior and interactive usability must be recorded separately with the exact installer hash.

## Signing and verification

Custom preview packages are **unsigned** unless the explicitly configured signing path is used. Windows may show Unknown Publisher or SmartScreen. SHA-256 verifies the artifact bytes; it does not authenticate an unsigned publisher. `RequireSigning` fails closed without a suitable externally supplied certificate; keys/passwords do not belong in source or artifacts.

`build/verify-release.ps1` is the clean-checkout release gate: documentation, formatting/build/tests, dependency audit, packaging, notices, prohibited payloads, hashes, signing state and native-asset integrity. The artifact-only hosted lifecycle verifies the exact candidate, per-user install, shortcuts/registration/version, normal launch/close/relaunch and uninstall. Record outcomes instead of treating configured checks as passed results.

No nebula.5 native Windows lifecycle, visual/DPI/GPU, screen-reader or OneDrive hydration result is implied by this document. [Release checklist](../RELEASE_CHECKLIST.md) and [testing](testing.md) track the remaining evidence.
