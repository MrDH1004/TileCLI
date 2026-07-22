using System.Text;

namespace TileCLI.Services;

/// <summary>
/// 사용자 UI 옵션/최근 상태를 config.env(KEY=VALUE, 사람이 읽고 편집하기 쉬운 .env 형식)에 저장/로드.
/// exe 옆 config.env 우선, 쓰기 불가 시 %APPDATA%\TileCLI\config.env 폴백(다른 스토어와 동일).
/// 복잡한 구조(단축키·숨긴 세션)는 settings.json에 남기고, 여기엔 스칼라/목록 옵션만 둔다.
/// </summary>
public sealed class ConfigEnv
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _primaryPath;
    private readonly string _fallbackPath;
    private string _activePath;

    public ConfigEnv(string? path = null)
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
        return Path.Combine(dir, "config.env");
    }

    private static string AppDataPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "TileCLI", "config.env");
    }

    /// <summary>파일에서 KEY=VALUE를 읽어 로드(주석 #, 빈 줄 무시).</summary>
    public void Load()
    {
        _map.Clear();
        foreach (var candidate in new[] { _primaryPath, _fallbackPath })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                foreach (var raw in File.ReadAllLines(candidate))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line[..eq].Trim();
                    string val = line[(eq + 1)..].Trim();
                    if (key.Length > 0) _map[key] = val;
                }
                _activePath = candidate;
                return;
            }
            catch { /* 다음 후보 */ }
        }
    }

    /// <summary>현재 값을 config.env로 기록. exe 폴더 실패 시 %APPDATA% 폴백.</summary>
    public void Save()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# TileCLI 설정 (KEY=VALUE) — 앱이 자동 저장/로드합니다.");
        foreach (var kv in _map)
            sb.Append(kv.Key).Append('=').AppendLine(kv.Value);
        string text = sb.ToString();

        try
        {
            File.WriteAllText(_primaryPath, text);
            _activePath = _primaryPath;
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            string? dir = Path.GetDirectoryName(_fallbackPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_fallbackPath, text);
            _activePath = _fallbackPath;
        }
    }

    // ---- 타입별 접근 ----
    public string? GetString(string key) => _map.TryGetValue(key, out var v) ? v : null;
    public void SetString(string key, string? value) => _map[key] = value ?? string.Empty;

    public bool GetBool(string key, bool def)
    {
        if (_map.TryGetValue(key, out var v) && bool.TryParse(v, out var b)) return b;
        return def;
    }
    public void SetBool(string key, bool value) => _map[key] = value ? "true" : "false";

    public int GetInt(string key, int def)
    {
        if (_map.TryGetValue(key, out var v) && int.TryParse(v, out var n)) return n;
        return def;
    }
    public void SetInt(string key, int value) => _map[key] = value.ToString();

    /// <summary>세미콜론(;)으로 구분된 목록.</summary>
    public List<string> GetList(string key) =>
        _map.TryGetValue(key, out var v) && v.Length > 0
            ? v.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList()
            : new List<string>();
    public void SetList(string key, IEnumerable<string> values) =>
        _map[key] = string.Join(";", values);
}
