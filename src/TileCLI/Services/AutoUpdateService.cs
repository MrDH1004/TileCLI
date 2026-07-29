using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace TileCLI.Services;

/// <summary>업데이트 정보(최신 릴리스).</summary>
public sealed class UpdateInfo
{
    public required Version Version { get; init; }
    public required string DownloadUrl { get; init; } // 실행 파일(asset) 다운로드 URL
    public required string Tag { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// GitHub Releases에서 최신 버전을 조회해 현재 버전과 비교하고, 새 버전이면 실행 파일을 내려받아
/// 교체·재실행한다(자동 업데이트). 실행 중 exe는 덮어쓸 수 없으므로 종료 후 배치가 스왑한다.
/// </summary>
public static class AutoUpdateService
{
    private const string Owner = "MrDH1004";
    private const string Repo = "TileCLI";

    /// <summary>최신 릴리스 조회. 실패/자산없음이면 null.</summary>
    public static async Task<UpdateInfo?> CheckLatestAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TileCLI-Updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        string json = await http.GetStringAsync(url);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ver = ParseVersion(root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null);
        if (ver is null) return null;

        string? assetUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    a.TryGetProperty("browser_download_url", out var u))
                { assetUrl = u.GetString(); break; }
            }
        if (string.IsNullOrEmpty(assetUrl)) return null;

        return new UpdateInfo
        {
            Version = ver,
            DownloadUrl = assetUrl!,
            Tag = tagEl.GetString() ?? ver.ToString(),
            Notes = root.TryGetProperty("body", out var b) ? b.GetString() : null
        };
    }

    /// <summary>"v1.2.3"/"1.2.3" → Version(3자리 정규화). 실패 시 null.</summary>
    private static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string t = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(t, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>새 버전 exe를 exe 폴더 옆 임시 파일로 내려받는다. 반환=받은 파일 경로(실패 null).</summary>
    public static async Task<string?> DownloadAsync(UpdateInfo info)
    {
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? AppContext.BaseDirectory;
        string newExe = Path.Combine(exeDir, "TileCLI_update.exe");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TileCLI-Updater");
        var bytes = await http.GetByteArrayAsync(info.DownloadUrl);
        if (bytes.Length < 1024) return null; // 비정상적으로 작으면 실패로 간주
        await File.WriteAllBytesAsync(newExe, bytes);
        return newExe;
    }

    /// <summary>
    /// 교체 배치를 만들어 실행한다: 현재 프로세스 종료 대기 → 새 exe로 교체 → 재실행.
    /// 호출부는 이 뒤에 앱을 종료해야 한다. 실패 시 false(교체 안 함).
    /// </summary>
    public static bool ApplyAndRestart(string newExe)
    {
        string? cur = Environment.ProcessPath;
        if (string.IsNullOrEmpty(cur) || !File.Exists(newExe)) return false;
        int pid = Environment.ProcessId;

        string bat = Path.Combine(Path.GetTempPath(), $"tilecli_update_{pid}.bat");
        // 순수 ASCII 배치(한글 없음) — CRLF. cmd 코드페이지 무관하게 안전.
        string content = string.Join("\r\n", new[]
        {
            "@echo off",
            ":wait",
            $"tasklist /fi \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul",
            "if not errorlevel 1 (",
            "  timeout /t 1 /nobreak >nul",
            "  goto wait",
            ")",
            $"move /y \"{newExe}\" \"{cur}\" >nul",
            $"start \"\" \"{cur}\"",
            "del \"%~f0\""
        }) + "\r\n";

        try
        {
            File.WriteAllText(bat, content, new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo
            {
                FileName = bat,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            return true;
        }
        catch { return false; }
    }
}
