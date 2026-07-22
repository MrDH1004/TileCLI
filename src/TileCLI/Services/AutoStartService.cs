using Microsoft.Win32;

namespace TileCLI.Services;

/// <summary>
/// Windows 시작 시 자동 실행(HKCU\Software\Microsoft\Windows\CurrentVersion\Run) 등록/해제.
/// 현재 사용자 키라 관리자 권한 불필요. exe 경로가 바뀌어도 등록이 살아 있도록,
/// 켜져 있으면 현재 exe 경로로 값을 갱신한다.
/// </summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TileCLI";

    /// <summary>현재 실행 중인 exe의 절대 경로(단일 파일 배포도 실제 exe 경로 반환).</summary>
    public static string CurrentExePath()
    {
        try
        {
            string? p = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(p)) return p!;
            return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    /// <summary>Run에 기록할 명령(경로에 공백이 있어도 안전하도록 따옴표로 감쌈).</summary>
    private static string QuotedCommand() => $"\"{CurrentExePath()}\"";

    /// <summary>현재 등록된 Run 명령 문자열(없으면 null).</summary>
    public static string? RegisteredCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) as string;
        }
        catch { return null; }
    }

    public static bool IsEnabled() => !string.IsNullOrEmpty(RegisteredCommand());

    /// <summary>현재 exe 경로로 자동 실행 등록(켜기).</summary>
    public static bool Enable(out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(CurrentExePath())) { error = "실행 파일 경로를 확인할 수 없습니다."; return false; }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key is null) { error = "레지스트리 Run 키를 열 수 없습니다."; return false; }
            key.SetValue(ValueName, QuotedCommand(), RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>자동 실행 등록 해제(끄기). 값이 없어도 성공으로 본다.</summary>
    public static bool Disable(out string error)
    {
        error = string.Empty;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(ValueName, false);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>
    /// 켜져 있는데 등록 경로가 현재 exe와 다르면 현재 경로로 덮어쓴다(경로 변경 대응).
    /// 반환값: 갱신했으면 true(경로 변경 감지), 이미 최신이거나 꺼져 있으면 false.
    /// </summary>
    public static bool RefreshIfEnabled()
    {
        var reg = RegisteredCommand();
        if (string.IsNullOrEmpty(reg)) return false;                 // 꺼져 있음
        string want = QuotedCommand();
        if (string.Equals(reg.Trim(), want.Trim(), StringComparison.OrdinalIgnoreCase)) return false; // 이미 최신
        return Enable(out _);                                         // 현재 경로로 갱신
    }
}
