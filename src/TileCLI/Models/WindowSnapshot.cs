using TileCLI.Native;

namespace TileCLI.Models;

/// <summary>
/// 정렬 직전 창 하나의 배치 상태(WINDOWPLACEMENT). 복원(정렬 취소)에 사용.
/// </summary>
internal sealed class WindowSnapshot
{
    public IntPtr Handle { get; init; }
    public NativeMethods.WINDOWPLACEMENT Placement { get; init; }
}
