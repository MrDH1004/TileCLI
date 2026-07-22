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
    private RadioButton _rbDark = null!;
    private RadioButton _rbLight = null!;
    private ComboBox _cboProfiles = null!;
    private TextBox _txtProfileName = null!;
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
        ClientSize = new Size(452, 476);
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
        var gGeneral = Card("일반", 12, 12, 428, 84);
        _chkTray = new CheckBox { Text = "X로 트레이 최소화 (완전 종료는 트레이 메뉴)", AutoSize = true, Location = new Point(16, 30), Checked = _main.MinimizeToTray };
        _chkTray.CheckedChanged += (_, _) => { if (!_loading) _main.MinimizeToTray = _chkTray.Checked; };
        _chkAutoStart = new CheckBox { Text = "Windows 시작 시 자동 실행", AutoSize = true, Location = new Point(16, 54), Checked = _main.AutoStartEnabled };
        _chkAutoStart.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _main.SetAutoStart(_chkAutoStart.Checked);
            _loading = true; _chkAutoStart.Checked = _main.AutoStartEnabled; _loading = false; // 실제 상태로 재동기화
        };
        gGeneral.Controls.Add(_chkTray);
        gGeneral.Controls.Add(_chkAutoStart);

        // ── 테마 ──
        var gTheme = Card("테마", 12, 104, 428, 64);
        _rbDark = new RadioButton { Text = "다크", AutoSize = true, Location = new Point(16, 30), Checked = _main.CurrentThemeMode == ThemeMode.Dark };
        _rbLight = new RadioButton { Text = "라이트", AutoSize = true, Location = new Point(120, 30), Checked = _main.CurrentThemeMode == ThemeMode.Light };
        // Click은 실제 사용자 조작에만 발화(프로그램적 Checked 변경으로는 안 뜸) → 표시 중 오발화 방지
        _rbDark.Click += (_, _) => _main.SetThemeMode(ThemeMode.Dark);
        _rbLight.Click += (_, _) => _main.SetThemeMode(ThemeMode.Light);
        gTheme.Controls.Add(_rbDark);
        gTheme.Controls.Add(_rbLight);

        // ── 단축키 ──
        var gHotkey = Card("전역 단축키", 12, 180, 428, 74);
        var lblHk = new Label { Text = "자동/가로/세로/그리드에 전역 단축키를 지정합니다.", AutoSize = true, Location = new Point(16, 30) };
        var btnHk = MakeButton("단축키 설정 열기…", 150, (_, _) => _main.OpenHotkeySettings());
        btnHk.Location = new Point(16, 44);
        gHotkey.Controls.Add(lblHk);
        gHotkey.Controls.Add(btnHk);

        // ── 프로파일 ──
        var gProfile = Card("프로파일 (배치 스타일)", 12, 266, 428, 150);
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

        // 닫기
        var btnClose = MakeButton("닫기", 90, (_, _) => Close());
        btnClose.Location = new Point(ClientSize.Width - 106, ClientSize.Height - 38);

        Controls.Add(gGeneral);
        Controls.Add(gTheme);
        Controls.Add(gHotkey);
        Controls.Add(gProfile);
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
        Invalidate(true);
    }
}
