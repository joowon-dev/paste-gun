# 설치본을 진짜 윈도우에서 한 바퀴 돌려 본다 (CI 의 windows 러너).
#   깔기 → 앱이 뜨는가 → 복사하고 Ctrl+V 를 누르면 글자가 찍히는가 → 제거하면 깨끗한가
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
$text = if (Test-Path $log) { Get-Content $log -Raw -Encoding UTF8 } else { "" }
Check "렌더러가 단축키를 등록했다 (다리가 살아 있다)" ($text -match "핫키 13개 등록 True")

Write-Host "› 쏘는가"
# 과녁: 글상자 하나뿐인 창. 타이머가 차례로 복사 → Ctrl+V → 읽기를 한다.
$form = New-Object System.Windows.Forms.Form
$form.Text = "target"; $form.TopMost = $true; $form.Width = 600; $form.Height = 200
$box = New-Object System.Windows.Forms.TextBox
$box.Dock = "Fill"; $box.Multiline = $true; $box.Font = New-Object System.Drawing.Font("Segoe UI", 16)
$form.Controls.Add($box)
$script:step = 0
$script:typed = ""
$bullet = "ABC 총알 123"
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1500
$timer.Add_Tick({
  $script:step += 1
  switch ($script:step) {
    1 { [Keys2]::SetForegroundWindow($form.Handle) | Out-Null; $box.Focus() | Out-Null }
    2 { [System.Windows.Forms.Clipboard]::SetText($bullet) }
    3 { [Keys2]::Down(0x11); [Keys2]::Down(0x56); [Keys2]::Up(0x56); [Keys2]::Up(0x11) }
    6 { Shot "after-fire"; $script:typed = $box.Text; $timer.Stop(); $form.Close() }
  }
})
$form.Add_Shown({ $form.Activate(); $timer.Start() })
[System.Windows.Forms.Application]::Run($form)
Set-Content (Join-Path $out "typed.txt") $script:typed -Encoding UTF8
Write-Host "  찍힌 글: [$($script:typed)]"
Check "Ctrl+V 로 복사한 글이 그대로, 한 번만 찍혔다" ($script:typed -eq $bullet)

$text = if (Test-Path $log) { Get-Content $log -Raw -Encoding UTF8 } else { "" }
Check "앱이 복사를 주웠다" ($text -match "복사 \d+자")
Check "앱이 Ctrl+V 를 가로챘다" ($text -match "hotkey 0")
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
