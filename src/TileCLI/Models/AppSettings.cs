namespace TileCLI.Models;

/// <summary>
/// 구조적 설정(settings.json): 동작별 전역 단축키 + 숨긴 세션 목록.
/// (트레이 최소화·인접 연동·모니터 선택·간격 등 스칼라 UI 옵션은 config.env로 관리)
/// </summary>
public sealed class AppSettings
{
    /// <summary>동작별 단축키. 키는 <see cref="HotkeyAction"/> 이름.</summary>
    public Dictionary<string, HotkeyBinding> Hotkeys { get; set; } = new();

    /// <summary>"앱에서 삭제"로 최근 목록에서 숨긴 세션들의 cwd(파일은 유지). 새로고침해도 안 뜨게 함.</summary>
    public List<string> HiddenSessions { get; set; } = new();

    /// <summary>기본값 생성(단축키는 제안 조합을 넣되 모두 비활성 — 실수로 키를 가로채지 않도록).</summary>
    public static AppSettings CreateDefault()
    {
        var s = new AppSettings();
        // 제안 조합(비활성). 사용자가 설정 창에서 켠다.
        s.Hotkeys[nameof(HotkeyAction.Auto)] = new HotkeyBinding { Enabled = false, Ctrl = true, Alt = true, KeyCode = (int)System.Windows.Forms.Keys.A };
        s.Hotkeys[nameof(HotkeyAction.Horizontal)] = new HotkeyBinding { Enabled = false, Ctrl = true, Alt = true, KeyCode = (int)System.Windows.Forms.Keys.H };
        s.Hotkeys[nameof(HotkeyAction.Vertical)] = new HotkeyBinding { Enabled = false, Ctrl = true, Alt = true, KeyCode = (int)System.Windows.Forms.Keys.V };
        s.Hotkeys[nameof(HotkeyAction.Grid)] = new HotkeyBinding { Enabled = false, Ctrl = true, Alt = true, KeyCode = (int)System.Windows.Forms.Keys.G };
        return s;
    }

    /// <summary>action에 해당하는 바인딩을 반환(없으면 빈 바인딩 생성해 등록).</summary>
    public HotkeyBinding GetOrCreate(HotkeyAction action)
    {
        string key = action.ToString();
        if (!Hotkeys.TryGetValue(key, out var b) || b is null)
            Hotkeys[key] = b = new HotkeyBinding();
        return b;
    }
}
