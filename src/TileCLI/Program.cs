using System.Drawing;
using TileCLI.Models;
using TileCLI.Native;
using TileCLI.Services;
using TileCLI.UI;

namespace TileCLI;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
            return SelfTest.Run();

        bool uiTest = args.Any(a => string.Equals(a, "--uitest", StringComparison.OrdinalIgnoreCase));
        bool uiDump = args.Any(a => string.Equals(a, "--uidump", StringComparison.OrdinalIgnoreCase));

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (uiDump)
            return RunUiDump();

        // 전역 예외: 처리되지 않은 예외로 프로세스가 죽지 않도록 안내 후 계속(uitest 제외)
        if (!uiTest)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
                MessageBox.Show(e.Exception.Message, "TileCLI — 오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    MessageBox.Show(ex.Message, "TileCLI — 치명적 오류",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
        }

        if (uiTest)
            return RunUiSmokeTest();

        Application.Run(new MainForm());
        return 0;
    }

    /// <summary>MainForm 생성 + Load/Shown(모니터/목록/프로파일 갱신)을 실행하고 즉시 닫는 UI 스모크 테스트.</summary>
    private static int RunUiSmokeTest()
    {
        string result;
        int code;
        try
        {
            using var f = new MainForm();
            // Application.Exit는 CloseReason.ApplicationExitCall → 트레이 최소화 가드에 걸리지 않고 실제 종료
            f.Shown += (_, _) => Application.Exit(); // Load/Shown 처리 후 바로 종료
            Application.Run(f);
            result = "UITEST: OK";
            code = 0;
        }
        catch (Exception ex)
        {
            result = "UITEST: FAIL\n" + ex;
            code = 1;
        }

        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "uitest-result.txt"), result); } catch { }
        NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
        try { Console.WriteLine(result); } catch { }
        return code;
    }

    /// <summary>
    /// 헤드리스 UI 검증: MainForm을 만들어 Load/Shown까지 돌린 뒤 컨트롤 트리(그룹·버튼·라벨·체크박스)와
    /// 그룹 내 직속 컨트롤 겹침을 텍스트로 덤프한다. 스크린샷 없이 구조/문구/겹침을 확인하는 용도.
    /// </summary>
    private static int RunUiDump()
    {
        var sb = new System.Text.StringBuilder();
        int code = 0;
        try
        {
            using var f = new MainForm();
            f.Shown += (_, _) =>
            {
                try
                {
                    DumpControls(f, 0, sb);
                    DumpOverlaps(f, sb);
                    // 입력 다이얼로그 레이아웃도 헤드리스 덤프(모달 표시 없이 구성만)
                    sb.AppendLine("-- InputDialog (작업 세트 저장) --");
                    using var dlg = InputDialog.Build("작업 세트 저장", "작업 세트 이름:", "예시", out _);
                    DumpControls(dlg, 0, sb);
                }
                catch (Exception ex) { sb.AppendLine("DUMP ERROR: " + ex); }
                Application.Exit();
            };
            Application.Run(f);
        }
        catch (Exception ex) { sb.AppendLine("UIDUMP FAIL: " + ex); code = 1; }

        string outp = sb.ToString();
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "uidump.txt"), outp); } catch { }
        NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
        try { Console.WriteLine(outp); } catch { }
        return code;
    }

    private static void DumpControls(Control c, int depth, System.Text.StringBuilder sb)
    {
        string label = c switch
        {
            GroupBox g => $"GroupBox \"{g.Text}\"",
            Button b => $"Button text=\"{b.Text}\" icon={(b.Image != null)}",
            CheckBox cb => $"CheckBox \"{cb.Text}\" checked={cb.Checked}",
            Label l => $"Label \"{l.Text}\"",
            ComboBox => "ComboBox",
            NumericUpDown => "NumericUpDown",
            ListView lv => $"ListView cols={lv.Columns.Count}",
            _ => c.GetType().Name
        };
        sb.AppendLine(new string(' ', depth * 2) + label + $"  @({c.Left},{c.Top}) {c.Width}x{c.Height}");
        foreach (Control child in c.Controls) DumpControls(child, depth + 1, sb);
    }

    private static void DumpOverlaps(Control root, System.Text.StringBuilder sb)
    {
        sb.AppendLine("-- overlap check (group direct children) --");
        int found = 0;
        void Walk(Control c)
        {
            if (c is GroupBox gb)
            {
                var kids = c.Controls.Cast<Control>()
                    .Where(k => k is Button or CheckBox or ComboBox or NumericUpDown).ToList();
                for (int i = 0; i < kids.Count; i++)
                    for (int j = i + 1; j < kids.Count; j++)
                    {
                        var inter = Rectangle.Intersect(kids[i].Bounds, kids[j].Bounds);
                        if (inter.Width > 2 && inter.Height > 2)
                        {
                            sb.AppendLine($"  OVERLAP in \"{gb.Text}\": {kids[i].GetType().Name}@{kids[i].Bounds} X {kids[j].GetType().Name}@{kids[j].Bounds}");
                            found++;
                        }
                    }
            }
            foreach (Control ch in c.Controls) Walk(ch);
        }
        Walk(root);
        sb.AppendLine(found == 0 ? "  overlaps: none" : $"  overlaps: {found}");
    }
}

