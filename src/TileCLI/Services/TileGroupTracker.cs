using System.Drawing;
using TileCLI.Native;

namespace TileCLI.Services;

/// <summary>
/// 타일 배치된 창들을 "그룹"으로 기억하고, 사용자가 그 중 한 창의 경계를 드래그하면 맞닿은 인접 창들을
/// 함께 조정한다 → Windows 스냅 그룹처럼 "붙여서 조정".
///
/// 실시간 연동: 드래그 시작(EVENT_SYSTEM_MOVESIZESTART)에서 대상 창을 잡고, 그 창의 스레드에만
/// EVENT_OBJECT_LOCATIONCHANGE 훅을 걸어 드래그 도중 위치가 바뀔 때마다 인접 창을 실시간 재배치한다.
/// 드래그 종료(EVENT_SYSTEM_MOVESIZEEND)에서 최종 반영 + 훅 해제. 커서 등 잡음 이벤트는 스레드 스코프로 차단하고,
/// 우리가 옮기는 인접 창은 hwnd==드래그창 필터로 무시해 피드백 루프를 막는다.
///
/// 재정렬 좌표 계산은 <see cref="ComputeReflow"/>(순수 함수)로 분리해 헤드리스 검증이 가능하다.
/// </summary>
public sealed class TileGroupTracker : IDisposable
{
    public const int DefaultTol = 6;      // 경계 정렬 허용 오차(px)
    public const int DefaultMinSize = 80; // 인접 창 최소 크기(px)
    private const int ThrottleMs = 15;    // 실시간 재배치 최소 간격(~66Hz)

    private readonly Dictionary<IntPtr, Rectangle> _cells = new(); // 창 → 현재(보이는) 셀
    private Rectangle _area;   // 그룹 컨테이너(작업영역)
    private int _gap;

    private IntPtr _moveHook;  // MOVESIZESTART..END (활성 동안 상시)
    private IntPtr _locHook;   // LOCATIONCHANGE (드래그 중에만, 대상 창 스레드 스코프)
    private NativeMethods.WinEventDelegate? _moveProc; // GC 방지
    private NativeMethods.WinEventDelegate? _locProc;

    private IntPtr _dragging;                      // 현재 드래그 중인 창(없으면 Zero)
    private Rectangle _dragStart;                  // 드래그 시작 시 대상 창의 보이는 사각형
    private Dictionary<IntPtr, Rectangle>? _dragCells; // 드래그 시작 시 그룹 스냅샷(기준)
    private int _lastReflowTick;
    private bool _busy;                            // 인접 창 이동 중 재진입 방지

    public bool Enabled { get; private set; }

    /// <summary>훅 on/off. on이면 드래그 시작/종료 훅 설치(UI 스레드에서 호출).</summary>
    public void SetEnabled(bool on)
    {
        if (on == Enabled) return;
        if (on)
        {
            _moveProc = OnMoveEvent;
            _moveHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_MOVESIZESTART, NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero, _moveProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            Enabled = _moveHook != IntPtr.Zero;
            if (!Enabled) _moveProc = null;
        }
        else
        {
            StopDragHook();
            if (_moveHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_moveHook);
            _moveHook = IntPtr.Zero;
            _moveProc = null;
            _dragging = IntPtr.Zero;
            _dragCells = null;
            Enabled = false;
        }
    }

    /// <summary>새 타일 그룹 등록. windows[i]가 cells[i]에 배치됐다고 본다.</summary>
    public void Track(IReadOnlyList<IntPtr> windows, IReadOnlyList<Rectangle> cells, Rectangle area, int gap)
    {
        _cells.Clear();
        _area = area;
        _gap = gap;
        int n = Math.Min(windows.Count, cells.Count);
        for (int i = 0; i < n; i++)
        {
            if (!NativeMethods.IsWindow(windows[i])) continue;
            _cells[windows[i]] = cells[i];
        }
    }

    public void Clear()
    {
        StopDragHook();
        _dragging = IntPtr.Zero;
        _dragCells = null;
        _cells.Clear();
    }

