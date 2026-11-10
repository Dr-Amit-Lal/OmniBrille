[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ArtifactDirectory,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $ExpectedCommit,
    [bool] $ExpectedSigned = $false
)

# Hosted Windows smoke test. Requires fresh app/settings paths and intentionally creates
# synthetic retired/unknown files; it does not claim a previous-version upgrade.
if (-not $IsWindows) { throw 'Installer lifecycle verification requires Windows.' }
if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { throw 'Installer lifecycle verification requires a dedicated runner temporary directory.' }

function Wait-ForSavedWorkspace {
    param(
        [Diagnostics.Process] $ApplicationProcessToCheck,
        [string] $WorkspacePathToCheck,
        [string] $ExpectedRootPath
    )
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $ApplicationProcessToCheck.Refresh()
        if ($ApplicationProcessToCheck.HasExited) { throw 'Installed application exited before its workspace was ready.' }
        if (Test-Path -LiteralPath $WorkspacePathToCheck -PathType Leaf) {
            try {
                $savedWorkspace = Get-Content -Raw -LiteralPath $WorkspacePathToCheck | ConvertFrom-Json -ErrorAction Stop
                if (@($savedWorkspace.Roots).Count -eq 1 -and
                    [string]::Equals([string]$savedWorkspace.Roots[0].Path, $ExpectedRootPath, [StringComparison]::OrdinalIgnoreCase)) {
                    return
                }
            }
            catch {
                # Initial save/replacement may still be in flight. Retry only within the bound.
            }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    throw 'Installed application did not persist the explicit smoke root before timeout.'
}

$ErrorActionPreference = 'Stop'
$artifactRoot = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$installer = @(Get-ChildItem -LiteralPath $artifactRoot -Recurse -File -Filter 'OmniBrille-*-win-x64-setup.exe')
if ($installer.Count -ne 1) { throw "Expected one installer, found $($installer.Count)." }
$installer = $installer[0]
$sidecar = Get-Item -LiteralPath "$($installer.FullName).sha256"
$manifestPaths = @(Get-ChildItem -LiteralPath $artifactRoot -Recurse -File -Filter '*-setup-manifest.json')
if ($manifestPaths.Count -ne 1) { throw "Expected one release manifest, found $($manifestPaths.Count)." }
$manifestPath = $manifestPaths[0]
$manifest = Get-Content -Raw -LiteralPath $manifestPath.FullName | ConvertFrom-Json
$expectedInstallerName = "OmniBrille-$($manifest.version)-win-x64-setup.exe"
if ($manifest.product -ne 'OmniBrille' -or $manifest.runtimeIdentifier -ne 'win-x64') {
  throw 'Manifest product or runtime does not identify the expected Windows x64 release.'
}
if ($manifest.schemaVersion -ne 6 -or
    $manifest.productMode -ne 'local-workspace' -or
    $manifest.projectLicenseExpression -ne 'MIT' -or
    $manifest.sourceUrl -ne "https://github.com/Dr-Amit-Lal/OmniBrille/tree/$ExpectedCommit") {
  throw 'Manifest schema, project license, or source/provenance URL does not match the release commit.'
}
if ($installer.Name -ne $manifest.installer.fileName -or
    $installer.Name -ne $expectedInstallerName -or
    $installer.Length -ne [long]$manifest.installer.bytes) {
  throw 'Installer filename or byte length does not match the manifest.'
}
if ([bool]$manifest.installer.signed -ne $expectedSigned) {
  throw 'Manifest signing state does not match the requested workflow mode.'
}
$actualHash = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
$sidecarHash = (((Get-Content -Raw -LiteralPath $sidecar.FullName).Trim()) -split '\s+')[0].ToUpperInvariant()
if ($actualHash -ne $sidecarHash -or $actualHash -ne $manifest.installer.sha256.ToUpperInvariant()) {
  throw 'Independent, sidecar, and manifest installer hashes do not agree.'
}
if ($manifest.commitSha -ne $ExpectedCommit) { throw 'Manifest commit does not match the workflow commit.' }

$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\OmniBrille'
if (Test-Path -LiteralPath $installDirectory) { throw 'The hosted install directory is not fresh.' }
# Synthetic retired assets exercise upgrade cleanup; this is not a previous-version upgrade run.
$settingsDirectory = Join-Path $env:LOCALAPPDATA 'OmniBrille'
if (Test-Path -LiteralPath $settingsDirectory) { throw 'The hosted settings directory is not fresh; refusing to replace existing preferences.' }
[void](New-Item -ItemType Directory -Path $settingsDirectory)
$preferencesPath = Join-Path $settingsDirectory 'visual-preferences.json'
$workspacePath = Join-Path $settingsDirectory 'spatial-workspace.json'
Set-Content -LiteralPath $preferencesPath -Value '{"Theme":"Light","ReducedMotion":true,"ReducedEffects":true}'
$preferencesHash = (Get-FileHash -LiteralPath $preferencesPath -Algorithm SHA256).Hash
$unknownAssetFixtures = @('Voice\Runtime\keep-user-note.txt', 'keep-user-note.txt')
$unknownFixtureContent = 'Unknown user content must survive install and uninstall.'
foreach ($unknownAsset in $unknownAssetFixtures) {
    $fixturePath = Join-Path $installDirectory $unknownAsset
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $fixturePath) -Force)
    Set-Content -LiteralPath $fixturePath -Value $unknownFixtureContent
}

