using TileCLI.Native;

namespace TileCLI.Services;

/// <summary>
/// 일괄 창 상태 제어: 전체 최소화 / 전체 다시 보이기 / 정렬 직전 복원.
/// (문자 그대로의 "전체 최대화"는 타일링과 충돌하므로 제공하지 않음 — 스펙 Round 10 결정)
/// </summary>
public static class BulkController
{
    /// <summary>대상 전체를 최소화. 반환값은 처리된 창 개수.</summary>
    public static int MinimizeAll(IReadOnlyList<IntPtr> windows)
    {
        int n = 0;
        foreach (var hWnd in windows)
        {
            if (!NativeMethods.IsWindow(hWnd)) continue;
            if (NativeMethods.ShowWindow(hWnd, NativeMethods.SW_MINIMIZE)) n++;
        }
        return n;
    }

    /// <summary>최소화됐던 대상들을 다시 화면에 표시(위치는 그대로). 반환값은 처리된 창 개수.</summary>
    public static int ShowAll(IReadOnlyList<IntPtr> windows)
    {
        int n = 0;
        foreach (var hWnd in windows)
        {
            if (!NativeMethods.IsWindow(hWnd)) continue;
            if (NativeMethods.IsIconic(hWnd))
            {
                if (NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE)) n++;
            }
            else
            {
                // 이미 보이면 표시 상태만 보장
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOW);
                n++;
            }
        }
        return n;
    }

    /// <summary>정렬 직전 상태로 복원.</summary>
    public static int RestorePrevious(SnapshotStore snapshots) => snapshots.Restore();
}
