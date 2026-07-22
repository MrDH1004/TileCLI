using System.Drawing;
using TileCLI.Models;
using TileCLI.Native;

namespace TileCLI.Services;

/// <summary>
/// 연결된 모니터를 열거해 <see cref="MonitorTarget"/> 목록으로 반환한다(물리 픽셀).
/// </summary>
public static class MonitorService
{
    public static List<MonitorTarget> Enumerate()
    {
        var result = new List<MonitorTarget>();

        // 델리게이트를 지역 변수로 잡아 GC 수집 방지(열거는 동기)
        NativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rc, IntPtr data) =>
        {
            var mi = new NativeMethods.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>()
            };

            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                result.Add(new MonitorTarget
                {
                    Index = result.Count,
                    Bounds = ToRect(mi.rcMonitor),
                    WorkArea = ToRect(mi.rcWork),
                    IsPrimary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                    DeviceName = mi.szDevice ?? string.Empty
                });
            }
            return true; // 계속 열거
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        // 주 모니터를 앞으로(인덱스 재부여로 UI 일관성)
        result.Sort((a, b) =>
        {
            if (a.IsPrimary != b.IsPrimary) return a.IsPrimary ? -1 : 1;
            int c = a.Bounds.X.CompareTo(b.Bounds.X);
            return c != 0 ? c : a.Bounds.Y.CompareTo(b.Bounds.Y);
        });

        for (int i = 0; i < result.Count; i++)
        {
            var m = result[i];
            result[i] = new MonitorTarget
            {
                Index = i,
                Bounds = m.Bounds,
                WorkArea = m.WorkArea,
                IsPrimary = m.IsPrimary,
                DeviceName = m.DeviceName
            };
        }

        return result;
    }

    private static Rectangle ToRect(NativeMethods.RECT r) =>
        new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
