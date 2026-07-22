using System.Drawing;
using System.Windows.Forms;
using TileCLI.Models;
using TileCLI.Services;

namespace TileCLI.UI;

/// <summary>
/// 동작별 전역 단축키 설정 대화상자(요구 3). 저장 시 앱 내부 조합 중복을 검사하고(요구 4),
/// 최종 등록 실패(다른 앱이 이미 점유)는 호출부가 안내한다.
/// </summary>
public sealed class HotkeySettingsForm : Form
{
    private static readonly HotkeyAction[] Actions =
        { HotkeyAction.Auto, HotkeyAction.Horizontal, HotkeyAction.Vertical, HotkeyAction.Grid };

    private readonly Dictionary<HotkeyAction, Row> _rows = new();

    /// <summary>OK로 닫혔을 때의 편집 결과(단축키 바인딩만 채워짐).</summary>
    public Dictionary<string, HotkeyBinding> Result { get; } = new();

    private sealed class Row
    {
        public CheckBox Enabled = null!;
        public CheckBox Ctrl = null!;
        public CheckBox Alt = null!;
        public CheckBox Shift = null!;
        public CheckBox Win = null!;
        public TextBox KeyField = null!;
        public int KeyCode;
    }

    public HotkeySettingsForm(AppSettings settings)
    {
        Text = "전역 단축키 설정";
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 300);

