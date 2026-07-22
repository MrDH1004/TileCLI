namespace TileCLI.Models;

/// <summary>
/// 방향 모드. 프리셋은 "칸 수"가 아니라 "방향"만 지정하며 칸 수는 대상 개수에 맞춰 자동 산출된다.
/// </summary>
public enum LayoutDirection
{
    /// <summary>가로 분할: 1행 N열 (창들이 좌우로 나란히)</summary>
    Horizontal,

    /// <summary>세로 분할: N행 1열 (창들이 위아래로 쌓임)</summary>
    Vertical,

    /// <summary>그리드: 균형 격자 (√N 기준, 마지막 행은 남은 개수로 스트레치)</summary>
    Grid
}