/// <summary>
/// GUI 없이 타일링 좌표 계산의 무겹침·꽉참을 자동 검증하고, 실제 모니터/터미널 탐지 결과를 출력한다.
/// 인수기준 ①(겹침 없는 정확한 타일링)을 헤드리스로 확인하는 용도.
/// </summary>
internal static class SelfTest
{
    private static readonly List<string> Lines = new();

    private static void Out(string s)
    {
        Lines.Add(s);
        try { Console.WriteLine(s); } catch { /* 콘솔 없음 무시 */ }
    }

    public static int Run()
    {
        // 창 좌표를 물리 픽셀로 얻도록 프로세스를 Per-Monitor V2 인식으로 설정(best-effort)
        try { NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch { /* 구형 OS 무시 */ }

        // 호출한 터미널에 출력 붙이기(콘솔 서브시스템이 아니어도 부모 콘솔로 씀)
        NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);

        Out("=== TileCLI self-test ===");

        // 1) 모니터 열거
        var monitors = MonitorService.Enumerate();
        Out($"[monitors] {monitors.Count} found");
        foreach (var m in monitors)
            Out($"  - {m}");

        // 2) 터미널 탐지 (+ 클로드/일반 분류: 요구 5)
        var terminals = WindowDiscovery.Discover();
        int claudeN = terminals.Count(x => x.Kind == Models.TerminalKind.Claude);
        int gptN = terminals.Count(x => x.Kind == Models.TerminalKind.Gpt);
        Out($"[terminals] {terminals.Count} found (claude {claudeN}, gpt {gptN})");
        foreach (var t in terminals)
            Out($"  - [{t.KindLabel}] {t}");

        // 2b) 최근 클로드 세션 (요구 6)
        var sessions = ClaudeSessionService.GetRecent(10);
        Out($"[claude-sessions] {sessions.Count} recent (top 10)");
        foreach (var s in sessions)
            Out($"  - {s.LastActive:yyyy-MM-dd HH:mm} | {s.ProjectName} | {s.Cwd} | {s.SessionId}");

        // 3) 타일링 좌표 검증 (합성 + 실제 모니터 작업영역)
        var testAreas = new List<Rectangle> { new(0, 0, 1920, 1040), new(3, 7, 2559, 1399) };
        if (monitors.Count > 0) testAreas.Add(monitors[0].WorkArea);

        int cases = 0, failures = 0;
        foreach (var area in testAreas)
        {
            foreach (LayoutDirection dir in Enum.GetValues<LayoutDirection>())
            {
                for (int count = 1; count <= 16; count++)
                {
                    cases++;
                    if (!VerifyTiling(count, dir, area, out string msg))
                    {
                        failures++;
                        Out($"  FAIL [{dir} n={count} area={area.Width}x{area.Height}] {msg}");
                    }
                }
            }
        }

        Out($"[tiling] {cases} cases, {failures} failures");

        // 4) 인접 창 연동(붙여서 조정) 재정렬 좌표 검증
        int rf = 0, rfFail = 0;
        foreach (var (name, ok) in ReflowCases())
        {
            rf++;
            if (!ok) { rfFail++; Out($"  FAIL [reflow] {name}"); }
        }
        Out($"[reflow] {rf} cases, {rfFail} failures");

        // 5) 작업 세트 저장/로드 왕복
        int wsFail = 0;
        try
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"tilecli_ws_{Environment.ProcessId}.json");
            try
            {
                var store = new WorkSetStore(tmp);
                var a = new WorkSet { Name = "A", Windows = { new WorkSetWindow { Title = "t1", X = 1, Y = 2, W = 3, H = 4, Cwd = "E:\\proj", SessionId = "sid1" } } };
                var b = new WorkSet { Name = "B" };
                store.Save(new[] { a, b });
                var loaded = store.Load();
                bool ok = loaded.Count == 2 && loaded[0].Name == "A" && loaded[0].Windows.Count == 1
                          && loaded[0].Windows[0].W == 3 && loaded[0].Windows[0].Cwd == "E:\\proj"
                          && loaded[0].Windows[0].SessionId == "sid1" && loaded[1].Name == "B";
                if (!ok) { wsFail++; Out("  FAIL [workset] round-trip mismatch"); }
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
        catch (Exception ex) { wsFail++; Out($"  FAIL [workset] {ex.Message}"); }
        Out($"[workset] round-trip {(wsFail == 0 ? "ok" : "FAIL")}");

        // 6) 감시자 훅 설치/해제
        int watchFail = 0;
        try
        {
            using var w = new WindowWatcher();
            w.SetEnabled(true);
            if (!w.Enabled) { watchFail++; Out("  FAIL [watcher] hook not installed"); }
            w.SetEnabled(false);
            if (w.Enabled) { watchFail++; Out("  FAIL [watcher] not disabled"); }
        }
        catch (Exception ex) { watchFail++; Out($"  FAIL [watcher] {ex.Message}"); }
        Out($"[watcher] hook {(watchFail == 0 ? "ok" : "FAIL")}");

        // 7) 작업 세트 배치 결정(WorkSetPlanner) — 헤드리스 검증
        int planFail = 0;
        void PChk(bool ok, string msg) { if (!ok) { planFail++; Out($"  FAIL [planner] {msg}"); } }
        {
            // A) 다 끄고 적용 → 세션 슬롯 전부 재실행
            var (a1, r1) = WorkSetPlanner.Plan(
                new List<(string, bool)> { ("A", true), ("B", true) },
                new List<(long, string)>());
            PChk(a1[0] == 0 && a1[1] == 0 && r1.SequenceEqual(new[] { 0, 1 }), "all-closed -> relaunch all session slots");

            // B) 다 열림(제목 일치) → 이동만, 재실행 없음(중복 방지)
            var (a2, r2) = WorkSetPlanner.Plan(
                new List<(string, bool)> { ("A", true), ("B", true) },
                new List<(long, string)> { (10, "A"), (20, "B") });
            PChk(a2[0] == 10 && a2[1] == 20 && r2.Count == 0, "all-open matched -> no relaunch");

            // C) 일부 닫힘 + 낯선 창 → 순서채움 후 남은 세션 슬롯만 재실행
            var (a3, r3) = WorkSetPlanner.Plan(
                new List<(string, bool)> { ("A", true), ("B", true), ("C", true) },
                new List<(long, string)> { (10, "A"), (20, "Xyz") });
            PChk(a3[0] == 10 && a3[1] == 20 && a3[2] == 0 && r3.SequenceEqual(new[] { 2 }), "partial: order-fill then relaunch deficit");

            // D) 세션 없는 슬롯은 닫혀도 재실행 안 함
            var (a4, r4) = WorkSetPlanner.Plan(
                new List<(string, bool)> { ("plain", false) },
                new List<(long, string)>());
            PChk(a4[0] == 0 && r4.Count == 0, "no-session slot -> not relaunched");

            // E) 재실행 창 매칭: 기준선에 없던 새 창을 순서대로 취함(개수 제한)
            var baseline = new HashSet<long> { 10, 20 };
            var taken = WorkSetPlanner.TakeNewWindows(baseline, new List<long> { 10, 20, 30, 40 }, 1);
            PChk(taken.SequenceEqual(new long[] { 30 }) && baseline.Contains(30), "take new windows (limit)");
        }
        Out($"[planner] decision {(planFail == 0 ? "ok" : "FAIL")}");

        // 8) GPT 세션 스캔(임시 디렉토리 + 가짜 rollout jsonl에서 cwd 재귀 탐색, Kind=Gpt)
        int gptFail = 0;
        try
        {
            string tmpRoot = Path.Combine(Path.GetTempPath(), $"tilecli_gpt_{Environment.ProcessId}");
            string cwd = AppContext.BaseDirectory.TrimEnd('\\', '/'); // 존재하는 폴더
            string sub = Path.Combine(tmpRoot, "2026", "07", "22");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "rollout-test.jsonl"),
                "{\"type\":\"session_meta\",\"payload\":{\"cwd\":\"" + cwd.Replace("\\", "\\\\") + "\"}}\n");
            try
            {
                var list = ClaudeSessionService.GetRecent(100, null, tmpRoot);
                bool found = list.Any(s => s.Kind == Models.TerminalKind.Gpt
                    && string.Equals(s.Cwd.TrimEnd('\\', '/'), cwd, StringComparison.OrdinalIgnoreCase));
                if (!found) { gptFail++; Out("  FAIL [gpt-session] rollout jsonl의 cwd를 스캔하지 못함"); }
            }
            finally { try { Directory.Delete(tmpRoot, true); } catch { } }
        }
        catch (Exception ex) { gptFail++; Out($"  FAIL [gpt-session] {ex.Message}"); }
        Out($"[gpt-session] scan {(gptFail == 0 ? "ok" : "FAIL")}");

        bool pass = failures == 0 && rfFail == 0 && wsFail == 0 && watchFail == 0 && planFail == 0 && gptFail == 0;
        Out(pass ? "RESULT: PASS" : "RESULT: FAIL");

        // 콘솔 캡처가 불확실하므로 결과를 파일로도 남김
        try
        {
            string outPath = Path.Combine(AppContext.BaseDirectory, "selftest-result.txt");
            File.WriteAllLines(outPath, Lines);
            var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "selftest-result.txt");
            if (!string.Equals(cwdPath, outPath, StringComparison.OrdinalIgnoreCase))
                File.WriteAllLines(cwdPath, Lines);
        }
        catch { /* 파일 기록 실패 무시 */ }

        return pass ? 0 : 1;
    }

    /// <summary>인접 창 연동 재정렬(ComputeReflow)의 대표 시나리오 검증 케이스.</summary>
    private static IEnumerable<(string name, bool ok)> ReflowCases()
    {
        // 2열 분할, 간격 0: A 우측 경계 +100 → B 좌측이 +100 따라옴
        {
            var cells = new List<(long, Rectangle)>
            { (1, Rectangle.FromLTRB(0, 0, 500, 400)), (2, Rectangle.FromLTRB(500, 0, 1000, 400)) };
            var u = TileGroupTracker.ComputeReflow(cells, 1,
                Rectangle.FromLTRB(0, 0, 500, 400), Rectangle.FromLTRB(0, 0, 600, 400),
                new Rectangle(0, 0, 1000, 400), 0);
            bool ok = u.Count == 1 && u[0].id == 2 && u[0].rect == Rectangle.FromLTRB(600, 0, 1000, 400);
            yield return ("2col: B follows A.right", ok);
        }
        // 2x2, 간격 0: A 우측 +100 → 세로 경계선 전체 이동(B.left, C.right, D.left)
        {
            var cells = new List<(long, Rectangle)>
            {
                (1, Rectangle.FromLTRB(0, 0, 500, 400)), (2, Rectangle.FromLTRB(500, 0, 1000, 400)),
                (3, Rectangle.FromLTRB(0, 400, 500, 800)), (4, Rectangle.FromLTRB(500, 400, 1000, 800))
            };
            var u = TileGroupTracker.ComputeReflow(cells, 1,
                Rectangle.FromLTRB(0, 0, 500, 400), Rectangle.FromLTRB(0, 0, 600, 400),
                new Rectangle(0, 0, 1000, 800), 0);
            var map = u.ToDictionary(x => x.id, x => x.rect);
            bool ok = u.Count == 3
                && map.TryGetValue(2, out var b) && b == Rectangle.FromLTRB(600, 0, 1000, 400)
                && map.TryGetValue(3, out var c) && c == Rectangle.FromLTRB(0, 400, 600, 800)
                && map.TryGetValue(4, out var d) && d == Rectangle.FromLTRB(600, 400, 1000, 800);
            yield return ("2x2: whole column line moves", ok);
        }
        // 간격 20 유지: A 우측 +100 → B 좌측 +100(간격 20 보존)
        {
            var cells = new List<(long, Rectangle)>
            { (1, Rectangle.FromLTRB(0, 0, 490, 400)), (2, Rectangle.FromLTRB(510, 0, 1000, 400)) };
            var u = TileGroupTracker.ComputeReflow(cells, 1,
                Rectangle.FromLTRB(0, 0, 490, 400), Rectangle.FromLTRB(0, 0, 590, 400),
                new Rectangle(0, 0, 1000, 400), 20);
            bool ok = u.Count == 1 && u[0].id == 2 && u[0].rect == Rectangle.FromLTRB(610, 0, 1000, 400);
            yield return ("2col gap20: gap preserved", ok);
        }
        // 단순 이동(리사이즈 아님): 조정 없음
        {
            var cells = new List<(long, Rectangle)>
            { (1, Rectangle.FromLTRB(0, 0, 500, 400)), (2, Rectangle.FromLTRB(500, 0, 1000, 400)) };
            var u = TileGroupTracker.ComputeReflow(cells, 1,
                Rectangle.FromLTRB(0, 0, 500, 400), Rectangle.FromLTRB(50, 0, 550, 400),
                new Rectangle(0, 0, 1000, 400), 0);
            yield return ("pure move: no reflow", u.Count == 0);
        }
        // 바깥(컨테이너) 경계 이동은 인접 조정 대상 아님
        {
            var cells = new List<(long, Rectangle)>
            { (1, Rectangle.FromLTRB(0, 0, 500, 400)), (2, Rectangle.FromLTRB(500, 0, 1000, 400)) };
            var u = TileGroupTracker.ComputeReflow(cells, 1,
                Rectangle.FromLTRB(0, 0, 500, 400), Rectangle.FromLTRB(50, 0, 500, 400),
                new Rectangle(0, 0, 1000, 400), 0);
            yield return ("outer edge: no neighbor shift", u.Count == 0);
        }
    }

    /// <summary>
    /// 셀들이 (a) 개수 일치 (b) 모두 양수 크기이며 영역 내부 (c) 상호 무겹침 (d) 합집합 면적==영역 면적 인지 검증.
    /// (b)+(c)+(d)는 gap=0에서 "영역을 빈틈·겹침 없이 정확히 분할"함을 의미한다.
    /// </summary>
    private static bool VerifyTiling(int count, LayoutDirection dir, Rectangle area, out string msg)
    {
        var cells = TilingEngine.ComputeCells(count, dir, area, 0);
        if (cells.Count != count)
        {
            msg = $"cell count {cells.Count} != {count}";
            return false;
        }

        long sum = 0;
        foreach (var c in cells)
        {
            if (c.Width <= 0 || c.Height <= 0)
            {
                msg = $"non-positive cell {c}";
                return false;
            }
            if (!area.Contains(c))
            {
                msg = $"cell out of area {c}";
                return false;
            }
            sum += (long)c.Width * c.Height;
        }

        for (int i = 0; i < cells.Count; i++)
        {
            for (int j = i + 1; j < cells.Count; j++)
            {
                var inter = Rectangle.Intersect(cells[i], cells[j]);
                if (inter.Width > 0 && inter.Height > 0)
                {
                    msg = $"overlap between {cells[i]} and {cells[j]}";
                    return false;
                }
            }
        }

        long areaSize = (long)area.Width * area.Height;
        if (sum != areaSize)
        {
            msg = $"coverage sum {sum} != area {areaSize}";
            return false;
        }

        msg = "ok";
        return true;
    }
}
