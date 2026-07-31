using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TileCLI.UI;

/// <summary>
/// 존/셀 격자를 그리는 컨트롤. 두 용도로 쓰인다 —
/// 1) 고정 배치 그룹의 "미리보기"(Interactive=false): 현재 폭 모드의 셀과 앵커 셀을 표시.
/// 2) 옵션의 "초기 위치 앵커"(Interactive=true, Grid3x3): 셀 클릭으로 앵커(열,행)를 고른다.
/// </summary>
public sealed class SnapZoneControl : Control
{
    // Full=1(전체), Halves=2열, Thirds=3열, Quarters=2x2, Grid3x3=3x3(앵커 선택용)
    public enum ZoneLayout { Full, Halves, Thirds, Quarters, Grid3x3 }

    private ZoneLayout _layout = ZoneLayout.Grid3x3;
    private readonly List<(Rectangle rect, int col, int row)> _cells = new();
    private int _hover = -1;

    /// <summary>true면 클릭으로 셀(앵커)을 고를 수 있다. false면 미리보기 전용.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    [System.ComponentModel.Browsable(false)]
    public bool Interactive { get; set; } = true;

    /// <summary>강조(선택)할 셀. 앵커 표시용. 없으면 -1.</summary>
    public int SelCol { get; private set; } = -1;
    public int SelRow { get; private set; } = -1;

    /// <summary>셀 클릭 시 (열,행) 전달(Interactive일 때만).</summary>
    public event EventHandler<(int col, int row)>? CellPicked;

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

    /// <summary>강조할 셀 지정(앵커). 범위 밖이면 강조 없음.</summary>
    public void SetSelectedCell(int col, int row)
    {
        if (SelCol != col || SelRow != row) { SelCol = col; SelRow = row; Invalidate(); }
    }

    private (int nr, int nc) Dims() => _layout switch
    {
        ZoneLayout.Full => (1, 1),
        ZoneLayout.Halves => (1, 2),
        ZoneLayout.Thirds => (1, 3),
        ZoneLayout.Quarters => (2, 2),
        _ => (3, 3),
    };

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = Interactive ? HitTest(e.Location) : -1;
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
        if (Interactive)
        {
            int h = HitTest(e.Location);
            if (h >= 0 && h < _cells.Count)
            {
                var c = _cells[h];
                SetSelectedCell(c.col, c.row);
                CellPicked?.Invoke(this, (c.col, c.row));
            }
        }
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

        var (nr, nc) = Dims();
        for (int r = 0; r < nr; r++)
            for (int c = 0; c < nc; c++)
            {
                int x0 = outer.Left + (int)Math.Round(c * (float)outer.Width / nc);
                int x1 = outer.Left + (int)Math.Round((c + 1) * (float)outer.Width / nc);
                int y0 = outer.Top + (int)Math.Round(r * (float)outer.Height / nr);
                int y1 = outer.Top + (int)Math.Round((r + 1) * (float)outer.Height / nr);
                var cell = Rectangle.FromLTRB(x0 + gapPx / 2, y0 + gapPx / 2, x1 - gapPx / 2, y1 - gapPx / 2);
                if (cell.Width <= 0 || cell.Height <= 0) continue;

                int idx = _cells.Count;
                _cells.Add((cell, c, r));

                bool hover = _hover == idx;
                bool sel = (c == SelCol && r == SelRow); // 앵커/선택 셀
                Color fill = hover ? Theme.Accent : (sel ? Theme.AccentDeep : Theme.Hover);
                Color border = (hover || sel) ? Theme.Gold : Theme.BorderHi;
                using var path = Round(cell, 5);
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var pen = new Pen(border, (hover || sel) ? 2f : 1f)) g.DrawPath(pen, path);
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
