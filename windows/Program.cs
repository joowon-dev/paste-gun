// 붙여넣기 총 — Windows 셸.
//
// 규칙은 전부 web\ 안의 JS 다. 이 파일은 mac/Sources/main.swift 와 같은 일을 하고,
// 다리(window.sneaky)도 같은 모양이다. 한쪽만 고치면 두 플랫폼이 다른 물건이 된다.
//
//   · 바탕화면을 덮는 투명·클릭 통과 오버레이 (상어 셸과 같은 방식: 레이어드 + DWM 픽셀 알파)
//   · 전역 핫키 — RegisterHotKey. 키를 삼킨다. 어떤 키인지는 렌더러가 정해 준다(기본 방아쇠는 Ctrl+V 그 자체)
//   · 클립보드 지켜보기 — AddClipboardFormatListener
//   · 발사 — 클립보드에 넣고 SendInput 으로 Ctrl+V
//
// 맥과 달리 권한이 필요 없다. 다만 **관리자 권한으로 뜬 창에는 못 쏜다**(UIPI 가
// 낮은 권한의 SendInput 을 막는다) — 에러 없이 조용히 안 찍힌다.
//
// **이 셸은 실제 윈도우에서 돌려 본 적이 없다.** 컴파일까지만 이 맥에서 확인했다.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace PasteGun;

static class Program
{
    [STAThread]
    static void Main()
    {
        // **둘이 뜨면 붙여넣기가 두 번 나간다.** 둘 다 같은 Ctrl+V 를 잡으려 들기 때문이다.
        // 설치본도 이 이름으로 「돌고 있는가」를 본다(installer.iss 의 AppMutex).
        using var mutex = new Mutex(true, "PasteGun.SingleInstance", out var first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new OverlayContext());
    }
}

/// <summary>창을 띄우지 않는 트레이 앱.</summary>
sealed class OverlayContext : ApplicationContext
{
    private readonly Overlay overlay = new();
    private readonly NotifyIcon tray = new();

    public OverlayContext()
    {
        overlay.Show();
        tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        tray.Visible = true;
        Refresh();
        overlay.SettingsChanged += Refresh;
    }

