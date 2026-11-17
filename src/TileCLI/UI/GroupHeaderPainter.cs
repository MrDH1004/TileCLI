using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TileCLI.UI;

/// <summary>
/// ListView 그룹 헤더(“정렬 대상 (n)” / “제외 (n)”)를 테마 색으로 직접 그린다.
///
/// 배경: WinForms OwnerDraw는 항목·열 헤더만 대신 그리게 해 주고 그룹 헤더는 시스템 테마가 그린다.
/// 다크 모드(DarkMode_Explorer)에서는 짙은 파랑 글자가 어두운 배경 위에 그려져 거의 안 보인다.
/// 부모가 컨트롤로 되돌려 보내는(reflect) NM_CUSTOMDRAW 중 그룹 항목(LVCDI_GROUP)만 가로채
/// 직접 그리고 기본 그리기를 건너뛴다(CDRF_SKIPDEFAULT). 그 외 알림은 그대로 흘려보낸다.
/// </summary>
internal sealed class GroupHeaderPainter : NativeWindow
{
    private const int WM_REFLECT_NOTIFY = 0x2000 + 0x004E; // WM_REFLECT | WM_NOTIFY (WinForms가 부모에서 되돌려 보냄)
    private const int NM_CUSTOMDRAW = -12;                // NM_FIRST(0) - 12
    private const int CDDS_PREPAINT = 0x00000001;
    private const int CDRF_SKIPDEFAULT = 0x00000004;
    private const int LVCDI_GROUP = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct NMHDR { public IntPtr hwndFrom; public UIntPtr idFrom; public int code; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NMCUSTOMDRAW
    {
        public NMHDR hdr;
        public int dwDrawStage;
        public IntPtr hdc;
        public RECT rc;
        public UIntPtr dwItemSpec;
        public uint uItemState;
        public IntPtr lItemlParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NMLVCUSTOMDRAW
    {
        public NMCUSTOMDRAW nmcd;
        public int clrText;
        public int clrTextBk;
        public int iSubItem;
        public int dwItemType;
        public int clrFace;
        public int iIconEffect;
        public int iIconPhase;
        public int iPartId;
        public int iStateId;
        public RECT rcText;
        public uint uAlign;
    }

    private readonly ListView _lv;

    public GroupHeaderPainter(ListView lv)
    {
        _lv = lv;
        if (lv.IsHandleCreated) AssignHandle(lv.Handle);
        lv.HandleCreated += (_, _) => AssignHandle(lv.Handle);
        lv.HandleDestroyed += (_, _) => ReleaseHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_REFLECT_NOTIFY && Marshal.PtrToStructure<NMHDR>(m.LParam).code == NM_CUSTOMDRAW)
        {
            var cd = Marshal.PtrToStructure<NMLVCUSTOMDRAW>(m.LParam);
            if (cd.nmcd.dwDrawStage == CDDS_PREPAINT && cd.dwItemType == LVCDI_GROUP)
            {
                var r = cd.rcText;
                var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                if (rect.Width > 0 && rect.Height > 0)
                {
                    using var g = Graphics.FromHdc(cd.nmcd.hdc);
                    Paint(g, rect);
                    m.Result = (IntPtr)CDRF_SKIPDEFAULT;
                    return;
                }
            }
        }
        base.WndProc(ref m);
    }

    private void Paint(Graphics g, Rectangle rect)
    {
        var group = GroupAt(rect);
        string text = group?.Header ?? string.Empty;
        bool target = group != null && _lv.Groups.IndexOf(group) == 0;

        using (var bg = new SolidBrush(Theme.Bg3)) g.FillRectangle(bg, rect);
        using var font = new Font(_lv.Font, FontStyle.Bold);
        var color = target ? Theme.Gold : Theme.Text; // 정렬 대상은 강조, 제외는 본문색(둘 다 배경과 대비 확보)
        var size = TextRenderer.MeasureText(g, text, font, rect.Size, TextFormatFlags.NoPadding);
        var textRect = new Rectangle(rect.Left + 6, rect.Top, Math.Min(size.Width + 2, Math.Max(0, rect.Width - 6)), rect.Height);
        TextRenderer.DrawText(g, text, font, textRect, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        // 그룹 구분선(절취선): 글자 끝에서 오른쪽 끝까지
        int y = rect.Top + rect.Height / 2;
        int x0 = textRect.Right + 8;
        if (x0 < rect.Right - 4)
        {
            using var pen = new Pen(Theme.BorderHi);
            g.DrawLine(pen, x0, y, rect.Right - 4, y);
        }
    }

    /// <summary>헤더 바로 아래에 첫 항목이 오는 그룹을 찾는다(그룹 ID 대신 위치로 대응).</summary>
    private ListViewGroup? GroupAt(Rectangle header)
    {
        ListViewGroup? best = null;
        int bestTop = int.MaxValue;
        foreach (ListViewGroup grp in _lv.Groups)
        {
            if (grp.Items.Count == 0) continue;
            int top = grp.Items[0].Bounds.Top;
            if (top >= header.Bottom - 2 && top < bestTop) { best = grp; bestTop = top; }
        }
        return best;
    }
}
