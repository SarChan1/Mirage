#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef PrerequisiteDir
  #error PrerequisiteDir is required
#endif
#ifndef DestinationDir
  #define DestinationDir "..\dist"
#endif

[Setup]
AppId={{26B6F277-76F2-4C29-9DAB-69D3098536C9}
AppName=Mirage
AppVersion={#AppVersion}
AppPublisher=Mirage
DefaultDirName={localappdata}\Programs\Mirage
DefaultGroupName=Mirage
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#DestinationDir}
OutputBaseFilename=Mirage-Setup-{#AppVersion}-win-x64
SetupIconFile=..\source\mirage.ico
UninstallDisplayIcon={app}\Mirage.exe
UninstallDisplayName=Mirage
VersionInfoVersion={#AppVersion}
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\Mirage.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PrerequisiteDir}\MicrosoftEdgeWebView2Setup.exe"; Flags: dontcopy
Source: "DISTRIBUICAO.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\source\Frontend\public\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Mirage"; Filename: "{app}\Mirage.exe"
Name: "{userdesktop}\Mirage"; Filename: "{app}\Mirage.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Mirage.exe"; Description: "Abrir o Mirage"; Flags: nowait postinstall skipifsilent

[Code]
const
  WebViewKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function HasWebView: Boolean;
var
  Version: String;
begin
  Result := (RegQueryStringValue(HKCU, WebViewKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
            (RegQueryStringValue(HKLM32, WebViewKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\Mirage.exe')) and CheckForMutexes('Local\Mirage.Desktop.1') then begin
    Result := 'Encerre o Mirage pelo ícone próximo ao relógio antes de atualizar.';
    exit;
  end;
  if not HasWebView then begin
    WizardForm.StatusLabel.Caption := 'Instalando o Microsoft WebView2. Esta etapa precisa de internet...';
    ExtractTemporaryFile('MicrosoftEdgeWebView2Setup.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebView2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
      Result := 'Não foi possível iniciar a instalação do Microsoft WebView2. Tente novamente.';
      exit;
    end;
    if not HasWebView then begin
      Result := 'O Microsoft WebView2 não foi instalado. Confira sua conexão com a internet e tente novamente. Código: ' + IntToStr(ExitCode);
      exit;
    end;
    NeedsRestart := ExitCode = 3010;
  end;
end;
