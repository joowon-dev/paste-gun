# 설치본을 진짜 윈도우에서 한 바퀴 돌려 본다 (CI 의 windows 러너).
#   깔기 → 앱이 뜨는가 → 복사하고 Ctrl+V 를 누르면 글자가 찍히는가 → 제거하면 깨끗한가
#
#   글자 커서 자리로 총이 가는가 → 무기고에 커서를 올려도 그대로 있고 눌리는가
#
# 이 맥에서는 윈도우 셸을 컴파일만 할 수 있다. 「돌아간다」는 말은 이 스크립트가 한다.
# 결과는 dist/smoke/ 에 남는다(로그·화면·찍힌 글).
param([string]$Setup = "dist\PasteGun-win-Setup.exe")

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Keys2 {
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  public static void Click(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(400);
    mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
  }
  public static void Down(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); }
  public static void Up(byte vk) { keybd_event(vk, 0, 2, UIntPtr.Zero); }
}
"@

$out = "dist\smoke"
New-Item -ItemType Directory -Force $out | Out-Null
$data = Join-Path $env:APPDATA "PasteGun"
$app = Join-Path $env:LOCALAPPDATA "Programs\Paste Gun"
$failed = @()
function Check($name, $ok) {
  if ($ok) { Write-Host "  ok    $name" } else { Write-Host "  FAIL  $name"; $script:failed += $name }
}
function Shot($name) {
  try {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    [System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $out "$name.png"))
  } catch { Write-Host "  (화면을 못 찍음: $_)" }
}

Write-Host "› 깔기"
$env:PASTEGUN_DEBUG = "1"   # 설치본이 띄우는 앱이 물려받는다
# -Wait 는 쓰지 않는다 — 자손까지 기다려서, 설치본이 띄운 앱이 꺼질 때까지 안 돌아온다.
$setupProcess = Start-Process $Setup -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -PassThru
$setupProcess.WaitForExit()
Check "앱 폴더가 생겼다" (Test-Path (Join-Path $app "PasteGun.exe"))
Check "제거 프로그램이 있다" (Test-Path (Join-Path $app "unins000.exe"))
Check "시작 프로그램에 들어갔다" (Test-Path (Join-Path ([Environment]::GetFolderPath("Startup")) "Paste Gun.lnk"))

Write-Host "› 뜨는가"
Start-Sleep 12
Check "설치가 끝나고 앱이 스스로 떴다" ($null -ne (Get-Process PasteGun -ErrorAction SilentlyContinue))
$log = Join-Path $data "debug.log"
function LogText { if (Test-Path $log) { Get-Content $log -Raw -Encoding UTF8 } else { "" } }
function LastMatch($pattern) {
  $all = [regex]::Matches((LogText), $pattern)
  if ($all.Count -gt 0) { $all[$all.Count - 1] } else { $null }
}
function LastGroup($pattern) { $m = LastMatch $pattern; if ($null -ne $m) { $m.Groups[1].Value } else { "" } }
function PressCtrlV { [Keys2]::Down(0x11); [Keys2]::Down(0x56); [Keys2]::Up(0x56); [Keys2]::Up(0x11) }
function PressPanelKey { [Keys2]::Down(0x12); [Keys2]::Down(0x10); [Keys2]::Down(0x41); [Keys2]::Up(0x41); [Keys2]::Up(0x10); [Keys2]::Up(0x12) }

# 과녁: 글상자 하나뿐인 창. 타이머가 차례로 복사 → Ctrl+V → 읽기를 한다.
# 마우스는 구석에 치워 둔다 — 총이 글자 커서가 아니라 마우스로 갔는지 가릴 수 있게.
function FireAt($bullet, $shotName) {
  $script:bullet = $bullet; $script:shotName = $shotName
  $script:form = New-Object System.Windows.Forms.Form
  $script:form.Text = "target"; $script:form.TopMost = $true; $script:form.Width = 600; $script:form.Height = 200
  $script:form.StartPosition = "Manual"; $script:form.Left = 200; $script:form.Top = 150
  $script:box = New-Object System.Windows.Forms.TextBox
  $script:box.Dock = "Fill"; $script:box.Multiline = $true; $script:box.Font = New-Object System.Drawing.Font("Segoe UI", 16)
  $script:form.Controls.Add($script:box)
  $script:step = 0
  $script:typed = ""
  $script:boxRect = $null
  $script:timer = New-Object System.Windows.Forms.Timer
  $script:timer.Interval = 1500
  $script:timer.Add_Tick({
    $script:step += 1
    switch ($script:step) {
      1 {
        [Keys2]::SetCursorPos(5, 5) | Out-Null
        [Keys2]::SetForegroundWindow($script:form.Handle) | Out-Null; $script:box.Focus() | Out-Null
        $script:boxRect = $script:box.RectangleToScreen($script:box.ClientRectangle)
      }
      2 { [System.Windows.Forms.Clipboard]::SetText($script:bullet) }
      3 { PressCtrlV }
      6 { Shot $script:shotName; $script:typed = $script:box.Text; $script:timer.Stop(); $script:form.Close() }
    }
  })
  $script:form.Add_Shown({ $script:form.Activate(); $script:timer.Start() })
  [System.Windows.Forms.Application]::Run($script:form)
  Write-Host "  찍힌 글: [$($script:typed)]"
  $script:typed
}

