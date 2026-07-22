using System.Text.Json;
using System.Text.Json.Serialization;
using TileCLI.Models;

namespace TileCLI.Services;

/// <summary>
/// 배치 스타일 프로파일을 exe 옆 profiles.json에 저장/로드(포터블). 대상 창은 저장하지 않는다.
/// exe 폴더가 읽기전용이면 %APPDATA%\TileCLI\profiles.json 으로 폴백한다.
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _primaryPath;
    private readonly string _fallbackPath;
    private string _activePath;

    public ProfileStore(string? path = null)
    {
        _primaryPath = path ?? ExeSidePath();
        _fallbackPath = AppDataPath();
        _activePath = _primaryPath;
    }

    /// <summary>현재 실제 저장에 사용 중인 경로.</summary>
    public string FilePath => _activePath;

    private static string ExeSidePath()
    {
        string dir;
        try
        {
            dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        }
        catch
        {
            dir = AppContext.BaseDirectory;
        }
        return Path.Combine(dir, "profiles.json");
    }

    private static string AppDataPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "TileCLI", "profiles.json");
    }

    public List<LayoutProfile> Load()
    {
        // exe 옆을 우선, 없으면 %APPDATA% 폴백
        foreach (var candidate in new[] { _primaryPath, _fallbackPath })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                string json = File.ReadAllText(candidate);
                if (string.IsNullOrWhiteSpace(json)) continue;
                var list = JsonSerializer.Deserialize<List<LayoutProfile>>(json, Options);
                if (list is not null)
                {
                    _activePath = candidate;
                    return list;
                }
            }
            catch
            {
                // 손상/접근불가 후보는 건너뜀
            }
        }
        return new List<LayoutProfile>();
    }

    /// <summary>저장. exe 폴더 실패 시 %APPDATA%로 폴백. 최종 실패는 예외를 던진다(호출부가 안내).</summary>
    public void Save(IEnumerable<LayoutProfile> profiles)
    {
        string json = JsonSerializer.Serialize(profiles, Options);

        try
        {
            File.WriteAllText(_primaryPath, json);
            _activePath = _primaryPath;
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // exe 폴더 쓰기 불가 → %APPDATA% 폴백
            string? dir = Path.GetDirectoryName(_fallbackPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_fallbackPath, json);
            _activePath = _fallbackPath;
        }
    }
}
