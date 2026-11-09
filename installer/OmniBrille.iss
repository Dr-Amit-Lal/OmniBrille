#ifndef AppVersion
  #define AppVersion "1.2.0-nebula.4"
#endif
#ifndef NumericVersion
  #define NumericVersion "1.2.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\packages"
#endif

#define AppName "OmniBrille"
#define AppExeName "OmniBrille.exe"
#define AppPublisher "OmniBrille Contributors"
#define AppUrl "https://github.com/Dr-Amit-Lal/OmniBrille"

[Setup]
AppId={{B191365D-A12A-4931-99DC-A19445B91651}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
DefaultDirName={localappdata}\Programs\OmniBrille
DefaultGroupName=OmniBrille
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=OmniBrille-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\OmniBrille.Desktop\Assets\OmniBrille.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
VersionInfoVersion={#NumericVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=OmniBrille Windows installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#NumericVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no
AllowNoIcons=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes

[InstallDelete]
; Replace the former My Files launcher during an in-place upgrade.
Type: files; Name: "{autoprograms}\OmniBrille.lnk"
Type: files; Name: "{app}\*.pdb"
; Public v1.0.0 carried this runtime diagnostics binary. .NET 8 servicing no longer publishes
; it for v1.1.0, so remove only that known installer-owned obsolete family during upgrade.
Type: files; Name: "{app}\mscordaccore_*.dll"

; Remove only known retired installer assets; keep unknown files and never follow reparse points.
Type: files; Name: "{app}\Voice\Runtime\ggml-base.dll"; Check: SafeRetiredPath('Voice\Runtime\ggml-base.dll')
Type: files; Name: "{app}\Voice\Runtime\ggml-cpu-alderlake.dll"; Check: SafeRetiredPath('Voice\Runtime\ggml-cpu-alderlake.dll')
Type: files; Name: "{app}\Voice\Runtime\ggml-cpu-cannonlake.dll"; Check: SafeRetiredPath('Voice\Runtime\ggml-cpu-cannonlake.dll')
Type: files; Name: "{app}\Voice\Runtime\ggml-cpu-cascadelake.dll"; Check: SafeRetiredPath('Voice\Runtime\ggml-cpu-cascadelake.dll')
Type: files; Name: "{app}\Voice\Runtime\ggml-cpu-haswell.dll"; Check: SafeRetiredPath('Voice\Runtime\ggml-cpu-haswell.dll')
Type: files; Name: "{app}\Voice\Runtime\ggml-cpu-icelake.dll"; Check: SafeRetiredPath('Voice\Runtime\ggml-cpu-icelake.dll')
Type: files; Name: "{app}\Voice\Runtime\ggml-cpu-sandybridge.dll"; Check: SafeRetiredPath('Voice\Runtime\ggml-cpu-sandybridge.dll')
Type: files; Name: "{app}\Voice\Runtime\ggml-cpu-skylakex.dll"; Check: SafeRetiredPath('Voice\Runtime\ggml-cpu-skylakex.dll')
Type: files; Name: "{app}\Voice\Runtime\ggml-cpu-sse42.dll"; Check: SafeRetiredPath('Voice\Runtime\ggml-cpu-sse42.dll')
Type: files; Name: "{app}\Voice\Runtime\ggml-cpu-x64.dll"; Check: SafeRetiredPath('Voice\Runtime\ggml-cpu-x64.dll')
Type: files; Name: "{app}\Voice\Runtime\ggml.dll"; Check: SafeRetiredPath('Voice\Runtime\ggml.dll')
Type: files; Name: "{app}\Voice\Runtime\whisper-cli.exe"; Check: SafeRetiredPath('Voice\Runtime\whisper-cli.exe')
Type: files; Name: "{app}\Voice\Runtime\whisper.dll"; Check: SafeRetiredPath('Voice\Runtime\whisper.dll')
Type: files; Name: "{app}\Voice\Models\ggml-base.en-q5_1.bin"; Check: SafeRetiredPath('Voice\Models\ggml-base.en-q5_1.bin')
Type: files; Name: "{app}\Voice\voice-bundle-manifest.json"; Check: SafeRetiredPath('Voice\voice-bundle-manifest.json')
Type: files; Name: "{app}\Voice\whisper.cpp-MIT.txt"; Check: SafeRetiredPath('Voice\whisper.cpp-MIT.txt')
Type: files; Name: "{app}\Voice\OpenAI-Whisper-MIT.txt"; Check: SafeRetiredPath('Voice\OpenAI-Whisper-MIT.txt')
Type: files; Name: "{app}\OmniSorSe.ExplorerProtocol.dll"; Check: SafeRetiredPath('OmniSorSe.ExplorerProtocol.dll')
Type: files; Name: "{app}\NAudio.Core.dll"; Check: SafeRetiredPath('NAudio.Core.dll')
Type: files; Name: "{app}\NAudio.WinMM.dll"; Check: SafeRetiredPath('NAudio.WinMM.dll')
Type: files; Name: "{app}\THIRD-PARTY-LICENSES\whisper.cpp-MIT.txt"; Check: SafeRetiredPath('THIRD-PARTY-LICENSES\whisper.cpp-MIT.txt')
Type: files; Name: "{app}\THIRD-PARTY-LICENSES\OpenAI-Whisper-MIT.txt"; Check: SafeRetiredPath('THIRD-PARTY-LICENSES\OpenAI-Whisper-MIT.txt')
Type: dirifempty; Name: "{app}\Voice\Runtime"; Check: SafeRetiredPath('Voice\Runtime')
Type: dirifempty; Name: "{app}\Voice\Models"; Check: SafeRetiredPath('Voice\Models')
Type: dirifempty; Name: "{app}\Voice"; Check: SafeRetiredPath('Voice')

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\OmniBrille Spatial Explorer"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Comment: "Open the Spatial Explorer"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch OmniBrille"; Flags: nowait postinstall skipifsilent

[Code]
function GetFileAttributesW(lpFileName: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function SafeRetiredPath(RelativePath: String): Boolean;
var
  BasePath, CurrentPath: String;
  Attributes: LongWord;
begin
  Result := False;
  BasePath := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  CurrentPath := BasePath + '\' + RelativePath;
  while Length(CurrentPath) >= Length(BasePath) do begin
    Attributes := GetFileAttributesW(CurrentPath);
    if (Attributes = $FFFFFFFF) or ((Attributes and $400) <> 0) then
      Exit;
    if CompareText(CurrentPath, BasePath) = 0 then begin
      Result := True;
      Exit;
    end;
    CurrentPath := ExtractFileDir(CurrentPath);
  end;
end;
