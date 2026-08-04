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

    // 창 핸들 -> 마지막으로 확정된 분류와 그때의 pid. 작업 중이라 제목 마커가 스피너로 덮이면
    // 이 캐시로 종류를 유지한다. 핸들 재사용 오분류를 막으려고 pid를 함께 검사하고,
    // 열거마다 사라진 창의 항목을 지운다.
    private static readonly Dictionary<IntPtr, (TerminalKind Kind, uint Pid)> KindCache = new();

    // 폴더명 -> 그 폴더에서 최근 세션을 남긴 CLI(세션 스캔 결과 캐시). 스피너에 마커가 가려진 창의 보조 신호.
    private static Dictionary<string, TerminalKind>? _sessionHints;
    private static DateTime _sessionHintsAt;
    private static readonly TimeSpan SessionHintTtl = TimeSpan.FromSeconds(60);
    private const int SessionHintMax = 300;

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
        IReadOnlyCollection<string>? gptTitleMarkers = null,
        string? gptSessionsDir = null)
    {
        ProcessNameCache.Clear();
        var list = new List<TerminalWindow>();
        var seen = new HashSet<IntPtr>();
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

                // 1순위: 창 제목 마커(창별 정확). 2순위: 프로세스 트리.
                var kind = ClassifyByTitle(title, claudeTitles, gptTitles);
                if (kind == TerminalKind.Unknown)
                {
                    // 마커는 상시 신뢰할 수 없다. 두 CLI 모두 상황에 따라 제목을 갈아끼우기 때문:
                    // Claude는 작업 중 ✳를 브라유 스피너로 바꾸고(✳ dh_Stock → ⠐ dh_Stock),
                    // codex는 애초에 마커 없이 "<스피너> <폴더명>"·"<폴더명>"을 쓴다.
                    // ① 이 창에서 직전에 확정된 종류가 있으면 그대로 유지한다.
                    if (KindCache.TryGetValue(hWnd, out var cached) && cached.Pid == pid)
                        kind = cached.Kind;
                    else
                        // ② 제목 본문(작업 폴더명)을 각 CLI의 최근 세션 cwd와 대조해 특정한다.
                        kind = ClassifyBySessionCwd(title, gptSessionsDir);

                    if (kind == TerminalKind.Unknown)
                    {
                        // ③ 그래도 못 맞추면: 스피너가 있으면 AI 창인 것만은 확실하므로 종류 미상으로 둔다.
                        // WT는 모든 창이 한 프로세스를 공유 → 프로세스 트리로는 창별 구분이 불가하다.
                        // 마커가 없으면 형제 창의 claude/codex로 오탐하므로 폴백을 쓰지 않고 일반으로 둔다.
                        kind = IsSpinnerPrefixed(title) ? TerminalKind.Ai
                            : IsSharedProcessHost(className, procName) ? TerminalKind.Plain
                            : inspector.ContainsClaude(pid) ? TerminalKind.Claude
                            : inspector.ContainsAny(pid, gptNames) ? TerminalKind.Gpt
                            : TerminalKind.Plain;
                    }
                }

                // 확정된 종류만 기억(미상 AI는 저장하지 않는다 → 나중에 마커가 보이면 그때 확정).
                if (kind is TerminalKind.Claude or TerminalKind.Gpt)
                    KindCache[hWnd] = (kind, pid);
                seen.Add(hWnd);

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

        // 닫힌 창의 분류 캐시 정리(핸들 재사용 시 엉뚱한 창을 Claude/GPT로 물려받는 것 방지)
        if (KindCache.Count > 0)
        {
            var stale = new List<IntPtr>();
            foreach (var kv in KindCache)
                if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var h in stale) KindCache.Remove(h);
        }

        return list;
    }

    private const char BrailleFirst = (char)0x2800; // 브라유 점자 블록 시작(U+2800은 보이지 않는 문자라 코드로 표기)
    private const char BrailleLast = (char)0x28FF;  // 브라유 점자 블록 끝(U+28FF)

    /// <summary>
    /// 제목이 브라유 점자 블록(U+2800–U+28FF)으로 시작하는가. Claude Code·Codex CLI가 작업 중일 때
    /// 창 제목 접두를 이 스피너 문자로 바꾼다 → 마커 판별이 깨지는 구간의 신호로 쓴다.
    /// </summary>
    private static bool IsSpinnerPrefixed(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        char c = title.TrimStart()[0];
        return c >= BrailleFirst && c <= BrailleLast;
    }

    /// <summary>
    /// 제목 본문(= 작업 폴더명)을 각 CLI 최근 세션의 cwd 폴더명과 대조해 분류한다. 폴더명이 정확히
    /// 일치할 때만 인정한다(부분 일치는 일반 창 오탐 위험).
    /// </summary>
    private static TerminalKind ClassifyBySessionCwd(string title, string? gptSessionsDir)
    {
        string body = TitleBody(title);
        if (body.Length == 0) return TerminalKind.Unknown;
        var hints = GetSessionHints(gptSessionsDir);
        return hints.TryGetValue(body, out var kind) && kind is TerminalKind.Claude or TerminalKind.Gpt
            ? kind : TerminalKind.Unknown;
    }

    /// <summary>제목에서 스피너 접두 한 글자와 앞뒤 공백을 걷어낸 본문.</summary>
    private static string TitleBody(string title)
    {
        string t = title.TrimStart();
        if (t.Length > 0 && t[0] >= BrailleFirst && t[0] <= BrailleLast) t = t[1..];
        return t.Trim();
    }

    /// <summary>
    /// 폴더명 → 그 폴더에서 가장 최근 세션을 남긴 CLI.
    /// 세션 스캔은 파일 I/O라 TTL 동안 재사용하고, 마커·캐시가 모두 빗나간 창이 있을 때만 호출된다.
    /// </summary>
    private static Dictionary<string, TerminalKind> GetSessionHints(string? gptSessionsDir)
    {
        if (_sessionHints is not null && DateTime.UtcNow - _sessionHintsAt < SessionHintTtl)
            return _sessionHints;

        var map = new Dictionary<string, TerminalKind>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // 같은 폴더를 Claude·GPT가 모두 쓴 적이 있으면 마지막으로 활동한 쪽으로 본다.
            // 마커·캐시가 모두 실패한 뒤의 마지막 폴백이라, 틀려도 AI 창끼리의 오분류에 그친다
            // (이 신호가 없으면 그 창은 일반 창으로 떨어져 목록에서 아예 빠진다).
            var best = new Dictionary<string, (TerminalKind Kind, DateTime At)>(StringComparer.OrdinalIgnoreCase);
            // 서브에이전트 세션도 포함한다 — 이어하기 대상은 아니어도 "그 폴더에서 codex가 돌았다"는
            // 증거로는 유효하고, 빼면 활동 시각이 옛것으로 밀려 창 분류가 뒤집힌다.
            foreach (var s in ClaudeSessionService.GetRecent(SessionHintMax, null, gptSessionsDir, includeSubagent: true))
            {
                string name = FolderName(s.Cwd);
                if (name.Length == 0) continue;
                if (!best.TryGetValue(name, out var prev) || s.LastActive > prev.At)
                    best[name] = (s.Kind, s.LastActive);
            }
            foreach (var kv in best) map[kv.Key] = kv.Value.Kind;
        }
        catch
        {
            // 세션 스캔 실패는 힌트 없음으로 처리(분류는 AI 미상으로 떨어짐)
        }

        _sessionHints = map;
        _sessionHintsAt = DateTime.UtcNow;
        return map;
    }

    private static string FolderName(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return string.Empty;
        try { return new DirectoryInfo(cwd.TrimEnd('\\', '/')).Name; }
        catch { return string.Empty; }
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

    /// <summary>
    /// 여러 창이 한 프로세스를 공유하는 호스트(Windows Terminal)인지. 이런 창은 프로세스 트리로
    /// 창별 판별이 불가하므로, 제목 마커가 없으면 claude/gpt로 단정하면 안 된다.
    /// </summary>
    private static bool IsSharedProcessHost(string className, string procName) =>
        string.Equals(className, CascadiaClass, StringComparison.Ordinal)
        || string.Equals(procName, "WindowsTerminal", StringComparison.OrdinalIgnoreCase)
        || string.Equals(procName, "wt", StringComparison.OrdinalIgnoreCase);

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
