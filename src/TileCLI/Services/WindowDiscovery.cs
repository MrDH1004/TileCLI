using System.Diagnostics;
using TileCLI.Models;
using TileCLI.Native;

namespace TileCLI.Services;

/// <summary>
/// 콘솔/터미널 창 탐지(휴리스틱, 폭넓게). 순수 콘솔 클래스만 잡으면 Windows Terminal(ConPTY)이
/// 누락되므로 [ConsoleWindowClass] ∪ [의사콘솔 터미널 호스트/프로세스] ∪ [mintty]를 포함한다.
/// 오탐/누락은 UI 체크박스로 최종 보정.
/// </summary>
public static class WindowDiscovery
{
    // 고전 콘솔 창 클래스
    private const string ConsoleClass = "ConsoleWindowClass";
    // Windows Terminal 호스팅 창 클래스
    private const string CascadiaClass = "CASCADIA_HOSTING_WINDOW_CLASS";

    // 알려진 터미널 프로세스명(.exe 제외, 대소문자 무시)
    private static readonly HashSet<string> TerminalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "windowsterminal", "wt", "cmd", "powershell", "pwsh", "conhost",
        "mintty", "bash", "sh", "git-bash", "gitbash",
        "alacritty", "wezterm-gui", "wezterm", "hyper", "cmder",
        "conemu", "conemu64", "tabby", "nu", "nushell", "cygwin",
        "wsl", "wslhost", "ubuntu", "kitty", "putty"
    };

    // pid -> 프로세스명 캐시(열거 1회 동안)
    private static readonly Dictionary<uint, string> ProcessNameCache = new();

    /// <summary>GPT CLI로 인식할 기본 프로세스명(config.env에서 덮어쓸 수 있음). 확장자·대소문자 무시.</summary>
    public static readonly IReadOnlyList<string> DefaultGptProcesses = new[] { "codex", "codex-cli", "chatgpt" };

    /// <summary>클로드 창으로 인식할 창 제목 접두 마커(관측: Claude Code 제목은 ✳로 시작).</summary>
    public static readonly IReadOnlyList<string> DefaultClaudeTitleMarkers = new[] { "✳" };

    /// <summary>GPT 창으로 인식할 창 제목 접두 마커(codex 제목/사용자 지정 접두).</summary>
    public static readonly IReadOnlyList<string> DefaultGptTitleMarkers = new[] { "codex", "gpt" };

    /// <summary>
    /// 터미널 창을 탐지·분류한다. Windows Terminal은 모든 창이 한 프로세스라 프로세스 트리로는
    /// 창별 구분이 안 되므로, 창 제목 접두 마커로 먼저 판별하고(창별 정확), 못 잡으면 프로세스 트리로 폴백한다.
    /// </summary>
    public static List<TerminalWindow> Discover(
        IReadOnlyCollection<string>? gptProcessNames = null,
        IReadOnlyCollection<string>? claudeTitleMarkers = null,
        IReadOnlyCollection<string>? gptTitleMarkers = null)
    {
        ProcessNameCache.Clear();
        var list = new List<TerminalWindow>();
        uint selfPid = (uint)Environment.ProcessId;

        var gptNames = gptProcessNames is { Count: > 0 } ? gptProcessNames : DefaultGptProcesses;
        var claudeTitles = claudeTitleMarkers is { Count: > 0 } ? claudeTitleMarkers : DefaultClaudeTitleMarkers;
        var gptTitles = gptTitleMarkers is { Count: > 0 } ? gptTitleMarkers : DefaultGptTitleMarkers;

        // 프로세스 트리 1회 스냅샷: 창별 구분 안 되는 제목 폴백용
        var inspector = ProcessTreeInspector.Capture();

        NativeMethods.EnumWindowsProc callback = (hWnd, _) =>
        {
            try
            {
                if (!NativeMethods.IsWindowVisible(hWnd)) return true;
                if (NativeMethods.IsCloaked(hWnd)) return true;            // UWP/가상데스크톱 유령 제외
                if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return true; // 최상위만

                long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
                bool isToolWindow = (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0;
                bool isAppWindow = (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0;
                if (isToolWindow && !isAppWindow) return true;            // 도구 창 제외

                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == selfPid) return true;                          // 자기 자신 제외

                string className = NativeMethods.GetWindowClass(hWnd);
                string procName = GetProcessName(pid);

                if (!IsTerminal(className, procName)) return true;

                string title = NativeMethods.GetWindowTitle(hWnd);

                // 1순위: 창 제목 마커(창별 정확 — WT 단일 프로세스 대응). 2순위: 프로세스 트리.
                var kind = ClassifyByTitle(title, claudeTitles, gptTitles);
                if (kind == TerminalKind.Unknown)
                    kind = inspector.ContainsClaude(pid) ? TerminalKind.Claude
                        : inspector.ContainsAny(pid, gptNames) ? TerminalKind.Gpt
                        : TerminalKind.Plain;

                list.Add(new TerminalWindow
                {
                    Handle = hWnd,
                    Title = title,
                    ProcessName = procName,
                    ClassName = className,
                    ProcessId = pid,
                    IsMinimized = NativeMethods.IsIconic(hWnd),
                    Kind = kind
                });
            }
            catch
            {
                // 개별 창 처리 실패는 무시하고 계속
            }
            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        return list;
    }

    /// <summary>창 제목 접두 마커로 분류(창별 정확). 못 맞추면 Unknown → 호출부가 프로세스 트리로 폴백.</summary>
    private static TerminalKind ClassifyByTitle(string title,
        IReadOnlyCollection<string> claudeMarkers, IReadOnlyCollection<string> gptMarkers)
    {
        if (string.IsNullOrWhiteSpace(title)) return TerminalKind.Unknown;
        string t = title.TrimStart();
        foreach (var m in gptMarkers)
            if (!string.IsNullOrWhiteSpace(m) && t.StartsWith(m, StringComparison.OrdinalIgnoreCase))
                return TerminalKind.Gpt;
        foreach (var m in claudeMarkers)
            if (!string.IsNullOrWhiteSpace(m) && t.StartsWith(m, StringComparison.OrdinalIgnoreCase))
                return TerminalKind.Claude;
        return TerminalKind.Unknown;
    }

    private static bool IsTerminal(string className, string procName)
    {
        if (string.Equals(className, ConsoleClass, StringComparison.Ordinal)) return true;
        if (string.Equals(className, CascadiaClass, StringComparison.Ordinal)) return true;
        if (className.StartsWith("mintty", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(procName) && TerminalProcesses.Contains(procName)) return true;
        return false;
    }

    private static string GetProcessName(uint pid)
    {
        if (ProcessNameCache.TryGetValue(pid, out var cached)) return cached;
        string name = string.Empty;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            name = p.ProcessName; // 확장자 없는 이름
        }
        catch
        {
            // 접근 불가 프로세스는 빈 이름
        }
        ProcessNameCache[pid] = name;
        return name;
    }
}
