using System.Text.Json;
using System.Text.Json.Serialization;
using TileCLI.Models;

namespace TileCLI.Services;

/// <summary>
/// 작업 세트를 exe 옆 worksets.json에 저장/로드(포터블). exe 폴더가 읽기전용이면
/// %APPDATA%\TileCLI\worksets.json 으로 폴백한다. (ProfileStore와 동일 패턴)
/// </summary>
public sealed class WorkSetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _primaryPath;
    private readonly string _fallbackPath;
    private string _activePath;

    public WorkSetStore(string? path = null)
    {
        _primaryPath = path ?? ExeSidePath();
        _fallbackPath = AppDataPath();
        _activePath = _primaryPath;
    }

    public string FilePath => _activePath;

    private static string ExeSidePath()
    {
        string dir;
        try { dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory; }
        catch { dir = AppContext.BaseDirectory; }
        return Path.Combine(dir, "worksets.json");
    }

    private static string AppDataPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "TileCLI", "worksets.json");
    }

    public List<WorkSet> Load()
    {
        foreach (var candidate in new[] { _primaryPath, _fallbackPath })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                string json = File.ReadAllText(candidate);
                if (string.IsNullOrWhiteSpace(json)) continue;
                var list = JsonSerializer.Deserialize<List<WorkSet>>(json, Options);
                if (list is not null) { _activePath = candidate; return list; }
            }
            catch { /* 손상/접근불가 후보는 건너뜀 */ }
        }
        return new List<WorkSet>();
    }

    /// <summary>저장. exe 폴더 실패 시 %APPDATA%로 폴백. 최종 실패는 예외를 던진다(호출부가 안내).</summary>
    public void Save(IEnumerable<WorkSet> sets)
    {
        string json = JsonSerializer.Serialize(sets, Options);
        try
        {
            File.WriteAllText(_primaryPath, json);
            _activePath = _primaryPath;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            string? dir = Path.GetDirectoryName(_fallbackPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_fallbackPath, json);
            _activePath = _fallbackPath;
        }
    }
}
