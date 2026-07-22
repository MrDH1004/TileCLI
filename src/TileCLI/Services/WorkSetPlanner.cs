namespace TileCLI.Services;

/// <summary>
/// 작업 세트 "적용"의 배치 결정 로직(순수 함수). GUI/Win32 없이 헤드리스로 검증 가능.
/// 규칙: (1) 슬롯을 제목(정확→부분)으로 열린 창에 매칭, (2) 남은 슬롯은 남은 열린 창을 순서대로 채움
/// (열린 창이 있으면 재실행 대신 그 창 사용 → 중복 실행 방지), (3) 그래도 빈 슬롯 중 세션이 연결된 것만
/// claude -c 재실행 대상. "다 끄고 적용" = 열린 창 0 → 세션 슬롯 전부 재실행.
/// </summary>
public static class WorkSetPlanner
{
    /// <summary>
    /// slots[i] = (제목, 세션연결여부). open[j] = (창 id, 제목). 반환: 슬롯별 배정 id(0=미배정)와
    /// 재실행해야 하는 슬롯 인덱스 목록(미배정 + 세션 있음).
    /// </summary>
    public static (long[] assign, List<int> relaunch) Plan(
        IReadOnlyList<(string title, bool hasSession)> slots,
        IReadOnlyList<(long id, string title)> open)
    {
        var assign = new long[slots.Count];
        var used = new HashSet<long>();

        // 1) 제목 매칭(정확 → 부분)
        for (int i = 0; i < slots.Count; i++)
        {
            long m = MatchByTitle(open, used, slots[i].title);
            if (m != 0) { assign[i] = m; used.Add(m); }
        }
        // 2) 남은 슬롯 ← 남은 열린 창을 순서대로
        var leftovers = new List<long>();
        foreach (var o in open) if (!used.Contains(o.id)) leftovers.Add(o.id);
        int li = 0;
        for (int i = 0; i < slots.Count && li < leftovers.Count; i++)
            if (assign[i] == 0) { assign[i] = leftovers[li++]; used.Add(assign[i]); }

        // 3) 남은 빈 슬롯 중 세션 있는 것 → 재실행
        var relaunch = new List<int>();
        for (int i = 0; i < slots.Count; i++)
            if (assign[i] == 0 && slots[i].hasSession) relaunch.Add(i);

        return (assign, relaunch);
    }

    private static long MatchByTitle(IReadOnlyList<(long id, string title)> open, HashSet<long> used, string? title)
    {
        title = (title ?? string.Empty).Trim();
        foreach (var o in open)
            if (!used.Contains(o.id) && string.Equals((o.title ?? "").Trim(), title, StringComparison.Ordinal))
                return o.id;
        if (title.Length == 0) return 0;
        foreach (var o in open)
        {
            if (used.Contains(o.id)) continue;
            string ot = (o.title ?? string.Empty).Trim();
            if (ot.Contains(title, StringComparison.OrdinalIgnoreCase) ||
                (ot.Length > 0 && title.Contains(ot, StringComparison.OrdinalIgnoreCase)))
                return o.id;
        }
        return 0;
    }

    /// <summary>
    /// 재실행 후 폴링: 현재 터미널 중 기준선(baseline)에 없던 "새 창"을 순서대로 최대 pendingCount개까지
    /// 취해 (배치 대상) 반환한다. 취한 창은 baseline에 추가한다(다음 폴에서 재사용 방지). 반환=새로 배치할 id들.
    /// </summary>
    public static List<long> TakeNewWindows(ISet<long> baseline, IReadOnlyList<long> current, int pendingCount)
    {
        var taken = new List<long>();
        foreach (var h in current)
        {
            if (taken.Count >= pendingCount) break;
            if (baseline.Contains(h)) continue;
            baseline.Add(h);
            taken.Add(h);
        }
        return taken;
    }
}
