namespace TileCLI.Models;

/// <summary>
/// 전역 단축키로 트리거할 수 있는 정렬 동작. 요구 핵심은 <see cref="Auto"/>(자동 배치)이며
/// 편의상 방향별 정렬도 각각 바인딩할 수 있다.
/// </summary>
public enum HotkeyAction
{
    /// <summary>자동 배치: 대상 개수/영역 비율에 맞춰 방향을 자동 선택.</summary>
    Auto,

    /// <summary>가로 분할.</summary>
    Horizontal,

    /// <summary>세로 분할.</summary>
    Vertical,

    /// <summary>그리드.</summary>
    Grid
}
