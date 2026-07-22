using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TileCLI.Native;

namespace TileCLI.UI;

public enum ThemeMode { Dark, Light }

/// <summary>
/// 다크/라이트 전환 테마(네이비+골드). 색은 현재 모드에 따라 반환되며, 컨트롤을 재귀 스타일링하고
/// 리스트/그룹박스/콤보/메뉴는 오너드로우로 테마화한다.
/// </summary>
public static class Theme
{
    public static ThemeMode Mode { get; private set; } = ThemeMode.Dark;
    public static bool IsDark => Mode == ThemeMode.Dark;
    public static void SetMode(ThemeMode m) => Mode = m;

    // ---- 다크 팔레트 ----
    private static readonly Color DBg = C(0x16, 0x19, 0x1F), DBg2 = C(0x1E, 0x22, 0x2A), DBg3 = C(0x26, 0x2B, 0x34);
    private static readonly Color DBgHeader = C(0x2A, 0x30, 0x3A), DBorder = C(0x33, 0x3A, 0x45), DBorderHi = C(0x45, 0x4E, 0x5C);
    private static readonly Color DText = C(0xE7, 0xEA, 0xF0), DTextDim = C(0x97, 0xA1, 0xB2), DHover = C(0x30, 0x37, 0x3F);
    private static readonly Color DGold = C(0xFD, 0xCF, 0x18), DGoldSoft = C(0xE6, 0xC2, 0x55);
    private static readonly Color DClaude = C(0xE0, 0x91, 0x3E), DGpt = C(0x40, 0xC0, 0x88);

    // ---- 라이트 팔레트 ----
    private static readonly Color LBg = C(0xEE, 0xF1, 0xF6), LBg2 = C(0xFF, 0xFF, 0xFF), LBg3 = C(0xFF, 0xFF, 0xFF);
    private static readonly Color LBgHeader = C(0xEC, 0xF0, 0xF6), LBorder = C(0xD5, 0xDB, 0xE4), LBorderHi = C(0xBA, 0xC2, 0xCE);
    private static readonly Color LText = C(0x20, 0x26, 0x2F), LTextDim = C(0x5C, 0x66, 0x75), LHover = C(0xE4, 0xE9, 0xF0);
    private static readonly Color LGold = C(0xD9, 0xA4, 0x00), LGoldSoft = C(0x9C, 0x74, 0x13);
    private static readonly Color LClaude = C(0xB8, 0x5C, 0x00), LGpt = C(0x0A, 0x7A, 0x4A);

    // 공통(모드 무관)
    public static Color Accent => C(0x2E, 0x6B, 0xE6);      // 네이비-블루(선택/호버)
    public static Color AccentDeep => C(0x1D, 0x3E, 0x79);  // 브랜드 네이비(채움)

    // ---- 모드별 접근자 ----
    public static Color Bg => IsDark ? DBg : LBg;
    public static Color Bg2 => IsDark ? DBg2 : LBg2;
    public static Color Bg3 => IsDark ? DBg3 : LBg3;
    public static Color BgHeader => IsDark ? DBgHeader : LBgHeader;
    public static Color Border => IsDark ? DBorder : LBorder;
    public static Color BorderHi => IsDark ? DBorderHi : LBorderHi;
    public static Color Text => IsDark ? DText : LText;
    public static Color TextDim => IsDark ? DTextDim : LTextDim;
    public static Color Hover => IsDark ? DHover : LHover;
    public static Color Gold => IsDark ? DGold : LGold;
    public static Color GoldSoft => IsDark ? DGoldSoft : LGoldSoft;
    public static Color Claude => IsDark ? DClaude : LClaude;
    public static Color Gpt => IsDark ? DGpt : LGpt;

    public static readonly Font Base = new("Segoe UI", 9F);
    public static readonly Font Header = new("Segoe UI Semibold", 9.5F, FontStyle.Bold);

    private static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

