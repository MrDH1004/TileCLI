using System.Drawing;
using System.Windows.Forms;

namespace TileCLI.UI;

/// <summary>
/// 새 프로젝트 다이얼로그: 작업 폴더(상위) + 프로젝트 이름(새 폴더) + 시작할 CLI(Claude/GPT)를 받는다.
/// 작업 폴더와 마지막 CLI 선택은 호출 측이 config에 기억해 다음에 기본값으로 넘긴다.
/// </summary>
internal sealed class NewProjectDialog : Form
{
    private readonly TextBox _root;
    private readonly TextBox _name;
    private readonly RadioButton _claude;
    private readonly RadioButton _gpt;

    public string Root => _root.Text.Trim();
    public string ProjectName => _name.Text.Trim();
    public bool UseGpt => _gpt.Checked;

    public NewProjectDialog(string root, bool gpt)
    {
        Text = "새 프로젝트";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 218);
        Font = new Font("Segoe UI", 9F);

        var lblRoot = new Label { Text = "작업 폴더", AutoSize = true, Location = new Point(12, 14) };
        _root = new TextBox { Location = new Point(12, 36), Size = new Size(310, 24), Text = root };
        var browse = new RoundedButton { Text = "찾아보기…", Size = new Size(84, 28), Location = new Point(324, 34) };
        browse.Click += (_, _) => Browse();

        var lblName = new Label { Text = "프로젝트 이름 (작업 폴더 안에 새 폴더로 만듦)", AutoSize = true, Location = new Point(12, 72) };
        _name = new TextBox { Location = new Point(12, 94), Size = new Size(396, 24) };

        var lblKind = new Label { Text = "어떤 CLI로 시작할까요?", AutoSize = true, Location = new Point(12, 130) };
        _claude = new RadioButton { Text = "Claude", AutoSize = true, Location = new Point(16, 152), Checked = !gpt };
        _gpt = new RadioButton { Text = "GPT (codex)", AutoSize = true, Location = new Point(120, 152), Checked = gpt };

        var ok = new RoundedButton { Text = "만들고 시작", Size = new Size(100, 30), Location = new Point(216, 178) };
        var cancel = new RoundedButton { Text = "취소", Size = new Size(84, 30), Location = new Point(324, 178), DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => { if (Validate(out string msg)) { DialogResult = DialogResult.OK; Close(); } else ShowError(msg); };

        Controls.AddRange(new Control[] { lblRoot, _root, browse, lblName, _name, lblKind, _claude, _gpt, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;

        Theme.Apply(this);
        HandleCreated += (_, _) => Theme.UseDarkTitleBar(this);
        Shown += (_, _) => (string.IsNullOrWhiteSpace(_root.Text) ? _root : _name).Focus();
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "프로젝트를 만들 작업 폴더",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(Root) ? Root : string.Empty
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) _root.Text = dlg.SelectedPath;
    }

    private bool Validate(out string msg)
    {
        msg = string.Empty;
        if (Root.Length == 0 || !Directory.Exists(Root)) { msg = "작업 폴더를 찾을 수 없습니다. 찾아보기로 선택하세요."; return false; }
        string n = ProjectName;
        if (n.Length == 0) { msg = "프로젝트 이름을 입력하세요."; return false; }
        if (n is "." or ".." || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        { msg = "프로젝트 이름에 폴더 이름으로 쓸 수 없는 문자가 있습니다."; return false; }
        return true;
    }

    private void ShowError(string msg) =>
        MessageBox.Show(this, msg, "새 프로젝트", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