# 총이 간 자리. 셸이 발마다 「과녁 <어떻게 알았나> x,y」를 적는다.
function CheckTarget($name, $how) {
  $m = LastMatch "과녁 (\w+) (-?\d+),(-?\d+)"
  if ($null -eq $m) { Check $name $false; return }
  $x = [int]$m.Groups[2].Value; $y = [int]$m.Groups[3].Value
  Write-Host "  과녁: $($m.Groups[1].Value) $x,$y  글상자: $($script:boxRect)"
  Check $name (($m.Groups[1].Value -match "^($how)$") -and $script:boxRect.Contains($x, $y))
}

function Restart($caret) {
  Get-Process PasteGun -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep 2
  $env:PASTEGUN_CARET = $caret
  Start-Process (Join-Path $app "PasteGun.exe")
  Start-Sleep 10
}

Check "렌더러가 단축키를 등록했다 (다리가 살아 있다)" ((LogText) -match "핫키 13개 등록 True")

Write-Host "› 쏘는가"
$bullet = "ABC 총알 123"
$typed = FireAt $bullet "after-fire"
Set-Content (Join-Path $out "typed.txt") $typed -Encoding UTF8
Check "Ctrl+V 로 복사한 글이 그대로, 한 번만 찍혔다" ($typed -eq $bullet)
Check "앱이 복사를 주웠다" ((LogText) -match "복사 \d+자")
Check "앱이 Ctrl+V 를 가로챘다" ((LogText) -match "hotkey 0")
CheckTarget "총이 마우스가 아니라 글자 커서로 갔다 (시스템 캐럿)" "caret"

