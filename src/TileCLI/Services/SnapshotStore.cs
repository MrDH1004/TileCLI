using System.Runtime.InteropServices;
using TileCLI.Native;

namespace TileCLI.Services;

/// <summary>
/// 정렬 직전 창 배치(WINDOWPLACEMENT)를 기억했다가 복원한다("정렬 취소").
/// Capture는 마지막 정렬 직전 상태로 덮어쓴다.
/// </summary>
public sealed class SnapshotStore
{
    private readonly Dictionary<IntPtr, NativeMethods.WINDOWPLACEMENT> _snapshots = new();

    public bool HasSnapshot => _snapshots.Count > 0;

    /// <summary>지정 창들의 현재 배치를 스냅샷(이전 스냅샷은 폐기).</summary>
    public void Capture(IReadOnlyList<IntPtr> windows)
    {
        _snapshots.Clear();
        foreach (var hWnd in windows)
        {
            if (!NativeMethods.IsWindow(hWnd)) continue;
            var wp = new NativeMethods.WINDOWPLACEMENT
            {
                length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>()
            };
            if (NativeMethods.GetWindowPlacement(hWnd, ref wp))
                _snapshots[hWnd] = wp;
        }
    }

    /// <summary>스냅샷된 배치로 되돌린다. 반환값은 복원된 창 개수.</summary>
    public int Restore()
    {
        int restored = 0;
        foreach (var kv in _snapshots)
        {
            if (!NativeMethods.IsWindow(kv.Key)) continue;
            var wp = kv.Value;
            if (NativeMethods.SetWindowPlacement(kv.Key, ref wp))
            {
                NativeMethods.SetSquareCorners(kv.Key, false); // 정렬 취소 → 둥근 모서리 복원
                restored++;
            }
        }
        return restored;
    }

    public void Clear() => _snapshots.Clear();
}
