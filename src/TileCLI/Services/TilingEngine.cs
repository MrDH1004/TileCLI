using System.Drawing;
using TileCLI.Models;
using TileCLI.Native;

namespace TileCLI.Services;

/// <summary>
/// 타일 좌표 계산(순수 함수) + 실제 창 적용. 좌표는 물리 픽셀.
/// 계산부는 GUI/Win32 없이 검증 가능하도록 <see cref="ComputeCells"/>로 분리.
/// </summary>
public static class TilingEngine
{
    /// <summary>딱 붙이기(gap 0)에서 인접 타일 사이 1px 틈/둥근 모서리 seam을 없애기 위한 내부 경계 겹침(px).</summary>
    public const int FlushOverlap = 1;

    /// <summary>
    /// 셀의 "내부" 경계(영역 경계에 닿지 않은 쪽)를 overlap만큼 바깥으로 확장한다.
    /// 인접 타일끼리 그만큼 겹쳐 배치돼 사이 틈이 사라진다. 바깥(영역 경계) 쪽은 그대로 둔다.
    /// </summary>
    public static Rectangle ExpandInternalEdges(Rectangle cell, Rectangle area, int overlap)
    {
        if (overlap <= 0) return cell;
        int l = cell.Left   > area.Left   ? cell.Left - overlap   : cell.Left;
        int t = cell.Top    > area.Top    ? cell.Top - overlap    : cell.Top;
        int r = cell.Right  < area.Right  ? cell.Right + overlap  : cell.Right;
        int b = cell.Bottom < area.Bottom ? cell.Bottom + overlap : cell.Bottom;
        return Rectangle.FromLTRB(l, t, r, b);
    }

    /// <summary>
    /// count개 창을 direction/area/gap에 맞춰 배치할 셀 사각형들을 계산한다.
    /// gap==0이면 셀들의 합집합이 area와 정확히 일치하고 상호 겹침이 없다(꽉참·무겹침 보장).
    /// </summary>
    public static List<Rectangle> ComputeCells(int count, LayoutDirection direction, Rectangle area, int gap = 0)
    {
        var cells = new List<Rectangle>(Math.Max(0, count));
        if (count <= 0 || area.Width <= 0 || area.Height <= 0) return cells;
        if (count == 1)
        {
            cells.Add(area);
            return cells;
        }

        switch (direction)
        {
            case LayoutDirection.Horizontal:
            {
                var cols = Split(area.Left, area.Width, count, gap);
                foreach (var (off, size) in cols)
                    cells.Add(new Rectangle(off, area.Top, size, area.Height));
                break;
            }
            case LayoutDirection.Vertical:
            {
                var rows = Split(area.Top, area.Height, count, gap);
                foreach (var (off, size) in rows)
                    cells.Add(new Rectangle(area.Left, off, area.Width, size));
                break;
            }
            case LayoutDirection.Grid:
            default:
            {
                int cols = (int)Math.Ceiling(Math.Sqrt(count));
                int rows = (int)Math.Ceiling((double)count / cols);
                var rowBands = Split(area.Top, area.Height, rows, gap);

                int placed = 0;
                for (int r = 0; r < rows && placed < count; r++)
                {
                    int itemsInRow = Math.Min(cols, count - r * cols);
                    var colBands = Split(area.Left, area.Width, itemsInRow, gap);
                    var (rowOff, rowSize) = rowBands[r];
                    for (int c = 0; c < itemsInRow; c++)
                    {
                        var (colOff, colSize) = colBands[c];
                        cells.Add(new Rectangle(colOff, rowOff, colSize, rowSize));
                        placed++;
                    }
                }
                break;
            }
        }

        return cells;
    }

    /// <summary>
    /// [start, start+total) 구간을 parts개로 정수 픽셀 분할. gap>0이면 사이 간격 삽입.
    /// gap==0이면 각 조각 크기 합==total, 마지막 조각이 정확히 start+total에서 끝난다(빈틈/겹침 없음).
    /// </summary>
    private static (int off, int size)[] Split(int start, int total, int parts, int gap)
    {
        var res = new (int, int)[parts];
        if (parts <= 0) return res;

        int avail = total - gap * (parts - 1);
        if (avail < parts) { avail = total; gap = 0; } // 간격이 과도하면 무시

        int baseSize = avail / parts;
        int rem = avail % parts;
        int cur = start;
        for (int i = 0; i < parts; i++)
        {
            int size = baseSize + (i < rem ? 1 : 0);
            res[i] = (cur, size);
            cur += size + gap;
        }
        return res;
    }

    /// <summary>
    /// 자동 배치 방향 선택(요구 1): 대상 개수와 영역 가로세로 비율로 최적 방향을 고른다.
    /// 1개는 방향 무관(꽉 채움). 2~3개는 넓으면 가로/높으면 세로. 4개 이상은 그리드.
    /// </summary>
    public static LayoutDirection AutoDirection(int count, Rectangle area)
    {
        if (count <= 1) return LayoutDirection.Grid; // 단일 → 영역 전체(방향 무의미)
        bool wide = area.Width >= area.Height;
        if (count <= 3) return wide ? LayoutDirection.Horizontal : LayoutDirection.Vertical;
        return LayoutDirection.Grid;
    }

