using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using TileCLI.Models;
using TileCLI.Native;
using TileCLI.Services;

namespace TileCLI.UI;

/// <summary>
/// 관리 창: 터미널 목록(체크박스·종류) + 모니터 선택 + 자동/방향/일괄/프로파일 조작 +
/// 전역 단축키 + 트레이 최소화 + 최근 클로드 세션 복구.
/// </summary>
public sealed class MainForm : Form
{
    private readonly SnapshotStore _snapshots = new();
    private readonly ProfileStore _profileStore = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly ConfigEnv _config = new();
    private AppSettings _settings;
    private bool _applyingConfig; // config 적용 중 변경 이벤트로 되저장 방지
    private bool _uiReady;        // 초기 로드 완료 후에만 저장
    private List<TerminalWindow> _terminals = new();
    private List<IntPtr> _windowOrder = new(); // 드래그로 정한 사용자 순서(핸들). 비면 탐지(Z)순서.

    // 목록 드래그 재정렬(게임 인벤토리 스타일): 집은 행이 마우스에 붙고, 대상 슬롯이 실시간 표시되며, 놓으면 교체
    private ListViewItem? _dragItem;   // 눌러서 집은 행(드래그 시작 전 후보 포함)
    private Point _dragDownPos;        // 드래그 시작 판정용 누른 좌표
    private bool _dragActive;          // 임계 거리를 넘어 실제 드래그 중
    private int _dragTargetIndex = -1; // 실시간 교체 대상 행(-1=없음)
    private bool _dragToEnd;           // 마지막 행 아래 빈 영역(맨 끝 슬롯) 드롭 여부
    private DragGhost? _ghost;
    private System.Windows.Forms.Timer? _dragScrollTimer; // 가장자리 자동 스크롤
    private List<string> _gptNames = new();    // GPT CLI로 인식할 프로세스명(config.env GptProcessNames)
    private List<string> _claudeTitleMarkers = new(); // 클로드 창 제목 접두 마커
    private List<string> _gptTitleMarkers = new();    // GPT 창 제목 접두 마커
    private List<MonitorTarget> _monitors = new();
    private List<LayoutProfile> _profiles = new();
    private LayoutDirection _currentDirection = LayoutDirection.Grid;
    private bool _monitorsInitialized;

    private HotkeyManager? _hotkeys;
    private NotifyIcon? _tray;
    private readonly TileGroupTracker _groupTracker = new();
    private bool _reallyExit;
    private bool _splitterSet;

    private ListView _listView = null!;
    private ListView _sessionView = null!;
    private SplitContainer _split = null!;
    private Label _lblCount = null!;
    private MonitorMapControl _monitorMap = null!;
    private CheckBox _chkSpan = null!;
    private CheckBox _chkLink = null!;   // 인접 창 연동(정렬 그룹에 노출 — 옵션 창에서 이동)
    private Label _statusLabel = null!;

    // 옵션 창으로 이동된 상태(트레이 최소화·인접 연동·선택 프로파일)
    private bool _minimizeToTray = true;
    private bool _linkAdjacent = true;
    private string _selectedProfile = "";
    private OptionsForm? _optionsForm;
    private readonly ToolTip _toolTip = new();
    private readonly List<(Button btn, IconKind kind)> _iconButtons = new();
    private Button _btnToggleAll = null!;

    // 작업 세트(창 배치 스냅샷 저장/복원) + 감시 모드(새 창 자동 배치)
    private readonly WorkSetStore _workSetStore = new();
    private List<WorkSet> _workSets = new();
    private ComboBox _cboWorkSets = null!;
    private CheckBox _chkWatch = null!;
    private readonly WindowWatcher _watcher = new();
    private bool _watchMode;
    private int _suppressWatchUntil;                 // 이 tick 전까지 감시 재배치 억제(우리 이동 루프 방지)

    // 작업 세트 적용 시 닫힌 세션 재실행 → 새 창이 뜨는 대로 저장 위치에 배치(비동기 폴링)
    private System.Windows.Forms.Timer? _restoreTimer;
    private List<WorkSetWindow>? _restorePending;    // 아직 배치 안 된 재실행 슬롯(순서)
    private HashSet<long>? _restoreBaseline;          // 재실행 직전 존재하던 터미널 핸들(id)
    private int _restoreDeadline;
    private int _restorePlacedBase;                  // 재실행 전 이미 이동한 창 수
    private int _restorePendingInit;                 // 재실행 대상 초기 개수
    private string _restoreName = "";

    public MainForm()
    {
        _settings = _settingsStore.Load();
        _config.Load();
        _gptNames = _config.GetList("GptProcessNames");
        if (_gptNames.Count == 0) _gptNames = new List<string>(WindowDiscovery.DefaultGptProcesses);
        _claudeTitleMarkers = _config.GetList("ClaudeTitleMarkers");
        if (_claudeTitleMarkers.Count == 0) _claudeTitleMarkers = new List<string>(WindowDiscovery.DefaultClaudeTitleMarkers);
        _gptTitleMarkers = _config.GetList("GptTitleMarkers");
        if (_gptTitleMarkers.Count == 0) _gptTitleMarkers = new List<string>(WindowDiscovery.DefaultGptTitleMarkers);
        if (string.Equals(_config.GetString("Theme"), "Light", StringComparison.OrdinalIgnoreCase))
            Theme.SetMode(ThemeMode.Light);

        Text = "TileCLI";
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(880, 640);
        Size = new Size(980, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = TryGetAppIcon();

        SetupToolTip();
        BuildUi();
        SetupTray();
        Theme.Apply(this);
        _listView.DrawSubItem += DrawDragOverlay; // Theme 그리기 뒤에 실행되도록 여기서 구독(드래그 하이라이트 오버레이)
        FormClosed += (_, _) => { _ghost?.Dispose(); _dragScrollTimer?.Dispose(); };
        ApplyPostThemeTweaks();
        RestyleIcons();

        Load += (_, _) =>
        {
            Theme.UseDarkTitleBar(this);
            RefreshMonitors();
            RefreshList();
            RefreshProfiles();
            RefreshSessions();
            _workSets = _workSetStore.Load();
            RefreshWorkSets();
            SetInitialSplitter();

            // 저장된 UI 옵션/상태 복원(모니터 선택·간격·걸쳐배치·방향·프로파일·체크옵션·감시모드)
            ApplyConfig();
            _uiReady = true;

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            // 핸들 생성 후 전역 단축키 등록(요구 3)
            _hotkeys = new HotkeyManager(Handle);
            ReRegisterHotkeys(silentOk: true);

            // 인접 창 연동 훅(붙여서 조정)
            _groupTracker.SetEnabled(_linkAdjacent);

            // 감시 모드: 새 창 표시/파괴 감지 → 디바운스 후 자동 재배치
            _watcher.Changed += OnWatcherChanged;

            // 자동 실행이 켜져 있는데 등록 경로가 현재 exe와 다르면 현재 경로로 갱신(경로 변경 대응)
            if (AutoStartService.RefreshIfEnabled())
                SetStatus($"자동 실행 경로 갱신됨 — {AutoStartService.CurrentExePath()}");
        };
        FormClosed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _hotkeys?.UnregisterAll();
            _groupTracker.Dispose();
            _watcher.Dispose();
            _restoreTimer?.Dispose();
            if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        };
    }

