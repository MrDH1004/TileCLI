using System.Reflection;

namespace TileCLI.Services;

/// <summary>실행 파일에 임베드된 앱 버전(csproj &lt;Version&gt;). 타이틀 표시·자동 업데이트 비교에 사용.</summary>
public static class AppVersion
{
    /// <summary>현재 버전(Major.Minor.Build, 3자리 정규화).</summary>
    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>표시용 문자열(예: "1.0.0").</summary>
    public static string Display => $"{Current.Major}.{Current.Minor}.{Current.Build}";
}
