namespace TileCLI.Models;

/// <summary>
/// 배치 스타일 프로파일. 방향 + 모니터 스코프 + 간격만 저장(대상 창은 저장하지 않음).
/// 불러오면 현재 체크된 창들에 이 스타일을 적용한다. JSON 직렬화 대상이라 mutable 프로퍼티 사용.
/// </summary>
public sealed class LayoutProfile
{
    public string Name { get; set; } = string.Empty;

    public LayoutDirection Direction { get; set; } = LayoutDirection.Grid;

    /// <summary>선택된 모니터 인덱스 목록. 비어 있으면 주 모니터.</summary>
    public List<int> MonitorIndices { get; set; } = new();

    /// <summary>true면 선택된 모니터들을 하나의 영역으로 걸쳐 배치. false면 첫 선택 모니터에만.</summary>
    public bool SpanMonitors { get; set; }

    /// <summary>타일 간격(px). 기본 0(꽉 붙임).</summary>
    public int Gap { get; set; }

    public override string ToString() => Name;
}
