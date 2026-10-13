; Studio lives in Program Files; user data is migrated before any cleanup.
#define AppName "T2T Campaign Studio"
#define AppVersion "Beta 1.0 revision 2"
#define ExeName "T2T Campaign Studio.exe"
#ifndef StudioDir
  #define StudioDir "..\build\CampaignStudio"
#endif

[Setup]
AppId={{D8C4C4D2-8B7A-4A9C-B2D3-C1F4E5A6D7B8}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion=1.0.0.2
VersionInfoDescription=T2T Campaign Studio Beta 1.0 revision 2 Setup
SetupIconFile=..\assets\branding\CampaignStudio.ico
DefaultDirName={autopf}\T2T Campaign Studio
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\build\installer
OutputBaseFilename=T2T_Custom_Campaign_Framework_Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#ExeName}
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "Migrate-UserContent.ps1"; Flags: dontcopy
Source: "Retire-LegacyInstall.ps1"; Flags: dontcopy
Source: "{#StudioDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\release\CustomCampaignFramework.dll"; DestDir: "{code:GameDirectory}\BepInEx\plugins"; Flags: ignoreversion
Source: "..\src\CustomCampaignFramework\bin\Release\net6.0\MapSeams\*"; DestDir: "{code:GameDirectory}\BepInEx\plugins\MapSeams"; Flags: ignoreversion
Source: "bepinex\*"; DestDir: "{code:GameDirectory}"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall

[InstallDelete]
; PrepareToInstall must finish migration successfully before these are reached.
; User-selected application folders are never recursively erased.
Type: filesandordirs; Name: "{code:GameDirectory}\BepInEx\interop"
Type: filesandordirs; Name: "{code:GameDirectory}\BepInEx\cache"
; Replace the complete loader runtime; do not mix DLLs from different builds.
Type: filesandordirs; Name: "{code:GameDirectory}\BepInEx\core"
Type: filesandordirs; Name: "{code:GameDirectory}\dotnet"
; Game artwork and the base-game rosters are no longer shipped: Campaign Studio builds them from the
; player's own install into %LOCALAPPDATA%\T2T Campaign Studio\game-art. Remove the copies older
; versions installed next to the application.
Type: filesandordirs; Name: "{app}\base-rosters"
Type: filesandordirs; Name: "{app}\head-preview"
Type: filesandordirs; Name: "{app}\head-templates"
Type: filesandordirs; Name: "{app}\team-logos"
Type: filesandordirs; Name: "{app}\flat-equipment"
Type: filesandordirs; Name: "{app}\node-images"
Type: filesandordirs; Name: "{app}\map-preview"

[Icons]
Name: "{commonprograms}\{#AppName}\{#AppName}"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"; Description: "Launch Campaign Studio"; Flags: postinstall nowait skipifsilent unchecked runasoriginaluser

[Code]
var
  GamePage: TInputDirWizardPage;

function GameDirectory(Param: String): String;
begin
  Result := GamePage.Values[0];
end;
function TrimQuotes(const S: String): String;
begin
  Result := S;
  if (Length(Result) >= 2) and (Result[1] = '"') and (Result[Length(Result)] = '"') then
    Result := Copy(Result, 2, Length(Result) - 2);
end;

function ParseSteamLibraries(const VdfPath: String; var Libs: TArrayOfString): Boolean;
var
  Lines: TArrayOfString;
  I, Q1, Q2: Integer;
  Line, PathVal: String;
  Count: Integer;
begin
  Result := False;
  if not LoadStringsFromFile(VdfPath, Lines) then Exit;
  Count := 0;
  SetArrayLength(Libs, 20);
  for I := 0 to GetArrayLength(Lines) - 1 do begin
    Line := Lines[I];
    if Pos('"path"', LowerCase(Line)) > 0 then begin
      // Extract the quoted path after "path"
      Q1 := Pos('"', Copy(Line, Pos('"path"', LowerCase(Line)) + 6, Length(Line)));
      if Q1 > 0 then begin
        Line := Copy(Line, Pos('"path"', LowerCase(Line)) + 6 + Q1, Length(Line));
        Q2 := Pos('"', Line);
        if Q2 > 0 then begin
          PathVal := Copy(Line, 1, Q2 - 1);
          // Steam escapes backslashes in vdf as \\
          StringChangeEx(PathVal, '\\', '\', True);
          if DirExists(PathVal) then begin
            SetArrayLength(Libs, Count + 1);
            Libs[Count] := PathVal;
            Count := Count + 1;
          end;
        end;
      end;
    end;
  end;
  SetArrayLength(Libs, Count);
  Result := Count > 0;
end;

function DetectTapeToTape(): String;
var
  SteamRoot: String;
  Libs: TArrayOfString;
  I: Integer;
  Candidate: String;
begin
  Result := '';
  // Read HKCU\Software\Valve\Steam\SteamPath
  if not RegQueryStringValue(HKCU, 'Software\Valve\Steam', 'SteamPath', SteamRoot) then
    Exit;
  StringChangeEx(SteamRoot, '/', '\', True);

  // Try the default library first
  Candidate := SteamRoot + '\steamapps\common\Tape to Tape';
  if DirExists(Candidate) then begin
    Result := Candidate;
    Exit;
  end;

  // Parse libraryfolders.vdf for additional libraries
  if ParseSteamLibraries(SteamRoot + '\steamapps\libraryfolders.vdf', Libs) then begin
    for I := 0 to GetArrayLength(Libs) - 1 do begin
      Candidate := Libs[I] + '\steamapps\common\Tape to Tape';
      if DirExists(Candidate) then begin
        Result := Candidate;
        Exit;
      end;
    end;
  end;
end;

function PathsOverlap(A, B: String): Boolean;
begin
  A := AddBackslash(LowerCase(ExpandFileName(A)));
  B := AddBackslash(LowerCase(ExpandFileName(B)));
  Result := (Pos(A, B) = 1) or (Pos(B, A) = 1);
end;

function DirectoryHasFiles(const Path: String): Boolean;
var
  Entry: TFindRec;
begin
  Result := False;
  if FindFirst(AddBackslash(Path) + '*', Entry) then begin
    try
      repeat
        if (Entry.Name <> '.') and (Entry.Name <> '..') then begin
          Result := True;
          Exit;
        end;
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
end;

function ValidateAppDirectory(): String;
var
  AppPath: String;
begin
  Result := '';
  AppPath := RemoveBackslashUnlessRoot(ExpandFileName(WizardDirValue()));
  if (AppPath = '') or (ExtractFileDir(AppPath) = AppPath) or
     (CompareText(AppPath, AddBackslash(ExtractFileDrive(AppPath))) = 0) then
    Result := 'Choose a dedicated folder for Campaign Studio, not a drive root.';
  if (Result = '') and (GameDirectory('') <> '') and PathsOverlap(AppPath, GameDirectory('')) then
    Result := 'Choose a Campaign Studio folder separate from the Tape to Tape game folder.';
  if (Result = '') and DirectoryHasFiles(AppPath) and
     not (FileExists(AppPath + '\{#ExeName}') and FileExists(AppPath + '\CampaignCore.dll')) then
    Result := 'Choose an empty folder or an existing Campaign Studio installation.';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Error: String;
begin
  Result := True;
  if (CurPageID = wpSelectDir) or (CurPageID = GamePage.ID) then begin
    Error := ValidateAppDirectory();
    if Error <> '' then begin
      MsgBox(Error, mbError, MB_OK);
      Result := False;
    end;
  end;
end;
procedure InitializeWizard();
begin
  GamePage := CreateInputDirPage(wpSelectDir, 'Tape to Tape game folder',
    'Choose the folder containing Tape to Tape.exe',
    'Only BepInEx and the game mod are installed here. Campaign Studio installs to the application folder you selected. Saved content is preserved in your Local AppData.', False, '');
  GamePage.Add('Game folder:');
  GamePage.Values[0] := ExpandConstant('{param:GAMEDIR|}') ;
  if GamePage.Values[0] = '' then GamePage.Values[0] := DetectTapeToTape();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  Args: String;
  ErrorText: AnsiString;
begin
  Result := '';
  Result := ValidateAppDirectory();
  if Result <> '' then Exit;
  if not FileExists(GameDirectory('') + '\Tape to Tape.exe') then begin
    Result := 'Select the game folder containing Tape to Tape.exe.';
    Exit;
  end;
  ExtractTemporaryFile('Migrate-UserContent.ps1');
  ExtractTemporaryFile('Retire-LegacyInstall.ps1');
  Args := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Migrate-UserContent.ps1') +
    '" -GameDir "' + GameDirectory('') + '" -AppDir "' + ExpandConstant('{app}') +
    '" -ErrorFile "' + ExpandConstant('{tmp}\migration-error.txt') + '"';
  if not ExecAsOriginalUser(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Result := 'Could not run saved-content migration. Nothing has been removed. Start Setup normally from your Windows account.'
  else if ExitCode <> 0 then begin
    Result := 'Saved-content migration failed. Nothing has been removed.';
    if LoadStringFromFile(ExpandConstant('{tmp}\migration-error.txt'), ErrorText) then
      Result := Result + #13#10 + String(ErrorText);
  end;
end;


// The pre-Studio installer (2.1.31 and earlier) used this same AppId and put its
// uninstaller in the game root. That uninstaller removes winhttp.dll, BepInEx\core
// and dotnet\, so it goes once this install has replaced the product. The log's
// header names its AppId at byte 64; anything else in the game root is left alone.
procedure RetireLegacyUninstaller(const Game: String);
var
  UninstallLog: AnsiString;
begin
  if not LoadStringFromFile(Game + '\unins000.dat', UninstallLog) then Exit;
  if Pos('{D8C4C4D2-8B7A-4A9C-B2D3-C1F4E5A6D7B8}', String(Copy(UninstallLog, 65, 64))) <> 1 then Exit;
  DeleteFile(Game + '\unins000.exe');
  DeleteFile(Game + '\unins000.dat');
  DeleteFile(Game + '\unins000.msg');
  Log('Removed the pre-Studio uninstaller from the game folder.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
  Plugins: String;
begin
  if CurStep <> ssPostInstall then Exit;
  if not ExecAsOriginalUser(ExpandConstant('{app}\{#ExeName}'), '--clean-upgrade-content', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    RaiseException('Could not clean migrated content. Old game content folders were not removed.')
  else if ExitCode <> 0 then
    RaiseException('Content cleanup failed. Old game content folders were not removed.');
  // These exact product-owned paths were copied and checked by PrepareToInstall.
  Plugins := ExpandFileName(GameDirectory('')) + '\BepInEx\plugins\';
  // Preserve trailing spaces in legacy generated folder names during removal too.
  if Copy(Plugins, 1, 2) = '\\' then Plugins := '\\?\UNC\' + Copy(Plugins, 3, Length(Plugins))
  else Plugins := '\\?\' + Plugins;
  if not DelTree(Plugins + 'Custom Campaigns Mod', True, True, True) then
    if DirExists(Plugins + 'Custom Campaigns Mod') then RaiseException('Close the old creator and retry Setup to finish removing it.');
  if not DelTree(Plugins + 'Campaigns', True, True, True) then
    if DirExists(Plugins + 'Campaigns') then RaiseException('Could not remove the old Campaigns directory.');
  DelTree(Plugins + 'T2T_Dumps', True, True, True);
  DeleteFile(Plugins + 'active.txt');
  DeleteFile(Plugins + 'defaults.txt');
  RetireLegacyUninstaller(ExpandFileName(GameDirectory('')));
  // The old Apps entry and shortcuts belong to the original user's profile, not the
  // elevated one. Failure is logged only: the product itself is installed by now.
  if not ExecAsOriginalUser(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Retire-LegacyInstall.ps1') +
    '" -GameDir "' + GameDirectory('') + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
    Log('Could not retire the pre-Studio Apps entry; remove "T2T Custom Campaign Framework" from Apps manually. Do not run its uninstaller.');
end;




