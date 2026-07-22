using System.Drawing;
using System.Windows.Forms;

namespace TileCLI.UI;

/// <summary>
/// 드래그 중인 목록 행을 마우스에 붙여 보여주는 반투명 고스트 창(게임 인벤토리 스타일).
/// 포커스·마우스 히트테스트를 뺏지 않도록 비활성/투과/툴윈도우 스타일로 띄운다.
/// </summary>
internal sealed class DragGhost : Form
{
    private string _title = string.Empty;
    private string _kind = string.Empty;
    private Color _kindColor;
    private Font _font = Theme.Base;

    public DragGhost()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Opacity = 0.88;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE — 포커스 안 뺏음
            cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT — 마우스 히트테스트 투과
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW — Alt+Tab 미노출
            return cp;
        }
    }

    /// <summary>고스트에 표시할 행 내용(제목·종류 색)과 크기를 지정.</summary>
    public void SetContent(string title, string kind, Color kindColor, Font font, int width, int height)
    {
        _title = title;
        _kind = kind;
        _kindColor = kindColor;
        _font = font;
        Size = new Size(width, height);
        Invalidate();
    }

    /// <summary>커서가 왼쪽 중앙을 잡은 모양으로 마우스에 붙여 이동.</summary>
    public void FollowCursor()
    {
        var p = Cursor.Position;
        Location = new Point(p.X - 14, p.Y - Height / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var bg = new SolidBrush(Theme.Accent)) g.FillRectangle(bg, r);
        using (var pen = new Pen(Theme.Gold)) g.DrawRectangle(pen, r);

        int kindW = string.IsNullOrEmpty(_kind) ? 0 : TextRenderer.MeasureText(_kind, _font).Width + 10;
        var titleRect = new Rectangle(8, 0, Width - kindW - 14, Height);
        TextRenderer.DrawText(g, _title, _font, titleRect, Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (kindW > 0)
        {
            var kindRect = new Rectangle(Width - kindW - 4, 0, kindW, Height);
            TextRenderer.DrawText(g, _kind, _font, kindRect, _kindColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
