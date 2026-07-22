using System.Runtime.InteropServices;
using TileCLI.Models;
using TileCLI.Native;

namespace TileCLI.Services;

/// <summary>
/// 전역 단축키 등록/해제 + WM_HOTKEY id ↔ 동작 매핑(요구 3).
/// RegisterHotKey 실패(다른 앱/OS가 이미 점유)를 수집해 호출부가 사용자에게 안내한다(요구 4).
/// </summary>
public sealed class HotkeyManager
{
    private const int IdBase = 0xB000; // 이 앱 전용 id 대역

    private readonly IntPtr _hWnd;
    private readonly Dictionary<int, HotkeyAction> _idToAction = new();

    public HotkeyManager(IntPtr hWnd) => _hWnd = hWnd;

    private static int IdOf(HotkeyAction action) => IdBase + (int)action;

    /// <summary>WM_HOTKEY의 id로 동작을 찾는다.</summary>
    public bool TryGetAction(int id, out HotkeyAction action) => _idToAction.TryGetValue(id, out action);

    /// <summary>등록된 모든 단축키 해제.</summary>
    public void UnregisterAll()
    {
        foreach (var id in _idToAction.Keys)
            NativeMethods.UnregisterHotKey(_hWnd, id);
        _idToAction.Clear();
    }

    /// <summary>
    /// 설정의 활성 바인딩을 모두 (재)등록한다. 먼저 전부 해제 후 다시 등록.
    /// 실패한 조합은 conflicts에 "동작=조합" 형태로 담아 반환한다(빈 리스트면 전부 성공).
    /// </summary>
    public List<string> ApplyFrom(AppSettings settings)
    {
        UnregisterAll();
        var conflicts = new List<string>();
        if (settings?.Hotkeys is null) return conflicts;

        foreach (HotkeyAction action in Enum.GetValues<HotkeyAction>())
        {
            if (!settings.Hotkeys.TryGetValue(action.ToString(), out var b) || b is null) continue;
            if (!b.Enabled || !b.IsComplete) continue;

            int id = IdOf(action);
            bool ok = NativeMethods.RegisterHotKey(_hWnd, id, b.Modifiers, b.VirtualKey);
            if (ok)
            {
                _idToAction[id] = action;
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                string why = err == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                    ? "다른 프로그램이 이미 사용 중"
                    : $"등록 실패(코드 {err})";
                conflicts.Add($"{ActionName(action)} = {b.Combo} → {why}");
            }
        }
        return conflicts;
    }

    public static string ActionName(HotkeyAction a) => a switch
    {
        HotkeyAction.Auto => "자동 배치",
        HotkeyAction.Horizontal => "가로 분할",
        HotkeyAction.Vertical => "세로 분할",
        HotkeyAction.Grid => "그리드",
        _ => a.ToString()
    };
}
