using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TileCLI.UI;

/// <summary>
/// 선택 모니터를 존으로 나눠 그리고, 존을 클릭하면 그 존의 "프랙션 사각형"(0~1)을 알려주는 컨트롤.
/// MainForm이 프랙션을 선택 모니터 작업영역에 곱해 선택 창을 그 위치·크기로 스냅한다.
/// Windows Win+화살표처럼 "선택한 창"을 특정 존에 붙이는 용도. 3종 레이아웃 지원.
/// </summary>
public sealed class SnapZoneControl : Control
{
    public enum ZoneLayout { Grid3x3, Thirds, Quarters }

    private ZoneLayout _layout = ZoneLayout.Grid3x3;
    private readonly List<(Rectangle rect, RectangleF frac)> _cells = new();
    private int _hover = -1;

    /// <summary>존 클릭 시 프랙션 사각형(0~1, 선택 모니터 영역 기준)을 전달.</summary>
    public event EventHandler<RectangleF>? ZonePicked;

    public SnapZoneControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Theme.Bg2;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    [System.ComponentModel.Browsable(false)]
    public ZoneLayout ZoneMode
    {
        get => _layout;
        set { if (_layout != value) { _layout = value; _hover = -1; Invalidate(); } }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hover != -1) { _hover = -1; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h >= 0 && h < _cells.Count) ZonePicked?.Invoke(this, _cells[h].frac);
        base.OnMouseClick(e);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _cells.Count; i++) if (_cells[i].rect.Contains(p)) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg2); // 동적 테마색(전환 시에도 배경이 따라옴)
        _cells.Clear();

        const int pad = 6, gapPx = 4;
        var outer = new Rectangle(pad, pad, ClientSize.Width - 2 * pad, ClientSize.Height - 2 * pad);
        if (outer.Width <= 4 || outer.Height <= 4) return;

        int nr, nc;
        switch (_layout)
        {
            case ZoneLayout.Thirds: nr = 1; nc = 3; break;   // 가로 1/3(전체 높이 3열)
            case ZoneLayout.Quarters: nr = 2; nc = 2; break; // 화면 1/4(2×2 사분면)
            default: nr = 3; nc = 3; break;                  // 3×3(각 1/3×1/3)
        }

        for (int r = 0; r < nr; r++)
            for (int c = 0; c < nc; c++)
            {
                float fw = 1f / nc, fh = 1f / nr;
                var frac = new RectangleF(c * fw, r * fh, fw, fh);

                // 인접 존이 어긋나지 않도록 경계를 반올림해 픽셀 정렬
                int x0 = outer.Left + (int)Math.Round(c * (float)outer.Width / nc);
                int x1 = outer.Left + (int)Math.Round((c + 1) * (float)outer.Width / nc);
                int y0 = outer.Top + (int)Math.Round(r * (float)outer.Height / nr);
                int y1 = outer.Top + (int)Math.Round((r + 1) * (float)outer.Height / nr);
                var cell = Rectangle.FromLTRB(x0 + gapPx / 2, y0 + gapPx / 2, x1 - gapPx / 2, y1 - gapPx / 2);
                if (cell.Width <= 0 || cell.Height <= 0) continue;

                int idx = _cells.Count;
                _cells.Add((cell, frac));

                bool hover = _hover == idx;
                Color fill = hover ? Theme.Accent : Theme.Bg3;
                Color border = hover ? Theme.Gold : Theme.Border;
                using var path = Round(cell, 5);
                using var b = new SolidBrush(fill);
                using var pen = new Pen(border, hover ? 2f : 1f);
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }
    }

    private static GraphicsPath Round(Rectangle r, int radius)
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
}
