using System.Drawing;
using System.Windows.Forms;

namespace TileCLI.UI;

/// <summary>
/// 옵션 창: 일반(트레이 최소화·인접 연동) + 테마 + 단축키 + 프로파일을 한곳에 모은다.
/// 실제 로직은 MainForm의 공개 API를 호출한다. MainForm이 소유하는 단일 인스턴스(비모달).
/// </summary>
public sealed class OptionsForm : Form
{
    private readonly MainForm _main;
    private CheckBox _chkTray = null!;
    private CheckBox _chkAutoStart = null!;
    private CheckBox _chkCliOnly = null!;
    private CheckBox _chkExcludeNonAi = null!;
    private RadioButton _rbDark = null!;
    private RadioButton _rbLight = null!;
    private ComboBox _cboProfiles = null!;
    private TextBox _txtProfileName = null!;
    private SnapZoneControl _anchorPick = null!;   // 시작 앵커(3×3, 첫 창 위치)
    private SnapZoneControl _fixedPreview = null!; // 폭 모드 + 앵커 미리보기
    private Button _owFull = null!, _owHalf = null!, _owThird = null!; // 폭 단위 버튼
    private SnapZoneControl.ZoneLayout _fixedW = SnapZoneControl.ZoneLayout.Full;
    private bool _loading;

    public OptionsForm(MainForm main)
    {
        _main = main;
        Text = "옵션";
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(452, 716);
        ShowInTaskbar = false;

        BuildUi();
        RefreshProfileList();

        Theme.Apply(this);
        HandleCreated += (_, _) => Theme.UseDarkTitleBar(this);
    }

    private void BuildUi()
    {
        _loading = true;

        // ── 일반 ──
        var gGeneral = Card("일반", 12, 12, 428, 132);
        _chkTray = new CheckBox { Text = "X로 트레이 최소화 (완전 종료는 트레이 메뉴)", AutoSize = true, Location = new Point(16, 30), Checked = _main.MinimizeToTray };
        _chkTray.CheckedChanged += (_, _) => { if (!_loading) _main.MinimizeToTray = _chkTray.Checked; };
        _chkAutoStart = new CheckBox { Text = "Windows 시작 시 자동 실행", AutoSize = true, Location = new Point(16, 54), Checked = _main.AutoStartEnabled };
        _chkAutoStart.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _main.SetAutoStart(_chkAutoStart.Checked);
            _loading = true; _chkAutoStart.Checked = _main.AutoStartEnabled; _loading = false; // 실제 상태로 재동기화
        };
        _chkCliOnly = new CheckBox { Text = "판별된 CLI(Claude·GPT) 창만 표시", AutoSize = true, Location = new Point(16, 78), Checked = _main.ClaudeGptOnly };
        _chkCliOnly.CheckedChanged += (_, _) => { if (!_loading) _main.ClaudeGptOnly = _chkCliOnly.Checked; };
        _chkExcludeNonAi = new CheckBox { Text = "AI(Claude·GPT) 아닌 창은 정렬에서 제외", AutoSize = true, Location = new Point(16, 102), Checked = _main.ExcludeNonAiFromArrange };
        _chkExcludeNonAi.CheckedChanged += (_, _) => { if (!_loading) _main.ExcludeNonAiFromArrange = _chkExcludeNonAi.Checked; };
        gGeneral.Controls.Add(_chkTray);
        gGeneral.Controls.Add(_chkAutoStart);
        gGeneral.Controls.Add(_chkCliOnly);
        gGeneral.Controls.Add(_chkExcludeNonAi);

        // ── 테마 ──
        var gTheme = Card("테마", 12, 152, 428, 64);
        _rbDark = new RadioButton { Text = "다크", AutoSize = true, Location = new Point(16, 30), Checked = _main.CurrentThemeMode == ThemeMode.Dark };
        _rbLight = new RadioButton { Text = "라이트", AutoSize = true, Location = new Point(120, 30), Checked = _main.CurrentThemeMode == ThemeMode.Light };
        // Click은 실제 사용자 조작에만 발화(프로그램적 Checked 변경으로는 안 뜸) → 표시 중 오발화 방지
        _rbDark.Click += (_, _) => _main.SetThemeMode(ThemeMode.Dark);
        _rbLight.Click += (_, _) => _main.SetThemeMode(ThemeMode.Light);
        gTheme.Controls.Add(_rbDark);
        gTheme.Controls.Add(_rbLight);