    /// <summary>선택된 모니터들로부터 타일링 대상 영역(작업영역)을 산출.</summary>
    public static Rectangle ComputeTargetArea(IReadOnlyList<MonitorTarget> selected, bool span)
    {
        if (selected.Count == 0) return Rectangle.Empty;
        if (!span || selected.Count == 1) return selected[0].WorkArea;

        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var m in selected)
        {
            left = Math.Min(left, m.WorkArea.Left);
            top = Math.Min(top, m.WorkArea.Top);
            right = Math.Max(right, m.WorkArea.Right);
            bottom = Math.Max(bottom, m.WorkArea.Bottom);
        }
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    /// <summary>
    /// 창 목록을 계산된 셀에 실제 배치. 배치 전 스냅샷을 저장(복원용).
    /// 최소화/최대화 창은 먼저 일반 상태로 복원 후 이동. DWM 확장프레임 보정으로 가시 테두리를 셀에 맞춤.
    /// </summary>
    /// <returns>실제 배치된 창 개수</returns>
    public static int ApplyTiling(IReadOnlyList<IntPtr> windows, LayoutDirection direction,
        Rectangle area, int gap, SnapshotStore snapshots)
    {
        if (windows.Count == 0 || area.Width <= 0 || area.Height <= 0) return 0;

        // 정렬 직전 상태 스냅샷(복원용)
        snapshots.Capture(windows);

        var cells = ComputeCells(windows.Count, direction, area, gap);
        int applied = 0;

        for (int i = 0; i < windows.Count && i < cells.Count; i++)
        {
            IntPtr hWnd = windows[i];
            if (!NativeMethods.IsWindow(hWnd)) continue;

            // 최소화/최대화 → 일반 상태로 먼저 되돌려야 위치 지정이 먹힘
            if (NativeMethods.IsIconic(hWnd) || IsMaximized(hWnd))
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);

            // 창 모서리를 각지게(사각) + 프레임 보더를 어둡게 — 상단/타일 사이 1px 밝은 줄 제거
            NativeMethods.SetSquareCorners(hWnd, true);
            NativeMethods.SetDarkFrameBorder(hWnd, true);

            Rectangle cell = cells[i];
            if (cell.Width <= 0 || cell.Height <= 0) continue; // 극단적으로 작은 영역의 0크기 셀 방어

            // 간격 0(딱 붙이기)이면 내부 경계를 살짝 겹쳐 인접 창 사이 1px 틈 제거(각진 모서리 미적용 대비 보강)
            Rectangle place = gap == 0 ? ExpandInternalEdges(cell, area, FlushOverlap) : cell;
            Rectangle target = CompensateBorders(hWnd, place);

            bool ok = NativeMethods.SetWindowPos(
                hWnd, IntPtr.Zero,
                target.X, target.Y, target.Width, target.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);

            if (ok) applied++;
        }

        return applied;
    }

    /// <summary>
    /// 창의 "보이는" 사각형(DWM 확장 프레임 기준)을 얻는다. 실패 시 GetWindowRect로 폴백.
    /// 타일 셀과 직접 비교/정렬하는 데 쓴다.
    /// </summary>
    public static bool TryGetVisibleRect(IntPtr hWnd, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (!NativeMethods.IsWindow(hWnd)) return false;
        int rectSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>();
        if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT ext, rectSize) == 0)
        {
            rect = Rectangle.FromLTRB(ext.Left, ext.Top, ext.Right, ext.Bottom);
            return rect.Width > 0 && rect.Height > 0;
        }
        if (NativeMethods.GetWindowRect(hWnd, out var wr))
        {
            rect = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
            return rect.Width > 0 && rect.Height > 0;
        }
        return false;
    }

    /// <summary>창의 "보이는" 테두리가 cell에 정확히 맞도록 이동/리사이즈(경계 보정 포함).</summary>
    public static bool MoveWindowVisible(IntPtr hWnd, Rectangle cell)
    {
        if (!NativeMethods.IsWindow(hWnd) || cell.Width <= 0 || cell.Height <= 0) return false;
        if (NativeMethods.IsIconic(hWnd) || IsMaximized(hWnd))
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);

        NativeMethods.SetSquareCorners(hWnd, true); // 연동 리사이즈 후에도 각진 모서리 유지
        NativeMethods.SetDarkFrameBorder(hWnd, true); // 프레임 보더 어둡게(1px 밝은 줄 방지)

        Rectangle target = CompensateBorders(hWnd, cell);
        return NativeMethods.SetWindowPos(hWnd, IntPtr.Zero,
            target.X, target.Y, target.Width, target.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    private static bool IsMaximized(IntPtr hWnd)
    {
        var wp = new NativeMethods.WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if (NativeMethods.GetWindowPlacement(hWnd, ref wp))
            return wp.showCmd == 3; // SW_SHOWMAXIMIZED
        return false;
    }

    /// <summary>
    /// DWM 확장 프레임(보이지 않는 리사이즈 테두리/그림자)을 보정해 "보이는" 창 테두리를 셀에 밀착.
    /// </summary>
    private static Rectangle CompensateBorders(IntPtr hWnd, Rectangle cell)
    {
        if (!NativeMethods.GetWindowRect(hWnd, out var wr)) return cell;
        int rectSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>();
        if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT ext, rectSize) != 0)
            return cell;

        int bl = ext.Left - wr.Left;      // 좌측 보이지 않는 테두리 두께
        int bt = ext.Top - wr.Top;        // 상단
        int br = wr.Right - ext.Right;     // 우측
        int bb = wr.Bottom - ext.Bottom;  // 하단

        // 음수/비정상 값 방어. SW_RESTORE 직후 확장프레임 값이 일시적으로 어긋난 경우 등
        // 비상식적으로 큰 델타(테두리 두께로 볼 수 없는 값)는 보정을 포기한다.
        const int MaxBorder = 64;
        if (bl < 0 || bt < 0 || br < 0 || bb < 0) return cell;
        if (bl > MaxBorder || bt > MaxBorder || br > MaxBorder || bb > MaxBorder) return cell;

        return new Rectangle(cell.X - bl, cell.Y - bt, cell.Width + bl + br, cell.Height + bt + bb);
    }
}
