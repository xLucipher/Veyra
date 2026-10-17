; Veyra – Installer (Inno Setup 6)
;
; Gebaut von build\release.ps1:
;   ISCC.exe /DAppVersion=1.1.0 /DSourceDir=<publish-Ordner> installer\Veyra.iss
;
; - Installation pro Benutzer nach %LocalAppData%\Programs\Veyra (keine Adminrechte; Updates können
;   die Dateien dort selbst ersetzen).
; - Prüft die .NET 10 Desktop Runtime. Fehlt sie, erklärt eine eigene Seite das und installiert sie nur
;   mit Zustimmung (Download direkt von Microsoft, Windows fragt per UAC nach Erlaubnis).
; - Test der Abhängigkeitsseite auf einem Rechner mit Runtime: Setup.exe /SIMULATEMISSINGRUNTIME

#define AppName "Veyra"
#define AppExe "Veyra.exe"
#define AppUrl "https://github.com/xLucipher/Veyra"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\" + AppVersion
#endif

[Setup]
AppId={{8F3C2B71-5A4D-4E0B-9C6E-7A1D2F3B4C5E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=xLucipher
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=© 2026 xLucipher
AppComments=Ressourcenschonende, konfigurierbare Zusatz-Taskleiste für Windows 11
AppContact={#AppUrl}/issues
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany=xLucipher
VersionInfoCopyright=© 2026 xLucipher
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=Veyra-Setup-{#AppVersion}
SetupIconFile=..\src\Veyra\Assets\Veyra.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern dynamic
WizardImageFile=wizard-large-1x.bmp,wizard-large-2x.bmp
WizardSmallImageFile=wizard-small-1x.bmp,wizard-small-2x.bmp
WizardImageFileDynamicDark=wizard-large-1x.bmp,wizard-large-2x.bmp
WizardSmallImageFileDynamicDark=wizard-small-1x.bmp,wizard-small-2x.bmp
WizardImageStretch=no
WizardImageBackColor=$0F0605
Compression=lzma2/max
SolidCompression=yes
CloseApplications=no
RestartApplications=no
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
german.DepsTitle=Benötigte Komponente fehlt
english.DepsTitle=Required component missing
german.DepsSubtitle=Veyra braucht die Microsoft .NET 10 Desktop Runtime.
english.DepsSubtitle=Veyra requires the Microsoft .NET 10 Desktop Runtime.
german.DepsText=Auf diesem Computer ist die Microsoft .NET 10 Desktop Runtime nicht installiert. Ohne sie kann Veyra nicht starten.%n%nDer Installer kann sie jetzt für dich einrichten: Sie wird direkt von Microsoft heruntergeladen (ca. 60 MB) und installiert. Windows fragt dabei einmal nach deiner Erlaubnis (Benutzerkontensteuerung), weil die Runtime für alle Benutzer installiert wird.
english.DepsText=The Microsoft .NET 10 Desktop Runtime is not installed on this computer. Veyra cannot start without it.%n%nSetup can install it for you now: it is downloaded directly from Microsoft (about 60 MB) and installed. Windows will ask for your permission once (User Account Control) because the runtime is installed for all users.
german.DepsInstall=Jetzt automatisch herunterladen und installieren (empfohlen)
english.DepsInstall=Download and install it now (recommended)
german.DepsSkip=Überspringen – ich installiere die Runtime selbst
english.DepsSkip=Skip – I will install the runtime myself
german.DepsDownloading=Microsoft .NET 10 Desktop Runtime wird heruntergeladen …
english.DepsDownloading=Downloading Microsoft .NET 10 Desktop Runtime …
german.DepsInstalling=Microsoft .NET 10 Desktop Runtime wird installiert – bitte die Windows-Abfrage bestätigen …
english.DepsInstalling=Installing Microsoft .NET 10 Desktop Runtime – please confirm the Windows prompt …
german.DepsFailed=Die .NET 10 Desktop Runtime konnte nicht installiert werden (%1).%n%nTrotzdem mit der Installation von Veyra fortfahren? Veyra startet erst, wenn die Runtime installiert ist.
english.DepsFailed=The .NET 10 Desktop Runtime could not be installed (%1).%n%nContinue installing Veyra anyway? Veyra will only start once the runtime is installed.
german.DepsCancelled=Abgebrochen
english.DepsCancelled=cancelled
german.DepsRestart=Die Runtime ist installiert. Windows empfiehlt einen Neustart, Veyra funktioniert aber in der Regel sofort.
english.DepsRestart=The runtime is installed. Windows recommends a restart, but Veyra usually works right away.
german.FinishedMissingRuntime=%n%nHinweis: Die Microsoft .NET 10 Desktop Runtime fehlt noch. Du findest sie unter https://dotnet.microsoft.com/download/dotnet/10.0 – danach Veyra über das Startmenü starten.
english.FinishedMissingRuntime=%n%nNote: the Microsoft .NET 10 Desktop Runtime is still missing. Get it from https://dotnet.microsoft.com/download/dotnet/10.0 and then start Veyra from the Start menu.
german.TaskGroup=Zusätzliche Optionen:
english.TaskGroup=Additional options:
german.TaskAutostart=Veyra beim Anmelden automatisch starten
english.TaskAutostart=Start Veyra automatically when I sign in
german.TaskDesktop=Verknüpfung auf dem Desktop erstellen
english.TaskDesktop=Create a desktop shortcut
german.LaunchNow=Veyra jetzt starten
english.LaunchNow=Launch Veyra now
german.RemoveSettings=Sollen auch deine Veyra-Einstellungen und Protokolle gelöscht werden?%n%nWähle „Nein“, wenn du Veyra später erneut installieren und deine Einstellungen behalten möchtest.
english.RemoveSettings=Do you also want to delete your Veyra settings and logs?%n%nChoose "No" if you might reinstall Veyra later and want to keep your settings.

[Messages]
german.WelcomeLabel2=Dieser Assistent installiert [name/ver] auf deinem Computer.%n%nVeyra ist eine schlanke, konfigurierbare Zusatz-Taskleiste am oberen Bildschirmrand – mit Uhr, Kalender, Audio-Schnellwechsel und automatischer Ausblendung bei Vollbildspielen.%n%nDie Installation benötigt keine Administratorrechte.
english.WelcomeLabel2=This will install [name/ver] on your computer.%n%nVeyra is a lightweight, configurable extra taskbar at the top of the screen – with clock, calendar, quick audio switching and automatic hiding during fullscreen games.%n%nNo administrator rights are required.
; Sichtbare Standardtexte in der Du-Form – passend zur Anrede in Veyra selbst.
german.SelectDirLabel3=Veyra wird in den folgenden Ordner installiert.
german.SelectDirBrowseLabel=Klicke auf „Weiter“, um fortzufahren, oder auf „Durchsuchen“, um einen anderen Ordner zu wählen. Der Ordner muss für dich beschreibbar sein, damit Veyra sich später selbst aktualisieren kann.
german.SelectTasksLabel2=Wähle aus, was zusätzlich eingerichtet werden soll, und klicke dann auf „Weiter“.
german.ReadyLabel1=Alles bereit – Veyra kann jetzt installiert werden.
german.ReadyLabel2a=Klicke auf „Installieren“, um zu beginnen, oder auf „Zurück“, um deine Auswahl zu ändern.
german.ReadyLabel2b=Klicke auf „Installieren“, um zu beginnen.
german.FinishedLabelNoIcons=Veyra wurde erfolgreich installiert.
german.FinishedLabel=Veyra wurde erfolgreich installiert. Du findest Veyra jederzeit im Startmenü.
german.ClickFinish=Klicke auf „Fertigstellen“, um den Assistenten zu schließen.
german.InstallingLabel=Bitte warte, während Veyra installiert wird.
german.ExitSetupMessage=Die Installation ist noch nicht abgeschlossen. Wenn du jetzt abbrichst, wird Veyra nicht installiert.%n%nDu kannst den Installer später erneut starten.%n%nInstallation abbrechen?
german.ConfirmUninstall=Möchtest du %1 wirklich entfernen?
german.UninstallStatusLabel=Bitte warte, während %1 entfernt wird.
german.UninstalledAll=%1 wurde vollständig entfernt.
german.UninstalledMost=%1 wurde entfernt.%n%nEinige Dateien konnten nicht gelöscht werden und können manuell entfernt werden.

[Tasks]
Name: "autostart"; Description: "{cm:TaskAutostart}"; GroupDescription: "{cm:TaskGroup}"
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; GroupDescription: "{cm:TaskGroup}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Autostart wie die App selbst ihn setzt (HKCU\…\Run, Wert "Veyra"); die App übernimmt ihn in ihre Einstellungen.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent; Check: RuntimeReady

[UninstallRun]
; Laufende Leiste sauber beenden (gibt auch einen reservierten Bildschirmbereich frei).
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "StopVeyra"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  RuntimeUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  RuntimeFile = 'windowsdesktop-runtime-10-win-x64.exe';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

var
  DepsPage: TWizardPage;
  InstallRadio: TNewRadioButton;
  SkipRadio: TNewRadioButton;
  DownloadPage: TDownloadWizardPage;
  RuntimeMissing: Boolean;

{ Microsoft.WindowsDesktop.App 10.x im systemweiten .NET-Ordner vorhanden? }
function IsDesktopRuntimeInstalled(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function SimulateMissingRuntime(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/SIMULATEMISSINGRUNTIME') = 0 then
      Result := True;
end;

function RuntimeReady(): Boolean;
begin
  Result := not RuntimeMissing;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard();
var
  Info: TNewStaticText;
begin
  RuntimeMissing := SimulateMissingRuntime() or not IsDesktopRuntimeInstalled();
  DownloadPage := CreateDownloadPage(CustomMessage('DepsTitle'), CustomMessage('DepsDownloading'), @OnDownloadProgress);

  if not RuntimeMissing then
    Exit;

  DepsPage := CreateCustomPage(wpWelcome, CustomMessage('DepsTitle'), CustomMessage('DepsSubtitle'));

  Info := TNewStaticText.Create(DepsPage);
  Info.Parent := DepsPage.Surface;
  Info.WordWrap := True;
  Info.Width := DepsPage.SurfaceWidth;
  Info.Caption := CustomMessage('DepsText');
  Info.AutoSize := True;

  InstallRadio := TNewRadioButton.Create(DepsPage);
  InstallRadio.Parent := DepsPage.Surface;
  InstallRadio.Top := Info.Top + Info.Height + ScaleY(20);
  InstallRadio.Width := DepsPage.SurfaceWidth;
  InstallRadio.Height := ScaleY(20);
  InstallRadio.Caption := CustomMessage('DepsInstall');
  InstallRadio.Font.Style := [fsBold];
  InstallRadio.Checked := True;

  SkipRadio := TNewRadioButton.Create(DepsPage);
  SkipRadio.Parent := DepsPage.Surface;
  SkipRadio.Top := InstallRadio.Top + InstallRadio.Height + ScaleY(8);
  SkipRadio.Width := DepsPage.SurfaceWidth;
  SkipRadio.Height := ScaleY(20);
  SkipRadio.Caption := CustomMessage('DepsSkip');
end;

{ Lädt die Runtime von Microsoft und installiert sie (Windows fragt per UAC). True = weiter zur nächsten Seite. }
function InstallRuntime(): Boolean;
var
  ResultCode: Integer;
  Installer, Reason: String;
begin
  Result := False;
  DownloadPage.Clear;
  DownloadPage.Add(RuntimeUrl, RuntimeFile, '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      if DownloadPage.AbortedByUser then
        Reason := CustomMessage('DepsCancelled')
      else
        Reason := GetExceptionMessage;
      Result := SuppressibleMsgBox(FmtMessage(CustomMessage('DepsFailed'), [Reason]), mbConfirmation, MB_YESNO, IDNO) = IDYES;
      Exit;
    end;

    DownloadPage.SetText(CustomMessage('DepsInstalling'), '');
    DownloadPage.ProgressBar.Style := npbstMarquee;
    Installer := ExpandConstant('{tmp}\' + RuntimeFile);
    { ShellExec löst die UAC-Abfrage des Microsoft-Installers aus; /passive zeigt dessen Fortschritt. }
    if not ShellExec('', Installer, '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      ResultCode := -1;
    Log(Format('Runtime-Installer beendet mit Code %d', [ResultCode]));

    if ((ResultCode = 0) or (ResultCode = 3010) or (ResultCode = 1641)) and IsDesktopRuntimeInstalled() then
    begin
      RuntimeMissing := False;
      if ResultCode <> 0 then
        SuppressibleMsgBox(CustomMessage('DepsRestart'), mbInformation, MB_OK, IDOK);
      Result := True;
    end
    else
    begin
      if ResultCode = 1223 then
        Reason := CustomMessage('DepsCancelled')
      else
        Reason := Format('Code %d', [ResultCode]);
      Result := SuppressibleMsgBox(FmtMessage(CustomMessage('DepsFailed'), [Reason]), mbConfirmation, MB_YESNO, IDNO) = IDYES;
    end;
  finally
    DownloadPage.ProgressBar.Style := npbstNormal;
    DownloadPage.Hide;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (DepsPage <> nil) and (CurPageID = DepsPage.ID) and InstallRadio.Checked and RuntimeMissing then
    Result := InstallRuntime();
end;

{ Läuft bereits eine Veyra-Instanz (Update über den Installer)? Dann vorher sauber beenden. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Exe: String;
begin
  Result := '';
  Exe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(Exe) and not RuntimeMissing then
  begin
    Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1500);
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and RuntimeMissing then
  begin
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + CustomMessage('FinishedMissingRuntime');
    WizardForm.FinishedLabel.AdjustHeight;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Settings: String;
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, '{#AppName}');

  if CurUninstallStep = usPostUninstall then
  begin
    Settings := ExpandConstant('{userappdata}\{#AppName}');
    if DirExists(Settings) then
      if SuppressibleMsgBox(CustomMessage('RemoveSettings'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(Settings, True, True, True);
  end;
end;