$retiredAssetFixtures = @(
  'Voice\Runtime\ggml-base.dll',
  'Voice\Runtime\ggml-cpu-alderlake.dll',
  'Voice\Runtime\ggml-cpu-cannonlake.dll',
  'Voice\Runtime\ggml-cpu-cascadelake.dll',
  'Voice\Runtime\ggml-cpu-haswell.dll',
  'Voice\Runtime\ggml-cpu-icelake.dll',
  'Voice\Runtime\ggml-cpu-sandybridge.dll',
  'Voice\Runtime\ggml-cpu-skylakex.dll',
  'Voice\Runtime\ggml-cpu-sse42.dll',
  'Voice\Runtime\ggml-cpu-x64.dll',
  'Voice\Runtime\ggml.dll',
  'Voice\Runtime\whisper-cli.exe',
  'Voice\Runtime\whisper.dll',
  'Voice\Models\ggml-base.en-q5_1.bin',
  'Voice\voice-bundle-manifest.json',
  'Voice\whisper.cpp-MIT.txt',
  'Voice\OpenAI-Whisper-MIT.txt',
  'OmniSorSe.ExplorerProtocol.dll',
  'NAudio.Core.dll',
  'NAudio.WinMM.dll',
  'THIRD-PARTY-LICENSES\whisper.cpp-MIT.txt',
  'THIRD-PARTY-LICENSES\OpenAI-Whisper-MIT.txt'
)
foreach ($retiredAsset in $retiredAssetFixtures) {
  $fixturePath = Join-Path $installDirectory $retiredAsset
  [void](New-Item -ItemType Directory -Path (Split-Path -Parent $fixturePath) -Force)
  Set-Content -LiteralPath $fixturePath -Value 'retired installer asset fixture'
}

$install = Start-Process -FilePath $installer.FullName -ArgumentList @(
  '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER'
) -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "Installer exited with code $($install.ExitCode)." }