        // ── 단축키 ──
        var gHotkey = Card("전역 단축키", 12, 228, 428, 74);
        var lblHk = new Label { Text = "자동/가로/세로/그리드에 전역 단축키를 지정합니다.", AutoSize = true, Location = new Point(16, 30) };
        var btnHk = MakeButton("단축키 설정 열기…", 150, (_, _) => _main.OpenHotkeySettings());
        btnHk.Location = new Point(16, 44);
        gHotkey.Controls.Add(lblHk);
        gHotkey.Controls.Add(btnHk);

        // ── 프로파일 ──
        var gProfile = Card("프로파일 (배치 스타일)", 12, 314, 428, 150);
        _cboProfiles = new ComboBox { Location = new Point(16, 32), Size = new Size(404, 24), DropDownStyle = ComboBoxStyle.DropDownList };
        _cboProfiles.SelectedIndexChanged += (_, _) => { if (!_loading && _cboProfiles.SelectedItem is string s) _main.SelectedProfile = s; };
        _txtProfileName = new TextBox { Location = new Point(16, 66), Size = new Size(250, 24), PlaceholderText = "저장할 이름" };
        var btnSave = MakeButton("현재 저장", 140, (_, _) => { _main.SaveProfile(_txtProfileName.Text); _txtProfileName.Clear(); RefreshProfileList(); });
        btnSave.Location = new Point(280, 65); btnSave.Size = new Size(140, 26);
        var btnApply = MakeButton("적용", 198, (_, _) => { if (_cboProfiles.SelectedItem is string s) _main.ApplyProfile(s); });
        btnApply.Location = new Point(16, 104);
        var btnDelete = MakeButton("삭제", 198, (_, _) => { if (_cboProfiles.SelectedItem is string s) { _main.DeleteProfile(s); RefreshProfileList(); } });
        btnDelete.Location = new Point(222, 104);
        gProfile.Controls.Add(_cboProfiles);
        gProfile.Controls.Add(_txtProfileName);
        gProfile.Controls.Add(btnSave);
        gProfile.Controls.Add(btnApply);
        gProfile.Controls.Add(btnDelete);

        // ── 시작 앵커(폭 단위 배치 + 첫 창 위치) ──
        _fixedW = _main.FixedMode;
        var gAnchor = Card("시작 앵커", 12, 470, 428, 196);
        var lblAnchor = new Label { Text = "선택/체크한 창을 폭 단위로 배치하고, 첫 창이 놓일 위치(앵커)를 정합니다. 오른쪽 기준 권장.", AutoSize = false, Location = new Point(16, 22), Size = new Size(404, 32) };
        var lblW = new Label { Text = "폭", AutoSize = true, Location = new Point(16, 62) };
        _owFull = MakeWidthButton("1", SnapZoneControl.ZoneLayout.Full, 42, 58, "폭 1/1 — 전체 화면");
        _owHalf = MakeWidthButton("2", SnapZoneControl.ZoneLayout.Halves, 76, 58, "폭 1/2 — 2열");
        _owThird = MakeWidthButton("3", SnapZoneControl.ZoneLayout.Thirds, 110, 58, "폭 1/3 — 3열");
        var lblA = new Label { Text = "앵커(첫 창)", AutoSize = true, Location = new Point(16, 94) };
        _anchorPick = new SnapZoneControl { Location = new Point(16, 114), Size = new Size(150, 72), Interactive = true, ZoneMode = SnapZoneControl.ZoneLayout.Grid3x3 };
        _anchorPick.SetSelectedCell(_main.AnchorCol, _main.AnchorRow);
        _anchorPick.CellPicked += (_, cr) => { if (!_loading) { _main.SetAnchor(cr.col, cr.row); UpdateAnchorPreview(); } };
        var lblP = new Label { Text = "미리보기", AutoSize = true, Location = new Point(180, 94) };
        _fixedPreview = new SnapZoneControl { Location = new Point(180, 114), Size = new Size(232, 72), Interactive = false };
        gAnchor.Controls.Add(lblAnchor);
        gAnchor.Controls.Add(lblW);
        gAnchor.Controls.Add(_owFull); gAnchor.Controls.Add(_owHalf); gAnchor.Controls.Add(_owThird);
        gAnchor.Controls.Add(lblA);
        gAnchor.Controls.Add(_anchorPick);
        gAnchor.Controls.Add(lblP);
        gAnchor.Controls.Add(_fixedPreview);
        UpdateWidthButtons();
        UpdateAnchorPreview();

