using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TileCLI.Models;

namespace TileCLI.UI;

/// <summary>
/// 실제 디스플레이 배치(Windows 디스플레이 설정처럼)를 축소해 그려, 어느 모니터가 1/2/3이고
/// 어디에 있는지 직관적으로 보여주는 컨트롤. 박스 클릭으로 타일 대상 모니터를 토글(다중 선택).
/// 번호는 크게, 해상도·주(主) 표시는 작게.
/// </summary>
public sealed class MonitorMapControl : Control
{
    private List<MonitorTarget> _monitors = new();
    private readonly HashSet<int> _checked = new();
    private readonly List<Rectangle> _drawn = new(); // index별 그려진 사각형(히트테스트)
    private int _hover = -1;
    private bool _singleSelect;

    public event EventHandler? SelectionChanged;

    public MonitorMapControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Theme.Bg2;
    }

    /// <summary>모니터 목록과 초기 체크 인덱스를 설정.</summary>
    public void SetMonitors(List<MonitorTarget> monitors, IEnumerable<int> checkedIndices)
    {
        _monitors = monitors ?? new List<MonitorTarget>();
        _checked.Clear();
        foreach (var i in checkedIndices) if (i >= 0 && i < _monitors.Count) _checked.Add(i);
        _hover = -1;
        if (_singleSelect) ReduceToSingle(); // 단일선택 모드면 하나만 유지
        Invalidate();
    }

    public IReadOnlyCollection<int> CheckedIndices => _checked;
    public int CheckedCount => _checked.Count;
    public bool IsChecked(int index) => _checked.Contains(index);

    /// <summary>true면 한 번에 한 모니터만 선택(라디오). 다중이 선택돼 있으면 하나로 줄인다.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    [System.ComponentModel.Browsable(false)]
    public bool SingleSelect
    {
        get => _singleSelect;
        set
        {
            _singleSelect = value;
            if (value) ReduceToSingle();
        }
    }

    /// <summary>여러 개 선택돼 있으면 하나만 남긴다(주 모니터 우선, 없으면 최소 인덱스).</summary>
    private void ReduceToSingle()
    {
        if (_checked.Count <= 1) return;
        int keep = -1;
        foreach (var i in _checked)
            if (i >= 0 && i < _monitors.Count && _monitors[i].IsPrimary) { keep = i; break; }
        if (keep < 0) keep = _checked.Min();
        _checked.Clear();
        _checked.Add(keep);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>체크 상태 지정. raise=true면 SelectionChanged 발생(설정 복원 중엔 false).</summary>
    public void SetChecked(int index, bool value, bool raise = false)
    {
        if (index < 0 || index >= _monitors.Count) return;
        bool changed = value ? _checked.Add(index) : _checked.Remove(index);
        if (changed) { Invalidate(); if (raise) SelectionChanged?.Invoke(this, EventArgs.Empty); }
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
        if (h >= 0)
        {
            if (_singleSelect)
            {
                // 단일 선택: 클릭한 것만 남긴다(이미 그것만 선택돼 있으면 변화 없음)
                if (_checked.Count == 1 && _checked.Contains(h)) { base.OnMouseClick(e); return; }
                _checked.Clear();
                _checked.Add(h);
            }
            else
            {
                if (!_checked.Remove(h)) _checked.Add(h);
            }
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseClick(e);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _drawn.Count; i++) if (_drawn[i].Contains(p)) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        _drawn.Clear();

        if (_monitors.Count == 0)
        {
            TextRenderer.DrawText(g, "모니터 없음", Font, ClientRectangle, Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        const int pad = 8;
        var area = new RectangleF(pad, pad, ClientSize.Width - 2 * pad, ClientSize.Height - 2 * pad);
        if (area.Width <= 0 || area.Height <= 0) return;

        // 실제 배치를 격자로 환산(중심 X/Y 군집 → 열/행). 각 모니터는 균일 정사각형으로 표현.
        const int tol = 400; // 이보다 가까운 중심은 같은 열/행(물리 px)
        var cols = Cluster(_monitors.Select(m => m.Bounds.Left + m.Bounds.Width / 2), tol);
        var rows = Cluster(_monitors.Select(m => m.Bounds.Top + m.Bounds.Height / 2), tol);
        int nc = Math.Max(1, cols.Count), nr = Math.Max(1, rows.Count);
        float cell = Math.Min(area.Width / nc, area.Height / nr);
        float sq = Math.Max(8f, cell - 8f);      // 정사각형(셀 안 간격 8)
        float ox = area.Left + (area.Width - cell * nc) / 2f;
        float oy = area.Top + (area.Height - cell * nr) / 2f;

        for (int i = 0; i < _monitors.Count; i++)
        {
            var m = _monitors[i];
            int col = Nearest(cols, m.Bounds.Left + m.Bounds.Width / 2);
            int row = Nearest(rows, m.Bounds.Top + m.Bounds.Height / 2);
            float ccx = ox + col * cell + cell / 2f, ccy = oy + row * cell + cell / 2f;
            var r = new Rectangle((int)(ccx - sq / 2f), (int)(ccy - sq / 2f), (int)sq, (int)sq);
            _drawn.Add(r);

            bool on = _checked.Contains(i);
            bool hover = _hover == i;
            Color fill = on ? (hover ? Theme.Accent : Theme.AccentDeep) : (hover ? Theme.Hover : Theme.Bg3);
            Color border = on ? Theme.Gold : Theme.Border;
            Color text = on ? Color.White : Theme.Text;

            int radius = Math.Min(8, Math.Min(r.Width, r.Height) / 4);
            using (var path = Round(r, radius))
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(border, on ? 2f : 1f))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }

            // 큰 번호(가운데)
            float numPx = Math.Max(11f, Math.Min(r.Height * 0.45f, r.Width * 0.45f));
            using (var nf = new Font("Segoe UI", numPx, FontStyle.Bold, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, m.DisplayNumber.ToString(), nf, r, text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // 주 모니터: 좌상단 코너에 바짝 붙여 볼드 "주"(내부 패딩 제거)
            if (m.IsPrimary && r.Height > 20 && r.Width > 20)
                using (var sf = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "주", sf, new Rectangle(r.Left + 2, r.Top + 1, 20, 14),
                        Theme.Gold, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>값들을 오름차순 정렬 후 tol 이내로 인접한 값을 하나의 대표값으로 군집화(열/행 산출).</summary>
    private static List<int> Cluster(IEnumerable<int> values, int tol)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var reps = new List<int>();
        foreach (var v in sorted)
            if (reps.Count == 0 || v - reps[^1] > tol) reps.Add(v);
        return reps;
    }

    /// <summary>대표값 목록에서 v에 가장 가까운 인덱스.</summary>
    private static int Nearest(List<int> reps, int v)
    {
        int best = 0, bd = int.MaxValue;
        for (int i = 0; i < reps.Count; i++)
        {
            int d = Math.Abs(reps[i] - v);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
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