foreach ($retiredAsset in $retiredAssetFixtures) {
  if (Test-Path -LiteralPath (Join-Path $installDirectory $retiredAsset)) {
    throw "Retired installer asset '$retiredAsset' was not removed."
  }
}
foreach ($unknownAsset in $unknownAssetFixtures) {
    if (-not (Test-Path -LiteralPath (Join-Path $installDirectory $unknownAsset) -PathType Leaf) -or
        (Get-Content -Raw -LiteralPath (Join-Path $installDirectory $unknownAsset)).Trim() -ne $unknownFixtureContent) {
        throw "Unknown install-directory content '$unknownAsset' was changed or removed."
    }
}
if ((Get-FileHash -LiteralPath $preferencesPath -Algorithm SHA256).Hash -ne $preferencesHash) {
    throw 'Installer changed user preferences.'
}
$application = Join-Path $installDirectory 'OmniBrille.exe'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\OmniBrille.lnk'
if (-not (Test-Path -LiteralPath $application -PathType Leaf)) { throw 'Installed executable was not found.' }
if (Test-Path -LiteralPath $shortcut) { throw 'Obsolete My Files shortcut remains after upgrade.' }
$spatialShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\OmniBrille Spatial Explorer.lnk'
if (-not (Test-Path -LiteralPath $spatialShortcut -PathType Leaf)) { throw 'Spatial Explorer shortcut was not found.' }
$shell = New-Object -ComObject WScript.Shell
$spatialLink = $shell.CreateShortcut($spatialShortcut)
if ($spatialLink.TargetPath -ne $application -or -not [string]::IsNullOrWhiteSpace($spatialLink.Arguments)) { throw 'Spatial Explorer shortcut does not launch the installed spatial workspace.' }
[void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($spatialLink)
[void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
$requiredNotices = @(
  'LICENSE',
  'THIRD-PARTY-NOTICES.txt',
  'THIRD-PARTY-LICENSES\ANGLE-LICENSE.txt',
  'THIRD-PARTY-LICENSES\Avalonia-LICENSE.txt',
  'THIRD-PARTY-LICENSES\DotNet-LICENSE.txt',
  'THIRD-PARTY-LICENSES\DotNet-Runtime-THIRD-PARTY-NOTICES.txt',
  'THIRD-PARTY-LICENSES\Inter-OFL-1.1.txt',
  'THIRD-PARTY-LICENSES\MicroCom-LICENSE.txt',
  'THIRD-PARTY-LICENSES\SkiaSharp-HarfBuzz-LICENSE.txt',
  'THIRD-PARTY-LICENSES\SkiaSharp-HarfBuzz-THIRD-PARTY-NOTICES.txt',
  'THIRD-PARTY-LICENSES\System.IO.Pipelines-THIRD-PARTY-NOTICES.txt',
  'THIRD-PARTY-LICENSES\Tmds.DBus-LICENSE.txt'
)
foreach ($requiredNotice in $requiredNotices) {
  if (-not (Test-Path -LiteralPath (Join-Path $installDirectory $requiredNotice) -PathType Leaf)) {
    throw "Required installed distribution notice '$requiredNotice' was not found."
  }
}
$installedSkiaNotice = Join-Path $installDirectory 'THIRD-PARTY-LICENSES\SkiaSharp-HarfBuzz-THIRD-PARTY-NOTICES.txt'
$installedSkiaNoticeHash = (Get-FileHash -LiteralPath $installedSkiaNotice -Algorithm SHA256).Hash.ToUpperInvariant()
if ($manifest.distributionNotices.skiaSharpNotice.path -ne 'THIRD-PARTY-LICENSES/SkiaSharp-HarfBuzz-THIRD-PARTY-NOTICES.txt' -or
    $manifest.distributionNotices.skiaSharpNotice.sha256.ToUpperInvariant() -ne $installedSkiaNoticeHash) {
  throw 'Installed DNG-free SkiaSharp notice does not match the reviewed bytes bound by the release manifest.'
}
$installedSkiaNoticeText = Get-Content -Raw -LiteralPath $installedSkiaNotice
if ($installedSkiaNoticeText.IndexOf('OMNIBRILLE DNG-FREE SKIASHARP 3.119.4 NOTICE', [StringComparison]::Ordinal) -lt 0) {
  throw 'Installed Skia notice is missing the reviewed DNG-free provenance header.'
}
foreach ($forbiddenDngNotice in @('DNG SDK License Agreement', '# DNG SDK', '# piex', 'external/dng_sdk', 'external/piex')) {
  if ($installedSkiaNoticeText.IndexOf($forbiddenDngNotice, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    throw "Installed Skia notice still claims excluded component '$forbiddenDngNotice'."
  }
}
$installedSkia = Join-Path $installDirectory 'libSkiaSharp.dll'
$installedSkiaHash = (Get-FileHash -LiteralPath $installedSkia -Algorithm SHA256).Hash.ToUpperInvariant()
if ($manifest.nativeComponents.skiaSharp.nativePackageId -ne 'OmniBrille.SkiaSharp.NativeAssets.Win32.NoDng' -or
    $manifest.nativeComponents.skiaSharp.nativeDllSha256.ToUpperInvariant() -ne $installedSkiaHash -or
    $manifest.nativeComponents.skiaSharp.authenticodeStatus -ne 'NotSigned' -or
    [bool]$manifest.nativeComponents.skiaSharp.dngSdkIncluded -or
    (Get-AuthenticodeSignature -LiteralPath $installedSkia).Status -ne 'NotSigned') {
  throw 'Installed Skia native binary does not match the manifest-bound DNG-free unsigned asset.'
}
$installedSkiaBinaryText = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($installedSkia))
foreach ($forbiddenNativeMarker in @('dng_pixel_buffer', 'dng_negative', 'dng_priority_manager', 'dng_sdk', 'DNG SDK', 'SkRawCodec', 'SkDngHost', 'SkDngImage', '.?AVdng_')) {
  if ($installedSkiaBinaryText.IndexOf($forbiddenNativeMarker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    throw "Installed Skia native binary contains excluded DNG/raw marker '$forbiddenNativeMarker'."
  }
}
$installedLicense = Get-Content -Raw -LiteralPath (Join-Path $installDirectory 'LICENSE')
if ($installedLicense -notmatch 'MIT License' -or
    $installedLicense -notmatch 'Copyright \(c\) 2026 OmniBrille Contributors' -or
    $installedLicense -notmatch 'Permission is hereby granted, free of charge' -or
    $installedLicense -notmatch 'THE SOFTWARE IS PROVIDED "AS IS"') {
  throw 'Installed project license is not the expected MIT text.'
}
$uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
$registration = @(Get-ChildItem -LiteralPath $uninstallRoot | ForEach-Object {
  Get-ItemProperty -LiteralPath $_.PSPath
} | Where-Object { $_.DisplayName -eq 'OmniBrille' })
if ($registration.Count -ne 1) { throw "Expected one uninstall registration, found $($registration.Count)." }
$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($application)
if ($versionInfo.FileVersion -ne $manifest.fileVersion -or
    $versionInfo.ProductVersion -ne $manifest.version) {
  throw "Installed version metadata does not match the manifest: '$($versionInfo.FileVersion)' / '$($versionInfo.ProductVersion)'."
}
$installerSignature = Get-AuthenticodeSignature -LiteralPath $installer.FullName
$applicationSignature = Get-AuthenticodeSignature -LiteralPath $application
if ($manifest.installer.signed) {
  if ($installerSignature.Status -ne 'Valid' -or $applicationSignature.Status -ne 'Valid') {
    throw 'Signed candidate did not retain valid installer and application signatures.'
  }
}
elseif ($manifest.installer.signatureStatus -ne 'NotSigned' -or
        $installerSignature.Status -ne 'NotSigned' -or
        $applicationSignature.Status -ne 'NotSigned') {
  throw "Unsigned candidate must report NotSigned for manifest, installer, and installed application: '$($manifest.installer.signatureStatus)' / '$($installerSignature.Status)' / '$($applicationSignature.Status)'."
}
$retiredPayload = @(Get-ChildItem -LiteralPath $installDirectory -Recurse -File | Where-Object {
  $_.Name -like 'NAudio*.dll' -or $_.Name -like 'OmniSorSe*.dll'
})
if ($retiredPayload.Count -gt 0) {
  throw 'Retired voice or connected payload remains in the installed application.'
}
$obsoleteRuntimeDiagnostics = @(
  Get-ChildItem -LiteralPath $installDirectory -File -Filter 'mscordaccore_*.dll'
)
if ($obsoleteRuntimeDiagnostics.Count -gt 0) {
  throw "Obsolete runtime-diagnostics files were found in the release installation: $($obsoleteRuntimeDiagnostics.Name -join ', ')"
}

$smokeRoot = Join-Path $env:RUNNER_TEMP 'OmniBrilleCleanSmoke'
New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null
Set-Content -LiteralPath (Join-Path $smokeRoot 'readme.txt') -Value 'Controlled clean-runner smoke fixture.'
$applicationProcess = Start-Process -FilePath $application -ArgumentList @(
  '--root', ('"' + $smokeRoot + '"'), '--theme', 'Light'
) -PassThru
$deadline = (Get-Date).AddSeconds(20)
do {
  Start-Sleep -Milliseconds 500
  $applicationProcess.Refresh()
} until (
  $applicationProcess.HasExited -or
  -not [string]::IsNullOrWhiteSpace($applicationProcess.MainWindowTitle) -or
  (Get-Date) -gt $deadline
)
if ($applicationProcess.HasExited) { throw 'Installed application exited before the smoke window became ready.' }
if ($applicationProcess.MainWindowTitle -ne 'Spatial Explorer') {
  throw "Installed application did not expose the expected window title: '$($applicationProcess.MainWindowTitle)'."
}
Wait-ForSavedWorkspace -ApplicationProcessToCheck $applicationProcess -WorkspacePathToCheck $workspacePath -ExpectedRootPath $smokeRoot

# Exercise the installed screen-edge tab using the actual Win32 windows. This
# checks minimize, edge placement, and pointer activation, not physical DPI or
# a full interactive feature sweep.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class OmniBrilleDockProbe {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    public static string DescribeWindows(uint processId) {
        var result = new StringBuilder();
        EnumWindows((window, data) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId) {
                var title = new StringBuilder(256);
                GetWindowText(window, title, title.Capacity);
                result.AppendFormat("[handle={0}, visible={1}, iconic={2}, title={3}] ", window, IsWindowVisible(window), IsIconic(window), title);
            }
            return true;
        }, IntPtr.Zero);
        return result.ToString();
    }
}
'@
$mainHandle = $applicationProcess.MainWindowHandle
if ($mainHandle -eq [IntPtr]::Zero) { throw 'Installed application has no main window handle.' }
[void][OmniBrilleDockProbe]::ShowWindow($mainHandle, 6) # SW_MINIMIZE
$deadline = (Get-Date).AddSeconds(15)
$dockHandle = [IntPtr]::Zero
do {
  Start-Sleep -Milliseconds 200
  $candidate = [OmniBrilleDockProbe]::FindWindow($null, 'Spatial Explorer tab')
  $candidatePid = [uint32]0
  if ($candidate -ne [IntPtr]::Zero) {
    [void][OmniBrilleDockProbe]::GetWindowThreadProcessId($candidate, [ref]$candidatePid)
    if ($candidatePid -eq $applicationProcess.Id -and [OmniBrilleDockProbe]::IsWindowVisible($candidate)) { $dockHandle = $candidate }
  }
} until (($dockHandle -ne [IntPtr]::Zero -and [OmniBrilleDockProbe]::IsIconic($mainHandle)) -or (Get-Date) -gt $deadline)
if ($dockHandle -eq [IntPtr]::Zero -or -not [OmniBrilleDockProbe]::IsIconic($mainHandle)) {
  $applicationProcess.Refresh()
  $windows = [OmniBrilleDockProbe]::DescribeWindows([uint32]$applicationProcess.Id)
  throw "Minimizing the installed app did not expose the edge tab. Main iconic: $([OmniBrilleDockProbe]::IsIconic($mainHandle)); process exited: $($applicationProcess.HasExited); candidate: $candidate; candidate PID: $candidatePid; windows: $windows"
}
$dockRect = [OmniBrilleDockProbe+RECT]::new()
$clientRect = [OmniBrilleDockProbe+RECT]::new()
$monitor = [OmniBrilleDockProbe]::MonitorFromWindow($dockHandle, 2)
$monitorInfo = [OmniBrilleDockProbe+MONITORINFO]::new()
$monitorInfo.Size = [Runtime.InteropServices.Marshal]::SizeOf($monitorInfo)
if (-not [OmniBrilleDockProbe]::GetWindowRect($dockHandle, [ref]$dockRect) -or
    -not [OmniBrilleDockProbe]::GetClientRect($dockHandle, [ref]$clientRect) -or
    -not [OmniBrilleDockProbe]::GetMonitorInfo($monitor, [ref]$monitorInfo) -or
    [Math]::Abs($dockRect.Left - $monitorInfo.Work.Left) -gt 2) {
  throw 'The installed edge tab is not positioned at the monitor work area left edge.'
}
$clickX = [int](($clientRect.Right - $clientRect.Left) / 2)
$clickY = [int](($clientRect.Bottom - $clientRect.Top) / 2)
$point = [IntPtr]::new(($clickY -shl 16) -bor $clickX)
if (-not [OmniBrilleDockProbe]::PostMessage($dockHandle, 0x0200, [IntPtr]::Zero, $point) -or
    -not [OmniBrilleDockProbe]::PostMessage($dockHandle, 0x0201, [IntPtr]::new(1), $point) -or
    -not [OmniBrilleDockProbe]::PostMessage($dockHandle, 0x0202, [IntPtr]::Zero, $point)) {
  throw 'Could not click the installed edge tab.'
}
$deadline = (Get-Date).AddSeconds(15)
do { Start-Sleep -Milliseconds 200 } until (
  (-not [OmniBrilleDockProbe]::IsIconic($mainHandle) -and -not [OmniBrilleDockProbe]::IsWindowVisible($dockHandle)) -or (Get-Date) -gt $deadline
)
if ([OmniBrilleDockProbe]::IsIconic($mainHandle) -or [OmniBrilleDockProbe]::IsWindowVisible($dockHandle)) {
  throw 'Clicking the installed edge tab did not restore the app and hide the tab.'
}
$applicationProcess.Refresh()
if (-not $applicationProcess.CloseMainWindow()) { throw 'Installed application did not accept a normal window-close request.' }
Wait-Process -Id $applicationProcess.Id -Timeout 10 -ErrorAction Stop