Write-Host "› 무기고"
function Grab($rect) {
  $bmp = New-Object System.Drawing.Bitmap $rect.Width, $rect.Height
  [System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen($rect.X, $rect.Y, 0, 0, $bmp.Size)
  $bmp
}
# 두 그림이 얼마나 다른가(0~1). 버튼 하나가 밝아지는 정도는 작고, 무기고가 통째로 사라지면 크다.
function Differ($a, $b) {
  $n = 0; $d = 0
  for ($y = 2; $y -lt $a.Height; $y += 6) {
    for ($x = 2; $x -lt $a.Width; $x += 6) {
      $p = $a.GetPixel($x, $y); $q = $b.GetPixel($x, $y); $n += 1
      if (([Math]::Abs($p.R - $q.R) + [Math]::Abs($p.G - $q.G) + [Math]::Abs($p.B - $q.B)) -gt 60) { $d += 1 }
    }
  }
  if ($n -eq 0) { 1 } else { $d / $n }
}
[Keys2]::SetCursorPos(5, 5) | Out-Null
PressPanelKey
# 러너가 느린 날은 무기고가 그려지기까지 2초가 넘는다. 자리를 알려 올 때까지 기다린다.
$open = $false
for ($i = 0; $i -lt 20 -and -not $open; $i += 1) {
  Start-Sleep -Milliseconds 500
  $m = LastMatch "panelRect (-?\d+),(-?\d+),(\d+),(\d+)"
  $open = ($null -ne $m) -and ([int]$m.Groups[3].Value -gt 0)
}
Start-Sleep 1
Check "Alt+Shift+A 로 무기고가 열렸다" $open
if ($open) {
  $rect = New-Object System.Drawing.Rectangle ([int]$m.Groups[1].Value), ([int]$m.Groups[2].Value), ([int]$m.Groups[3].Value), ([int]$m.Groups[4].Value)
  Write-Host "  무기고 자리: $rect"
  $away = Grab $rect; Shot "panel-open"

  [Keys2]::SetCursorPos($rect.X + [int]($rect.Width / 2), $rect.Y + [int]($rect.Height / 2)) | Out-Null
  Start-Sleep 2
  $hover = Grab $rect; Shot "panel-hover"
  $diff = Differ $away $hover
  Write-Host ("  커서를 올리기 전과 다른 정도: {0:P1}" -f $diff)
  Check "커서가 올라가면 창이 마우스를 받는다" ((LastGroup "pass (True|False)") -eq "False")
  Check "커서를 올려도 무기고가 그대로 보인다" ($diff -lt 0.15)

  # 총 고르는 칸은 네 칸이 폭을 나눠 쓴다. 둘째가 기관총, 첫째가 권총.
  $gunsY = $rect.Y + 62
  [Keys2]::Click($rect.X + [int]($rect.Width * 0.375), $gunsY); Start-Sleep 2
  Shot "panel-clicked"
  Check "무기고의 버튼이 눌린다 (기관총으로 바뀌었다)" ((LastGroup "status (\S+)") -eq "기관총")
  [Keys2]::Click($rect.X + [int]($rect.Width * 0.125), $gunsY); Start-Sleep 2
  Check "다시 권총으로 돌아왔다" ((LastGroup "status (\S+)") -eq "권총")

  [Keys2]::SetCursorPos(5, 5) | Out-Null
  Start-Sleep 2
  Check "커서가 떠나면 다시 클릭을 통과시킨다" ((LastGroup "pass (True|False)") -eq "True")
  $diff = Differ $away (Grab $rect)
  Write-Host ("  커서가 떠난 뒤 처음과 다른 정도: {0:P1}" -f $diff)
  Check "커서가 떠난 뒤에도 무기고가 그대로 보인다" ($diff -lt 0.15)
  PressPanelKey
  Start-Sleep 1
}

# 브라우저는 시스템 캐럿을 안 만든다. 거기서도 글자 커서를 찾는지 본다.
# 러너의 엣지 사정(첫 실행 화면, 포커스)에 달려 있어서 결과는 적기만 하고 실패로 치지 않는다.
Write-Host "› 브라우저에서 (참고)"
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (Test-Path $edge) {
  try {
    $page = Join-Path $env:TEMP "pastegun-target.html"
    Set-Content $page '<body style="margin:60px"><textarea autofocus style="width:500px;height:160px;font-size:24px"></textarea></body>' -Encoding UTF8
    $userData = Join-Path $env:TEMP "pastegun-edge"
    Start-Process $edge -ArgumentList "--no-first-run", "--no-default-browser-check", "--user-data-dir=`"$userData`"", "--window-position=150,100", "--window-size=800,500", "--app=file:///$($page -replace '\\', '/')"
    Start-Sleep 10
    $window = Get-Process msedge -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($window) { [Keys2]::SetForegroundWindow($window.MainWindowHandle) | Out-Null }
    [Keys2]::SetCursorPos(5, 5) | Out-Null
    [System.Windows.Forms.Clipboard]::SetText("JKL 총알 000")
    Start-Sleep 2
    PressCtrlV
    Start-Sleep 4
    Shot "edge"
    $m = LastMatch "과녁 (\w+) (-?\d+),(-?\d+)"
    Write-Host "  과녁: $(if ($m) { $m.Value })  (mouse 면 못 찾은 것, 창은 150,100 에 800x500)"
  } catch { Write-Host "  (엣지를 못 돌림: $_)" }
  Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
} else { Write-Host "  (엣지가 없음)" }

# 캐럿을 묻는 길 셋 가운데 나머지 둘. 같은 글상자에 한 길만 쓰게 해서 각각 본다.
Write-Host "› 글자 커서를 묻는 다른 길"
Restart "acc"
$typed = FireAt "DEF 총알 456" "after-fire-acc"
Check "다시 켠 뒤에도 쏜다" ($typed -eq "DEF 총알 456")
CheckTarget "MSAA 캐럿으로 글자 커서를 찾았다 (크롬·일렉트론이 답하는 길)" "acc"
Restart "uia"
$typed = FireAt "GHI 총알 789" "after-fire-uia"
CheckTarget "UI 자동화로 글자 커서를 찾았다 (새 메모장·WPF 가 답하는 길)" "uia|recent"

if (Test-Path $log) { Copy-Item $log (Join-Path $out "debug.log") }

Write-Host "› 제거"
Start-Process (Join-Path $app "unins000.exe") -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"
# 제거 프로그램은 자기를 임시 폴더로 옮겨 놓고 바로 돌아온다. 끝날 때까지 폴더를 본다.
for ($i = 0; $i -lt 30 -and (Test-Path (Join-Path $app "PasteGun.exe")); $i += 1) { Start-Sleep 1 }
Start-Sleep 2
Check "앱이 꺼졌다" ($null -eq (Get-Process PasteGun -ErrorAction SilentlyContinue))
Check "앱 폴더가 없어졌다" (-not (Test-Path (Join-Path $app "PasteGun.exe")))
Check "저장한 탄창이 없어졌다" (-not (Test-Path (Join-Path $data "state.json")))
Check "시작 프로그램에서 빠졌다" (-not (Test-Path (Join-Path ([Environment]::GetFolderPath("Startup")) "Paste Gun.lnk")))

if ($failed.Count -gt 0) {
  Write-Host "`n실패 $($failed.Count)개: $($failed -join ' / ')"
  exit 1
}
Write-Host "`n다 통과"