    // ---- 유틸 ----
    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(0, radius) * 2;
        if (d < 2 || d > r.Width || d > r.Height) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void UseDarkTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        int on = IsDark ? 1 : 0;
        try { NativeMethods.DwmSetWindowAttribute(form.Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)); }
        catch { /* 구형 OS 무시 */ }
    }

    // ---- 버튼 ----
    public static void StyleButton(Button b, bool primary = false)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.FlatAppearance.BorderSize = 1;
        if (primary)
        {
            b.BackColor = AccentDeep;
            b.ForeColor = Gold;
            b.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            b.FlatAppearance.BorderColor = Gold;
            b.FlatAppearance.MouseOverBackColor = Accent;
            b.FlatAppearance.MouseDownBackColor = C(0x16, 0x30, 0x60);
        }
        else
        {
            b.Font = Base;
            b.BackColor = Bg3;
            b.ForeColor = Text;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = Hover;
            b.FlatAppearance.MouseDownBackColor = Accent;
        }
        if (b is RoundedButton rb)
        {
            rb.CornerRadius = 8;
            rb.BorderColor = primary ? Gold : Border;
            rb.HoverColor = primary ? Accent : Hover;
            rb.PressColor = primary ? C(0x16, 0x30, 0x60) : Accent;
            rb.CornerColor = b.Parent is DarkGroupBox ? Bg2 : (b.Parent?.BackColor ?? Bg); // 카드=Bg2, 그 외=Bg
        }
    }

    // ---- 콤보박스 ----
    public static void StyleComboBox(ComboBox cb)
    {
        cb.FlatStyle = FlatStyle.Flat;
        cb.BackColor = Bg3;
        cb.ForeColor = Text;
        cb.DrawMode = DrawMode.OwnerDrawFixed;
        cb.DrawItem -= ComboDraw;
        cb.DrawItem += ComboDraw;
    }

    private static void ComboDraw(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(sel ? Accent : Bg3)) e.Graphics.FillRectangle(b, e.Bounds);
        if (e.Index >= 0)
        {
            string text = cb.GetItemText(cb.Items[e.Index]) ?? string.Empty;
            TextRenderer.DrawText(e.Graphics, text, cb.Font, e.Bounds, sel ? Color.White : Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // ---- 리스트뷰 ----
    // 선택/호버 배경·텍스트를 직접 그려 대비를 보장(시스템 DarkMode 기본값은 어두워서 텍스트가 묻힘).
    private sealed class LvState { public int Hover = -1; public bool Wired; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ListView, LvState> _lvStates = new();

    public static void StyleListView(ListView lv)
    {
        var st = _lvStates.GetValue(lv, _ => new LvState());
        lv.BackColor = Bg3;
        lv.ForeColor = Text;
        lv.BorderStyle = BorderStyle.None;
        lv.OwnerDraw = true;
        TrySetDoubleBuffered(lv); // 호버 리페인트 깜빡임 방지

        if (!st.Wired)
        {
            st.Wired = true;
            lv.DrawColumnHeader += HeaderDraw;
            lv.DrawItem += (_, e) => e.DrawDefault = false; // 배경·텍스트는 서브아이템에서 전부 그림
            lv.DrawSubItem += (_, e) => DrawSub(lv, st, e);
            lv.MouseMove += (_, e) =>
            {
                int i = lv.HitTest(e.X, e.Y).Item?.Index ?? -1;
                if (i == st.Hover) return;
                int old = st.Hover; st.Hover = i;
                if (old >= 0 && old < lv.Items.Count) lv.Invalidate(lv.Items[old].Bounds);
                if (i >= 0 && i < lv.Items.Count) lv.Invalidate(lv.Items[i].Bounds);
            };
            lv.MouseLeave += (_, _) =>
            {
                int old = st.Hover; st.Hover = -1;
                if (old >= 0 && old < lv.Items.Count) lv.Invalidate(lv.Items[old].Bounds);
            };
        }

        void applyScroll() { try { NativeMethods.SetWindowTheme(lv.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { } }
        if (lv.IsHandleCreated) applyScroll(); else lv.HandleCreated += (_, _) => applyScroll();
    }

    private static void TrySetDoubleBuffered(ListView lv)
    {
        try
        {
            typeof(ListView).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(lv, true);
        }
        catch { /* 무시 */ }
    }

    private static void HeaderDraw(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using (var b = new SolidBrush(BgHeader)) e.Graphics.FillRectangle(b, e.Bounds);
        using (var pen = new Pen(Border))
        {
            e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1); // 하단선
            e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4); // 열 경계(세로)
        }
        var r = e.Bounds; r.X += 6; r.Width -= 8;
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Header, r, GoldSoft,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static void DrawSub(ListView lv, LvState st, DrawListViewSubItemEventArgs e)
    {
        if (e.Item is null) { using var bg0 = new SolidBrush(Bg3); e.Graphics.FillRectangle(bg0, e.Bounds); return; }
        bool sel = e.Item.Selected;
        bool hot = e.ItemIndex == st.Hover;
        Color rowHot = IsDark ? C(0x27, 0x3A, 0x5C) : C(0xDA, 0xE6, 0xF7);
        Color bg = sel ? Accent : (hot ? rowHot : Bg3);

        using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
        if (!sel) // 열 경계(세로 구분선). 선택 행은 강조색 유지 위해 생략
            using (var sep = new Pen(IsDark ? C(0x2C, 0x32, 0x3C) : C(0xE1, 0xE6, 0xEE)))
                e.Graphics.DrawLine(sep, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
        if (e.SubItem is null) return;

        // 텍스트색: 선택=흰색, 공용스타일=아이템색(세션), 개별스타일=서브아이템색(터미널 종류)
        Color fg = sel ? Color.White
                       : e.Item.UseItemStyleForSubItems ? e.Item.ForeColor : e.SubItem.ForeColor;

        var rect = e.Bounds;
        // 첫 열 체크박스(테마 커스텀): 켜짐=골드+검은 체크, 꺼짐=어두운 사각+테두리
        if (e.ColumnIndex == 0 && lv.CheckBoxes)
        {
            const int box = 15;
            int bx = rect.Left + 4, by = rect.Top + (rect.Height - box) / 2;
            var brc = new Rectangle(bx, by, box, box);
            var prevSm = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bb = new SolidBrush(e.Item.Checked ? Gold : (IsDark ? C(0x14, 0x17, 0x1D) : Color.White)))
                e.Graphics.FillRectangle(bb, brc);
            using (var bp = new Pen(e.Item.Checked ? Gold : (sel ? Color.White : BorderHi)))
                e.Graphics.DrawRectangle(bp, brc);
            if (e.Item.Checked)
                using (var cp = new Pen(C(0x1A, 0x1A, 0x1A), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    e.Graphics.DrawLines(cp, new[] { new PointF(bx + 3, by + 8), new PointF(bx + 6, by + 11), new PointF(bx + 12, by + 4) });
            e.Graphics.SmoothingMode = prevSm;
            rect.X += box + 8; rect.Width -= box + 8;
        }
        else { rect.X += 6; rect.Width -= 8; }

        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lv.Font, rect, fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // ---- 재귀 스타일 ----
    public static void Apply(Control root)
    {
        if (root is Form f) { f.BackColor = Bg; f.ForeColor = Text; f.Font = Base; }
        foreach (Control c in root.Controls) StyleControl(c);
    }

    private static void StyleControl(Control c)
    {
        switch (c)
        {
            case MonitorMapControl mm:
                mm.BackColor = Bg2; mm.Invalidate();
                break;
            case DarkGroupBox gb:
                gb.ForeColor = GoldSoft; gb.Invalidate();
                break;
            case Button b:
                StyleButton(b, b.Tag as string == "primary");
                break;
            case RadioButton rb:
                // Standard: 시스템 라디오(다크 배경에서도 체크 상태가 또렷)
                rb.FlatStyle = FlatStyle.Standard; rb.ForeColor = Text; rb.BackColor = Color.Transparent;
                break;
            case CheckBox chk:
                chk.FlatStyle = FlatStyle.Standard; chk.ForeColor = Text; chk.BackColor = Color.Transparent;
                break;
            case ComboBox cb:
                StyleComboBox(cb);
                break;
            case ListView lv:
                StyleListView(lv);
                break;
            case NumericUpDown num:
                num.BackColor = Bg3; num.ForeColor = Text; num.BorderStyle = BorderStyle.FixedSingle;
                break;
            case TextBox tb:
                tb.BackColor = Bg3; tb.ForeColor = Text; tb.BorderStyle = BorderStyle.FixedSingle;
                break;
            case Label lbl:
                lbl.BackColor = Color.Transparent;
                lbl.ForeColor = (lbl.Tag as string) == "gold" ? GoldSoft : (lbl.Font.Bold ? Text : TextDim);
                break;
            case SplitContainer sc:
                sc.BackColor = Border;
                sc.Panel1.BackColor = Bg; sc.Panel2.BackColor = Bg;
                break;
            case FlowLayoutPanel:
            case TableLayoutPanel:
            case Panel:
                c.BackColor = Bg;
                break;
        }
        foreach (Control child in c.Controls) StyleControl(child);
    }
}

/// <summary>안티에일리어싱 라운드 버튼(단색 채움 + 라운드 테두리). 아이콘 또는 텍스트 중 하나를 그린다.</summary>
public sealed class RoundedButton : Button
{
    // 디자이너 직렬화 대상이 아니므로 필드로 둔다(WFO1000 회피). StyleButton에서 설정.
    public int CornerRadius = 8;
    public Color BorderColor = Color.Transparent;
    public Color HoverColor = Color.Empty;
    public Color PressColor = Color.Empty;
    public Color CornerColor = Color.Empty; // 라운드 바깥(모서리) 채움색 = 버튼 뒤 실제 배경
    private bool _hover, _press;

    // ── 누름 애니메이션 상태 ──
    private float _pressAmt;        // 0=뗌, 1=눌림(이징)
    private float _rippleT = -1f;   // -1=없음, 0..1 리플 진행
    private PointF _rippleAt;
    private System.Windows.Forms.Timer? _anim;

    public RoundedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    private void EnsureAnim()
    {
        if (_anim is not null) return;
        _anim = new System.Windows.Forms.Timer { Interval = 15 };
        _anim.Tick += (_, _) => Step();
        _anim.Start();
    }

    private void Step()
    {
        float target = _press ? 1f : 0f;
        _pressAmt += (target - _pressAmt) * 0.30f;                    // 목표로 이징
        if (Math.Abs(_pressAmt - target) < 0.01f) _pressAmt = target;
        if (_rippleT >= 0f) { _rippleT += 0.055f; if (_rippleT > 1f) _rippleT = -1f; }

        Invalidate();

        if (_pressAmt == target && _rippleT < 0f) { _anim?.Stop(); _anim?.Dispose(); _anim = null; }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _press = false; EnsureAnim(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _press = true; _rippleT = 0f; _rippleAt = e.Location; EnsureAnim(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _press = false; EnsureAnim(); base.OnMouseUp(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _anim?.Stop(); _anim?.Dispose(); _anim = null; }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(CornerColor.A > 0 ? CornerColor : (Parent?.BackColor ?? BackColor));

        // 배경색: 호버색 → 누름색으로 부드럽게 보간
        Color baseFill = _hover && HoverColor.A > 0 ? HoverColor : BackColor;
        Color fill = PressColor.A > 0 ? Lerp(baseFill, PressColor, _pressAmt) : baseFill;

        // 누르면 살짝 안으로 축소(최대 2px)
        int inset = (int)Math.Round(_pressAmt * 2f);
        var rect = new Rectangle(inset, inset, Width - 1 - inset * 2, Height - 1 - inset * 2);
        int radius = Math.Max(2, Math.Min(CornerRadius, Math.Min(rect.Width, rect.Height) / 2));
        using var path = Theme.RoundRect(rect, radius);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        if (BorderColor.A > 0) using (var pen = new Pen(BorderColor, 1.3f)) g.DrawPath(pen, path);

        // 리플: 눌린 지점에서 퍼지는 원(라운드 안쪽으로 클리핑)
        if (_rippleT >= 0f)
        {
            float ease = 1f - (1f - _rippleT) * (1f - _rippleT);      // ease-out
            float r = (float)Math.Sqrt(Width * Width + Height * Height) * ease;
            int alpha = (int)(70 * (1f - _rippleT));
            Color rc = Luminance(fill) < 0.5f ? Color.White : Color.Black;
            var state = g.Save();
            g.SetClip(path);
            using (var rb = new SolidBrush(Color.FromArgb(Math.Max(0, alpha), rc)))
                g.FillEllipse(rb, _rippleAt.X - r, _rippleAt.Y - r, r * 2, r * 2);
            g.Restore(state);
        }

        if (Image is { } img)
            g.DrawImage(img, (Width - img.Width) / 2, (Height - img.Height) / 2, img.Width, img.Height);
        else if (!string.IsNullOrEmpty(Text))
            TextRenderer.DrawText(g, Text, Font, new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2), ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    private static float Luminance(Color c) => (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
}

/// <summary>테마 라운드 카드 + 골드 타이틀로 그리는 GroupBox(다크/라이트 공용).</summary>
public sealed class DarkGroupBox : GroupBox
{
    public DarkGroupBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Bg);

        var card = new Rectangle(0, 10, Width - 1, Height - 11);
        using (var path = Theme.RoundRect(card, 8))
        using (var b = new SolidBrush(Theme.Bg2))
        using (var pen = new Pen(Theme.Border))
        {
            g.FillPath(b, path);
            g.DrawPath(pen, path);
        }
        if (!string.IsNullOrEmpty(Text))
        {
            var ts = TextRenderer.MeasureText(g, Text, Theme.Header);
            var tr = new Rectangle(12, 2, ts.Width + 12, ts.Height + 2);
            using var bg = new SolidBrush(Parent?.BackColor ?? Theme.Bg);
            g.FillRectangle(bg, tr);
            using (var gb = new SolidBrush(Theme.Gold))
                g.FillRectangle(gb, new Rectangle(12, 4, 3, ts.Height - 1));
            TextRenderer.DrawText(g, Text, Theme.Header, new Point(18, 2), Theme.GoldSoft);
        }
    }
}

/// <summary>ContextMenuStrip 테마 렌더러(다크/라이트 공용).</summary>
public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Selected ? Color.White : Theme.Text;
        base.OnRenderItemText(e);
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Bg2;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Accent;
        public override Color MenuItemSelected => Theme.Accent;
        public override Color MenuItemSelectedGradientBegin => Theme.Accent;
        public override Color MenuItemSelectedGradientEnd => Theme.Accent;
        public override Color ImageMarginGradientBegin => Theme.Bg2;
        public override Color ImageMarginGradientMiddle => Theme.Bg2;
        public override Color ImageMarginGradientEnd => Theme.Bg2;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }
}
