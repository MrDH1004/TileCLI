using System.Drawing;
using System.Drawing.Drawing2D;

namespace TileCLI.UI;

public enum IconKind
{
    Refresh, CheckEmpty, CheckFilled, Settings, OpenNew, Delete,
    // 정렬
    AutoArrange, SplitH, SplitV, Grid,
    // 일괄
    MinimizeAll, ShowAll, RestorePrev,
    // 작업 세트
    Save, Apply
}

/// <summary>
/// GDI+로 직접 그린 단색 벡터 아이콘(폰트 글리프·특수문자 미사용). 테마 색으로 렌더.
/// </summary>
public static class Icons
{
    public static Bitmap Draw(IconKind kind, int size, Color color)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        float s = size;
        float sw = Math.Max(1.3f, s * 0.095f);
        using var pen = new Pen(color, sw) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);

        switch (kind)
        {
            case IconKind.CheckEmpty:
                using (var p = Round(Inset(s, 0.14f), s * 0.16f)) g.DrawPath(pen, p);
                break;

            case IconKind.CheckFilled:
                using (var p = Round(Inset(s, 0.14f), s * 0.16f)) g.DrawPath(pen, p);
                g.DrawLines(pen, new[] {
                    P(s, 0.32f, 0.52f), P(s, 0.45f, 0.66f), P(s, 0.70f, 0.36f) });
                break;

            case IconKind.Refresh:
            {
                float r = s * 0.30f, cx = s / 2f, cy = s / 2f;
                var rc = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                g.DrawArc(pen, rc, 70, 250);            // 위쪽 열린 원호
                // 원호 시작점(70°)에 접선 방향 화살촉
                ArrowHead(g, brush, cx + r * Cos(70), cy + r * Sin(70), 70 + 90, s * 0.26f);
                break;
            }

            case IconKind.Settings: // 톱니바퀴(설정)
            {
                const int teeth = 8;
                float cx = s / 2f, cy = s / 2f;
                float rOut = s * 0.46f, rIn = s * 0.34f, rHole = s * 0.155f;
                float step = (float)(Math.PI * 2 / teeth);
                var pts = new PointF[teeth * 4];
                int k = 0;
                for (int i = 0; i < teeth; i++)
                {
                    float b = i * step;
                    pts[k++] = Polar(cx, cy, rOut, b + 0.00f * step);
                    pts[k++] = Polar(cx, cy, rOut, b + 0.34f * step);
                    pts[k++] = Polar(cx, cy, rIn, b + 0.46f * step);
                    pts[k++] = Polar(cx, cy, rIn, b + 0.88f * step);
                }
                using var gp = new GraphicsPath { FillMode = FillMode.Alternate };
                gp.AddPolygon(pts);
                gp.AddEllipse(cx - rHole, cy - rHole, rHole * 2, rHole * 2); // 가운데 구멍
                g.FillPath(brush, gp);
                break;
            }

            case IconKind.OpenNew: // 복구(세션 실행): 재생 삼각형 ▶
            {
                var tri = new[] { P(s, 0.34f, 0.24f), P(s, 0.34f, 0.76f), P(s, 0.78f, 0.50f) };
                g.FillPolygon(brush, tri);
                break;
            }

            case IconKind.Delete: // 휴지통
            {
                g.DrawLine(pen, s * 0.22f, s * 0.28f, s * 0.78f, s * 0.28f);          // 뚜껑
                g.DrawLine(pen, s * 0.40f, s * 0.28f, s * 0.42f, s * 0.20f);          // 손잡이 좌
                g.DrawLine(pen, s * 0.42f, s * 0.20f, s * 0.58f, s * 0.20f);          // 손잡이
                g.DrawLine(pen, s * 0.58f, s * 0.20f, s * 0.60f, s * 0.28f);          // 손잡이 우
                g.DrawLines(pen, new[] {                                             // 통 몸통(사다리꼴)
                    P(s, 0.29f, 0.32f), P(s, 0.34f, 0.82f), P(s, 0.66f, 0.82f), P(s, 0.71f, 0.32f) });
                g.DrawLine(pen, s * 0.44f, s * 0.40f, s * 0.44f, s * 0.72f);          // 내부 선
                g.DrawLine(pen, s * 0.56f, s * 0.40f, s * 0.56f, s * 0.72f);
                break;
            }

            case IconKind.AutoArrange: // 번개(자동 배치)
            {
                var bolt = new[]
                {
                    P(s, 0.58f, 0.06f), P(s, 0.30f, 0.54f), P(s, 0.47f, 0.54f),
                    P(s, 0.40f, 0.94f), P(s, 0.72f, 0.42f), P(s, 0.53f, 0.42f), P(s, 0.60f, 0.06f)
                };
                g.FillPolygon(brush, bolt);
                break;
            }

            case IconKind.SplitH: // 좌우 2분할(세로 구분선)
            {
                var r = Inset(s, 0.16f);
                using (var p = Round(r, s * 0.10f)) g.DrawPath(pen, p);
                float mx = r.Left + r.Width / 2f;
                g.DrawLine(pen, mx, r.Top, mx, r.Bottom);
                break;
            }

            case IconKind.SplitV: // 상하 2분할(가로 구분선)
            {
                var r = Inset(s, 0.16f);
                using (var p = Round(r, s * 0.10f)) g.DrawPath(pen, p);
                float my = r.Top + r.Height / 2f;
                g.DrawLine(pen, r.Left, my, r.Right, my);
                break;
            }

            case IconKind.Grid: // 2x2 격자
            {
                var r = Inset(s, 0.16f);
                using (var p = Round(r, s * 0.10f)) g.DrawPath(pen, p);
                float mx = r.Left + r.Width / 2f, my = r.Top + r.Height / 2f;
                g.DrawLine(pen, mx, r.Top, mx, r.Bottom);
                g.DrawLine(pen, r.Left, my, r.Right, my);
                break;
            }

            case IconKind.MinimizeAll: // 전체 최소화: 아래로 내려가 작업표시줄 바에 붙음(최소화 느낌)
            {
                float cx = s * 0.5f;
                g.DrawLine(pen, cx, s * 0.16f, cx, s * 0.58f);                                         // 세로 줄기
                g.DrawLines(pen, new[] { P(s, 0.33f, 0.42f), P(s, 0.50f, 0.60f), P(s, 0.67f, 0.42f) }); // 아래 화살촉
                g.DrawLine(pen, s * 0.24f, s * 0.80f, s * 0.76f, s * 0.80f);                           // 작업표시줄 바
                break;
            }

            case IconKind.ShowAll: // 전체 다시 보이기: 모서리로 뻗는 프레임 코너(확대 느낌)
                g.DrawLine(pen, s * 0.18f, s * 0.18f, s * 0.40f, s * 0.18f); g.DrawLine(pen, s * 0.18f, s * 0.18f, s * 0.18f, s * 0.40f); // TL
                g.DrawLine(pen, s * 0.82f, s * 0.18f, s * 0.60f, s * 0.18f); g.DrawLine(pen, s * 0.82f, s * 0.18f, s * 0.82f, s * 0.40f); // TR
                g.DrawLine(pen, s * 0.18f, s * 0.82f, s * 0.40f, s * 0.82f); g.DrawLine(pen, s * 0.18f, s * 0.82f, s * 0.18f, s * 0.60f); // BL
                g.DrawLine(pen, s * 0.82f, s * 0.82f, s * 0.60f, s * 0.82f); g.DrawLine(pen, s * 0.82f, s * 0.82f, s * 0.82f, s * 0.60f); // BR
                break;

            case IconKind.RestorePrev: // 되돌리기(반시계 원호 화살표)
            {
                float r = s * 0.30f, cx = s / 2f, cy = s / 2f;
                var rc = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                g.DrawArc(pen, rc, 110, 250);
                ArrowHead(g, brush, cx + r * Cos(110), cy + r * Sin(110), 110 - 90, s * 0.26f);
                break;
            }

            case IconKind.Save: // 플로피 디스크(저장)
            {
                var r = Inset(s, 0.16f);
                using (var p = Round(r, s * 0.06f)) g.DrawPath(pen, p);
                g.FillRectangle(brush, r.Left + r.Width * 0.55f, r.Top, r.Width * 0.16f, r.Height * 0.28f); // 쓰기방지 탭
                g.DrawRectangle(pen, r.Left + r.Width * 0.26f, r.Top + r.Height * 0.52f, r.Width * 0.48f, r.Height * 0.30f); // 라벨
                break;
            }

            case IconKind.Apply: // 체크(적용)
            {
                g.DrawLines(pen, new[] { P(s, 0.20f, 0.54f), P(s, 0.42f, 0.74f), P(s, 0.80f, 0.28f) });
                break;
            }
        }
        return bmp;
    }

    private static RectangleF Inset(float s, float m) => new(s * m, s * m, s * (1 - 2 * m), s * (1 - 2 * m));
    private static PointF P(float s, float fx, float fy) => new(s * fx, s * fy);
    private static PointF Polar(float cx, float cy, float r, float rad) =>
        new(cx + r * (float)Math.Cos(rad), cy + r * (float)Math.Sin(rad));
    private static float Cos(float deg) => (float)Math.Cos(deg * Math.PI / 180);
    private static float Sin(float deg) => (float)Math.Sin(deg * Math.PI / 180);

    private static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Max(0, radius) * 2;
        if (d < 2) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static void ArrowHead(Graphics g, Brush b, float px, float py, float dirDeg, float len)
    {
        float tipx = px + len * Cos(dirDeg), tipy = py + len * Sin(dirDeg);
        float lx = px + len * Cos(dirDeg + 140), ly = py + len * Sin(dirDeg + 140);
        float rx = px + len * Cos(dirDeg - 140), ry = py + len * Sin(dirDeg - 140);
        g.FillPolygon(b, new[] { new PointF(tipx, tipy), new PointF(lx, ly), new PointF(rx, ry) });
    }
}