        // 닫기
        var btnClose = MakeButton("닫기", 90, (_, _) => Close());
        btnClose.Location = new Point(ClientSize.Width - 106, ClientSize.Height - 38);

        Controls.Add(gGeneral);
        Controls.Add(gTheme);
        Controls.Add(gHotkey);
        Controls.Add(gProfile);
        Controls.Add(gAnchor);
        Controls.Add(btnClose);

        _loading = false;
    }

    private static DarkGroupBox Card(string title, int x, int y, int w, int h) =>
        new() { Text = title, Location = new Point(x, y), Size = new Size(w, h) };

    private static Button MakeButton(string text, int width, EventHandler onClick)
    {
        var b = new RoundedButton { Text = text, Width = width, Height = 28 };
        b.Click += onClick;
        return b;
    }

    private Button MakeWidthButton(string text, SnapZoneControl.ZoneLayout mode, int x, int y, string tip)
    {
        var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(30, 26), TabStop = false };
        Theme.StyleButton(b);
        b.Click += (_, _) =>
        {
            if (_loading) return;
            _fixedW = mode;
            _main.ApplyFixedPlacement(mode); // 메인 창의 선택/체크 창을 이 폭으로 배치
            UpdateWidthButtons();
            UpdateAnchorPreview();
        };
        return b;
    }

    /// <summary>폭 버튼 활성/비활성 강조.</summary>
    private void UpdateWidthButtons()
    {
        void Style(Button b, bool active)
        {
            b.BackColor = active ? Theme.AccentDeep : Theme.Bg3;
            b.ForeColor = active ? Theme.Gold : Theme.Text;
            b.FlatAppearance.BorderColor = active ? Theme.Gold : Theme.Border;
        }
        Style(_owFull, _fixedW == SnapZoneControl.ZoneLayout.Full);
        Style(_owHalf, _fixedW == SnapZoneControl.ZoneLayout.Halves);
        Style(_owThird, _fixedW == SnapZoneControl.ZoneLayout.Thirds);
    }

    /// <summary>앵커 강조 + 미리보기(현재 폭 모드 + 앵커) 갱신.</summary>
    private void UpdateAnchorPreview()
    {
        _anchorPick.SetSelectedCell(_main.AnchorCol, _main.AnchorRow);
        _main.ConfigurePreview(_fixedPreview, _fixedW);
    }

    /// <summary>프로파일 목록 재적재(선택 유지).</summary>
    public void RefreshProfileList()
    {
        bool prev = _loading;
        _loading = true;
        _cboProfiles.Items.Clear();
        foreach (var n in _main.ProfileNames) _cboProfiles.Items.Add(n);
        var sel = _main.SelectedProfile;
        if (!string.IsNullOrEmpty(sel) && _cboProfiles.Items.Contains(sel)) _cboProfiles.SelectedItem = sel;
        else if (_cboProfiles.Items.Count > 0) _cboProfiles.SelectedIndex = 0;
        _loading = prev;
    }

    /// <summary>테마 전환 시 이 창도 재스타일(MainForm이 호출).</summary>
    public void ReapplyTheme()
    {
        _loading = true;
        _rbDark.Checked = _main.CurrentThemeMode == ThemeMode.Dark;
        _rbLight.Checked = _main.CurrentThemeMode == ThemeMode.Light;
        _loading = false;
        Theme.Apply(this);
        if (IsHandleCreated) Theme.UseDarkTitleBar(this);
        UpdateWidthButtons();   // 폭 버튼 강조 재적용
        UpdateAnchorPreview();  // 미리보기 재적용
        Invalidate(true);
    }
}
