using Adnd.Core.Config;
using System.Drawing;
using System.Windows.Forms;

namespace Adnd.Game;

public sealed class RuleApplicationInfoForm : Form
{
    private readonly TextBox _logBox;

    public RuleApplicationInfoForm()
    {
        Text = "AD&D Rule and Dice Info";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(700, 420);
        ShowInTaskbar = true;
        TopMost = true;

        _logBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 10f)
        };

        Controls.Add(_logBox);
    }

    public void AppendInfo(string message)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendInfo), message);
            return;
        }

        if (!string.IsNullOrWhiteSpace(message))
            _logBox.AppendText(message + Environment.NewLine);
    }
}