$relaunchProcess = Start-Process -FilePath $application -ArgumentList @(
  '--theme', 'Dark'
) -PassThru
$deadline = (Get-Date).AddSeconds(20)
do {
  Start-Sleep -Milliseconds 500
  $relaunchProcess.Refresh()
} until (
  $relaunchProcess.HasExited -or
  -not [string]::IsNullOrWhiteSpace($relaunchProcess.MainWindowTitle) -or
  (Get-Date) -gt $deadline
)
if ($relaunchProcess.HasExited -or $relaunchProcess.MainWindowTitle -ne 'Spatial Explorer') {
  throw 'Installed application did not expose the expected window on relaunch.'
}
Wait-ForSavedWorkspace -ApplicationProcessToCheck $relaunchProcess -WorkspacePathToCheck $workspacePath -ExpectedRootPath $smokeRoot
if (-not $relaunchProcess.CloseMainWindow()) { throw 'Relaunched application did not accept a normal window-close request.' }
Wait-Process -Id $relaunchProcess.Id -Timeout 10 -ErrorAction Stop

if ((Get-FileHash -LiteralPath $preferencesPath -Algorithm SHA256).Hash -ne $preferencesHash) {
    throw 'Launch/relaunch changed untouched user preferences.'
}
if (-not (Test-Path -LiteralPath $workspacePath -PathType Leaf)) { throw 'Installed application did not save the explicit smoke workspace.' }
$workspaceHash = (Get-FileHash -LiteralPath $workspacePath -Algorithm SHA256).Hash
$uninstaller = Join-Path $installDirectory 'unins000.exe'
if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) { throw 'Uninstaller was not found.' }
$uninstall = Start-Process -FilePath $uninstaller -ArgumentList @(
  '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
) -Wait -PassThru
if ($uninstall.ExitCode -ne 0) { throw "Uninstaller exited with code $($uninstall.ExitCode)." }
foreach ($unknownAsset in $unknownAssetFixtures) {
    if (-not (Test-Path -LiteralPath (Join-Path $installDirectory $unknownAsset) -PathType Leaf) -or
        (Get-Content -Raw -LiteralPath (Join-Path $installDirectory $unknownAsset)).Trim() -ne $unknownFixtureContent) {
        throw "Uninstall changed or removed unknown content '$unknownAsset'."
    }
}
$remainingInstalledFiles = @(Get-ChildItem -LiteralPath $installDirectory -Recurse -File -Force)
$expectedRemainingFiles = @($unknownAssetFixtures | ForEach-Object { Join-Path $installDirectory $_ })
if ($remainingInstalledFiles.Count -ne $expectedRemainingFiles.Count -or
    @($remainingInstalledFiles | Where-Object { $_.FullName -notin $expectedRemainingFiles }).Count -ne 0) {
    throw 'Installer-owned application files remain after uninstall.'
}
if ((Get-FileHash -LiteralPath $preferencesPath -Algorithm SHA256).Hash -ne $preferencesHash -or
    (Get-FileHash -LiteralPath $workspacePath -Algorithm SHA256).Hash -ne $workspaceHash) {
    throw 'Uninstall changed or removed saved workspace/preferences.'
}
# Remove only our two sentinel files and empty directories in this fresh, owned
# fixture after proving preservation. Pre-existing fixture directories (including
# THIRD-PARTY-LICENSES) can legitimately survive the uninstall with no files.
foreach ($fixturePath in $expectedRemainingFiles) { Remove-Item -LiteralPath $fixturePath }
$fixtureDirectories = [Collections.Generic.List[IO.DirectoryInfo]]::new()
$pendingDirectories = [Collections.Generic.Stack[string]]::new()
$pendingDirectories.Push($installDirectory)
while ($pendingDirectories.Count -gt 0) {
    $currentDirectory = Get-Item -LiteralPath ($pendingDirectories.Pop()) -Force
    if (($currentDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Unexpected reparse directory remains in the owned fixture.'
    }
    foreach ($childDirectory in @(Get-ChildItem -LiteralPath $currentDirectory.FullName -Directory -Force)) {
        if (($childDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not $childDirectory.FullName.StartsWith($installDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Unexpected directory authority remains in the owned fixture.'
        }
        $fixtureDirectories.Add($childDirectory)
        $pendingDirectories.Push($childDirectory.FullName)
    }
}
foreach ($fixtureDirectory in @($fixtureDirectories | Sort-Object { $_.FullName.Length } -Descending)) {
    if (@(Get-ChildItem -LiteralPath $fixtureDirectory.FullName -Force).Count -eq 0) {
        [IO.Directory]::Delete($fixtureDirectory.FullName, $false)
    }
}
if (@(Get-ChildItem -LiteralPath $installDirectory -Force).Count -ne 0) { throw 'Unexpected content remains in the fixture directory.' }
[IO.Directory]::Delete($installDirectory, $false)
if (Test-Path -LiteralPath $shortcut) { throw 'Start Menu shortcut remains after uninstall.' }
if (Test-Path -LiteralPath $spatialShortcut) { throw 'Spatial Explorer shortcut remains after uninstall.' }
$remainingRegistration = @(Get-ChildItem -LiteralPath $uninstallRoot | ForEach-Object {
  Get-ItemProperty -LiteralPath $_.PSPath
} | Where-Object { $_.DisplayName -eq 'OmniBrille' })
if ($remainingRegistration.Count -ne 0) { throw 'Uninstall registration remains after uninstall.' }

$validationPath = Join-Path $artifactRoot 'OmniBrille-release-validation.json'
[ordered]@{
  schemaVersion = 1
  product = 'OmniBrille'
  version = $manifest.version
  commitSha = $manifest.commitSha
  installerFileName = $installer.Name
  installerBytes = $installer.Length
  sha256 = $actualHash
  signed = [bool] $manifest.installer.signed
  workflowRunId = $env:GITHUB_RUN_ID
  validationTimestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
  gates = [ordered]@{
    freshHostedWindowsRunner = 'passed'
    independentArtifactIntegrity = 'passed'
    perUserInstall = 'passed'
    retiredAssetFixtureCleanup = 'passed'
    unknownInstallContentPreserved = 'passed'
    workspaceAndPreferencesPreserved = 'passed'
    startMenuAndUninstallRegistration = 'passed'
    installedStandaloneWindow = 'passed'
    installedVersionMetadata = 'passed'
    edgeTabMinimizeAndRestore = 'passed'
    normalCloseAndRelaunch = 'passed'
    signatureWhenRequired = 'passed'
    uninstallCleanup = 'passed'
    interactiveFeatureSweep = 'not run on hosted runner'
    inPlaceUpgrade = 'not run on hosted runner'
  }
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $validationPath -Encoding UTF8
