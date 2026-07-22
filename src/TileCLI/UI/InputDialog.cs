using System.Drawing;
using System.Windows.Forms;

namespace TileCLI.UI;

/// <summary>
/// 작고 테마가 적용된 한 줄 입력 다이얼로그. VB Interaction.InputBox의 크고 밋밋한 창을 대체.
/// </summary>
public static class InputDialog
{
    /// <summary>확인 시 입력 문자열(트림), 취소/빈값 시 null.</summary>
    public static string? Show(IWin32Window owner, string title, string prompt, string defaultText = "")
    {
        using var f = Build(title, prompt, defaultText, out var txt);
        f.HandleCreated += (_, _) => Theme.UseDarkTitleBar(f);
        f.Shown += (_, _) => { txt.Focus(); txt.SelectAll(); };
        return f.ShowDialog(owner) == DialogResult.OK ? txt.Text.Trim() : null;
    }

    /// <summary>다이얼로그 폼 구성(모달 표시는 Show가 담당). 헤드리스 레이아웃 검증에도 사용.</summary>
    internal static Form Build(string title, string prompt, string defaultText, out TextBox txt)
    {
        var f = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(316, 116),
            Font = new Font("Segoe UI", 9F)
        };
        var lbl = new Label { Text = prompt, AutoSize = true, Location = new Point(12, 14) };
        txt = new TextBox { Location = new Point(12, 38), Size = new Size(292, 24), Text = defaultText };
        var ok = new RoundedButton { Text = "확인", Size = new Size(84, 30), Location = new Point(132, 74), DialogResult = DialogResult.OK };
        var cancel = new RoundedButton { Text = "취소", Size = new Size(84, 30), Location = new Point(220, 74), DialogResult = DialogResult.Cancel };

        f.Controls.Add(lbl);
        f.Controls.Add(txt);
        f.Controls.Add(ok);
        f.Controls.Add(cancel);
        f.AcceptButton = ok;
        f.CancelButton = cancel;

        Theme.Apply(f);
        return f;
    }
}