    private void Refresh()
    {
        // 트레이 글자는 63자까지다. 넘으면 예외가 난다.
        var text = $"붙여넣기 총 — {overlay.StatusText}";
        tray.Text = text.Length > 63 ? text[..63] : text;

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(overlay.StatusText) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        // 새 버전이 있을 때만 낸다. 없으면 메뉴에 아무 흔적도 없다.
        if (overlay.UpdateVersion is not null)
        {
            menu.Items.Add(new ToolStripMenuItem(
                overlay.UpdateNote ?? $"새 버전 {overlay.UpdateVersion} 설치",
                null, (_, _) => overlay.InstallUpdate())
            {
                Enabled = !overlay.Updating,
            });
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add(new ToolStripMenuItem(overlay.PanelKeyLabel.Length > 0 ? $"무기고  {overlay.PanelKeyLabel}" : "무기고", null, (_, _) => overlay.TogglePanel())
        {
            Checked = overlay.PanelOpen,
        });
        menu.Items.Add(new ToolStripMenuItem("HUD 숨기기", null, (_, _) => overlay.ToggleHidden())
        {
            Checked = overlay.HudHidden,
        });
        menu.Items.Add(new ToolStripSeparator());
        if (overlay.KeyHint.Length > 0)
        {
            menu.Items.Add(new ToolStripMenuItem(overlay.KeyHint) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
        }
        if (Overlay.Uninstaller is not null)
        {
            menu.Items.Add(new ToolStripMenuItem("붙여넣기 총 제거…", null, (_, _) =>
            {
                // 설치본이 만든 제거 프로그램에 넘긴다. 물어보는 것도, 저장한 탄창을 지우는 것도 거기서 한다.
                if (!Overlay.StartUninstaller()) return;
                Quit();
            }));
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add(new ToolStripMenuItem($"버전 {Overlay.CurrentVersion}") { Enabled = false });
        menu.Items.Add(new ToolStripMenuItem("종료", null, (_, _) => Quit()));
        tray.ContextMenuStrip = menu;
    }

    private void Quit()
    {
        tray.Visible = false;
        overlay.Close();
        ExitThread();
    }
}

sealed class Overlay : Form
{
    // 창 스타일 — 마우스 통과 + 작업 표시줄에 안 뜸 + 포커스 안 가져감.
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint LWA_COLORKEY = 0x00000001;
    private const uint LWA_ALPHA = 0x00000002;

    private const int WM_HOTKEY = 0x0312;
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008;
    private const int MOD_NOREPEAT = 0x4000;

    private const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const ushort VK_TAB = 0x09, VK_RETURN = 0x0D, VK_V = 0x56;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    // 핫키 번호. 맥 셸의 HotKey 와 같다 — 저격 슬롯은 SNIPE_BASE + 슬롯 번호(1~9).
    private const int HOTKEY_TRIGGER = 0, HOTKEY_GUN = 1, HOTKEY_MAGAZINE = 2, HOTKEY_PANEL = 3;
    private const int SNIPE_BASE = 10;

    /// <summary>클립보드에 넣고 Ctrl+V 를 누르기까지, 그 뒤에 Tab·Enter 를 누르기까지(ms).</summary>
    private const int BeforePaste = 20, BeforeAfterKey = 70, AfterPaste = 120;
    /// <summary>방아쇠 핫키의 Alt+Shift 에서 손이 떨어지기를 기다리는 한도(ms).</summary>
    private const int ModifierWait = 1000;

    /// <summary>비밀번호 관리자가 「이건 기록하지 말라」고 붙이는 표시. 총알로 안 받는다.</summary>
    private static readonly string[] SecretFormats =
        ["ExcludeClipboardContentFromMonitorProcessing", "Clipboard Viewer Ignore"];

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr param);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr param);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, int mod, int vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // 유니온의 크기는 가장 큰 멤버(MOUSEINPUT)가 정한다. 키보드만 넣으면 구조체가 작아져서
    // SendInput 이 크기가 틀렸다고 조용히 0 을 돌려준다.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(IntPtr hWnd, ref DWM_BLURBEHIND bb);

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_BLURBEHIND
    {
        public int dwFlags;
        public bool fEnable;
        public IntPtr hRgnBlur;
        public bool fTransitionOnMaximized;
    }

    private const int DWM_BB_ENABLE = 0x1;
    private const int DWM_BB_BLURREGION = 0x2;

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PasteGun");
    private static readonly string StatePath = Path.Combine(DataDir, "state.json");
    private static readonly bool Debugging = Environment.GetEnvironmentVariable("PASTEGUN_DEBUG") is not null;
    /// <summary>켜면 붙여넣지 않고 무엇을 쏘려 했는지만 적는다.</summary>
    private static readonly bool DryRun = Environment.GetEnvironmentVariable("PASTEGUN_DRYRUN") is not null;

    /// <summary>WinExe 라 콘솔이 없다. PASTEGUN_DEBUG 가 켜져 있을 때만 debug.log 로 흘린다.</summary>
    private static void DebugLog(string text)
    {
        if (!Debugging) return;
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(Path.Combine(DataDir, "debug.log"),
                $"[gun] {DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}");
        }
        catch
        {
            // 로그 쓰기 실패로 앱이 죽으면 안 된다.
        }
    }

    /// <summary>값을 JS 리터럴로 만든다. 손으로 따옴표를 붙이지 않는다.</summary>
    private static string Js(object? value) => JsonSerializer.Serialize(value);

    private readonly WebView2 web = new();
    private readonly System.Windows.Forms.Timer cursorTimer = new() { Interval = 33 };

    private bool ready;
    private bool passingThrough = true;
    private bool holdingKeyboard;
    private Rectangle panelRect = Rectangle.Empty;
    private bool cursorOverPanel;

    /// <summary>발사하느라 <b>우리가</b> 바꾼 것. 이걸 다시 주우면 쏠 때마다 탄창이 불어난다.</summary>
    private uint ownSequence;
    private readonly Queue<(string Text, string? After, bool Typed, int Lead)> shots = new();
    private bool firing;

    public string StatusText { get; private set; } = "권총 · 현장 탄창 0/0";
    public bool PanelOpen { get; private set; }
    public bool HudHidden { get; private set; }
    public event Action? SettingsChanged;

    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen!.WorkingArea; // 작업 표시줄 자리는 비워 둔다

        // DWM 합성에 맡길 때는 폼이 스스로 배경을 칠하면 안 된다. 검정은 「아무것도 없음」이다.
        BackColor = Color.Black;

        web.Dock = DockStyle.Fill;
        web.DefaultBackgroundColor = Color.Transparent;
        Controls.Add(web);

        cursorTimer.Tick += (_, _) => UpdateCursorOverPanel();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // 켠 지 20초 뒤에 한 번, 그 뒤로는 하루 한 번.
        var firstCheck = new System.Windows.Forms.Timer { Interval = 20_000 };
        firstCheck.Tick += (_, _) => { firstCheck.Stop(); _ = CheckForUpdateAsync(); };
        firstCheck.Start();
        updateTimer.Tick += (_, _) => _ = CheckForUpdateAsync();
        updateTimer.Start();

