#define AppName "LocalVoice"
#ifndef AppVersion
  #define AppVersion "1.3.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{E8A293D1-A2B3-4B0C-B0E2-83D66B871C61}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=LocalVoice contributors
AppPublisherURL=https://github.com/BalaRmack/html-editor
AppSupportURL=https://github.com/BalaRmack/html-editor/issues
DefaultDirName={localappdata}\Programs\LocalVoice
DefaultGroupName=LocalVoice
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=LocalVoice-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\LocalVoice.App\Assets\LocalVoice.ico
UninstallDisplayIcon={app}\LocalVoice.exe
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
CloseApplications=no
RestartApplications=no
AppMutex=Local\LocalVoice.Instance
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=LocalVoice per-user installer
LicenseFile=..\LICENSE
ShowLanguageDialog=no
MinVersion=10.0.17763

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.LaunchApp=Запустить LocalVoice
english.LaunchApp=Launch LocalVoice
russian.AutoStart=Запускать вместе с Windows (можно изменить в настройках)
english.AutoStart=Start with Windows (can be changed in Settings)
russian.DesktopIcon=Создать ярлык на рабочем столе
english.DesktopIcon=Create a desktop shortcut

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"; Flags: unchecked
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\LocalVoice\LocalVoice"; Filename: "{app}\LocalVoice.exe"; WorkingDir: "{app}"
Name: "{userprograms}\LocalVoice\Удалить LocalVoice"; Filename: "{uninstallexe}"
Name: "{userdesktop}\LocalVoice"; Filename: "{app}\LocalVoice.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "LocalVoice"; ValueType: string; ValueData: """{app}\LocalVoice.exe"" --minimized"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\LocalVoice.exe"; Description: "{cm:LaunchApp}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'LocalVoice');
    DataDir := ExpandConstant('{localappdata}\LocalVoice');
    if DirExists(DataDir) and not UninstallSilent then
      if MsgBox('Удалить также настройки, историю и скачанные модели?' + #13#10 +
                'Нет — сохранить их для следующей установки.' + #13#10 + #13#10 +
                'Also delete settings, transcript history and downloaded models?',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