    // ==== 전역 단축키 수신(요구 3) ====
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && _hotkeys is not null &&
            _hotkeys.TryGetAction(m.WParam.ToInt32(), out var action))
        {
            RunHotkeyAction(action);
            return;
        }
        base.WndProc(ref m);
    }

    // ==== X → 트레이 최소화(요구 2) ====
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveConfig(); // 닫힘/숨김 직전 상태 저장(강제종료 대비)

        // 사용자가 X/Alt+F4로 닫을 때만 트레이로 숨김. 완전 종료·OS 종료는 그대로 진행.
        if (!_reallyExit && _minimizeToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnFormClosing(e);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(RefreshMonitors)); return; }
        RefreshMonitors();
        SetStatus("디스플레이 구성 변경 감지 — 모니터 목록 갱신됨.");
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Row 0: 상단 툴바
        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        top.Controls.Add(MakeIconButton(IconKind.Refresh, 38, (_, _) => { RefreshMonitors(); RefreshList(); RefreshSessions(); }, "터미널·모니터·세션 목록 새로고침"));
        _btnToggleAll = MakeIconButton(IconKind.CheckEmpty, 38, (_, _) => ToggleAllChecks(), "터미널 모두 선택 / 모두 해제 (토글)", track: false);
        top.Controls.Add(_btnToggleAll);
        top.Controls.Add(MakeIconButton(IconKind.Settings, 38, (_, _) => OpenOptions(), "옵션 — 단축키·테마·프로파일·트레이 최소화·인접 창 연동"));
        _lblCount = new Label { AutoSize = true, Padding = new Padding(16, 8, 0, 0), Text = "터미널 0개" };
        top.Controls.Add(_lblCount);
        root.Controls.Add(top, 0, 0);

        // Row 1: 좌(터미널 목록) | 우(최근 클로드 세션)
        // MinSize/SplitterDistance는 폭이 확정된 뒤(SetInitialSplitter)에 설정한다.
        // 생성 시점엔 폭이 작아 큰 MinSize를 주면 검증 예외가 난다.
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1   // 터미널 패널을 좁은 고정폭으로, 세션 패널이 나머지 차지
        };
        _split.Panel1.Controls.Add(BuildTerminalList());
        _split.Panel2.Controls.Add(BuildSessionPanel());
        root.Controls.Add(_split, 0, 1);

        // Row 2: 조작 패널
        var controls = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 0)
        };
        controls.Controls.Add(BuildMonitorGroup());
        controls.Controls.Add(BuildArrangeGroup());
        controls.Controls.Add(BuildWorkSetGroup());
        root.Controls.Add(controls, 0, 2);

        // Row 3: 상태
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            BorderStyle = BorderStyle.Fixed3D,
            Text = "준비됨"
        };
        root.Controls.Add(_statusLabel, 0, 3);

        Controls.Add(root);

        // 값 변경 시 config.env 저장(걸쳐배치·모니터선택)
        _chkSpan.CheckedChanged += (_, _) => { _monitorMap.SingleSelect = !_chkSpan.Checked; SaveConfig(); };
    }

    /// <summary>테마 적용 후 세부 보정(상태바·카운트).</summary>
    private void ApplyPostThemeTweaks()
    {
        _statusLabel.BackColor = Theme.Bg2;
        _statusLabel.ForeColor = Theme.TextDim;
        _statusLabel.BorderStyle = BorderStyle.None;
        _statusLabel.Padding = new Padding(10, 0, 0, 0);
        _lblCount.ForeColor = Theme.TextDim;
    }

    // ==== 옵션 창이 호출하는 공개 API ====
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set { _minimizeToTray = value; SaveConfig(); }
    }

    /// <summary>옵션 창용: 현재 Windows 시작 시 자동 실행 등록 여부.</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AutoStartEnabled => AutoStartService.IsEnabled();

    /// <summary>옵션 창용: 자동 실행 켜기/끄기(현재 exe 경로로 등록). 상태바에 결과 표시.</summary>
    public void SetAutoStart(bool on)
    {
        string err;
        bool ok = on ? AutoStartService.Enable(out err) : AutoStartService.Disable(out err);
        if (ok)
            SetStatus(on ? $"Windows 시작 시 자동 실행 켬 — {AutoStartService.CurrentExePath()}" : "Windows 시작 시 자동 실행 끔.");
        else
            SetStatus($"자동 실행 설정 실패: {err}");
    }

    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool LinkAdjacent
    {
        get => _linkAdjacent;
        set
        {
            _linkAdjacent = value;
            _groupTracker.SetEnabled(value);
            if (!value) _groupTracker.Clear();
            SaveConfig();
            SetStatus(value
                ? "인접 창 연동 켬 — 다음 정렬부터 경계 드래그 시 옆 창이 따라 붙습니다."
                : "인접 창 연동 끔.");
        }
    }

    public ThemeMode CurrentThemeMode => Theme.Mode;

    /// <summary>테마 모드 지정 + 전체 재스타일 + 저장. 옵션 창도 함께 재스타일.</summary>
    public void SetThemeMode(ThemeMode mode)
    {
        if (Theme.Mode == mode) return;
        Theme.SetMode(mode);
        ReapplyTheme();
        _optionsForm?.ReapplyTheme();
        SaveConfig();
        SetStatus(Theme.IsDark ? "다크 테마" : "라이트 테마");
    }

    /// <summary>옵션 창 열기(단일 인스턴스).</summary>
    private void OpenOptions()
    {
        if (_optionsForm is { IsDisposed: false })
        {
            _optionsForm.Activate();
            return;
        }
        _optionsForm = new OptionsForm(this);
        _optionsForm.FormClosed += (_, _) => _optionsForm = null;
        _optionsForm.Show(this);
    }

    /// <summary>현재 모드로 전체 재스타일 + 커스텀 드로우/아이템 색 갱신.</summary>
    private void ReapplyTheme()
    {
        Theme.Apply(this);
        ApplyPostThemeTweaks();
        RestyleIcons();     // 아이콘을 새 테마 색으로 재생성
        Theme.UseDarkTitleBar(this);
        RefreshList();      // 아이템 강조색 재적용
        RefreshSessions();  // 세션 텍스트색 재적용
        Invalidate(true);
    }

    private Control BuildTerminalList()
    {
        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false
        };
        _listView.Columns.Add("제목", 300);
        _listView.Columns.Add("종류", 84);
        // 제목 열이 남는 폭을 채우도록(빈 공간 제거)
        _listView.Resize += (_, _) => FitTerminalColumns();

        // 드래그 재정렬(인벤토리 스타일): 행을 집으면 마우스에 붙고, 대상 슬롯이 실시간 표시되며, 놓으면 교체
        _listView.MouseDown += OnTerminalListMouseDown;
        _listView.MouseMove += OnTerminalListMouseMove;
        _listView.MouseUp += OnTerminalListMouseUp;
        _listView.MouseCaptureChanged += (_, _) => { if (_dragActive) CancelDrag(); };
        _listView.KeyDown += (_, e) => { if (_dragActive && e.KeyCode == Keys.Escape) { CancelDrag(); e.Handled = true; } };
        _listView.ContextMenuStrip = BuildTerminalContextMenu();
        return _listView;
    }

    /// <summary>터미널 목록 우클릭 메뉴: 선택 창 강제 종료.</summary>
    private ContextMenuStrip BuildTerminalContextMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), BackColor = Theme.Bg2, ForeColor = Theme.Text };
        menu.Items.Add("강제 종료", null, (_, _) => ForceKillSelectedTerminals());
        return menu;
    }

    /// <summary>목록에서 선택(하이라이트)된 터미널들.</summary>
    private List<TerminalWindow> GetSelectedTerminals()
    {
        var byHandle = _terminals.ToDictionary(t => t.Handle);
        var list = new List<TerminalWindow>();
        foreach (ListViewItem it in _listView.SelectedItems)
            if (it.Tag is IntPtr h && byHandle.TryGetValue(h, out var t)) list.Add(t);
        return list;
    }

    /// <summary>
    /// 선택 터미널을 강제 종료. Windows Terminal처럼 여러 창이 한 프로세스를 공유하면
    /// 프로세스 kill이 다른 창까지 죽이므로 창별 WM_CLOSE로 닫고, 프로세스가 분리된
    /// 클래식 콘솔은 프로세스를 강제 종료한다.
    /// </summary>
    private void ForceKillSelectedTerminals()
    {
        var sel = GetSelectedTerminals();
        if (sel.Count == 0) { SetStatus("강제 종료할 터미널을 선택하세요."); return; }

        if (MessageBox.Show(this,
                $"선택한 터미널 {sel.Count}개를 강제 종료합니다.\n저장하지 않은 작업은 사라집니다. 계속할까요?",
                "강제 종료", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        int ok = 0; string? lastErr = null;
        foreach (var t in sel)
        {
            try
            {
                bool sharedProc = _terminals.Count(x => x.ProcessId == t.ProcessId) > 1
                                  || t.ProcessName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase)
                                  || t.ProcessName.Equals("wt", StringComparison.OrdinalIgnoreCase);
                if (sharedProc)
                {
                    // 공유 프로세스(WT) → 이 창만 닫음(다른 창 보호)
                    if (NativeMethods.PostMessage(t.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero)) ok++;
                }
                else
                {
                    // 프로세스 분리 콘솔 → 프로세스 강제 종료
                    using var p = System.Diagnostics.Process.GetProcessById((int)t.ProcessId);
                    p.Kill();
                    ok++;
                }
            }
            catch (Exception ex) { lastErr = ex.Message; }
        }

        // 창이 닫히는 데 약간 걸릴 수 있어 잠깐 뒤 목록 갱신
        var refresh = new System.Windows.Forms.Timer { Interval = 500 };
        refresh.Tick += (_, _) => { refresh.Stop(); refresh.Dispose(); if (!IsDisposed) RefreshList(); };
        refresh.Start();

        SetStatus(lastErr is null ? $"강제 종료: {ok}/{sel.Count}개" : $"강제 종료: {ok}/{sel.Count}개 (일부 실패: {lastErr})");
    }

    // ---- 목록 드래그 재정렬(게임 인벤토리 스타일) ----

    private void OnTerminalListMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            // 우클릭한 행을 선택(이미 다중 선택에 포함된 경우는 유지) → 컨텍스트 메뉴 대상 확정
            var rh = _listView.HitTest(e.Location);
            if (rh.Item is not null && !rh.Item.Selected)
            {
                _listView.SelectedItems.Clear();
                rh.Item.Selected = true;
            }
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        var hit = _listView.HitTest(e.Location);
        if (hit.Item is null) return;
        if (hit.Location == ListViewHitTestLocations.StateImage) return; // 체크박스 클릭은 드래그 아님
        _dragItem = hit.Item;
        _dragDownPos = e.Location;
    }

    private void OnTerminalListMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragItem is null || e.Button != MouseButtons.Left) return;

        if (!_dragActive)
        {
            var dz = SystemInformation.DragSize;
            if (Math.Abs(e.X - _dragDownPos.X) < dz.Width && Math.Abs(e.Y - _dragDownPos.Y) < dz.Height) return;
            StartDrag();
        }
        UpdateDragTarget(e.Location);
        _ghost?.FollowCursor();
    }

    private void OnTerminalListMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_dragActive) { _dragItem = null; return; }
        UpdateDragTarget(e.Location);

        int src = _dragItem!.Index;
        int dst = _dragTargetIndex;
        bool toEnd = _dragToEnd;
        EndDragVisuals();

        if (toEnd && src != _listView.Items.Count - 1) MoveRowToEnd(src);
        else if (dst >= 0 && dst != src) SwapRows(src, dst);
        else SetStatus("순서 변경 없음.");
        _dragItem = null;
    }

    /// <summary>임계 거리를 넘으면 드래그 시작: 마우스에 붙는 고스트 생성 + 가장자리 자동 스크롤 타이머 가동.</summary>
    private void StartDrag()
    {
        if (_dragItem is null) return;
        _dragActive = true;
        _dragTargetIndex = -1;
        _dragToEnd = false;

        string kind = _dragItem.SubItems.Count > 1 ? _dragItem.SubItems[1].Text : "";
        Color kindColor = _dragItem.SubItems.Count > 1 ? _dragItem.SubItems[1].ForeColor : Theme.Text;
        int h = Math.Max(22, _dragItem.Bounds.Height + 4);
        int w = Math.Clamp(_listView.Columns[0].Width, 160, 280);
        _ghost ??= new DragGhost();
        _ghost.SetContent(_dragItem.Text, kind, kindColor, _listView.Font, w, h);
        _ghost.FollowCursor();
        _ghost.Show();

        if (_dragScrollTimer is null)
        {
            _dragScrollTimer = new System.Windows.Forms.Timer { Interval = 80 };
            _dragScrollTimer.Tick += OnDragScrollTick;
        }
        _dragScrollTimer.Start();
        _listView.Invalidate();
    }

    /// <summary>커서 아래 슬롯을 실시간 계산 → 하이라이트·상태줄 갱신(어디로 이동될지 항상 표시).</summary>
    private void UpdateDragTarget(Point client)
    {
        if (_dragItem is null) return;
        int newTarget = -1;
        bool toEnd = false;

        int x = Math.Clamp(client.X, 0, Math.Max(0, _listView.ClientSize.Width - 1));
        var it = _listView.GetItemAt(x, client.Y);
        if (it is not null && it.Index != _dragItem.Index)
            newTarget = it.Index;
        else if (it is null && _listView.Items.Count > 0 && client.Y >= _listView.Items[^1].Bounds.Bottom)
            toEnd = true; // 마지막 행 아래 빈 영역 = 맨 끝 슬롯

        if (newTarget == _dragTargetIndex && toEnd == _dragToEnd) return;
        _dragTargetIndex = newTarget;
        _dragToEnd = toEnd;
        _listView.Invalidate();

        if (_dragToEnd)
            SetStatus($"'{Ellipsis(_dragItem.Text)}' → 맨 끝으로 이동 (놓으면 확정)");
        else if (_dragTargetIndex >= 0)
            SetStatus($"'{Ellipsis(_dragItem.Text)}' ↔ {_dragTargetIndex + 1}번 '{Ellipsis(_listView.Items[_dragTargetIndex].Text)}' 교체 (놓으면 확정)");
        else
            SetStatus($"'{Ellipsis(_dragItem.Text)}' 드래그 중 — 바꿀 슬롯 위에 놓으세요 (Esc 취소)");
    }

    /// <summary>드래그 중 목록 위/아래 가장자리 근처면 자동 스크롤(화면 밖 슬롯으로도 이동 가능).</summary>
    private void OnDragScrollTick(object? sender, EventArgs e)
    {
        if (!_dragActive) { _dragScrollTimer?.Stop(); return; }
        if (_listView.Items.Count == 0) return;

        var cp = _listView.PointToClient(Cursor.Position);
        const int edge = 14;
        if (cp.Y < edge)
        {
            int top = _listView.TopItem?.Index ?? 0;
            if (top > 0) _listView.EnsureVisible(top - 1);
        }
        else if (cp.Y > _listView.ClientSize.Height - edge)
        {
            int top = _listView.TopItem?.Index ?? 0;
            int rowH = Math.Max(1, _listView.Items[0].Bounds.Height);
            int next = Math.Min(_listView.Items.Count - 1, top + Math.Max(1, _listView.ClientSize.Height / rowH));
            _listView.EnsureVisible(next);
        }
        UpdateDragTarget(cp);
        _ghost?.FollowCursor(); // 스크롤 중에도 고스트는 마우스에 붙어 있음
    }

    /// <summary>두 슬롯의 행을 서로 교체(인벤토리 스왑)하고 배치 순서를 저장.</summary>
    private void SwapRows(int src, int dst)
    {
        var items = _listView.Items.Cast<ListViewItem>().ToList();
        var dragged = items[src];
        (items[src], items[dst]) = (items[dst], items[src]);
        RebuildListOrder(items, dragged);
        SetStatus($"{src + 1}번 ↔ {dst + 1}번 슬롯 교체 — 위→아래 순서대로 배치됩니다.");
    }

    /// <summary>드래그한 행을 맨 끝 슬롯으로 이동(나머지는 앞으로 당김).</summary>
    private void MoveRowToEnd(int src)
    {
        var items = _listView.Items.Cast<ListViewItem>().ToList();
        var dragged = items[src];
        items.RemoveAt(src);
        items.Add(dragged);
        RebuildListOrder(items, dragged);
        SetStatus($"{src + 1}번 행을 맨 끝으로 이동 — 위→아래 순서대로 배치됩니다.");
    }

    /// <summary>목록을 새 순서로 재구성하고 _windowOrder(배치 순서)를 갱신. 드래그한 행은 선택 유지.</summary>
    private void RebuildListOrder(List<ListViewItem> newOrder, ListViewItem keepSelected)
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var it in newOrder) _listView.Items.Add(it);
        _listView.EndUpdate();

        keepSelected.Selected = true;
        keepSelected.EnsureVisible();
        _windowOrder = newOrder.Select(x => x.Tag).OfType<IntPtr>().ToList();
    }

    /// <summary>드래그 시각 상태 정리(고스트·타이머·하이라이트).</summary>
    private void EndDragVisuals()
    {
        _dragActive = false;
        _dragTargetIndex = -1;
        _dragToEnd = false;
        _dragScrollTimer?.Stop();
        _ghost?.Hide();
        _listView.Invalidate();
    }

    /// <summary>드래그 취소(Esc·캡처 상실·목록 갱신): 순서 변경 없이 종료.</summary>
    private void CancelDrag()
    {
        EndDragVisuals();
        _dragItem = null;
        SetStatus("드래그 취소됨.");
    }

    /// <summary>드래그 중 실시간 표시: 원본 행은 어둡게(들어올린 슬롯), 대상 행은 골드 프레임(교체될 슬롯).</summary>
    private void DrawDragOverlay(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (!_dragActive || e.Item is null || _dragItem is null) return;
        var g = e.Graphics;
        var b = e.Bounds;

        if (e.ItemIndex == _dragItem.Index)
        {
            using var dim = new SolidBrush(Color.FromArgb(130, Color.Black));
            g.FillRectangle(dim, b);
            return;
        }

        if (e.ItemIndex == _dragTargetIndex)
        {
            using (var tint = new SolidBrush(Color.FromArgb(46, Theme.Gold))) g.FillRectangle(tint, b);
            using var pen = new Pen(Theme.Gold, 2f);
            g.DrawLine(pen, b.Left, b.Top + 1, b.Right, b.Top + 1);
            g.DrawLine(pen, b.Left, b.Bottom - 2, b.Right, b.Bottom - 2);
            if (e.ColumnIndex == 0) g.DrawLine(pen, b.Left + 1, b.Top, b.Left + 1, b.Bottom);
            if (e.ColumnIndex == _listView.Columns.Count - 1) g.DrawLine(pen, b.Right - 2, b.Top, b.Right - 2, b.Bottom);
        }
        else if (_dragToEnd && e.ItemIndex == _listView.Items.Count - 1)
        {
            using var pen = new Pen(Theme.Gold, 3f);
            g.DrawLine(pen, b.Left, b.Bottom - 2, b.Right, b.Bottom - 2); // 맨 끝 슬롯 삽입 표시
        }
    }

    private static string Ellipsis(string s) => s.Length <= 24 ? s : s[..23] + "…";

    private Control BuildSessionPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        bar.Controls.Add(new Label { Text = "최근 세션", AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Padding = new Padding(2, 8, 8, 0), Tag = "gold" });
        bar.Controls.Add(MakeIconButton(IconKind.OpenNew, 38, (_, _) => ResumeSelectedSessions(), "선택 세션을 이어서 복구 (새 터미널, claude -c)"));
        bar.Controls.Add(MakeIconButton(IconKind.Refresh, 38, (_, _) => RefreshSessions(), "최근 클로드 세션 목록 새로고침"));
        // 삭제 드롭다운(앱에서 삭제 / 저장폴더 삭제)
        var btnDelete = MakeIconButton(IconKind.Delete, 38, null, "선택 세션 삭제 — 앱에서 숨김 / 저장폴더 휴지통");
        btnDelete.Click += (_, _) =>
        {
            var m = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), BackColor = Theme.Bg2, ForeColor = Theme.Text };
            m.Items.Add("앱에서 삭제 (목록에서 숨김)", null, (_, _) => HideSelectedSessions());
            m.Items.Add("세션 저장폴더 삭제 (휴지통)", null, (_, _) => DeleteSelectedSessionStorage());
            m.Show(btnDelete, new Point(0, btnDelete.Height));
        };
        bar.Controls.Add(btnDelete);
        panel.Controls.Add(bar, 0, 0);

        _sessionView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            MultiSelect = true   // 여러 세션 선택 가능
        };
        _sessionView.Columns.Add("세션", 140);
        _sessionView.Columns.Add("폴더", 300);
        _sessionView.Columns.Add("최근", 116);
        // 폴더 열이 남는 폭을 채우도록(경로 잘림·빈 공간 제거)
        _sessionView.Resize += (_, _) => FitSessionColumns();
        _sessionView.DoubleClick += (_, _) => ResumeSelectedSessions();
        _sessionView.ContextMenuStrip = BuildSessionContextMenu();
        panel.Controls.Add(_sessionView, 0, 1);
        return panel;
    }

    /// <summary>세션 목록 우클릭 메뉴: 복구 / 앱에서 삭제(숨김) / 세션 저장폴더 삭제(휴지통).</summary>
    private ContextMenuStrip BuildSessionContextMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), BackColor = Theme.Bg2, ForeColor = Theme.Text };
        menu.Items.Add("복구 (이어서, claude -c)", null, (_, _) => ResumeSelectedSessions());
        menu.Items.Add("새 세션으로 열기 (claude)", null, (_, _) => LaunchNewSelectedSessions());
        menu.Items.Add("탐색기에서 폴더 열기", null, (_, _) => OpenSelectedSessionFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("앱에서 삭제 (목록에서 숨김)", null, (_, _) => HideSelectedSessions());
        menu.Items.Add("세션 저장폴더 삭제 (휴지통)", null, (_, _) => DeleteSelectedSessionStorage());
        return menu;
    }

    private GroupBox BuildMonitorGroup()
    {
        var grp = new DarkGroupBox { Text = "모니터 (실제 배치)", Size = new Size(190, 176), Margin = new Padding(4) };
        _monitorMap = new MonitorMapControl
        {
            Location = new Point(10, 20),
            Size = new Size(170, 122)
        };
        _monitorMap.SelectionChanged += (_, _) => SaveConfig();
        _chkSpan = new CheckBox
        {
            Text = "모니터 걸침",
            Location = new Point(10, 148),
            AutoSize = true
        };
        _toolTip.SetToolTip(_chkSpan, "선택한 모니터들을 하나의 영역으로 걸쳐서 배치합니다.");
        _monitorMap.SingleSelect = !_chkSpan.Checked; // 걸쳐배치 꺼짐 → 한 개만 선택
        grp.Controls.Add(_monitorMap);
        grp.Controls.Add(_chkSpan);
        return grp;
    }

    private GroupBox BuildArrangeGroup()
    {
        var grp = new DarkGroupBox { Text = "정렬", Size = new Size(210, 176), Margin = new Padding(4) };

        var btnAuto = MakeIconButton(IconKind.AutoArrange, 44, (_, _) => DoAuto(), "자동 배치 — 개수·비율로 최적 방향 자동");
        btnAuto.Location = new Point(10, 26); btnAuto.Size = new Size(44, 40);
        btnAuto.Tag = "accent"; // 일반 버튼 배경 + 골드 아이콘으로만 강조(항상 눌린 느낌 방지)

        var btnH = MakeIconButton(IconKind.SplitH, 44, (_, _) => DoArrange(LayoutDirection.Horizontal), "가로 분할 (좌우로 나란히)");
        btnH.Location = new Point(58, 26); btnH.Size = new Size(44, 40);
        var btnV = MakeIconButton(IconKind.SplitV, 44, (_, _) => DoArrange(LayoutDirection.Vertical), "세로 분할 (위아래로 쌓기)");
        btnV.Location = new Point(106, 26); btnV.Size = new Size(44, 40);
        var btnG = MakeIconButton(IconKind.Grid, 44, (_, _) => DoArrange(LayoutDirection.Grid), "그리드 (격자 배치)");
        btnG.Location = new Point(154, 26); btnG.Size = new Size(44, 40);

        // 2행: 일괄(전체 최소화 / 다시 보이기 / 정렬 직전 복원)
        var btnMin = MakeIconButton(IconKind.MinimizeAll, 44, (_, _) => DoBulk(BulkKind.Minimize), "전체 최소화");
        btnMin.Location = new Point(10, 72); btnMin.Size = new Size(44, 40);
        var btnShow = MakeIconButton(IconKind.ShowAll, 44, (_, _) => DoBulk(BulkKind.Show), "전체 다시 보이기");
        btnShow.Location = new Point(58, 72); btnShow.Size = new Size(44, 40);
        var btnRestore = MakeIconButton(IconKind.RestorePrev, 44, (_, _) => DoBulk(BulkKind.Restore), "정렬 직전 복원 (되돌리기)");
        btnRestore.Location = new Point(106, 72); btnRestore.Size = new Size(44, 40);

        _chkLink = new CheckBox { Text = "인접 창 연동", AutoSize = true, Location = new Point(10, 118), Checked = _linkAdjacent };
        _chkLink.CheckedChanged += (_, _) => { if (_applyingConfig) return; LinkAdjacent = _chkLink.Checked; };
        _toolTip.SetToolTip(_chkLink, "붙여서 조정 — 타일 경계를 드래그하면 맞닿은 옆 창이 함께 따라 붙습니다.");

        // 창 자동 배치(감시 모드): 새 창이 열리거나 닫히면 자동으로 재배치
        _chkWatch = new CheckBox { Text = "창 자동 배치", AutoSize = true, Location = new Point(10, 142) };
        _toolTip.SetToolTip(_chkWatch, "감시 모드 — 터미널 창이 열리거나 닫히면 마지막 방향으로 자동 재배치합니다.");
        _chkWatch.CheckedChanged += (_, _) =>
        {
            if (_applyingConfig) return;
            _watchMode = _chkWatch.Checked;
            _watcher.SetEnabled(_watchMode);
            SaveConfig();
            SetStatus(_watchMode ? "창 자동 배치 ON — 창이 열리거나 닫히면 자동 재배치." : "창 자동 배치 OFF.");
        };

        grp.Controls.Add(btnAuto);
        grp.Controls.Add(btnH);
        grp.Controls.Add(btnV);
        grp.Controls.Add(btnG);
        grp.Controls.Add(btnMin);
        grp.Controls.Add(btnShow);
        grp.Controls.Add(btnRestore);
        grp.Controls.Add(_chkLink);
        grp.Controls.Add(_chkWatch);
        return grp;
    }

    /// <summary>작업 세트(창 배치 저장/복원) + 감시 모드(새 창 자동 배치) 그룹.</summary>
    private GroupBox BuildWorkSetGroup()
    {
        var grp = new DarkGroupBox { Text = "작업 세트", Size = new Size(208, 176), Margin = new Padding(4) };

        _cboWorkSets = new ComboBox { Location = new Point(10, 24), Size = new Size(188, 24), DropDownStyle = ComboBoxStyle.DropDownList };

        var btnSave = MakeIconButton(IconKind.Save, 44, (_, _) => SaveWorkSetPrompt(), "현재 창 배치 저장");
        btnSave.Location = new Point(10, 54); btnSave.Size = new Size(44, 34);
        var btnApply = MakeIconButton(IconKind.Apply, 44, (_, _) => { if (_cboWorkSets.SelectedItem is string s) ApplyWorkSet(s); else SetStatus("적용할 작업 세트를 선택하세요."); }, "저장한 배치로 복원(적용) — 닫힌 세션은 재실행");
        btnApply.Location = new Point(58, 54); btnApply.Size = new Size(44, 34);
        var btnDel = MakeIconButton(IconKind.Delete, 44, (_, _) => { if (_cboWorkSets.SelectedItem is string s) DeleteWorkSet(s); }, "작업 세트 삭제");
        btnDel.Location = new Point(106, 54); btnDel.Size = new Size(44, 34);

        var hint = new Label
        {
            Text = "현재 창 배치를 저장 →\n한 번에 복원(적용).\n닫힌 세션은 자동 재실행.",
            AutoSize = true,
            Location = new Point(10, 96),
            Font = new Font("Segoe UI", 8F)
        };

        grp.Controls.Add(_cboWorkSets);
        grp.Controls.Add(btnSave);
        grp.Controls.Add(btnApply);
        grp.Controls.Add(btnDel);
        grp.Controls.Add(hint);
        return grp;
    }

    /// <summary>아이콘만 있는 버튼(텍스트 없음). 이미지는 RestyleIcons에서 테마 색으로 그린다. 1초 툴팁 등록.</summary>
    private Button MakeIconButton(IconKind kind, int width, EventHandler? onClick, string tooltip, bool track = true)
    {
        var b = new RoundedButton
        {
            Text = "", Width = width, Height = 28, Margin = new Padding(2),
            ImageAlign = ContentAlignment.MiddleCenter
        };
        if (onClick is not null) b.Click += onClick;
        if (track) _iconButtons.Add((b, kind));
        _toolTip.SetToolTip(b, tooltip);
        return b;
    }

    /// <summary>툴팁: 1초 뒤 표시 + 다크/라이트 오너드로우.</summary>
    private void SetupToolTip()
    {
        _toolTip.InitialDelay = 500;    // 마우스 오버 0.5초 뒤
        _toolTip.ReshowDelay = 400;
        _toolTip.AutoPopDelay = 9000;
        _toolTip.OwnerDraw = true;
        _toolTip.Popup += (_, e) =>
        {
            string text = (e.AssociatedControl is null ? "" : _toolTip.GetToolTip(e.AssociatedControl)) ?? "";
            var sz = TextRenderer.MeasureText(text, Theme.Base);
            e.ToolTipSize = new Size(sz.Width + 18, sz.Height + 10);
        };
        _toolTip.Draw += (_, e) =>
        {
            using var bg = new SolidBrush(Theme.Bg2);
            e.Graphics.FillRectangle(bg, e.Bounds);
            using var pen = new Pen(Theme.BorderHi);
            e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Theme.Base, e.Bounds, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
    }

    /// <summary>아이콘 버튼 이미지를 현재 테마 색으로 (재)생성. 테마 전환 시에도 호출.</summary>
    private void RestyleIcons()
    {
        foreach (var (btn, kind) in _iconButtons)
        {
            var color = (btn.Tag as string) is "primary" or "accent" ? Theme.Gold : Theme.Text; // 주/강조 버튼은 골드 아이콘
            var old = btn.Image;
            btn.Image = Icons.Draw(kind, 18, color);
            old?.Dispose();
        }
        UpdateToggleAllButton();
    }

    private bool AllTerminalsChecked()
    {
        foreach (ListViewItem i in _listView.Items) if (!i.Checked) return false;
        return true;
    }

    /// <summary>모두 선택 ↔ 모두 해제 토글(현재 상태의 반대로).</summary>
    private void ToggleAllChecks()
    {
        bool all = _listView.Items.Count > 0 && AllTerminalsChecked();
        SetAllChecks(!all);
        UpdateToggleAllButton();
    }

    /// <summary>토글 버튼 라벨/아이콘을 현재 선택 상태에 맞춰 갱신.</summary>
    private void UpdateToggleAllButton()
    {
        if (_btnToggleAll is null) return;
        bool all = _listView.Items.Count > 0 && AllTerminalsChecked();
        var old = _btnToggleAll.Image;
        _btnToggleAll.Image = Icons.Draw(all ? IconKind.CheckFilled : IconKind.CheckEmpty, 18, Theme.Text);
        old?.Dispose();
    }

    private static Icon TryGetAppIcon()
    {
        // 1순위: 임베드된 app.ico(멀티 해상도 — 창/트레이 선명)
        try
        {
            using var s = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico");
            if (s is not null) return new Icon(s);
        }
        catch { /* 폴백 */ }

        // 2순위: exe에 박힌 아이콘
        try
        {
            string? p = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(p))
            {
                var ico = Icon.ExtractAssociatedIcon(p);
                if (ico is not null) return ico;
            }
        }
        catch { /* 폴백 */ }

        return SystemIcons.Application;
    }

    // ==== 트레이 ====
    private void SetupTray()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), BackColor = Theme.Bg2, ForeColor = Theme.Text };
        menu.Items.Add("열기", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("완전 종료", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Text = "TileCLI",
            Icon = TryGetAppIcon(),
            Visible = true,   // 창 표시 여부와 무관하게 트레이 아이콘은 항상 유지
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void HideToTray()
    {
        if (_tray is not null) _tray.Visible = true;
        // ShowInTaskbar를 토글하지 않는다: 창 핸들 재생성으로 전역 단축키가 풀림.
        // Hide()만으로 작업표시줄에서 사라지고, 핸들이 유지돼 단축키가 계속 동작한다.
        Hide();
        SetStatus("트레이로 최소화됨 — 트레이 아이콘에서 '완전 종료'.");
    }

    /// <summary>
    /// 세션 패널(우측)이 ~480px가 되도록 스플리터를 설정하고 MinSize를 부여.
    /// 폭이 확정된 뒤(Load/Shown)에만 1회 적용한다. SplitterDistance를 먼저 유효 범위로 잡은 뒤
    /// MinSize를 줘야 검증 예외를 피한다.
    /// </summary>
    private void SetInitialSplitter()
    {
        if (_splitterSet) return;
        int avail = _split.Width;
        if (avail < 820) return; // 아직 레이아웃 전(폭 부족)이면 기본 유지, 다음 기회에 재시도
        try
        {
            int dist = Math.Clamp(430, 25, avail - 25);
            _split.SplitterDistance = dist;   // 터미널 패널 폭 430(고정). 나머지는 세션 패널
            _split.Panel1MinSize = 320;
            _split.Panel2MinSize = 380;
            _splitterSet = true;
            FitTerminalColumns();
            FitSessionColumns();
        }
        catch { /* 경계 위반 방어 */ }
    }

    protected override void OnShown(EventArgs e)
    {
        SetInitialSplitter(); // Load 시 폭이 아직 안 잡혔을 수 있어 여기서 한 번 더(base 이전 적용)
        base.OnShown(e);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        // 트레이 아이콘은 창을 열어도 숨기지 않고 항상 유지한다.
    }

    private void ExitApp()
    {
        _reallyExit = true;
        Close();
    }

    // ---- 데이터 갱신 ----
    private void RefreshMonitors()
    {
        // 기존 체크를 DeviceName 기준으로 보존(핫플러그/해상도 변경 대응)
        var previouslyChecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _monitors.Count; i++)
            if (_monitorMap.IsChecked(i)) previouslyChecked.Add(_monitors[i].DeviceName);

        _monitors = MonitorService.Enumerate();
        var checkedIdx = new List<int>();
        for (int i = 0; i < _monitors.Count; i++)
        {
            bool check = _monitorsInitialized ? previouslyChecked.Contains(_monitors[i].DeviceName) : _monitors[i].IsPrimary;
            if (check) checkedIdx.Add(i);
        }
        if (checkedIdx.Count == 0 && _monitors.Count > 0)
        {
            int pi = _monitors.FindIndex(x => x.IsPrimary);
            checkedIdx.Add(pi < 0 ? 0 : pi);
        }
        _monitorMap.SetMonitors(_monitors, checkedIdx);

        _monitorsInitialized = true;
    }

    private void RefreshList()
    {
        if (_dragActive) CancelDrag(); // 갱신으로 행이 재구성되면 진행 중 드래그는 무효

        var previouslyChecked = GetCheckedHandles();

        _terminals = WindowDiscovery.Discover(_gptNames, _claudeTitleMarkers, _gptTitleMarkers);

        // 사용자가 드래그로 정한 순서 유지: 알려진 핸들은 그 순서대로, 신규 창은 탐지 순서로 뒤에
        if (_windowOrder.Count > 0)
        {
            var byHandle = _terminals.ToDictionary(t => t.Handle);
            var ordered = new List<TerminalWindow>(_terminals.Count);
            foreach (var h in _windowOrder)
                if (byHandle.Remove(h, out var t)) ordered.Add(t);
            foreach (var t in _terminals)
                if (byHandle.ContainsKey(t.Handle)) ordered.Add(t); // 남은 신규 창(탐지 순서)
            _terminals = ordered;
            _windowOrder = _terminals.Select(t => t.Handle).ToList(); // 닫힌 창 정리 + 신규 반영
        }

        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var t in _terminals)
        {
            var item = new ListViewItem(t.DisplayTitle) { Tag = t.Handle };
            item.SubItems.Add(t.KindLabel);
            item.UseItemStyleForSubItems = false;
            foreach (ListViewItem.ListViewSubItem si in item.SubItems) si.ForeColor = Theme.Text;
            item.SubItems[1].ForeColor = t.Kind switch  // "종류" 셀만 강조색
            {
                TerminalKind.Claude => Theme.Claude,
                TerminalKind.Gpt => Theme.Gpt,
                _ => Theme.TextDim
            };
            item.Checked = previouslyChecked.Contains(t.Handle);
            _listView.Items.Add(item);
        }
        _listView.EndUpdate();
        FitTerminalColumns();
        int claudeCount = _terminals.Count(t => t.Kind == TerminalKind.Claude);
        int gptCount = _terminals.Count(t => t.Kind == TerminalKind.Gpt);
        _lblCount.Text = $"터미널 {_terminals.Count}개 (Claude {claudeCount}, GPT {gptCount})";
        UpdateToggleAllButton();
        SetStatus($"목록 갱신: {_terminals.Count}개 탐지 (Claude {claudeCount}, GPT {gptCount}).");
    }

    private void RefreshProfiles()
    {
        _profiles = _profileStore.Load();
        _optionsForm?.RefreshProfileList(); // 옵션 창이 열려있으면 목록 갱신
    }

    /// <summary>옵션 창용: 저장된 프로파일 이름 목록.</summary>
    public List<string> ProfileNames => _profiles.Select(p => p.Name).ToList();

    /// <summary>옵션 창용: 선택 프로파일(설정 저장/복원).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value ?? string.Empty; SaveConfig(); }
    }

    private void RefreshSessions()
    {
        List<ClaudeSession> sessions;
        try { sessions = ClaudeSessionService.GetRecent(30, _settings.HiddenSessions); }
        catch { sessions = new List<ClaudeSession>(); }

        _sessionView.BeginUpdate();
        _sessionView.Items.Clear();
        foreach (var s in sessions)
        {
            var it = new ListViewItem(s.DisplayLabel) { Tag = s, ToolTipText = s.Cwd, ForeColor = Theme.Text };
            it.SubItems.Add(s.Cwd);
            it.SubItems.Add(s.LastActive.ToString("yyyy-MM-dd HH:mm"));
            _sessionView.Items.Add(it);
        }
        _sessionView.EndUpdate();
        FitSessionColumns();
        if (sessions.Count == 0)
            SetStatus("최근 클로드 세션 없음 (~/.claude/projects).");
    }

    /// <summary>제목 열이 목록 폭을 채우도록(종류 열 제외한 나머지). ClientSize는 세로 스크롤바를 이미 제외함.</summary>
    private void FitTerminalColumns()
    {
        if (_listView is null || _listView.Columns.Count < 2) return;
        int w = _listView.ClientSize.Width - _listView.Columns[1].Width;
        _listView.Columns[0].Width = Math.Max(160, w);
    }

    /// <summary>세션=고정 자연폭(넓히지 않음), 최근=날짜+시간 폭, 폴더가 나머지를 흡수(끝 빈 헤더 방지).</summary>
    private void FitSessionColumns()
    {
        if (_sessionView is null || _sessionView.Columns.Count < 3) return;
        int cw = _sessionView.ClientSize.Width;
        int recent = Math.Clamp(TextRenderer.MeasureText("2026-07-21 21:19", _sessionView.Font).Width + 16, 110, 190); // 최근 = 날짜+시간 폭
        int name = 165;                                // 세션 = 고정 자연폭
        int folder = cw - name - recent;               // 폴더 = 나머지 흡수
        if (folder < 140) { folder = 140; name = Math.Max(90, cw - folder - recent); } // 폭 부족 시 세션 축소
        _sessionView.Columns[0].Width = name;
        _sessionView.Columns[1].Width = folder;
        _sessionView.Columns[2].Width = recent;
    }

    // ---- 선택 헬퍼 ----
    private HashSet<IntPtr> GetCheckedHandles()
    {
        var set = new HashSet<IntPtr>();
        foreach (ListViewItem item in _listView.Items)
            if (item.Checked && item.Tag is IntPtr h) set.Add(h);
        return set;
    }

    private List<IntPtr> GetTargetWindows()
    {
        var checkedHandles = new List<IntPtr>();
        foreach (ListViewItem item in _listView.Items)
            if (item.Checked && item.Tag is IntPtr h) checkedHandles.Add(h);

        if (checkedHandles.Count == 0)
            foreach (ListViewItem item in _listView.Items)
                if (item.Tag is IntPtr h) checkedHandles.Add(h);

        return checkedHandles;
    }

    private (List<MonitorTarget> monitors, bool span) GetSelectedMonitors()
    {
        var selected = new List<MonitorTarget>();
        foreach (int idx in _monitorMap.CheckedIndices)
            if (idx >= 0 && idx < _monitors.Count) selected.Add(_monitors[idx]);

        if (selected.Count == 0 && _monitors.Count > 0)
            selected.Add(_monitors[0]);

        return (selected, _chkSpan.Checked);
    }

    private void SetAllChecks(bool value)
    {
        foreach (ListViewItem item in _listView.Items) item.Checked = value;
    }

    // ---- 동작 ----
    /// <summary>자동 배치(요구 1): 대상 개수·영역 비율로 방향을 자동 선택해 정렬.</summary>
    private void DoAuto()
    {
        var targets = GetTargetWindows();
        if (targets.Count == 0) { SetStatus("정렬할 터미널이 없습니다."); return; }

        var (monitors, span) = GetSelectedMonitors();
        if (monitors.Count == 0) { SetStatus("모니터를 찾을 수 없습니다."); return; }

        var area = TilingEngine.ComputeTargetArea(monitors, span);
        if (area.Width <= 0 || area.Height <= 0) { SetStatus("유효한 배치 영역이 없습니다."); return; }

        var dir = TilingEngine.AutoDirection(targets.Count, area);
        DoArrange(dir);
        SetStatus($"⚡ 자동 배치: {DirName(dir)} — {targets.Count}개 (영역 {area.Width}x{area.Height}).");
    }

    private void DoArrange(LayoutDirection direction, IReadOnlyList<IntPtr>? explicitTargets = null)
    {
        _currentDirection = direction;
        _suppressWatchUntil = Environment.TickCount + 1200; // 우리 이동이 감시 재배치를 재트리거하지 않게
        var targets = explicitTargets is not null ? new List<IntPtr>(explicitTargets) : GetTargetWindows();
        if (targets.Count == 0) { SetStatus("정렬할 터미널이 없습니다."); return; }

        var (monitors, span) = GetSelectedMonitors();
        if (monitors.Count == 0) { SetStatus("모니터를 찾을 수 없습니다."); return; }

        var area = TilingEngine.ComputeTargetArea(monitors, span);
        if (area.Width <= 0 || area.Height <= 0) { SetStatus("유효한 배치 영역이 없습니다."); return; }

        int gap = 0; // 간격 조절 UI 제거 → 항상 꽉 붙임
        int applied = TilingEngine.ApplyTiling(targets, direction, area, gap, _snapshots);

        // 인접 창 연동: 방금 만든 배치를 그룹으로 등록(경계 드래그 시 옆 창이 따라 붙음)
        if (_linkAdjacent)
            _groupTracker.Track(targets, TilingEngine.ComputeCells(targets.Count, direction, area, gap), area, gap);

        SaveConfig(); // 최근 사용 방향/간격/걸쳐배치/모니터 기록
        RefreshList();
        SetStatus($"{DirName(direction)} 정렬: {applied}/{targets.Count}개 배치 (영역 {area.Width}x{area.Height}).");
    }

    private enum BulkKind { Minimize, Show, Restore }

    private void DoBulk(BulkKind kind)
    {
        var targets = GetTargetWindows();
        switch (kind)
        {
            case BulkKind.Minimize:
                SetStatus($"전체 최소화: {BulkController.MinimizeAll(targets)}개");
                break;
            case BulkKind.Show:
                SetStatus($"전체 다시 보이기: {BulkController.ShowAll(targets)}개");
                break;
            case BulkKind.Restore:
                if (!_snapshots.HasSnapshot) { SetStatus("복원할 정렬 기록이 없습니다."); return; }
                SetStatus($"정렬 직전 복원: {BulkController.RestorePrevious(_snapshots)}개");
                break;
        }
        RefreshList();
    }

    // ---- 세션 복구/삭제(요구 6 + 다중 선택·삭제) ----
    private List<ClaudeSession> GetSelectedSessions()
    {
        var list = new List<ClaudeSession>();
        foreach (ListViewItem it in _sessionView.SelectedItems)
            if (it.Tag is ClaudeSession s) list.Add(s);
        return list;
    }

    private void ResumeSelectedSessions()
    {
        var sel = GetSelectedSessions();
        if (sel.Count == 0) { SetStatus("복구할 세션을 선택하세요."); return; }

        // 여러 개면 확인(터미널이 개수만큼 열림)
        if (sel.Count > 3 && MessageBox.Show(this,
                $"{sel.Count}개 세션을 각각 새 터미널로 복구합니다. 계속할까요?",
                "세션 복구", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        int ok = 0; string? lastErr = null;
        foreach (var s in sel)
            if (ClaudeSessionService.Resume(s, out string err)) ok++; else lastErr = err;

        SetStatus(ok == sel.Count
            ? $"세션 복구 실행: {ok}개"
            : $"세션 복구: {ok}/{sel.Count}개 (실패: {lastErr})");
    }

    /// <summary>선택한 세션의 폴더에서 새 클로드 세션(claude, -c 없음)을 새 터미널로 연다.</summary>
    private void LaunchNewSelectedSessions()
    {
        var sel = GetSelectedSessions();
        if (sel.Count == 0) { SetStatus("새로 열 세션(폴더)을 선택하세요."); return; }

        if (sel.Count > 3 && MessageBox.Show(this,
                $"{sel.Count}개 폴더에서 각각 새 클로드 세션을 엽니다. 계속할까요?",
                "새 세션으로 열기", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        int ok = 0; string? lastErr = null;
        foreach (var s in sel)
            if (ClaudeSessionService.LaunchNew(s.Cwd, out string err)) ok++; else lastErr = err;

        SetStatus(ok == sel.Count
            ? $"새 세션 열기: {ok}개"
            : $"새 세션 열기: {ok}/{sel.Count}개 (실패: {lastErr})");
    }

    /// <summary>선택 세션의 작업 폴더(cwd)를 파일 탐색기로 연다.</summary>
    private void OpenSelectedSessionFolder()
    {
        var sel = GetSelectedSessions();
        if (sel.Count == 0) { SetStatus("폴더를 열 세션을 선택하세요."); return; }

        int ok = 0; string? lastErr = null;
        foreach (var s in sel)
            if (ClaudeSessionService.OpenFolder(s.Cwd, out string err)) ok++; else lastErr = err;

        SetStatus(ok > 0 ? $"탐색기에서 폴더 열기: {ok}개" : $"폴더 열기 실패: {lastErr}");
    }

    /// <summary>앱에서 삭제 = 선택 세션을 목록에서 숨김(설정에 cwd 기록, 파일은 유지).</summary>
    private void HideSelectedSessions()
    {
        var sel = GetSelectedSessions();
        if (sel.Count == 0) { SetStatus("숨길 세션을 선택하세요."); return; }

        int added = 0;
        foreach (var s in sel)
        {
            string key = string.IsNullOrWhiteSpace(s.Cwd) ? s.TranscriptPath : s.Cwd;
            if (string.IsNullOrEmpty(key)) continue;
            if (!_settings.HiddenSessions.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                _settings.HiddenSessions.Add(key);
                added++;
            }
        }
        TrySaveSettings();
        RefreshSessions();
        SetStatus($"앱에서 삭제(숨김): {added}개 — 파일은 보존됨. (settings.json HiddenSessions)");
    }

    /// <summary>세션 저장폴더 삭제 = 선택 세션의 ~/.claude/projects/&lt;encoded&gt;를 휴지통으로.</summary>
    private void DeleteSelectedSessionStorage()
    {
        var sel = GetSelectedSessions();
        if (sel.Count == 0) { SetStatus("삭제할 세션을 선택하세요."); return; }

        // 삭제될 폴더 목록을 보여주고 확인
        var folders = sel
            .Select(s => ClaudeSessionService.StorageFolder(s))
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (folders.Count == 0) { SetStatus("삭제할 저장폴더를 찾을 수 없습니다."); return; }

        string preview = string.Join("\n", folders.Take(10));
        if (folders.Count > 10) preview += $"\n… 외 {folders.Count - 10}개";

        var res = MessageBox.Show(this,
            $"다음 Claude 세션 저장폴더 {folders.Count}개를 휴지통으로 보냅니다.\n" +
            "(코드/작업 폴더가 아니라 세션 기록만 삭제됩니다. 휴지통에서 복구 가능)\n\n" +
            preview,
            "세션 저장폴더 삭제", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (res != DialogResult.OK) { SetStatus("삭제 취소됨."); return; }

        int ok = 0; string? lastErr = null;
        foreach (var s in sel)
            if (ClaudeSessionService.DeleteSessionStorage(s, out string err)) ok++; else lastErr = err;

        RefreshSessions();
        SetStatus(lastErr is null
            ? $"세션 저장폴더 삭제(휴지통): {ok}개"
            : $"세션 저장폴더 삭제: {ok}/{sel.Count}개 (실패: {lastErr})");
    }

    // ---- 단축키(요구 3/4) ----
    private void RunHotkeyAction(HotkeyAction action)
    {
        // 숨겨진 동안 열리거나 닫힌 창을 반영한 뒤 실행
        RefreshList();
        switch (action)
        {
            case HotkeyAction.Auto: DoAuto(); break;
            case HotkeyAction.Horizontal: DoArrange(LayoutDirection.Horizontal); break;
            case HotkeyAction.Vertical: DoArrange(LayoutDirection.Vertical); break;
            case HotkeyAction.Grid: DoArrange(LayoutDirection.Grid); break;
        }
    }

    public void OpenHotkeySettings()
    {
        // 캡처 중 기존 단축키가 트리거되지 않도록 잠시 해제
        _hotkeys?.UnregisterAll();

        using var dlg = new HotkeySettingsForm(_settings);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.Hotkeys = dlg.Result;
            TrySaveSettings();
        }
        ReRegisterHotkeys();
    }

    private void ReRegisterHotkeys(bool silentOk = false)
    {
        if (_hotkeys is null) return;
        var conflicts = _hotkeys.ApplyFrom(_settings);
        if (conflicts.Count > 0)
        {
            MessageBox.Show(this,
                "다음 단축키는 등록하지 못했습니다(다른 프로그램과 충돌):\n\n" + string.Join("\n", conflicts),
                "단축키 충돌", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SetStatus($"단축키 {conflicts.Count}건 충돌 — 등록 실패.");
            return;
        }

        int active = _settings.Hotkeys.Count(kv => kv.Value.Enabled && kv.Value.IsComplete);
        if (!silentOk || active > 0)
            SetStatus(active > 0 ? $"단축키 {active}건 등록됨." : "활성 단축키 없음 — '단축키…'에서 설정.");
    }

    private void TrySaveSettings()
    {
        try { _settingsStore.Save(_settings); }
        catch (Exception ex) { SetStatus($"설정 저장 실패: {ex.Message}"); }
    }

    // ---- config.env: UI 옵션/최근 상태 저장·로드 ----
    /// <summary>현재 UI 상태를 config.env에 기록. 초기 로드/적용 중에는 건너뜀.</summary>
    private void SaveConfig()
    {
        if (!_uiReady || _applyingConfig) return;

        _config.SetBool("MinimizeToTray", _minimizeToTray);
        _config.SetBool("LinkAdjacent", _linkAdjacent);
        _config.SetBool("WatchMode", _watchMode);
        _config.SetBool("SpanMonitors", _chkSpan.Checked);
        _config.SetString("Direction", _currentDirection.ToString());

        // 체크된 모니터(DeviceName 기준 — 핫플러그/해상도 변경에 안정적)
        var mons = new List<string>();
        foreach (int idx in _monitorMap.CheckedIndices)
            if (idx >= 0 && idx < _monitors.Count) mons.Add(_monitors[idx].DeviceName);
        _config.SetList("Monitors", mons);

        _config.SetString("Profile", _selectedProfile);
        _config.SetString("Theme", Theme.IsDark ? "Dark" : "Light");
        _config.SetList("GptProcessNames", _gptNames);          // GPT CLI 프로세스명(편집 가능)
        _config.SetList("ClaudeTitleMarkers", _claudeTitleMarkers); // 클로드 창 제목 접두 마커
        _config.SetList("GptTitleMarkers", _gptTitleMarkers);   // GPT 창 제목 접두 마커

        try { _config.Save(); }
        catch (Exception ex) { SetStatus($"config.env 저장 실패: {ex.Message}"); }
    }

    /// <summary>config.env에 저장된 UI 상태를 복원(모니터·프로파일 목록이 채워진 뒤 호출).</summary>
    private void ApplyConfig()
    {
        _applyingConfig = true;
        try
        {
            _minimizeToTray = _config.GetBool("MinimizeToTray", true);
            _linkAdjacent = _config.GetBool("LinkAdjacent", true);
            _chkLink.Checked = _linkAdjacent;
            _watchMode = _config.GetBool("WatchMode", false);
            _chkWatch.Checked = _watchMode;
            _watcher.SetEnabled(_watchMode);
            _chkSpan.Checked = _config.GetBool("SpanMonitors", false);

            if (Enum.TryParse<LayoutDirection>(_config.GetString("Direction"), out var dir))
                _currentDirection = dir;

            // 모니터 선택 복원(저장값 있을 때만)
            var savedMonitors = _config.GetList("Monitors");
            if (savedMonitors.Count > 0)
            {
                for (int i = 0; i < _monitors.Count; i++)
                    _monitorMap.SetChecked(i, savedMonitors.Contains(_monitors[i].DeviceName, StringComparer.OrdinalIgnoreCase));

                if (_monitorMap.CheckedCount == 0 && _monitors.Count > 0)
                {
                    int pi = _monitors.FindIndex(x => x.IsPrimary);
                    _monitorMap.SetChecked(pi < 0 ? 0 : pi, true);
                }
            }
            // 걸쳐배치 상태에 맞춰 단일/다중 확정(구 설정이 다중이면 하나로 정리)
            _monitorMap.SingleSelect = !_chkSpan.Checked;

            // 선택 프로파일 복원
            _selectedProfile = _config.GetString("Profile") ?? string.Empty;
        }
        finally { _applyingConfig = false; }
    }

    // ---- 프로파일(옵션 창에서 호출) ----
    /// <summary>현재 배치 상태(방향·모니터·간격·걸쳐배치)를 name으로 저장.</summary>
    public void SaveProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { SetStatus("프로파일 이름을 입력하세요."); return; }
        name = name.Trim();

        var (monitors, span) = GetSelectedMonitors();
        var profile = new LayoutProfile
        {
            Name = name,
            Direction = _currentDirection,
            MonitorIndices = monitors.Select(m => m.Index).ToList(),
            SpanMonitors = span,
            Gap = 0
        };

        var updated = new List<LayoutProfile>(_profiles);
        int existing = updated.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) updated[existing] = profile;
        else updated.Add(profile);

        try
        {
            _profileStore.Save(updated);
        }
        catch (Exception ex)
        {
            SetStatus($"프로파일 저장 실패: {ex.Message}");
            return;
        }

        _profiles = updated;
        _selectedProfile = name;
        RefreshProfiles();
        SetStatus($"프로파일 저장: '{name}' ({DirName(_currentDirection)}, 모니터 {profile.MonitorIndices.Count}개, 간격 {profile.Gap}).");
    }

    /// <summary>이름으로 프로파일 적용(현재 체크된 창에 배치). 옵션 창에서 호출.</summary>
    public void ApplyProfile(string name)
    {
        var p = _profiles.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (p is null) { SetStatus("프로파일을 찾을 수 없습니다."); return; }

        _chkSpan.Checked = p.SpanMonitors;
        for (int i = 0; i < _monitors.Count; i++)
            _monitorMap.SetChecked(i, p.MonitorIndices.Contains(i));

        _selectedProfile = p.Name;
        DoArrange(p.Direction);
        SetStatus($"프로파일 적용: '{p.Name}'.");
    }

    /// <summary>이름으로 프로파일 삭제. 옵션 창에서 호출.</summary>
    public void DeleteProfile(string name)
    {
        var updated = new List<LayoutProfile>(_profiles);
        updated.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        try
        {
            _profileStore.Save(updated);
        }
        catch (Exception ex)
        {
            SetStatus($"프로파일 삭제 실패: {ex.Message}");
            return;
        }

        _profiles = updated;
        if (string.Equals(_selectedProfile, name, StringComparison.OrdinalIgnoreCase)) _selectedProfile = "";
        RefreshProfiles();
        SetStatus($"프로파일 삭제: '{name}'.");
    }

    // ==== 작업 세트(창 배치 저장/복원) ====
    /// <summary>작업 세트 목록을 콤보에 재적재(선택 유지).</summary>
    private void RefreshWorkSets(string? select = null)
    {
        _cboWorkSets.Items.Clear();
        foreach (var w in _workSets) _cboWorkSets.Items.Add(w.Name);
        if (!string.IsNullOrEmpty(select) && _cboWorkSets.Items.Contains(select)) _cboWorkSets.SelectedItem = select;
        else if (_cboWorkSets.Items.Count > 0) _cboWorkSets.SelectedIndex = 0;
    }

    /// <summary>이름을 입력받아 현재 창 배치를 작업 세트로 저장(선택된 이름이 있으면 덮어쓰기 기본값).</summary>
    private void SaveWorkSetPrompt()
    {
        string def = _cboWorkSets.SelectedItem as string ?? $"작업세트 {_workSets.Count + 1}";
        string? name = InputDialog.Show(this, "작업 세트 저장", "작업 세트 이름:", def);
        if (string.IsNullOrWhiteSpace(name)) return;
        SaveWorkSet(name.Trim());
    }

    /// <summary>현재 열린 터미널들의 위치/크기를 name으로 저장.</summary>
    private void SaveWorkSet(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { SetStatus("작업 세트 이름을 입력하세요."); return; }
        RefreshList(); // 최신 상태 기준

        // 열린 클로드 창 수만큼 최근 세션을 근사 연결(창↔세션 정확 매칭은 WT 단일 프로세스 한계 → 최근성 기준).
        int claudeCount = _terminals.Count(t => t.Kind == TerminalKind.Claude);
        List<ClaudeSession> recent;
        try { recent = ClaudeSessionService.GetRecent(claudeCount, _settings.HiddenSessions); }
        catch { recent = new List<ClaudeSession>(); }
        int ri = 0;

        var ws = new WorkSet { Name = name };
        foreach (var t in _terminals)
        {
            if (!TilingEngine.TryGetVisibleRect(t.Handle, out var r)) continue;
            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            string mon = _monitors.FirstOrDefault(m => m.Bounds.Contains(cx, cy))?.DeviceName ?? "";
            var w = new WorkSetWindow { Title = t.Title, Kind = t.KindLabel, X = r.X, Y = r.Y, W = r.Width, H = r.Height, Monitor = mon };
            if (t.Kind == TerminalKind.Claude && ri < recent.Count)   // 닫힌 뒤에도 되살릴 수 있게 세션 cwd 저장
            {
                w.Cwd = recent[ri].Cwd;
                w.SessionId = recent[ri].SessionId;
                ri++;
            }
            ws.Windows.Add(w);
        }
        if (ws.Windows.Count == 0) { SetStatus("저장할 터미널 창이 없습니다."); return; }
        int linked = ws.Windows.Count(x => !string.IsNullOrEmpty(x.Cwd));

        var updated = new List<WorkSet>(_workSets);
        int ex = updated.FindIndex(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
        if (ex >= 0) updated[ex] = ws; else updated.Add(ws);
        try { _workSetStore.Save(updated); }
        catch (Exception e) { SetStatus($"작업 세트 저장 실패: {e.Message}"); return; }

        _workSets = updated;
        RefreshWorkSets(name);
        SetStatus($"작업 세트 저장: '{name}' (창 {ws.Windows.Count}개, 세션 연결 {linked}개 — 닫혀도 복원 가능).");
    }

    /// <summary>
    /// 작업 세트 적용: (1) 열린 창은 제목/순서 매칭으로 저장 위치에 이동, (2) 창이 부족한 세션 슬롯은
    /// claude -c로 재실행한 뒤 새 창이 뜨는 대로 저장 위치에 배치(자동 복원).
    /// </summary>
    public void ApplyWorkSet(string name)
    {
        var ws = _workSets.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
        if (ws is null || ws.Windows.Count == 0) { SetStatus("적용할 작업 세트가 없습니다."); return; }

        _suppressWatchUntil = Environment.TickCount + 12000; // 재실행+배치 동안 감시 억제
        RefreshList();

        // 배치 결정은 순수 함수(WorkSetPlanner)로 위임(헤드리스 검증 가능)
        var slots = ws.Windows.Select(w => (w.Title, hasSession: !string.IsNullOrWhiteSpace(w.Cwd))).ToList();
        var open = _terminals.Select(t => (id: t.Handle.ToInt64(), t.Title)).ToList();
        var (assignIds, relaunch) = WorkSetPlanner.Plan(slots, open);

        // 복원용 스냅샷(배치 직전) → "정렬 직전 복원"으로 되돌릴 수 있음
        var moving = assignIds.Where(id => id != 0).Select(id => new IntPtr(id)).ToList();
        if (moving.Count > 0) _snapshots.Capture(moving);

        int placed = 0;
        for (int i = 0; i < ws.Windows.Count; i++)
        {
            if (assignIds[i] == 0) continue;
            var s = ws.Windows[i];
            if (TilingEngine.MoveWindowVisible(new IntPtr(assignIds[i]), new Rectangle(s.X, s.Y, s.W, s.H))) placed++;
        }

        // 재실행 대상 슬롯(미배정 + 세션 있음)
        var toLaunch = relaunch.Select(i => ws.Windows[i]).ToList();

        if (toLaunch.Count == 0)
        {
            RefreshList();
            SetStatus($"작업 세트 적용: '{ws.Name}' — {placed}/{ws.Windows.Count}개 배치.");
            return;
        }

        BeginRestoreSessions(ws.Name, toLaunch, placed);
    }

    /// <summary>닫힌 세션 슬롯들을 claude -c로 재실행하고, 새 창이 뜨는 대로 저장 위치에 배치(폴링).</summary>
    private void BeginRestoreSessions(string name, List<WorkSetWindow> pending, int placedBase)
    {
        _restoreBaseline = _terminals.Select(t => t.Handle.ToInt64()).ToHashSet();
        _restorePending = new List<WorkSetWindow>(pending);
        _restorePendingInit = pending.Count;
        _restorePlacedBase = placedBase;
        _restoreName = name;
        _restoreDeadline = Environment.TickCount + 12000; // 최대 12초 대기
        _suppressWatchUntil = _restoreDeadline;

        int launched = 0; string? lastErr = null;
        foreach (var slot in pending)
        {
            var sess = new ClaudeSession { Cwd = slot.Cwd, SessionId = slot.SessionId };
            if (ClaudeSessionService.Resume(sess, out string err)) launched++; else lastErr = err;
        }
        SetStatus(launched > 0
            ? $"작업 세트 '{name}': 이동 {placedBase}개 + 세션 {launched}개 재실행 중… (창 뜨면 자동 배치)"
            : $"작업 세트 '{name}': 세션 재실행 실패 — {lastErr}");

        _restoreTimer ??= CreateRestoreTimer();
        _restoreTimer.Stop();
        _restoreTimer.Start();
    }

    private System.Windows.Forms.Timer CreateRestoreTimer()
    {
        var t = new System.Windows.Forms.Timer { Interval = 500 };
        t.Tick += (_, _) => RestoreTick();
        return t;
    }

    /// <summary>재실행으로 새로 뜬 터미널을 순서대로 대기 슬롯에 배치. 모두 배치 or 마감시각 초과 시 종료.</summary>
    private void RestoreTick()
    {
        if (_restorePending is null || _restoreBaseline is null) { _restoreTimer?.Stop(); return; }
        _suppressWatchUntil = Environment.TickCount + 3000; // 배치 중 감시 억제 연장

        List<TerminalWindow> found;
        try { found = WindowDiscovery.Discover(_gptNames, _claudeTitleMarkers, _gptTitleMarkers); }
        catch { return; }

        // 새로 뜬 창(기준선에 없던 것)을 순서대로 대기 슬롯에 배치(TakeNewWindows가 baseline에 추가 → 재사용 방지)
        var currentIds = found.Select(t => t.Handle.ToInt64()).ToList();
        var newIds = WorkSetPlanner.TakeNewWindows(_restoreBaseline, currentIds, _restorePending.Count);
        foreach (var id in newIds)
        {
            var slot = _restorePending[0];
            _restorePending.RemoveAt(0);
            TilingEngine.MoveWindowVisible(new IntPtr(id), new Rectangle(slot.X, slot.Y, slot.W, slot.H));
        }

        if (_restorePending.Count == 0 || Environment.TickCount > _restoreDeadline)
        {
            _restoreTimer?.Stop();
            int restored = _restorePendingInit - _restorePending.Count;
            int waiting = _restorePending.Count;
            _restorePending = null;
            _restoreBaseline = null;
            _suppressWatchUntil = Environment.TickCount + 1500;
            RefreshList();
            SetStatus(waiting == 0
                ? $"작업 세트 적용 완료: '{_restoreName}' — 이동 {_restorePlacedBase}개 + 재실행 배치 {restored}개."
                : $"작업 세트 적용: '{_restoreName}' — 이동 {_restorePlacedBase}개 + 재실행 배치 {restored}개 (미출현 {waiting}개는 시간초과).");
        }
    }

    /// <summary>작업 세트 삭제.</summary>
    private void DeleteWorkSet(string name)
    {
        var updated = _workSets.Where(w => !string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        try { _workSetStore.Save(updated); }
        catch (Exception e) { SetStatus($"작업 세트 삭제 실패: {e.Message}"); return; }
        _workSets = updated;
        RefreshWorkSets();
        SetStatus($"작업 세트 삭제: '{name}'.");
    }

    // ==== 감시 모드(새 창 자동 배치) ====
    /// <summary>옵션 창/외부용 감시 모드 토글.</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool WatchMode
    {
        get => _watchMode;
        set
        {
            _watchMode = value;
            if (_chkWatch is not null) _chkWatch.Checked = value;
            _watcher.SetEnabled(value);
            if (_uiReady) SaveConfig();
        }
    }

    /// <summary>
    /// 감시 폴 틱: 억제 구간이 아니면 가벼운 재탐지로 터미널 집합 변화만 확인하고,
    /// 바뀐 경우에만 UI 갱신 + 전 창 자동 재배치(평상시엔 UI를 건드리지 않아 깜빡임 없음).
    /// </summary>
    private void OnWatcherChanged()
    {
        if (!_watchMode || IsDisposed || !_uiReady) return;
        if (Environment.TickCount < _suppressWatchUntil) return;

        // UI 갱신 없이 핸들 집합만 비교(EnumWindows는 가벼움)
        List<TerminalWindow> found;
        try { found = WindowDiscovery.Discover(_gptNames, _claudeTitleMarkers, _gptTitleMarkers); }
        catch { return; }

        var after = found.Select(t => t.Handle).ToHashSet();
        var before = _terminals.Select(t => t.Handle).ToHashSet();
        if (after.SetEquals(before)) return;        // 변화 없음 → UI/배치 건드리지 않음

        RefreshList();                               // 집합이 바뀐 경우만 UI 갱신
        if (_terminals.Count == 0) return;

        var allTargets = _terminals.Select(t => t.Handle).ToList();
        DoArrange(_currentDirection, allTargets);    // 전 창 대상(체크 무시) — 새 창 포함 재배치
        SetStatus($"감시 모드: 창 변화 감지 → 자동 배치({DirName(_currentDirection)}, {allTargets.Count}개).");
    }

    private static string DirName(LayoutDirection d) => d switch
    {
        LayoutDirection.Horizontal => "가로",
        LayoutDirection.Vertical => "세로",
        _ => "그리드"
    };

    private void SetStatus(string msg) => _statusLabel.Text = msg;
}
