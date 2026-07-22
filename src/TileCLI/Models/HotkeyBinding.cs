using System.Windows.Forms;
using TileCLI.Native;

namespace TileCLI.Models;

/// <summary>
/// 하나의 전역 단축키 바인딩. JSON 직렬화 대상이라 mutable. 수식키는 bool로 저장(가독성),
/// 주 키는 <see cref="Keys"/>의 정수값으로 저장한다.
/// </summary>
public sealed class HotkeyBinding
{
    public bool Enabled { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }

    /// <summary>주 키((int)System.Windows.Forms.Keys). 0이면 미지정.</summary>
    public int KeyCode { get; set; }

    /// <summary>수식키가 하나라도 있고 주 키가 지정됐는지(등록 가능 여부).</summary>
    public bool IsComplete => KeyCode != 0 && (Ctrl || Alt || Shift || Win);

    /// <summary>RegisterHotKey용 fsModifiers(연타 방지 MOD_NOREPEAT 포함).</summary>
    public uint Modifiers
    {
        get
        {
            uint m = NativeMethods.MOD_NOREPEAT;
            if (Ctrl) m |= NativeMethods.MOD_CONTROL;
            if (Alt) m |= NativeMethods.MOD_ALT;
            if (Shift) m |= NativeMethods.MOD_SHIFT;
            if (Win) m |= NativeMethods.MOD_WIN;
            return m;
        }
    }

    /// <summary>가상 키 코드(RegisterHotKey vk).</summary>
    public uint VirtualKey => (uint)KeyCode;

    /// <summary>두 바인딩이 같은 키 조합인지(수식키+주 키 동일). Enabled는 비교하지 않는다.</summary>
    public bool SameCombo(HotkeyBinding other) =>
        other is not null && Ctrl == other.Ctrl && Alt == other.Alt &&
        Shift == other.Shift && Win == other.Win && KeyCode == other.KeyCode;

    /// <summary>사람이 읽는 조합 표기(예: "Ctrl + Alt + A"). 미지정이면 "(없음)".</summary>
    public string Combo
    {
        get
        {
            if (KeyCode == 0 && !Ctrl && !Alt && !Shift && !Win) return "(없음)";
            var parts = new List<string>(5);
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            if (KeyCode != 0) parts.Add(KeyName((Keys)KeyCode));
            return string.Join(" + ", parts);
        }
    }

    private static string KeyName(Keys k) => k switch
    {
        Keys.Oemtilde => "`",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.OemPeriod => ".",
        Keys.Oemcomma => ",",
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num" + (k - Keys.NumPad0),
        _ => k.ToString()
    };

    public HotkeyBinding Clone() => new()
    {
        Enabled = Enabled, Ctrl = Ctrl, Alt = Alt, Shift = Shift, Win = Win, KeyCode = KeyCode
    };
}
