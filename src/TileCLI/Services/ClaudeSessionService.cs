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

    /// <summary>터미널에서 실행된 codex 세션의 originator 값. 데스크톱 앱은 "Codex Desktop"으로 기록된다.</summary>
    private const string TuiOriginator = "codex-tui";

    /// <summary>
    /// 터미널에서 실행된 claude 세션의 entrypoint 값. Claude 데스크톱 앱이 만든 세션은
    /// "claude-desktop"으로 기록되며 cwd가 %APPDATA%\Claude\scratch-workspaces\… 같은 임시 폴더다.
    /// </summary>
    private const string CliEntrypoint = "cli";

    /// <summary>
    /// 세션 jsonl의 entrypoint 값으로 "터미널에서 돌린 claude 세션"인지 판정한다.
    /// 데스크톱 앱 세션(claude-desktop)은 정렬할 터미널 창도 없고 이어하기 대상도 아니므로 제외.
    /// entrypoint 필드가 없는(구버전) 기록은 유지한다.
    /// </summary>
    public static bool IsCliEntrypoint(string? entrypoint) =>
        entrypoint is null || string.Equals(entrypoint, CliEntrypoint, StringComparison.OrdinalIgnoreCase);

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
    /// <param name="includeSubagent">
    /// codex가 서브에이전트로 파생시킨 세션까지 포함할지. 기본은 제외 — 사용자가 "이어하기"로 열
    /// 대상이 아니기 때문이다(codex resume picker도 비대화 세션을 기본 제외한다).
    /// 창 분류용 힌트처럼 "그 폴더에서 codex가 활동했다"는 사실만 필요할 때는 true로 켠다.
    /// </param>
    public static List<ClaudeSession> GetRecent(int max = 20, IEnumerable<string>? hiddenCwds = null,
        string? gptSessionsDir = null, bool includeSubagent = false)
    {
        var hidden = new HashSet<string>(hiddenCwds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var all = new List<ClaudeSession>();
        all.AddRange(ScanClaude(hidden));
        all.AddRange(ScanGpt(gptSessionsDir, hidden, includeSubagent));
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
    private static List<ClaudeSession> ScanGpt(string? dir, HashSet<string> hidden, bool includeSubagent)
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
            string? cwd; DateTime last; bool isSubagent; string? originator;
            try { var fi = new FileInfo(file); (cwd, isSubagent, originator) = ReadGptMeta(fi); last = fi.LastWriteTime; }
            catch { continue; }
            // 터미널에서 돌린 세션만. Codex 데스크톱 앱은 originator="Codex Desktop"(source="vscode")으로
            // 기록되는데, 터미널 창이 아니라 이어하기 대상도 창 분류 근거도 되지 못한다.
            if (originator is not null &&
                !string.Equals(originator, TuiOriginator, StringComparison.OrdinalIgnoreCase)) continue;
            if (isSubagent && !includeSubagent) continue;
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

    /// <summary>
    /// codex 세션 jsonl 앞부분에서 cwd·서브에이전트 여부·originator를 함께 읽는다.
    /// 관측된 형식: 첫 줄이
    /// <c>{"type":"session_meta","payload":{"cwd":…,"originator":"codex-tui","source":{"subagent":{…}}}}</c>.
    /// 데스크톱 앱 세션은 originator="Codex Desktop", source="vscode"(문자열)로 기록된다.
    /// session_meta를 못 찾으면 형식 무관 cwd 재귀 탐색으로 폴백한다(originator는 null).
    /// </summary>
    private static (string? Cwd, bool IsSubagent, string? Originator) ReadGptMeta(FileInfo file)
    {
        try
        {
            foreach (var line in ReadHeadLines(file.FullName, MaxLinesToScan))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("type", out var typeEl) &&
                        typeEl.ValueKind == JsonValueKind.String && typeEl.GetString() == "session_meta" &&
                        root.TryGetProperty("payload", out var payload) &&
                        payload.ValueKind == JsonValueKind.Object)
                    {
                        bool sub = payload.TryGetProperty("source", out var src) &&
                                   src.ValueKind == JsonValueKind.Object &&
                                   src.TryGetProperty("subagent", out _);
                        string? cwd = payload.TryGetProperty("cwd", out var cwdEl) &&
                                      cwdEl.ValueKind == JsonValueKind.String
                            ? cwdEl.GetString() : FindCwdIn(root);
                        string? origin = payload.TryGetProperty("originator", out var orgEl) &&
                                         orgEl.ValueKind == JsonValueKind.String
                            ? orgEl.GetString() : null;
                        return (cwd, sub, origin);
                    }

                    var any = FindCwdIn(root);
                    if (!string.IsNullOrWhiteSpace(any)) return (any, false, null);
                }
                catch { /* 라인 파싱 실패 무시 */ }
            }
        }
        catch { /* 파일 읽기 실패 */ }
        return (null, false, null);
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
    /// 삭제 대상 경로. Claude는 프로젝트 폴더(~/.claude/projects/&lt;encoded&gt;) 전체가 그 프로젝트 것이라
    /// 폴더째 지우지만, codex는 날짜별 폴더(sessions/YYYY/MM/DD)에 여러 프로젝트 세션이 섞여 있어
    /// (실측: 한 날짜 폴더에 11개 중 10개가 다른 프로젝트) 폴더를 지우면 남의 세션까지 날아간다.
    /// 따라서 GPT는 그 세션 파일 하나만 대상으로 한다.
    /// </summary>
    public static string? DeleteTarget(ClaudeSession session)
    {
        if (session is null || string.IsNullOrEmpty(session.TranscriptPath)) return null;
        return session.Kind == TerminalKind.Gpt
            ? session.TranscriptPath
            : Path.GetDirectoryName(session.TranscriptPath);
    }

    /// <summary>
    /// 세션 기록을 휴지통으로 보낸다. Claude는 프로젝트 폴더(~/.claude/projects/&lt;encoded&gt;)째,
    /// GPT는 그 세션 파일 하나만(같은 날짜 폴더에 다른 프로젝트 세션이 섞여 있으므로).
    /// 안전장치: 각 CLI의 세션 루트 하위 경로일 때만 삭제한다(엉뚱한 경로 삭제 방지).
    /// </summary>
    public static bool DeleteSessionStorage(ClaudeSession session, out string error, string? gptSessionsDir = null)
    {
        error = string.Empty;
        bool isGpt = session?.Kind == TerminalKind.Gpt;
        var target = DeleteTarget(session!);
        bool exists = !string.IsNullOrEmpty(target) && (isGpt ? File.Exists(target) : Directory.Exists(target));
        if (!exists)
        {
            error = isGpt ? $"세션 파일을 찾을 수 없습니다: {target}" : $"세션 저장폴더를 찾을 수 없습니다: {target}";
            return false;
        }

        // 안전 검사: 그 CLI의 세션 루트 하위인지 확인
        string rootPath = isGpt
            ? (string.IsNullOrWhiteSpace(gptSessionsDir) ? GptSessionsRoot : gptSessionsDir!)
            : ProjectsRoot;
        string root = Path.GetFullPath(rootPath).TrimEnd('\\', '/');
        string full = Path.GetFullPath(target!).TrimEnd('\\', '/');
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            full.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            error = $"안전 검사 실패 — {(isGpt ? "codex 세션" : "projects")} 루트 하위가 아니거나 루트 자체입니다: {full}";
            return false;
        }

        try
        {
            // 휴지통으로 이동(복구 가능)
            if (isGpt)
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    full,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                    Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
            else
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    full,
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

    /// <summary>
    /// 세션 jsonl 앞부분을 공유 모드로 읽는다. File.ReadLines는 FileShare.Read라서 지금 실행 중인
    /// CLI가 쓰고 있는 파일에서 IOException이 나고, 그 결과 "가장 최근 = 지금 돌고 있는" 세션이
    /// 목록·창 분류에서 통째로 빠졌다(실측: codex 활성 세션이 스캔에서 누락).
    /// </summary>
    private static IEnumerable<string> ReadHeadLines(string path, int maxLines)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        for (int i = 0; i < maxLines; i++)
        {
            string? line = reader.ReadLine();
            if (line is null) yield break;
            yield return line;
        }
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
        string? entrypoint = null;
        try
        {
            foreach (var line in ReadHeadLines(file.FullName, MaxLinesToScan))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var el = doc.RootElement;
                    if (cwd is null && el.TryGetProperty("cwd", out var cwdEl) && cwdEl.ValueKind == JsonValueKind.String)
                        cwd = cwdEl.GetString();
                    if (entrypoint is null && el.TryGetProperty("entrypoint", out var epEl) &&
                        epEl.ValueKind == JsonValueKind.String)
                        entrypoint = epEl.GetString();
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

        // 터미널에서 돌린 세션만. Claude 데스크톱 앱 세션(entrypoint="claude-desktop")은 목록은 물론
        // 창 분류 힌트에서도 뺀다 — 그 폴더에서 터미널 claude가 돌았다는 근거가 아니기 때문.
        if (!IsCliEntrypoint(entrypoint)) return null;

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
    /// title을 주면 WT 탭 제목을 그 이름으로 고정한다(--title + --suppressApplicationTitle).
    /// 앱이 제목을 덮어쓰지 못하므로 세션 이름/배지(/rename)와 무관하게 터미널 타이틀만 바뀐다.
    /// </summary>
    public static bool LaunchCommand(string? cwd, string command, out string error, string? title = null)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
        {
            error = $"작업 폴더를 찾을 수 없습니다: {cwd}";
            return false;
        }
        if (string.IsNullOrWhiteSpace(command)) { error = "실행할 명령이 비어 있습니다."; return false; }

        string titleArgs = string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : $"--title \"{title.Replace("\"", "")}\" --suppressApplicationTitle ";

        // 한글/유니코드 보장: cmd 기본 코드페이지(CP949)에서는 Node 기반 CLI(claude/codex)의
        // 한글 입출력이 깨진다 → UTF-8(65001)로 전환한 뒤 실행한다.
        string run = $"chcp 65001 >nul & {command}";

        // 1순위: Windows Terminal (정리된 환경으로 CreateProcess)
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{cwd}\" {titleArgs}cmd /k {run}",
                UseShellExecute = false,   // 환경변수를 제어하려면 false 필요
                WorkingDirectory = cwd
            };
            CleanChildEnvironment(psi);
            Process.Start(psi);
            return true;
        }
        catch { /* wt 실행 불가 → 폴백 */ }

        // 폴백: cmd 새 창 (정리된 환경, 제목은 best-effort — 앱이 덮어쓸 수 있음)
        try
        {
            string cmdArgs = string.IsNullOrWhiteSpace(title)
                ? $"/k {run}"
                : $"/k title {title.Replace("\"", "")} & {run}";
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
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
        RefreshFromRegistry(env);

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

    /// <summary>
    /// 시스템/사용자 환경변수를 레지스트리에서 다시 읽어 자식 환경에 덮어쓴다.
    ///
    /// 배경: wt.exe는 새 탭에 호출한 프로세스의 환경을 그대로 넘긴다. 이 앱이 오래 떠 있는 동안
    /// 설치 프로그램이 PATH를 바꾸면(예: Unity CLI, cargo) 앱이 들고 있는 시작 시점 환경이 전달되어
    /// 새 터미널에서 명령을 찾지 못한다. 로그온 때와 같은 규칙(시스템 → 사용자 덮어쓰기, PATH는 이어붙임)으로
    /// 최신 값을 반영한다. 레지스트리 조회 실패 시 기존 환경을 그대로 둔다.
    /// </summary>
    internal static void RefreshFromRegistry(IDictionary<string, string?> env)
    {
        try
        {
            var machine = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine);
            var user = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User);

            foreach (System.Collections.DictionaryEntry e in machine)
                env[(string)e.Key] = e.Value as string;
            foreach (System.Collections.DictionaryEntry e in user)
            {
                var key = (string)e.Key;
                if (!key.Equals("Path", StringComparison.OrdinalIgnoreCase))
                    env[key] = e.Value as string;
            }

            // 시스템 변수에는 USERNAME=SYSTEM이 있다 → 로그온 때처럼 세션 변수(USERNAME, USERPROFILE 등)로 되돌린다.
            using (var vol = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Volatile Environment"))
            {
                if (vol != null)
                {
                    foreach (var name in vol.GetValueNames())
                        if (vol.GetValue(name) is string v) env[name] = v;
                }
            }

            string machinePath = machine["Path"] as string ?? string.Empty;
            string userPath = user["Path"] as string ?? string.Empty;
            string merged = string.Join(';', new[] { machinePath, userPath }.Where(p => p.Length > 0));
            if (merged.Length > 0)
            {
                foreach (var k in env.Keys.Where(k => k.Equals("Path", StringComparison.OrdinalIgnoreCase)).ToList())
                    env.Remove(k);
                env["Path"] = merged;
            }
        }
        catch (System.Security.SecurityException) { /* 레지스트리 접근 불가 → 기존 환경 유지 */ }
    }
}
