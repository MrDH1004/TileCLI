using System.Drawing;

namespace TileCLI.Models;

/// <summary>
/// 하나의 모니터. 좌표는 모두 물리 픽셀(가상 데스크톱 좌표계).
/// </summary>
public sealed class MonitorTarget
{
    public int Index { get; init; }

    /// <summary>모니터 전체 영역(물리 픽셀).</summary>
    public Rectangle Bounds { get; init; }

    /// <summary>작업 영역 = 작업표시줄 제외(물리 픽셀). 타일링은 여기에 배치.</summary>
    public Rectangle WorkArea { get; init; }

    public bool IsPrimary { get; init; }
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>
    /// Windows 디스플레이 설정의 식별 번호(\\.\DISPLAYN의 N). 설정 화면 "식별" 번호와 일치.
    /// 파싱 실패 시 Index+1로 폴백.
    /// </summary>
    public int DisplayNumber
    {
        get
        {
            string name = DeviceName ?? string.Empty;
            int end = name.Length, i = end - 1;
            while (i >= 0 && char.IsDigit(name[i])) i--;
            if (i + 1 < end && int.TryParse(name.AsSpan(i + 1), out int n)) return n;
            return Index + 1;
        }
    }

    public string DisplayName =>
        $"모니터 {DisplayNumber}{(IsPrimary ? " (주)" : "")} — {Bounds.Width}x{Bounds.Height} @({Bounds.X},{Bounds.Y})";

    public override string ToString() =>
        $"{DisplayName} work={WorkArea.Width}x{WorkArea.Height}@({WorkArea.X},{WorkArea.Y})";
}
