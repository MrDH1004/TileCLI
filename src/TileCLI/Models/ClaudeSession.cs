namespace TileCLI.Models;

/// <summary>
/// 최근 목록에 표시하는 클로드 세션 하나(프로젝트 폴더 단위, 가장 최근 세션).
/// ~/.claude/projects/&lt;encoded&gt;/&lt;sessionId&gt;.jsonl 에서 추출한다.
/// </summary>
public sealed class ClaudeSession
{
    /// <summary>세션 파일명(GUID). 표시/식별용.</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>실제 작업 폴더(jsonl 내부 cwd, 없으면 폴더명 디코딩).</summary>
    public string Cwd { get; init; } = string.Empty;

    /// <summary>세션 요약 제목(있으면). 목록 라벨 보조.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>마지막 활동 시각(세션 파일 수정 시각 기준).</summary>
    public DateTime LastActive { get; init; }

    /// <summary>세션 transcript 파일 경로.</summary>
    public string TranscriptPath { get; init; } = string.Empty;

    /// <summary>폴더 이름(cwd의 마지막 구간). 라벨용.</summary>
    public string ProjectName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Cwd)) return "(알 수 없음)";
            string trimmed = Cwd.TrimEnd('\\', '/');
            int idx = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            return idx >= 0 && idx < trimmed.Length - 1 ? trimmed[(idx + 1)..] : trimmed;
        }
    }

    /// <summary>목록 첫 열 라벨(제목 우선, 없으면 폴더명).</summary>
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Title) ? ProjectName : Title;
}