    // ==== 드래그 시작/종료 ====
    private void OnMoveEvent(IntPtr hHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != NativeMethods.OBJID_WINDOW) return;
        try
        {
            if (eventType == NativeMethods.EVENT_SYSTEM_MOVESIZESTART) StartDrag(hwnd);
            else if (eventType == NativeMethods.EVENT_SYSTEM_MOVESIZEEND) EndDrag(hwnd);
        }
        catch { /* 개별 실패 무시 */ }
    }

    private void StartDrag(IntPtr hwnd)
    {
        if (!_cells.ContainsKey(hwnd)) return;
        if (!TilingEngine.TryGetVisibleRect(hwnd, out var start)) return;

        StopDragHook(); // 직전 드래그 훅이 남아있으면 정리(누수 방지)
        _dragging = hwnd;
        _dragStart = start;
        _dragCells = new Dictionary<IntPtr, Rectangle>(_cells); // 기준 스냅샷
        _lastReflowTick = 0;

        // 대상 창의 스레드에만 LOCATIONCHANGE 훅(이벤트 폭주/커서 잡음 차단)
        uint tid = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        _locProc = OnLocationEvent;
        _locHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _locProc, 0, tid, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    private void EndDrag(IntPtr hwnd)
    {
        // 드래그 중이던 창의 종료: 최종 반영 + 셀 갱신
        if (_dragging != IntPtr.Zero && hwnd == _dragging && _dragCells is not null)
        {
            var updates = ComputeCurrentUpdates(out var cur);
            ApplyUpdates(updates);
            if (cur.HasValue)
            {
                _cells[_dragging] = cur.Value;
                foreach (var (id, rect) in updates) _cells[new IntPtr(id)] = rect;
            }
            StopDragHook();
            _dragging = IntPtr.Zero;
            _dragCells = null;
            return;
        }

        // START를 못 잡은 경우 폴백: 저장 셀 기준 1회 재정렬
        if (_cells.ContainsKey(hwnd)) OneShotReflow(hwnd);
    }

    private void StopDragHook()
    {
        if (_locHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_locHook);
        _locHook = IntPtr.Zero;
        _locProc = null;
    }

    // ==== 드래그 중 실시간 ====
    private void OnLocationEvent(IntPtr hHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (_busy) return;
        if (hwnd != _dragging || idObject != NativeMethods.OBJID_WINDOW) return;

        int now = Environment.TickCount;
        if (now - _lastReflowTick < ThrottleMs) return; // 스로틀
        _lastReflowTick = now;

        var updates = ComputeCurrentUpdates(out _);
        ApplyUpdates(updates);
    }

    /// <summary>드래그 시작 스냅샷 기준으로 현재 대상 창 위치에 맞는 인접 창 좌표를 계산.</summary>
    private List<(long id, Rectangle rect)> ComputeCurrentUpdates(out Rectangle? cur)
    {
        cur = null;
        if (_dragging == IntPtr.Zero || _dragCells is null) return new();
        if (!TilingEngine.TryGetVisibleRect(_dragging, out var current)) return new();
        cur = current;

        var list = new List<(long id, Rectangle rect)>(_dragCells.Count);
        foreach (var kv in _dragCells) list.Add((kv.Key.ToInt64(), kv.Value));
        return ComputeReflow(list, _dragging.ToInt64(), _dragStart, current, _area, _gap, DefaultTol, DefaultMinSize);
    }

    /// <summary>START 없이 종료만 잡힌 경우: 저장 셀 기준 1회 재정렬.</summary>
    private void OneShotReflow(IntPtr hwnd)
    {
        if (!_cells.TryGetValue(hwnd, out var old)) return;
        if (!TilingEngine.TryGetVisibleRect(hwnd, out var cur)) return;

        var list = new List<(long id, Rectangle rect)>(_cells.Count);
        foreach (var kv in _cells) list.Add((kv.Key.ToInt64(), kv.Value));
        var updates = ComputeReflow(list, hwnd.ToInt64(), old, cur, _area, _gap, DefaultTol, DefaultMinSize);
        ApplyUpdates(updates);
        _cells[hwnd] = cur;
        foreach (var (id, rect) in updates) _cells[new IntPtr(id)] = rect;
    }

    /// <summary>인접 창들을 실제로 이동(간격 0이면 겹쳐 붙임). 재진입 방지.</summary>
    private void ApplyUpdates(List<(long id, Rectangle rect)> updates)
    {
        if (updates.Count == 0) return;
        _busy = true;
        try
        {
            foreach (var (id, rect) in updates)
            {
                var h = new IntPtr(id);
                var place = _gap == 0 ? TilingEngine.ExpandInternalEdges(rect, _area, TilingEngine.FlushOverlap) : rect;
                TilingEngine.MoveWindowVisible(h, place);
            }
        }
        finally { _busy = false; }
    }

    /// <summary>
    /// [순수 함수] mover 창이 old→cur로 바뀌었을 때, 그룹 내 인접 창들이 공유 경계를 유지하도록
    /// 조정할 (id, 새 사각형) 목록을 계산한다. 창/Win32에 의존하지 않아 단위 검증 가능.
    /// </summary>
    public static List<(long id, Rectangle rect)> ComputeReflow(
        IReadOnlyList<(long id, Rectangle rect)> cells, long moverId,
        Rectangle old, Rectangle cur, Rectangle area, int gap,
        int tol = DefaultTol, int minSize = DefaultMinSize)
    {
        var result = new List<(long, Rectangle)>();
        int dL = cur.Left - old.Left, dR = cur.Right - old.Right;
        int dT = cur.Top - old.Top, dB = cur.Bottom - old.Bottom;
        if (dL == 0 && dR == 0 && dT == 0 && dB == 0) return result;

        var updates = new Dictionary<long, Rectangle>();
        int m = gap + tol; // 간격만큼 떨어진 반대편 창도 인접으로 인정

        Rectangle Cur(long id, Rectangle orig) => updates.TryGetValue(id, out var u) ? u : orig;

        if (dL != dR)
        {
            if (dL != 0 && Math.Abs(old.Left - area.Left) > tol)
                ShiftX(cells, moverId, old.Left, dL, m, updates, Cur);
            if (dR != 0 && Math.Abs(old.Right - area.Right) > tol)
                ShiftX(cells, moverId, old.Right, dR, m, updates, Cur);
        }
        if (dT != dB)
        {
            if (dT != 0 && Math.Abs(old.Top - area.Top) > tol)
                ShiftY(cells, moverId, old.Top, dT, m, updates, Cur);
            if (dB != 0 && Math.Abs(old.Bottom - area.Bottom) > tol)
                ShiftY(cells, moverId, old.Bottom, dB, m, updates, Cur);
        }

        foreach (var kv in updates)
            if (kv.Value.Width >= minSize && kv.Value.Height >= minSize)
                result.Add((kv.Key, kv.Value));
        return result;
    }

    private static void ShiftX(IReadOnlyList<(long id, Rectangle rect)> cells, long moverId,
        int coord, int delta, int m, Dictionary<long, Rectangle> updates, Func<long, Rectangle, Rectangle> cur)
    {
        foreach (var (id, orig) in cells)
        {
            if (id == moverId) continue;
            Rectangle r = cur(id, orig);
            int left = r.Left, right = r.Right;
            bool changed = false;
            if (Math.Abs(left - coord) <= m) { left += delta; changed = true; }
            if (Math.Abs(right - coord) <= m) { right += delta; changed = true; }
            if (changed) updates[id] = Rectangle.FromLTRB(left, r.Top, right, r.Bottom);
        }
    }

    private static void ShiftY(IReadOnlyList<(long id, Rectangle rect)> cells, long moverId,
        int coord, int delta, int m, Dictionary<long, Rectangle> updates, Func<long, Rectangle, Rectangle> cur)
    {
        foreach (var (id, orig) in cells)
        {
            if (id == moverId) continue;
            Rectangle r = cur(id, orig);
            int top = r.Top, bottom = r.Bottom;
            bool changed = false;
            if (Math.Abs(top - coord) <= m) { top += delta; changed = true; }
            if (Math.Abs(bottom - coord) <= m) { bottom += delta; changed = true; }
            if (changed) updates[id] = Rectangle.FromLTRB(r.Left, top, r.Right, bottom);
        }
    }

    public void Dispose()
    {
        SetEnabled(false);
        _cells.Clear();
    }
}
