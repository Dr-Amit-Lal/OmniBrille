# OmniBrille 1.2.0-nebula.5 · Spatial Explorer

This update calms the spatial map while retaining the saved local folder workspace. Generated artifact notes bind this guidance to the exact installer hash and commit. No test, publication or native acceptance result is implied by this template.

## Install

Use `OmniBrille-1.2.0-nebula.5-win-x64-setup.exe` and its matching checksum/manifest from the custom build. Run as your normal Windows user, then open **OmniBrille Spatial Explorer** from Start Menu. The self-contained package installs under `%LOCALAPPDATA%\Programs\OmniBrille`; no administrator rights or separate .NET runtime are required.

```powershell
Get-FileHash .\OmniBrille-1.2.0-nebula.5-win-x64-setup.exe -Algorithm SHA256
```

Preview packages are **unsigned**. Windows may show Unknown Publisher or SmartScreen. Compare the checksum with the matching artifact files; this verifies bytes, not publisher identity.

## Changes

- Save up to 8 chosen roots and 12 independent folder pins in one local workspace.
- Explore separate root clusters; Back/Up moves toward the workspace through real containing folders.
- Use smaller folders and labels with muted, thin straight connections that stay clear of text.
- Hover marks just one folder/file with a soft pulse and label highlight. Search marks the nearest visible ancestor without adding branches or moving the map.
- Remove the redundant Open folder node, enlarging hover lens, floating positions, gold preview routes and dust. Click the parent folder itself to enter; previous/next paging retains access to all contents.
- Restore saved locations on launch, migrate older explicit root/pin settings, and retain unavailable drives for retry.
- Refresh visible data through root-scoped change notifications and bounded periodic/manual reads.
- Keep Nebula/Light appearance, reduced motion/effects and the temporary hide-controls view.
- Remove My Files, voice/audio, OmniSorSe/Context/Hybrid, old diagnostics and their runtime/package dependencies.

Pins outside chosen roots open a temporary exact-folder scope; they do not add search/watch roots. Files remain in place. Selected files open only through a deliberate supported action and path validation.

Upgrade removes known retired Voice/protocol/NAudio assets and the obsolete launcher while preserving local settings and user files. Unknown install-directory content is retained. Uninstall leaves the workspace settings under `%LOCALAPPDATA%\OmniBrille`.

## Evidence and limits

The Windows build/lifecycle workflows verify the artifact when run successfully. Their synthetic retired-file fixture is not a full predecessor-upgrade test. Record native Windows usability, DPI/GPU, screen-reader, OneDrive hydration and real upgrade results separately; this preview does not inherit those claims from upstream v1.2.0.

The application has no telemetry, content analysis, service, automatic updater or cloud-account connection. Name/path search is bounded and can return partial results. Missing drives or unavailable cloud files are reported without discarding saved locations.

Project code is distributed under the **MIT License**. The installed project license and component notices remain authoritative. The reviewed DNG-free SkiaSharp native asset is unchanged; see [native provenance](native-skia.md), [privacy](SECURITY-PRIVACY.md) and [packaging](PACKAGING.md).
