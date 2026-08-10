; ════════════════════════════════════════════════════════════════════════════
;  Altéchap — script d'installation Inno Setup 6
;
;  Compilé par ..\build.ps1, qui injecte AppVersion et SourceDir. On peut aussi
;  l'ouvrir directement dans Inno Setup Compiler : les #ifndef ci-dessous
;  fournissent alors des valeurs par défaut.
;
;  Choix structurants :
;   · Installation PAR UTILISATEUR (PrivilegesRequired=lowest) → aucune fenêtre
;     UAC. C'est ce qui rend la mise à jour automatique silencieuse possible :
;     un installeur admin déclencherait une invite que personne ne voit quand
;     elle est lancée depuis l'app.
;   · CloseApplications=yes → le Restart Manager ferme Altéchap si l'utilisateur
;     l'a relancé entre-temps, au lieu d'échouer sur un .exe verrouillé.
; ════════════════════════════════════════════════════════════════════════════

#define AppName      "Altéchap"
#define AppExeName   "Altechap.exe"
#define AppPublisher "3WVKV"
#define AppUrl       "https://github.com/3WVKV/Altechap"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
; AppId identifie le produit pour Windows : NE JAMAIS le changer, sinon chaque
; version s'installerait à côté de la précédente au lieu de la remplacer.
AppId={{8F3C2A17-5D46-4B29-9E71-2C0A6B84D3F1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

DefaultDirName={autopf}\Altechap
DefaultGroupName=Altéchap
DisableProgramGroupPage=yes
DisableDirPage=auto
AllowNoIcons=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

OutputDir={#OutputDir}
OutputBaseFilename=Altechap-Setup-{#AppVersion}
SetupIconFile=..\Resources\icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no

; Ferme proprement Altéchap si une instance tourne pendant l'installation.
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no

VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Installeur d'Altéchap
VersionInfoProductName={#AppName}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le &Bureau"; \
    GroupDescription: "Raccourcis :"
Name: "startupicon"; Description: "Lancer Altéchap au &démarrage de Windows"; \
    GroupDescription: "Options :"; Flags: unchecked

[Files]
; Tout le contenu de la publication .NET (exe + runtime embarqué + dépendances).
Source: "{#SourceDir}\*"; DestDir: "{app}"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Altéchap";                 Filename: "{app}\{#AppExeName}"
Name: "{group}\Désinstaller Altéchap";    Filename: "{uninstallexe}"
Name: "{autodesktop}\Altéchap";           Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\Altéchap";           Filename: "{app}\{#AppExeName}"; Tasks: startupicon

[Run]
; Installation interactive : case « Lancer Altéchap » en fin d'assistant.
Filename: "{app}\{#AppExeName}"; Description: "Lancer {#AppName}"; \
    Flags: nowait postinstall skipifsilent

; Mise à jour automatique : l'app appelle setup.exe avec /AUTOLAUNCH=1 et se
; ferme ; c'est cette ligne qui la relance une fois les fichiers remplacés.
Filename: "{app}\{#AppExeName}"; Flags: nowait; Check: ShouldAutoLaunch

[UninstallDelete]
; Les journaux se recréent tout seuls — inutile de les laisser derrière.
Type: files; Name: "{userappdata}\Altechap\log.txt"
Type: files; Name: "{userappdata}\Altechap\log.old.txt"

[Code]
{ Vrai uniquement pour une installation silencieuse déclenchée par l'app. }
function ShouldAutoLaunch: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:AUTOLAUNCH|0}') = '1');
end;

{ La configuration (personnages, profils, raccourcis) survit par défaut à une
  désinstallation — elle représente un vrai travail de saisie. On ne la
  supprime que si l'utilisateur le demande explicitement. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Dir := ExpandConstant('{userappdata}\Altechap');
    if DirExists(Dir) and (not UninstallSilent) then
      if MsgBox('Supprimer aussi vos personnages, profils et raccourcis ?' + #13#10 +
                '(Répondez Non pour les conserver en vue d''une réinstallation.)',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(Dir, True, True, True);
  end;
end;
