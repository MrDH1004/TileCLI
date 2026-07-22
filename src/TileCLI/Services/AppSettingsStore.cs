using System.Text.Json;
using System.Text.Json.Serialization;
using TileCLI.Models;

namespace TileCLI.Services;

/// <summary>
/// 앱 설정(트레이 최소화 + 단축키)을 exe 옆 settings.json에 저장/로드(포터블).
/// exe 폴더가 읽기전용이면 %APPDATA%\TileCLI\settings.json 으로 폴백한다.
/// (ProfileStore와 동일한 폴백 전략)
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _primaryPath;
    private readonly string _fallbackPath;
    private string _activePath;

    public AppSettingsStore(string? path = null)
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
        return Path.Combine(dir, "settings.json");
    }

    private static string AppDataPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "TileCLI", "settings.json");
    }

    public AppSettings Load()
    {
        foreach (var candidate in new[] { _primaryPath, _fallbackPath })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                string json = File.ReadAllText(candidate);
                if (string.IsNullOrWhiteSpace(json)) continue;
                var s = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (s is not null)
                {
                    _activePath = candidate;
                    return Normalize(s);
                }
            }
            catch
            {
                // 손상/접근불가 후보 건너뜀
            }
        }
        return AppSettings.CreateDefault();
    }

    /// <summary>저장. exe 폴더 실패 시 %APPDATA%로 폴백. 최종 실패는 예외를 던진다.</summary>
    public void Save(AppSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, Options);
        try
        {
            File.WriteAllText(_primaryPath, json);
            _activePath = _primaryPath;
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            string? dir = Path.GetDirectoryName(_fallbackPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_fallbackPath, json);
            _activePath = _fallbackPath;
        }
    }

    /// <summary>누락된 동작 키를 기본 제안값으로 보충(구버전 파일 호환).</summary>
    private static AppSettings Normalize(AppSettings s)
    {
        var def = AppSettings.CreateDefault();
        s.Hotkeys ??= new Dictionary<string, HotkeyBinding>();
        foreach (var kv in def.Hotkeys)
            if (!s.Hotkeys.ContainsKey(kv.Key)) s.Hotkeys[kv.Key] = kv.Value;
        return s;
    }
}
