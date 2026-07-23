using System.Diagnostics;
using System.Text.Json;
using TileCLI.Models;

namespace TileCLI.Services;

/// <summary>
/// ~/.claude/projects/&lt;encoded&gt;/&lt;sessionId&gt;.jsonl 를 스캔해 "클로드로 실행했던 최근 세션"만
/// 프로젝트(cwd) 단위로 추린다(요구 6). 폴더명 디코딩은 손실이 있으므로 cwd는 jsonl 내부 값을 우선한다.
/// 복구는 기존 우클릭 메뉴와 동일하게 wt.exe -d &lt;cwd&gt; cmd /k claude -c 로 새 터미널을 띄운다.
/// </summary>
public static class ClaudeSessionService
{
    private const int MaxLinesToScan = 60; // cwd/summary는 파일 앞부분에 있음

    /// <summary>클로드 projects 루트 경로(~\.claude\projects).</summary>
    public static string ProjectsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    /// <summary>
    /// 프로젝트별 가장 최근 세션을 LastActive 내림차순으로 최대 max개 반환.
    /// hiddenCwds에 포함된 cwd는 "앱에서 삭제"로 숨긴 것이라 제외한다.
    /// </summary>
    /// <summary>GPT CLI 세션 루트(~/.codex/sessions). config(GptSessionsDir)로 덮어쓸 수 있음.</summary>
    public static string GptSessionsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    /// <summary>
    /// Claude(~/.claude/projects) + GPT(gptSessionsDir, 기본 ~/.codex/sessions) 최근 세션을
    /// cwd별 최신 1개씩 추려 LastActive 내림차순 최대 max개 반환. hiddenCwds는 제외.
    /// </summary>
    public static List<ClaudeSession> GetRecent(int max = 20, IEnumerable<string>? hiddenCwds = null, string? gptSessionsDir = null)
    {
        var hidden = new HashSet<string>(hiddenCwds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var all = new List<ClaudeSession>();
        all.AddRange(ScanClaude(hidden));
        all.AddRange(ScanGpt(gptSessionsDir, hidden));
        all.Sort((a, b) => b.LastActive.CompareTo(a.LastActive));
        if (all.Count > max) all.RemoveRange(max, all.Count - max);
        return all;
    }

    /// <summary>~/.claude/projects 스캔(cwd별 최신 1개, Kind=Claude).</summary>
    private static List<ClaudeSession> ScanClaude(HashSet<string> hidden)
    {
        var result = new List<ClaudeSession>();
        string root = ProjectsRoot;
        if (!Directory.Exists(root)) return result;

        var byCwd = new Dictionary<string, ClaudeSession>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in SafeEnumerateDirectories(root))
        {
            var newest = NewestTranscript(dir);
            if (newest is null) continue;
            var session = Parse(newest, dir);
            if (session is null) continue;
            string key = string.IsNullOrWhiteSpace(session.Cwd) ? dir : session.Cwd;
            if (hidden.Contains(key)) continue;
            if (!byCwd.TryGetValue(key, out var existing) || session.LastActive > existing.LastActive)
                byCwd[key] = session;
        }
        result.AddRange(byCwd.Values);
        return result;
    }

