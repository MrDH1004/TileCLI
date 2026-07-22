namespace TileCLI.Models;

/// <summary>
/// 터미널 창 분류. 프로세스 트리에 claude 실행 파일이 있으면 <see cref="Claude"/>,
/// 그 외 콘솔/서버 창은 <see cref="Plain"/>. 판별 불가/미조사는 <see cref="Unknown"/>.
/// </summary>
public enum TerminalKind
{
    /// <summary>판별하지 못함(프로세스 접근 불가 등).</summary>
    Unknown,

    /// <summary>클로드(claude CLI)가 실행 중인 창.</summary>
    Claude,

    /// <summary>GPT CLI(OpenAI Codex 등)가 실행 중인 창.</summary>
    Gpt,

    /// <summary>클로드/GPT가 아닌 일반/서버 콘솔 창.</summary>
    Plain
}