        _ = InitWebAsync();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Bounds = Screen.PrimaryScreen!.WorkingArea;

    // MARK: 업데이트와 제거
    //
    // <b>두 단계다.</b> 1단계는 「새 버전이 있다」고 메뉴에 알리는 것뿐이고, 2단계는 눌렀을 때
    // 설치본을 받아 조용히 다시 까는 것이다. 눌러야만 깐다 — 붙여넣는 도중에 앱이 혼자 다시 뜨면 안 된다.
    // 탄창과 단축키는 %AppData%\PasteGun\state.json 에 있어서 다시 깔아도 그대로다.

    private const string ReleaseApi = "https://api.github.com/repos/joowon-dev/paste-gun/releases/latest";
    private const string ReleasePage = "https://joowonkoh.com/playground/paste-gun";
    /// <summary>자동 업데이트가 받아 가는 것은 설치본이다 — zip 은 사람이 직접 풀 때 쓴다.</summary>
    private const string WinAssetSuffix = "-win-Setup.exe";

    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 24 * 60 * 60 * 1000 };

    public string? UpdateVersion { get; private set; }
    public string? UpdateNote { get; private set; }
    public bool Updating { get; private set; }
    private string? updateAsset;

    public static string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>"v1.10.0" 이 "1.9.0" 보다 높다. 문자열로 비교하면 거꾸로 나온다.</summary>
    public static bool IsNewerVersion(string candidate, string current)
    {
        static int[] Parts(string text) => text.TrimStart('v', 'V', ' ')
            .Split('.')
            .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0)
            .ToArray();

        var a = Parts(candidate);
        var b = Parts(current);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i += 1)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    /// <summary>하루 한 번 물어본다. 실패는 조용히 삼킨다 — 새 버전을 못 찾는 것과 총이 안 나가는 것은 다른 일이다.</summary>
    private async Task CheckForUpdateAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi);
            request.Headers.Add("Accept", "application/vnd.github+json");
            // GitHub 은 User-Agent 없는 요청을 거절한다.
            request.Headers.Add("User-Agent", "PasteGun");

            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;

            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            var tag = json.GetProperty("tag_name").GetString();
            DebugLog($"릴리스 {tag} · 지금 {CurrentVersion}");
            if (tag is null || !IsNewerVersion(tag, CurrentVersion)) return;

            string? asset = null;
            if (json.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name is not null && name.EndsWith(WinAssetSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        asset = a.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            BeginInvoke(() =>
            {
                UpdateVersion = tag;
                updateAsset = asset;
                SettingsChanged?.Invoke();
            });
        }
        catch
        {
            // 조용히.
        }
    }

    /// <summary>받아서 조용히 다시 깐다. 한 걸음이라도 어긋나면 받는 곳을 연다.</summary>
    public async void InstallUpdate()
    {
        if (Updating) return;
        if (updateAsset is null)
        {
            OpenReleasePage();
            return;
        }

        Updating = true;
        UpdateNote = "내려받는 중…";
        SettingsChanged?.Invoke();

        var path = Path.Combine(Path.GetTempPath(), $"PasteGun-{Guid.NewGuid():N}.exe");
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, updateAsset))
            {
                request.Headers.Add("User-Agent", "PasteGun");
                using var response = await http.SendAsync(request);
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(path);
                await response.Content.CopyToAsync(file);
            }

            UpdateNote = "설치하는 중…";
            SettingsChanged?.Invoke();

            // 조용히 다시 깐다.
            Process.Start(new ProcessStartInfo(path)
            {
                Arguments = "/SILENT /NORESTART",
                UseShellExecute = true,
            });
            // 물러난다. 설치본이 끝나면 새 것을 다시 띄운다(installer.iss 의 [Run]).
            Application.Exit();
        }
        catch
        {
            Updating = false;
            UpdateNote = "직접 받기";
            SettingsChanged?.Invoke();
            OpenReleasePage();
        }
    }

    private static void OpenReleasePage()
    {
        try { Process.Start(new ProcessStartInfo(ReleasePage) { UseShellExecute = true }); }
        catch { /* 브라우저가 없으면 할 수 있는 것이 없다. */ }
    }

    /// <summary>설치본으로 깔았을 때만 있다. zip 을 풀어 쓰는 사람은 폴더를 지우면 끝이다.</summary>
    public static string? Uninstaller
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
            return File.Exists(path) ? path : null;
        }
    }

    public static bool StartUninstaller()
    {
        if (Uninstaller is not { } path) return false;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    // MARK: 투명과 클릭 — 상어 셸에서 실제 PC 로 확인된 방식 그대로다.

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            // WS_EX_TRANSPARENT 는 WS_EX_LAYERED 와 같이 있어야만 클릭을 통과시킨다.
            p.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return p;
        }
    }

    /// <summary>뜰 때 포커스를 가져가지 않는다.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // 레이어드 창은 알파를 정해 주지 않으면 아예 안 그려진다. 검정 컬러 키는 DWM 이
        // 픽셀 알파를 무시하는 기계에서도 빈 곳을 뚫는 안전장치다 — 그래서 렌더러는
        // 순수 검정(#000)을 칠하지 않는다.
        SetLayeredWindowAttributes(Handle, 0, 255, LWA_COLORKEY | LWA_ALPHA);
        EnablePerPixelAlpha();

        // 핫키는 렌더러가 단축키를 알려 줄 때 건다(SetHotkeys).
        ownSequence = GetClipboardSequenceNumber();
        AddClipboardFormatListener(Handle);
    }

    /// <summary>빈 영역으로 블러를 켠다 — 흐림은 없고 픽셀 단위 알파만 얻는다.</summary>
    private void EnablePerPixelAlpha()
    {
        var region = CreateRectRgn(0, 0, -1, -1);
        try
        {
            var bb = new DWM_BLURBEHIND { dwFlags = DWM_BB_ENABLE | DWM_BB_BLURREGION, fEnable = true, hRgnBlur = region };
            DebugLog($"DwmEnableBlurBehindWindow hr={DwmEnableBlurBehindWindow(Handle, ref bb)}");
        }
        finally
        {
            DeleteObject(region);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnregisterAll();
        RemoveClipboardFormatListener(Handle);
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) HotKeyPressed((int)m.WParam);
        else if (m.Msg == WM_CLIPBOARDUPDATE) ClipboardChanged();
        base.WndProc(ref m);
    }

    // MARK: 핫키

    /// <summary>렌더러가 정해 준 단축키(src/game/keys.js). id, 수식키, 가상 키.</summary>
    private readonly List<(int id, int mods, int vk)> hotkeys = [];
    private bool hotkeysOn;
    public string KeyHint { get; private set; } = "";
    public string PanelKeyLabel { get; private set; } = "";

    private void SetHotkeys(JsonElement list)
    {
        UnregisterAll();
        hotkeys.Clear();
        foreach (var item in list.EnumerateArray())
        {
            var mods = MOD_NOREPEAT;
            if (Flag(item, "ctrl")) mods |= MOD_CONTROL;
            if (Flag(item, "alt")) mods |= MOD_ALT;
            if (Flag(item, "shift")) mods |= MOD_SHIFT;
            if (Flag(item, "meta")) mods |= MOD_WIN;
            hotkeys.Add(((int)Num(item, "id"), mods, (int)Num(item, "key")));
        }
        if (!holdingKeyboard) RegisterAll();
    }

    private static bool Flag(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary><b>등록이 성공해도 시스템이 먼저 가로챌 수 있다.</b> 성공은 검증이 아니다.</summary>
    private void RegisterAll()
    {
        if (hotkeysOn) return;
        hotkeysOn = true;
        var ok = true;
        foreach (var (id, mods, vk) in hotkeys) ok &= RegisterHotKey(Handle, id, mods, vk);
        DebugLog($"핫키 {hotkeys.Count}개 등록 {ok}");
    }

    private void UnregisterAll()
    {
        if (!hotkeysOn) return;
        hotkeysOn = false;
        foreach (var (id, _, _) in hotkeys) UnregisterHotKey(Handle, id);
    }

    /// <summary>
    /// Ctrl+V 를 <b>우리가</b> 누른다. 기본 방아쇠가 Ctrl+V 라, 핫키를 건 채로 누르면 우리 핫키가
    /// 그걸 다시 잡아 한 발이 또 나간다. 누르는 동안만 핫키를 풀었다 다시 건다.
    /// </summary>
    private async Task SendPasteAsync()
    {
        UnregisterAll();
        Tap(VK_V, withControl: true);
        await Task.Delay(60);
        if (!holdingKeyboard) RegisterAll();
    }

    /// <summary>총이 쏠 일이 아니다. 삼켰던 붙여넣기 키를 원래대로 눌러 준다. 클립보드는 안 건드린다.</summary>
    private async void PassPaste()
    {
        if (DryRun) return;
        await SendPasteAsync();
        DebugLog("흘려보냄 Ctrl+V");
    }

    private void HotKeyPressed(int id)
    {
        DebugLog($"hotkey {id}");
        if (id > SNIPE_BASE) Call("__gunSnipe", id - SNIPE_BASE);
        else if (id == HOTKEY_TRIGGER) Call("__gunTrigger");
        else if (id == HOTKEY_GUN) Call("__gunNextGun");
        else if (id == HOTKEY_MAGAZINE) Call("__gunNextMagazine");
        else if (id == HOTKEY_PANEL) TogglePanel();
    }

    // MARK: 클립보드 — 복사한 것이 총알이 된다

    private void ClipboardChanged()
    {
        if (GetClipboardSequenceNumber() == ownSequence) return;
        try
        {
            if (SecretFormats.Any(Clipboard.ContainsData) || !Clipboard.ContainsText())
            {
                // 그림·파일·비밀번호. 총알이 아니다 — 렌더러에 알려야 Ctrl+V 가 그것을 그대로 붙여넣는다.
                Call("__gunCopiedOther");
                return;
            }
            var text = Clipboard.GetText();
            DebugLog($"복사 {text.Length}자");
            Call("__gunCopied", text);
        }
        catch (ExternalException)
        {
            // 다른 프로그램이 클립보드를 쥐고 있다. 이번 복사는 놓친다 — 죽는 것보다 낫다.
        }
    }

    // MARK: 발사

    /// <summary>
    /// 한 발. <b>줄을 세워서 하나씩 한다</b> — 앞 발의 Ctrl+V 가 나가기 전에 다음 발이
    /// 클립보드를 덮어쓰면 같은 총알이 두 번 찍힌다.
    /// </summary>
    private async void Fire(string text, string? after, bool typed, int lead)
    {
        shots.Enqueue((text, after, typed, lead));
        if (firing) return;
        firing = true;
        try
        {
            while (shots.Count > 0)
            {
                var shot = shots.Dequeue();
                try
                {
                    await FireOneAsync(shot.Text, shot.After, shot.Typed, shot.Lead);
                }
                catch (Exception error)
                {
                    DebugLog($"발사 실패 {error.Message}");
                }
            }
        }
        finally
        {
            firing = false;
        }
    }

    /// <summary>
    /// <b>넣는 길이 둘이다.</b> 한 글자씩 쏘는 총은 글자를 직접 쳐 넣고, 저격총은 통째로
    /// 붙여넣는다. 초당 30발을 붙여넣기로 쏘면 받는 앱이 읽기 전에 클립보드가 덮어써진다.
    /// 다만 <b>줄바꿈과 탭은 쳐 넣지 않고 붙여넣는다</b> — 쳐 넣은 Enter 는 메신저에서
    /// 「보내기」다. 맥 셸의 fire 와 같은 규칙이다.
    /// </summary>
    private async Task FireOneAsync(string text, string? after, bool typed, int lead)
    {
        // 화면에서 글자가 총구를 떠나 과녁에 닿기까지(ms). 그만큼 기다렸다 넣으면 꽂히는 순간에 찍힌다.
        lead = Math.Clamp(lead, 0, 400);
        typed = typed && !text.Any(c => c is '\n' or '\r' or '\t');
        if (DryRun)
        {
            DebugLog($"발사(연습) {Js(text)} after={after ?? "-"}");
            ShowShot(text);
            return;
        }

        await WaitForModifiersUpAsync();

        // **붙여넣기 전에 묻는다.** 붙여넣고 나면 커서는 이미 글 끝으로 가 있다.
        ShowShot(text);

        if (typed)
        {
            if (lead > 0) await Task.Delay(lead);
            TypeText(text);
        }
        else
        {
            // 클립보드가 잠깐 잠겨 있을 수 있어 몇 번 다시 해 본다.
            Clipboard.SetDataObject(text, true, 5, 40);
            ownSequence = GetClipboardSequenceNumber();

            await Task.Delay(Math.Max(BeforePaste, lead));
            await SendPasteAsync();
            // 받는 앱은 Ctrl+V 를 받은 뒤에 클립보드를 읽는다. 다음 발이 그 전에 덮어쓰면 안 된다.
            await Task.Delay(AfterPaste);
        }

        var key = after == "tab" ? VK_TAB : after == "enter" ? VK_RETURN : (ushort)0;
        if (key != 0)
        {
            if (!typed) await Task.Delay(BeforeAfterKey);
            Tap(key, withControl: false);
        }
        DebugLog($"발사 {(typed ? "타자" : "붙여넣기")} {text.Length}자 after={after ?? "-"}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

    /// <summary>
    /// 붙여넣는 자리를 렌더러에 알린다 — 총이 거기로 와서 쏜다.
    ///
    /// 앞에 있는 창의 글자 커서를 묻는다(GetGUIThreadInfo, 스레드 0 = 앞 창). <b>모든 앱이
    /// 답해 주지는 않는다</b> — 시스템 캐럿을 안 쓰는 앱(크롬·일렉트론 계열 등)은 없다고
    /// 나온다. 그럴 때는 마우스 자리로 대신한다. 맥 셸의 caretPoint 와 같은 일이다.
    /// </summary>
    private void ShowShot(string text)
    {
        var point = Cursor.Position;
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (GetGUIThreadInfo(0, ref info) && info.hwndCaret != IntPtr.Zero)
        {
            var caret = new Point(info.rcCaret.left, (info.rcCaret.top + info.rcCaret.bottom) / 2);
            if (ClientToScreen(info.hwndCaret, ref caret)) point = caret;
        }

        // 화면 좌표는 실제 픽셀이고 렌더러는 CSS 픽셀로 그린다. 배율로 나눠서 넘긴다.
        if (!ready) return;
        var x = point.X - Bounds.Left;
        var y = point.Y - Bounds.Top;
        _ = web.ExecuteScriptAsync(
            $"window.__gunShotAt && window.__gunShotAt({x} / (window.devicePixelRatio || 1), {y} / (window.devicePixelRatio || 1), {Js(text)})");
    }

    private static readonly ushort[] Modifiers = [VK_MENU, VK_SHIFT, VK_CONTROL, VK_LWIN, VK_RWIN];

    private static bool Held(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// 방아쇠 핫키(Alt+Shift+V)의 Alt+Shift 에서 손이 떨어질 때까지 기다린다.
    ///
    /// SendInput 은 지금 눌린 키와 <b>섞인다</b> — 안 기다리면 받는 앱이 Ctrl+V 가 아니라
    /// Ctrl+Alt+Shift+V 를 받는다. 누르고 있는지 읽기만 하는 것이라 아무것도 삼키지 않는다.
    /// 끝까지 안 떼면 떼는 신호를 대신 보내고 쏜다.
    /// </summary>
    private static async Task WaitForModifiersUpAsync()
    {
        for (var waited = 0; waited < ModifierWait && Modifiers.Any(Held); waited += 10)
            await Task.Delay(10);

        var stuck = Modifiers.Where(Held).Select(vk => Key(vk, up: true)).ToArray();
        if (stuck.Length > 0) SendInput((uint)stuck.Length, stuck, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
    };

    /// <summary>
    /// 글자를 직접 쳐 넣는다. 가상 키가 아니라 <b>글자 자체</b>(UTF-16 단위)를 실어 보내므로
    /// 자판 배열과도 한/영 상태와도 상관없다. 클립보드를 건드리지 않는다.
    /// </summary>
    private static void TypeText(string text)
    {
        var inputs = new INPUT[text.Length * 2];
        for (var i = 0; i < text.Length; i += 1)
        {
            inputs[i * 2] = Unicode(text[i], up: false);
            inputs[i * 2 + 1] = Unicode(text[i], up: true);
        }
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length) DebugLog($"SendInput {sent}/{inputs.Length} err={Marshal.GetLastWin32Error()}");
    }

    private static INPUT Unicode(char unit, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wScan = unit, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } },
    };

    private static void Tap(ushort vk, bool withControl)
    {
        INPUT[] inputs = withControl
            ? [Key(VK_CONTROL, false), Key(vk, false), Key(vk, true), Key(VK_CONTROL, true)]
            : [Key(vk, false), Key(vk, true)];
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length) DebugLog($"SendInput {sent}/{inputs.Length} err={Marshal.GetLastWin32Error()}");
    }

    // MARK: 웹뷰

    private async Task InitWebAsync()
    {
        // 사용자 데이터 폴더를 AppData 에 둔다 — Program Files 에 설치되면 앱 폴더에 쓸 수 없다.
        // 총소리는 누가 클릭해서 나는 소리가 아니라서 자동 재생을 풀어 둔다.
        var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(DataDir, "webview"), options);
        await web.EnsureCoreWebView2Async(env);

        web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        web.CoreWebView2.Settings.AreDevToolsEnabled = false;
        web.CoreWebView2.Settings.IsStatusBarEnabled = false;
        web.CoreWebView2.WebMessageReceived += OnWebMessage;

        // 맥 셸의 커스텀 스킴에 해당하는 것. file:// 로 열면 ES 모듈 import 가 막힌다.
        web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "pastegun.local", Path.Combine(AppContext.BaseDirectory, "web"),
            CoreWebView2HostResourceAccessKind.Allow);

        await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript());
        web.CoreWebView2.Navigate("https://pastegun.local/renderer/index.html");

        ready = true;

        // **여기서 다시 건다.** WebView2 의 자식 창은 지금에서야 생겼고, 그 자식이 클릭을
        // 삼키면 이 모니터 전체가 먹통이 된다.
        UpdateMousePass(force: true);
    }

    /// <summary>
    /// 저장해 둔 것. 렌더러가 준 JSON 을 그대로 들고 있다가 그대로 돌려준다 — 셸은 그 안을
    /// 모른다. 한 번 읽었다 다시 써서, 깨진 파일이 스크립트에 섞이지 않게 한다.
    /// </summary>
    private static string SavedStateLiteral()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(StatePath));
            return JsonSerializer.Serialize(doc.RootElement);
        }
        catch
        {
            return "null";
        }
    }

    /// <summary>맥 셸과 <b>같은 모양</b>의 다리.</summary>
    private static string BridgeScript() => $$"""
        (() => {
          const post = (m) => window.chrome.webview.postMessage(m)
          const hook = (name) => (handler) => { window[name] = handler }
          window.sneaky = {
            platform: 'win',
            getState: () => Promise.resolve({{SavedStateLiteral()}}),
            saveState: (s) => post({ type: 'state', json: JSON.stringify(s) }),
            setHotkeys: (list, hint, panelLabel) => post({ type: 'hotkeys', list, hint: String(hint || ''), panelLabel: String(panelLabel || '') }),
            pass: () => post({ type: 'pass' }),
            fire: (shot) => post({ type: 'fire', text: String(shot.text), after: shot.after || null, how: shot.how || 'paste', lead: Number(shot.lead) || 0 }),
            setStatus: (text) => post({ type: 'status', text: String(text) }),
            // 렌더러는 CSS 픽셀로 말하고 커서는 실제 픽셀로 온다. 배율이 150% 면 그대로 견줄 때
            // 무기고 자리가 1.5 배 어긋난다.
            setPanelRect: (r) => { const s = window.devicePixelRatio || 1; post({
              type: 'panelRect', x: r ? r.x * s : -1, y: r ? r.y * s : -1, w: r ? r.w * s : 0, h: r ? r.h * s : 0,
            }) },
            closePanel: () => post({ type: 'closePanel' }),
            grabKeyboard: () => post({ type: 'grabKeyboard' }),
            releaseKeyboard: () => post({ type: 'releaseKeyboard' }),
            onTrigger: hook('__gunTrigger'),
            onSnipe: hook('__gunSnipe'),
            onNextGun: hook('__gunNextGun'),
            onNextMagazine: hook('__gunNextMagazine'),
            onCopied: hook('__gunCopied'),
            onCopiedOther: hook('__gunCopiedOther'),
            onPanel: hook('__gunPanel'),
            onHidden: hook('__gunHidden'),
            onShotAt: hook('__gunShotAt'),
            // 윈도우는 키를 보내는 데 권한이 필요 없다. 언제나 「쏠 수 있음」이다.
            onTrusted: (handler) => { handler(true) },
          }
          window.addEventListener('error', (e) => post({ type: 'log', text: `${e.message} (${e.filename}:${e.lineno})` }))
          window.addEventListener('unhandledrejection', (e) => post({ type: 'log', text: `reject ${e.reason}` }))
          console.error = (...args) => post({ type: 'log', text: args.join(' ') })
        })()
        """;

    private void Call(string name, params object?[] arguments)
    {
        if (!ready) return;
        var list = string.Join(", ", arguments.Select(Js));
        _ = web.ExecuteScriptAsync($"window.{name} && window.{name}({list})");
    }

    // MARK: 무기고

    public void TogglePanel() => SetPanel(!PanelOpen);

    private void SetPanel(bool open)
    {
        if (open == PanelOpen) return;
        if (open && HudHidden) SetHidden(false);
        PanelOpen = open;
        Call("__gunPanel", open);

        // 커서가 무기고 위인지는 물어봐야 안다 — 창이 마우스를 안 받는 동안에는 이벤트가 안 온다.
        cursorTimer.Enabled = open;
        if (!open)
        {
            panelRect = Rectangle.Empty;
            cursorOverPanel = false;
            ReleaseKeyboard();
        }
        UpdateMousePass();
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// <b>창이 마우스를 받는 것은 커서가 무기고 네모 위에 있을 때뿐이다.</b> 이 창은 화면
    /// 전체라, 무기고가 열렸다고 통째로 받으면 다른 창을 아예 못 누른다.
    /// </summary>
    private void UpdateCursorOverPanel()
    {
        var cursor = Cursor.Position;
        var inside = PanelOpen && !panelRect.IsEmpty
            && panelRect.Contains(cursor.X - Bounds.Left, cursor.Y - Bounds.Top);
        if (inside == cursorOverPanel) return;
        cursorOverPanel = inside;
        UpdateMousePass();
    }

    private void UpdateMousePass(bool force = false)
    {
        var wantPass = !cursorOverPanel;
        if (wantPass == passingThrough && !force) return;
        passingThrough = wantPass;
        DebugLog($"pass {wantPass}");

        // **창 하나에만 걸면 모자란다 — WebView2 가 자기 자식 창을 만든다.** 자식에는
        // 스타일이 안 붙어서, 자식이 클릭을 삼키면 밑의 앱을 누를 수가 없다.
        Touch(Handle);
        EnumChildWindows(Handle, (child, _) => { Touch(child); return true; }, IntPtr.Zero);

        void Touch(IntPtr hWnd)
        {
            var style = GetWindowLong(hWnd, GWL_EXSTYLE);
            SetWindowLong(hWnd, GWL_EXSTYLE,
                wantPass ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT);
        }
    }

    /// <summary>글자 칸을 눌렀다. 이제서야 키보드를 가져온다.</summary>
    private void GrabKeyboard()
    {
        if (holdingKeyboard) return;
        holdingKeyboard = true;
        UnregisterAll(); // 무기고에서 글자를 치는 동안에는 Ctrl+V 가 붙여넣기여야 한다
        SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) & ~WS_EX_NOACTIVATE);
        SetForegroundWindow(Handle);
    }

    /// <summary>글자 칸에서 손을 뗐거나 무기고를 닫았다.</summary>
    private void ReleaseKeyboard()
    {
        if (!holdingKeyboard) return;
        holdingKeyboard = false;
        RegisterAll();
        SetWindowLong(Handle, GWL_EXSTYLE, GetWindowLong(Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
    }

    /// <summary>
    /// HUD 만 감춘다. <b>창은 그대로 둔다</b> — 창을 내리면 웹뷰가 잠들어 엔진이 멈추고,
    /// 숨긴 채로는 총이 안 나간다.
    /// </summary>
    public void ToggleHidden() => SetHidden(!HudHidden);

    private void SetHidden(bool hidden)
    {
        HudHidden = hidden;
        if (hidden) SetPanel(false);
        Call("__gunHidden", hidden);
        SettingsChanged?.Invoke();
    }

    // MARK: 렌더러에서 오는 것

    private static string? Str(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static double Num(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble() : 0;

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var body = JsonDocument.Parse(e.WebMessageAsJson).RootElement;
            switch (Str(body, "type"))
            {
                case "log":
                    DebugLog($"[web] {Str(body, "text")}");
                    break;

                case "fire":
                    if (Str(body, "text") is { Length: > 0 } text) Fire(text, Str(body, "after"), Str(body, "how") == "type", (int)Num(body, "lead"));
                    break;

                case "pass":
                    PassPaste();
                    break;

                case "hotkeys":
                    if (body.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array) SetHotkeys(list);
                    KeyHint = Str(body, "hint") ?? "";
                    PanelKeyLabel = Str(body, "panelLabel") ?? "";
                    SettingsChanged?.Invoke();
                    break;

                case "state":
                    if (Str(body, "json") is { } json)
                    {
                        Directory.CreateDirectory(DataDir);
                        File.WriteAllText(StatePath, json);
                    }
                    break;

                case "status":
                    if (Str(body, "text") is { } status && status != StatusText)
                    {
                        StatusText = status;
                        SettingsChanged?.Invoke();
                    }
                    break;

                case "panelRect":
                    var w = Num(body, "w");
                    panelRect = w > 0
                        ? new Rectangle((int)Num(body, "x"), (int)Num(body, "y"), (int)w, (int)Num(body, "h"))
                        : Rectangle.Empty;
                    break;

                case "closePanel": SetPanel(false); break;
                case "grabKeyboard": GrabKeyboard(); break;
                case "releaseKeyboard": ReleaseKeyboard(); break;
            }
        }
        catch (Exception error)
        {
            DebugLog($"메시지 처리 실패 {error.Message}");
        }
    }
}