    /// <summary>GPT 세션 디렉토리 재귀 스캔(*.jsonl에서 cwd 탐색, cwd별 최신 1개, Kind=Gpt).</summary>
    private static List<ClaudeSession> ScanGpt(string? dir, HashSet<string> hidden)
    {
        var result = new List<ClaudeSession>();
        string root = string.IsNullOrWhiteSpace(dir) ? GptSessionsRoot : dir!;
        if (!Directory.Exists(root)) return result;

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories); }
        catch { return result; }

        var byCwd = new Dictionary<string, ClaudeSession>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            string? cwd; DateTime last;
            try { var fi = new FileInfo(file); cwd = FindCwd(fi); last = fi.LastWriteTime; }
            catch { continue; }
            if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd)) continue;
            if (hidden.Contains(cwd)) continue;

            var s = new ClaudeSession
            {
                SessionId = Path.GetFileNameWithoutExtension(file),
                Cwd = cwd,
                LastActive = last,
                TranscriptPath = file,
                Kind = TerminalKind.Gpt
            };
            if (!byCwd.TryGetValue(cwd, out var ex) || s.LastActive > ex.LastActive) byCwd[cwd] = s;
        }
        result.AddRange(byCwd.Values);
        return result;
    }

    /// <summary>jsonl 앞부분(최대 MaxLinesToScan줄)에서 "cwd" 문자열 속성을 재귀 탐색(CLI 형식 무관).</summary>
    private static string? FindCwd(FileInfo file)
    {
        try
        {
            int n = 0;
            foreach (var line in File.ReadLines(file.FullName))
            {
                if (++n > MaxLinesToScan) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var cwd = FindCwdIn(doc.RootElement);
                    if (!string.IsNullOrWhiteSpace(cwd)) return cwd;
                }
                catch { /* 라인 파싱 실패 무시 */ }
            }
        }
        catch { /* 파일 읽기 실패 */ }
        return null;
    }

    private static string? FindCwdIn(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    if (p.NameEquals("cwd") && p.Value.ValueKind == JsonValueKind.String)
                        return p.Value.GetString();
                    var r = FindCwdIn(p.Value);
                    if (r is not null) return r;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    var r = FindCwdIn(item);
                    if (r is not null) return r;
                }
                break;
        }
        return null;
    }

    /// <summary>세션이 저장된 폴더(~/.claude/projects/&lt;encoded&gt;). 없으면 null.</summary>
    public static string? StorageFolder(ClaudeSession session) =>
        string.IsNullOrEmpty(session?.TranscriptPath) ? null : Path.GetDirectoryName(session!.TranscriptPath);

    /// <summary>
    /// 세션 저장폴더(~/.claude/projects/&lt;encoded&gt;)를 휴지통으로 이동한다("실제 디렉토리 삭제").
    /// 안전장치: 반드시 projects 루트 하위 경로일 때만 삭제한다(엉뚱한 폴더 삭제 방지).
    /// </summary>
    public static bool DeleteSessionStorage(ClaudeSession session, out string error)
    {
        error = string.Empty;
        var folder = StorageFolder(session);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            error = $"세션 저장폴더를 찾을 수 없습니다: {folder}";
            return false;
        }

        // 안전 검사: projects 루트 하위인지 확인
        string root = Path.GetFullPath(ProjectsRoot).TrimEnd('\\', '/');
        string full = Path.GetFullPath(folder).TrimEnd('\\', '/');
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            full.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            error = $"안전 검사 실패 — projects 하위가 아니거나 루트 자체입니다: {full}";
            return false;
        }

        try
        {
            // 휴지통으로 이동(복구 가능)
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                folder,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try { return Directory.EnumerateDirectories(root); }
        catch { return Array.Empty<string>(); }
    }

    private static FileInfo? NewestTranscript(string dir)
    {
        try
        {
            FileInfo? best = null;
            foreach (var path in Directory.EnumerateFiles(dir, "*.jsonl"))
            {
                var fi = new FileInfo(path);
                if (best is null || fi.LastWriteTimeUtc > best.LastWriteTimeUtc) best = fi;
            }
            return best;
        }
        catch { return null; }
    }

    private static ClaudeSession? Parse(FileInfo file, string dir)
    {
        string? cwd = null;
        string? title = null;
        try
        {
            int n = 0;
            foreach (var line in File.ReadLines(file.FullName))
            {
                if (++n > MaxLinesToScan) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var el = doc.RootElement;
                    if (cwd is null && el.TryGetProperty("cwd", out var cwdEl) && cwdEl.ValueKind == JsonValueKind.String)
                        cwd = cwdEl.GetString();
                    if (title is null && el.TryGetProperty("type", out var typeEl) &&
                        typeEl.ValueKind == JsonValueKind.String && typeEl.GetString() == "summary" &&
                        el.TryGetProperty("summary", out var sumEl) && sumEl.ValueKind == JsonValueKind.String)
                        title = sumEl.GetString();
                }
                catch { /* 개별 라인 파싱 실패 무시 */ }

                if (cwd is not null && title is not null) break;
            }
        }
        catch { /* 파일 읽기 실패 → cwd null */ }

        // cwd가 없으면 폴더명 디코딩으로 근사(불완전 — 표시용)
        cwd ??= DecodeFolderName(Path.GetFileName(dir));
        if (string.IsNullOrWhiteSpace(cwd)) return null;

        return new ClaudeSession
        {
            SessionId = Path.GetFileNameWithoutExtension(file.Name),
            Cwd = cwd,
            Title = title ?? string.Empty,
            LastActive = file.LastWriteTime,
            TranscriptPath = file.FullName
        };
    }

    /// <summary>
    /// 폴더명(예: "E--AIWork-CLIEx")을 경로로 근사 복원. '-'가 구분자/경로문자 모두를 대체하므로
    /// 완전 복원은 불가 — jsonl의 cwd가 없을 때만 표시용 폴백으로 쓴다.
    /// </summary>
    private static string DecodeFolderName(string encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return string.Empty;
        // "X--..." → "X:\..." 패턴 근사: 첫 "--"를 ":\"로, 나머지 '-'를 '\'로
        int dd = encoded.IndexOf("--", StringComparison.Ordinal);
        if (dd == 1) // 드라이브 문자 1개
            return encoded[0] + ":\\" + encoded[(dd + 2)..].Replace('-', '\\');
        return encoded.Replace('-', '\\');
    }

    /// <summary>
    /// 세션을 새 터미널에서 복구. wt.exe -d &lt;cwd&gt; cmd /k claude -c (기존 우클릭 메뉴와 동일).
    /// wt.exe가 없으면 cmd.exe로 폴백.
    /// </summary>
    /// <summary>세션 작업 폴더(cwd)를 파일 탐색기로 연다.</summary>
    public static bool OpenFolder(string? cwd, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
        {
            error = $"폴더를 찾을 수 없습니다: {cwd}";
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{cwd}\"", UseShellExecute = true });
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>
    /// cwd의 git origin 리모트 URL에서 저장소 이름을 추출한다(예: .../TileCLI.git → TileCLI).
    /// git 미설치·리모트 없음·타임아웃이면 null(호출부가 폴더명으로 폴백).
    /// </summary>
    public static string? TryGetRepoName(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd)) return null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "remote get-url origin",
                WorkingDirectory = cwd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            string outp = p.StandardOutput.ReadToEnd().Trim();
            if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return null; }
            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(outp)) return null;

            string name = outp.TrimEnd('/');
            int i = name.LastIndexOfAny(new[] { '/', ':' });
            if (i >= 0) name = name[(i + 1)..];
            if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch { return null; }
    }

    /// <summary>기존 Claude 세션 이어서 복구(claude -c).</summary>
    public static bool Resume(ClaudeSession session, out string error) =>
        LaunchCommand(session?.Cwd, "claude -c", out error);

    /// <summary>해당 폴더에서 새 Claude 세션 시작(claude, -c 없음).</summary>
    public static bool LaunchNew(string? cwd, out string error) =>
        LaunchCommand(cwd, "claude", out error);

    /// <summary>
    /// cwd에서 새 터미널로 임의 CLI 명령을 실행한다(예: "claude -c" / "codex --continue" / "codex").
    /// 정리된 환경(CLAUDECODE 등 제거 + 컬러 힌트)으로 띄워 색상이 정상 출력된다.
    /// wt.exe -d &lt;cwd&gt; cmd /k &lt;command&gt; 우선, 실패 시 cmd.exe 폴백.
    /// </summary>
    public static bool LaunchCommand(string? cwd, string command, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
        {
            error = $"작업 폴더를 찾을 수 없습니다: {cwd}";
            return false;
        }
        if (string.IsNullOrWhiteSpace(command)) { error = "실행할 명령이 비어 있습니다."; return false; }

        // 1순위: Windows Terminal (정리된 환경으로 CreateProcess)
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{cwd}\" cmd /k {command}",
                UseShellExecute = false,   // 환경변수를 제어하려면 false 필요
                WorkingDirectory = cwd
            };
            CleanChildEnvironment(psi);
            Process.Start(psi);
            return true;
        }
        catch { /* wt 실행 불가 → 폴백 */ }

        // 폴백: cmd 새 창 (정리된 환경)
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k {command}",
                UseShellExecute = false,
                WorkingDirectory = cwd
            };
            CleanChildEnvironment(psi);
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 새 터미널에서 뜨는 claude가 "최상위 대화형 세션"으로 인식되도록 자식 환경을 정리한다.
    ///
    /// 배경: 이 앱이 Claude Code 세션 안에서 실행되면 CLAUDECODE=1, CLAUDE_CODE_CHILD_SESSION=1 등을
    /// 상속한다. 그 상태로 claude를 띄우면 "다른 클로드에 중첩 실행됐다(출력이 상위 에이전트에 캡처됨)"고
    /// 판단해 컬러/TUI 렌더링을 꺼버려 화면이 흑백으로 나온다. 해당 변수들을 제거하고 컬러 힌트를 넣어
    /// 정상 색상이 나오게 한다. (앱을 탐색기에서 직접 실행한 경우엔 원래 이 변수들이 없어 무해한 no-op.)
    /// </summary>
    private static void CleanChildEnvironment(ProcessStartInfo psi)
    {
        var env = psi.Environment;
        var remove = env.Keys.Where(k =>
            k.Equals("CLAUDECODE", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith("CLAUDE_CODE", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("CLAUDE_EFFORT", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("CLAUDE_PID", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("AI_AGENT", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("NO_COLOR", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var k in remove) env.Remove(k);

        // 대상은 항상 실제 터미널 콘솔(wt/cmd) → 트루컬러 지원. 색상 강제로 확실히.
        env["FORCE_COLOR"] = "3";
        env["COLORTERM"] = "truecolor";
    }
}
