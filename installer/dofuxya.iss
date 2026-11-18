; Installateur de Dofuxya (Inno Setup 7 ou 6).
; Compile par publier.bat (publication automatique sur GitHub), avec le numero de version.

#ifndef AppVer
  #define AppVer "0.0.0"
#endif

[Setup]
AppId={{8F3C2A51-6B7D-4E2A-9C1F-5D0B7A3E4C21}
AppName=Dofuxya
AppVersion={#AppVer}
AppVerName=Dofuxya {#AppVer}
AppPublisher=Dofuxya
DefaultDirName={localappdata}\Programs\Dofuxya
; Le joueur choisit le dossier d'installation ; les mises a jour silencieuses reprennent ce dossier.
DisableDirPage=no
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=Dofuxya-Setup-{#AppVer}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\Dofuxya.exe
UninstallDisplayName=Dofuxya
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; Flags: unchecked

[Files]
Source: "..\Dofuxya.exe"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Anciens noms (versions installees avant le renommage en Dofuxya).
Type: files; Name: "{app}\DofuxyaManager.exe"
Type: files; Name: "{userprograms}\Dofuxya Manager.lnk"
Type: files; Name: "{userdesktop}\Dofuxya Manager.lnk"
; Fichiers des versions 1.2.3 et avant : notice, dossier d'icones (vide) cree a cote de l'exe.
Type: files; Name: "{app}\LISEZMOI.txt"
Type: dirifempty; Name: "{app}\icones"

[Icons]
Name: "{userprograms}\Dofuxya"; Filename: "{app}\Dofuxya.exe"
Name: "{userdesktop}\Dofuxya"; Filename: "{app}\Dofuxya.exe"; Tasks: desktopicon

[Run]
; Installation a la main : case "Lancer Dofuxya" a la fin.
Filename: "{app}\Dofuxya.exe"; Description: "Lancer Dofuxya"; Flags: nowait postinstall skipifsilent
; Mise a jour automatique (silencieuse) : l'outil est relance, sauf si on l'a quitte.
Filename: "{app}\Dofuxya.exe"; Parameters: "--apres-maj"; Flags: nowait skipifnotsilent; Check: ShouldRelaunch

[Code]
function ShouldRelaunch: Boolean;
var
  i: Integer;
begin
  Result := True;
  for i := 1 to ParamCount do
    if CompareText(ParamStr(i), '/NORELANCE') = 0 then
      Result := False;
end;

function InitializeSetup(): Boolean;
var
  n: Integer;
begin
  Result := True;
  n := 0;
  { Lors d'une mise a jour, l'outil se ferme juste apres avoir lance l'installateur : on l'attend. }
  while WizardSilent and CheckForMutexes('DofuxyaManager.InstanceUnique') and (n < 75) do
  begin
    Sleep(200);
    n := n + 1;
  end;
  if (not WizardSilent) and CheckForMutexes('DofuxyaManager.InstanceUnique') then
  begin
    MsgBox('Dofuxya est ouvert. Quitte-le (clic droit sur son icône près de l''horloge, puis Quitter), puis relance l''installation.', mbError, MB_OK);
    Result := False;
  end;
end;

// Installation sans droits d'administrateur : le dossier choisi doit etre accessible en ecriture
// (pas C:\Program Files, par exemple). On le verifie avant d'aller plus loin.
function CanWriteTo(Dir: String): Boolean;
var
  Probe, Parent: String;
begin
  while not DirExists(Dir) do
  begin
    Parent := ExtractFileDir(Dir);
    if (Parent = '') or (Parent = Dir) then
    begin
      Result := False;
      exit;
    end;
    Dir := Parent;
  end;
  Probe := AddBackslash(Dir) + 'dofuxya-ecriture.tmp';
  Result := SaveStringToFile(Probe, '', False);
  if Result then
    DeleteFile(Probe);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and not CanWriteTo(WizardDirValue) then
  begin
    MsgBox('Dofuxya s''installe sans droits d''administrateur : ce dossier est protégé par Windows.' + #13#10 +
      'Choisis un autre dossier, par exemple dans ton dossier personnel.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Dofuxya');
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DofuxyaManager');
  end;
end;
