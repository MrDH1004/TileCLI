using System.Runtime.InteropServices;
using TileCLI.Native;

namespace TileCLI.Services;

/// <summary>
/// Toolhelp32 스냅샷으로 전체 프로세스의 (pid → 자식들, pid → 실행파일명) 맵을 1회 만들고,
/// 특정 창 프로세스의 자기 자신+자손 트리에 claude 실행 파일이 있는지로
/// "클로드가 실행된 창"을 판별한다(요구 5). 부모 PID 재사용 가능성 때문에 best-effort.
/// </summary>
public sealed class ProcessTreeInspector
{
    private readonly Dictionary<uint, List<uint>> _children = new();
    private readonly Dictionary<uint, string> _names = new(); // pid → 실행파일명(소문자)

    private ProcessTreeInspector() { }

    /// <summary>현재 프로세스 목록을 1회 스냅샷. 실패해도 빈 인스펙터를 반환(예외 없음).</summary>
    public static ProcessTreeInspector Capture()
    {
        var inst = new ProcessTreeInspector();
        IntPtr snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snap == NativeMethods.INVALID_HANDLE_VALUE) return inst;

        try
        {
            var pe = new NativeMethods.PROCESSENTRY32
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>()
            };
            if (!NativeMethods.Process32First(snap, ref pe)) return inst;

            do
            {
                uint pid = pe.th32ProcessID;
                uint parent = pe.th32ParentProcessID;
                string name = (pe.szExeFile ?? string.Empty).ToLowerInvariant();

                _ = inst._names.TryAdd(pid, name);
                if (!inst._children.TryGetValue(parent, out var lst))
                    inst._children[parent] = lst = new List<uint>();
                lst.Add(pid);
            }
            while (NativeMethods.Process32Next(snap, ref pe));
        }
        catch
        {
            // 스냅샷 열거 실패는 무시(빈/부분 맵)
        }
        finally
        {
            NativeMethods.CloseHandle(snap);
        }

        return inst;
    }

    /// <summary>rootPid의 자기 자신+자손 트리에 이름 조건을 만족하는 프로세스가 있으면 true.</summary>
    public bool SubtreeContains(uint rootPid, Func<string, bool> nameMatch)
    {
        var seen = new HashSet<uint> { rootPid };
        var queue = new Queue<uint>();
        queue.Enqueue(rootPid);

        while (queue.Count > 0)
        {
            uint pid = queue.Dequeue();
            if (_names.TryGetValue(pid, out var nm) && nameMatch(nm)) return true;
            if (_children.TryGetValue(pid, out var kids))
                foreach (var k in kids)
                    if (seen.Add(k)) queue.Enqueue(k); // 중복/순환 방지
        }
        return false;
    }

    /// <summary>트리에 claude 실행 파일이 있으면 true.</summary>
    public bool ContainsClaude(uint rootPid) =>
        SubtreeContains(rootPid, IsClaudeExe);

    private static bool IsClaudeExe(string exe) =>
        exe is "claude.exe" or "claude" || exe.StartsWith("claude", StringComparison.Ordinal);

    /// <summary>트리에 names 중 하나와 일치하는 실행 파일이 있으면 true(GPT CLI 등 임의 도구용).</summary>
    public bool ContainsAny(uint rootPid, IReadOnlyCollection<string> names)
    {
        if (names is null || names.Count == 0) return false;
        return SubtreeContains(rootPid, exe => MatchesAny(exe, names));
    }

    /// <summary>exe(소문자, 보통 "codex.exe")가 names(예 "codex")와 일치/시작하는가.</summary>
    private static bool MatchesAny(string exe, IReadOnlyCollection<string> names)
    {
        string bare = exe.EndsWith(".exe", StringComparison.Ordinal) ? exe[..^4] : exe;
        foreach (var n in names)
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (bare.Equals(n, StringComparison.OrdinalIgnoreCase) ||
                bare.StartsWith(n, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