        BuildUi(settings);
        Theme.Apply(this);
        HandleCreated += (_, _) => Theme.UseDarkTitleBar(this);
    }

    private void BuildUi(AppSettings settings)
    {
        var grid = new TableLayoutPanel
        {
            Location = new Point(12, 12),
            Size = new Size(536, 200),
            ColumnCount = 7,
            RowCount = Actions.Length + 1
        };
        int[] widths = { 96, 52, 50, 50, 54, 48, 176 };
        foreach (var w in widths) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));

        // 헤더
        AddHeader(grid, "동작", 0);
        AddHeader(grid, "사용", 1);
        AddHeader(grid, "Ctrl", 2);
        AddHeader(grid, "Alt", 3);
        AddHeader(grid, "Shift", 4);
        AddHeader(grid, "Win", 5);
        AddHeader(grid, "키 (클릭 후 누르기)", 6);

        for (int i = 0; i < Actions.Length; i++)
        {
            var action = Actions[i];
            var b = settings.Hotkeys.TryGetValue(action.ToString(), out var found) && found is not null
                ? found.Clone()
                : new HotkeyBinding();

            var row = new Row { KeyCode = b.KeyCode };

            var lbl = new Label { Text = HotkeyManager.ActionName(action), AutoSize = false, Size = new Size(90, 24), TextAlign = ContentAlignment.MiddleLeft };
            row.Enabled = MakeCheck(b.Enabled);
            row.Ctrl = MakeCheck(b.Ctrl);
            row.Alt = MakeCheck(b.Alt);
            row.Shift = MakeCheck(b.Shift);
            row.Win = MakeCheck(b.Win);
            row.KeyField = new TextBox
            {
                ReadOnly = true,
                Width = 168,
                Text = b.KeyCode == 0 ? "(없음)" : KeyDisplay(b.KeyCode),
                Cursor = Cursors.Hand,
                TextAlign = HorizontalAlignment.Center,
                BackColor = SystemColors.Window
            };
            row.KeyField.KeyDown += (s, e) => OnKeyFieldKeyDown(row, e);
            row.KeyField.Enter += (s, e) => row.KeyField.BackColor = Color.LightYellow;
            row.KeyField.Leave += (s, e) => row.KeyField.BackColor = SystemColors.Window;

            int r = i + 1;
            grid.Controls.Add(lbl, 0, r);
            grid.Controls.Add(Wrap(row.Enabled), 1, r);
            grid.Controls.Add(Wrap(row.Ctrl), 2, r);
            grid.Controls.Add(Wrap(row.Alt), 3, r);
            grid.Controls.Add(Wrap(row.Shift), 4, r);
            grid.Controls.Add(Wrap(row.Win), 5, r);
            grid.Controls.Add(row.KeyField, 6, r);

            _rows[action] = row;
        }

        var hint = new Label
        {
            Location = new Point(12, 220),
            Size = new Size(536, 34),
            ForeColor = SystemColors.GrayText,
            Text = "· 키 칸을 클릭하고 원하는 키를 누르세요. 수식키(Ctrl/Alt/Shift/Win)는 최소 1개 필요.\n· 저장 시 앱 내 중복과 다른 프로그램과의 충돌을 검사합니다."
        };

        var btnOk = new RoundedButton { Text = "저장", Size = new Size(90, 30), Location = new Point(356, 260), DialogResult = DialogResult.None };
        btnOk.Click += OnOk;
        var btnCancel = new RoundedButton { Text = "취소", Size = new Size(90, 30), Location = new Point(456, 260), DialogResult = DialogResult.Cancel };

        Controls.Add(grid);
        Controls.Add(hint);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private static Label AddHeader(TableLayoutPanel grid, string text, int col)
    {
        var l = new Label { Text = text, AutoSize = false, Size = new Size(90, 22), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
        grid.Controls.Add(l, col, 0);
        return l;
    }

    private static CheckBox MakeCheck(bool value) =>
        new() { Checked = value, AutoSize = true, Anchor = AnchorStyles.None };

    // CheckBox를 셀 중앙에 두기 위한 래퍼
    private static Panel Wrap(Control c)
    {
        var p = new Panel { Dock = DockStyle.Fill, Height = 24 };
        c.Anchor = AnchorStyles.None;
        c.Location = new Point((50 - c.PreferredSize.Width) / 2, 2);
        p.Controls.Add(c);
        return p;
    }

    private void OnKeyFieldKeyDown(Row row, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        var key = e.KeyCode;
        // 수식키/시스템키 단독은 주 키로 받지 않음
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
            or Keys.None or Keys.Capital or Keys.NumLock or Keys.Scroll)
            return;

        if (key == Keys.Back || key == Keys.Delete)
        {
            row.KeyCode = 0;
            row.KeyField.Text = "(없음)";
            return;
        }

        row.KeyCode = (int)key;
        row.KeyField.Text = KeyDisplay(row.KeyCode);
        // 편의: 눌린 수식키를 체크박스에 반영
        if (e.Control) row.Ctrl.Checked = true;
        if (e.Alt) row.Alt.Checked = true;
        if (e.Shift) row.Shift.Checked = true;
    }

    private static string KeyDisplay(int keyCode) => new HotkeyBinding { KeyCode = keyCode }.Combo;

    private void OnOk(object? sender, EventArgs e)
    {
        var built = new Dictionary<string, HotkeyBinding>();
        var enabledList = new List<(HotkeyAction action, HotkeyBinding b)>();

        foreach (var action in Actions)
        {
            var row = _rows[action];
            var b = new HotkeyBinding
            {
                Enabled = row.Enabled.Checked,
                Ctrl = row.Ctrl.Checked,
                Alt = row.Alt.Checked,
                Shift = row.Shift.Checked,
                Win = row.Win.Checked,
                KeyCode = row.KeyCode
            };
            built[action.ToString()] = b;

            if (b.Enabled)
            {
                if (!b.IsComplete)
                {
                    MessageBox.Show(this,
                        $"'{HotkeyManager.ActionName(action)}' 단축키가 완성되지 않았습니다.\n수식키(Ctrl/Alt/Shift/Win) 1개 이상과 주 키를 지정하세요.",
                        "단축키 설정", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                enabledList.Add((action, b));
            }
        }

        // 앱 내부 중복 검사(요구 4): 활성 바인딩끼리 같은 조합 금지
        for (int i = 0; i < enabledList.Count; i++)
            for (int j = i + 1; j < enabledList.Count; j++)
                if (enabledList[i].b.SameCombo(enabledList[j].b))
                {
                    MessageBox.Show(this,
                        $"같은 단축키가 중복됩니다: {enabledList[i].b.Combo}\n" +
                        $"· {HotkeyManager.ActionName(enabledList[i].action)}\n" +
                        $"· {HotkeyManager.ActionName(enabledList[j].action)}\n\n" +
                        "서로 다른 조합으로 지정하세요.",
                        "단축키 중복", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

        Result.Clear();
        foreach (var kv in built) Result[kv.Key] = kv.Value;
        DialogResult = DialogResult.OK;
        Close();
    }
}
