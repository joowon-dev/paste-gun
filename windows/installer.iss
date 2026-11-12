; 윈도우 설치본. Inno Setup 6 으로 만든다.
;
;   dotnet publish windows/PasteGun.csproj -c Release -o dist/win
;   iscc /DAppVersion=1.0.0 windows/installer.iss
;
; **자동 업데이트가 이 설치본을 조용히 다시 돌린다**(`/SILENT`). 앱은 설치본을 띄우고 스스로
; 물러나고, 설치본이 끝나면 새 것을 다시 띄운다([Run] 의 둘째 줄) — 이게 없으면 업데이트가
; 「총이 사라진 것」처럼 보인다.
;
; 제거는 「설정 → 앱」에서도, 트레이 메뉴의 「붙여넣기 총 제거…」에서도 된다. 둘 다 같은
; 제거 프로그램이고, 저장한 탄창(복사해 둔 글이 들어 있다)까지 지운다.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6B0C1C5E-9B1F-4D0B-8E57-5A6E0F3C7A21}
AppName=Paste Gun
AppVersion={#AppVersion}
AppPublisher=JooWon Koh
AppPublisherURL=https://joowonkoh.com/playground/paste-gun
DefaultDirName={autopf}\Paste Gun
DefaultGroupName=Paste Gun
DisableProgramGroupPage=yes
; 관리자 권한 없이 사용자 폴더에 깐다. 조용한 업데이트에 UAC 창이 뜨면 안 된다.
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=PasteGun-win-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=icon.ico
UninstallDisplayIcon={app}\PasteGun.exe
UninstallDisplayName=Paste Gun (붙여넣기 총)
; 돌던 앱은 아래 [Code] 가 직접 닫는다. 재시작 관리자에 맡기면 창 없는 트레이 앱을 못 닫을 때가 있다.
CloseApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
; 붙여넣기는 하루 종일 쓰는 것이라, 켜 두는 것을 잊으면 없는 앱이 된다. 기본으로 켠다.
Name: "startup"; Description: "윈도우를 켤 때 함께 실행"; GroupDescription: "추가 설정:"

[Files]
Source: "..\dist\win\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Paste Gun"; Filename: "{app}\PasteGun.exe"
Name: "{userstartup}\Paste Gun"; Filename: "{app}\PasteGun.exe"; Tasks: startup

[Run]
; 손으로 깔 때는 마지막 화면의 체크 상자로 띄우고,
Filename: "{app}\PasteGun.exe"; Description: "지금 실행"; Flags: nowait postinstall skipifsilent
; 조용한 업데이트일 때는 묻지 않고 다시 띄운다.
Filename: "{app}\PasteGun.exe"; Flags: nowait; Check: WizardSilent

[UninstallDelete]
; 탄창·단축키(state.json)와 웹뷰 캐시. 업데이트는 제거를 거치지 않으므로 이건 진짜 제거 때만 지워진다.
Type: filesandordirs; Name: "{userappdata}\PasteGun"

[Code]
{ **런타임을 따로 깔게 시키지 않는다.** 앱을 작게 두려고 .NET 을 담지 않았는데, 없으면 실행해도
  아무 일도 안 일어난 것처럼 보인다. 그래서 없을 때만 설치본이 마이크로소프트에서 받아 깐다
  (그때 한 번 관리자 확인 창이 뜬다). 이미 있으면 아무것도 받지 않는다. }
var
  DownloadPage: TDownloadWizardPage;

function RuntimeIn(root: String): Boolean;
var
  find: TFindRec;
begin
  Result := False;
  if root = '' then Exit;
  if FindFirst(AddBackslash(root) + 'shared\Microsoft.WindowsDesktop.App\9.*', find) then
  begin
    Result := True;
    FindClose(find);
  end;
end;

function HasDesktopRuntime(): Boolean;
begin
  Result := RuntimeIn(ExpandConstant('{commonpf64}\dotnet')) or RuntimeIn(GetEnv('DOTNET_ROOT'));
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage('.NET 데스크톱 런타임', '붙여넣기 총이 쓰는 런타임을 받고 있습니다.', nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  code: Integer;
begin
  Result := True;
  if (CurPageID <> wpReady) or HasDesktopRuntime() then Exit;

  DownloadPage.Clear;
  DownloadPage.Add('https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe', 'dotnet-desktop-runtime.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      { 관리자 확인이 필요한 설치본이라 ShellExec 로 띄운다(Exec 는 승격을 못 시킨다). }
      ShellExec('', ExpandConstant('{tmp}\dotnet-desktop-runtime.exe'), '/install /quiet /norestart', '',
        SW_SHOW, ewWaitUntilTerminated, code);
    except
      SuppressibleMsgBox('.NET 데스크톱 런타임을 받지 못했습니다. 설치는 계속하지만, 앱이 켜지지 않으면 '
        + 'https://dotnet.microsoft.com/download/dotnet/9.0 에서 「.NET 데스크톱 런타임 x64」를 깔아 주세요.',
        mbInformation, MB_OK, IDOK);
    end;
  finally
    DownloadPage.Hide;
  end;
end;

procedure StopApp();
var
  code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM PasteGun.exe', '', SW_HIDE, ewWaitUntilTerminated, code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopApp();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { 「제거할까요?」에 그렇다고 한 뒤에 닫는다. 취소했는데 총이 꺼져 있으면 안 된다. }
  if CurUninstallStep = usUninstall then StopApp();
end;
