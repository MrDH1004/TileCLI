namespace TileCLI.Models;

/// <summary>
/// 탐지된 하나의 터미널 창. 목록/체크박스 대상이 되는 최상위 도메인 엔티티.
/// </summary>
public sealed class TerminalWindow
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public uint ProcessId { get; init; }
    public bool IsMinimized { get; init; }

    /// <summary>프로세스 트리 기반 분류(클로드 실행 창 / 일반·서버 창).</summary>
    public TerminalKind Kind { get; init; } = TerminalKind.Unknown;

    /// <summary>목록 "종류" 열 라벨.</summary>
    public string KindLabel => Kind switch
    {
        TerminalKind.Claude => "Claude",
        TerminalKind.Gpt => "GPT",
        TerminalKind.Plain => "서버/일반",
        _ => "-"
    };

    /// <summary>목록 표시용 라벨.</summary>
    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(Title) ? $"(제목 없음) [{ProcessName}]" : Title;

    public override string ToString() =>
        $"0x{Handle.ToInt64():X} | {ProcessName} | {ClassName} | {DisplayTitle}";
}
