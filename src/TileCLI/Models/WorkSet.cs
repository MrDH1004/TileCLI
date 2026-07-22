namespace TileCLI.Models;

/// <summary>
/// 작업 세트: 저장 시점에 열려 있던 터미널 창들의 위치/크기를 통째로 저장했다가 원클릭으로 복원한다.
/// 창 매칭은 제목(정확→부분) 우선, 미매칭 슬롯은 남은 창을 순서대로 채운다.
/// JSON 직렬화 대상이라 mutable 프로퍼티 사용.
/// </summary>
public sealed class WorkSet
{
    public string Name { get; set; } = string.Empty;
    public List<WorkSetWindow> Windows { get; set; } = new();
    public override string ToString() => Name;
}

/// <summary>작업 세트에 저장되는 창 한 개의 슬롯(보이는 사각형 + 매칭용 제목).</summary>
public sealed class WorkSetWindow
{
    public string Title { get; set; } = string.Empty; // 매칭 키(정확/부분)
    public string Kind { get; set; } = string.Empty;  // 종류 라벨(참고용)
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public string Monitor { get; set; } = string.Empty; // 모니터 DeviceName(참고용)

    // 세션 연결(클로드 창만). 적용 시 창이 닫혀 있으면 이 세션을 claude -c로 재실행해 되살린다.
    public string Cwd { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
}
