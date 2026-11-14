using System;
using System.Drawing;
using System.Windows.Forms;
using Adnd.Core.Config;

namespace Adnd.Game;

/// <summary>The small framed "AN ENCOUNTER" notice shown when monsters are met, before the fight screen.</summary>
internal static class EncounterBanner
{
    public static void Show(IWin32Window? owner, string text = "AN ENCOUNTER", int autoCloseMilliseconds = 1500)
    {
        using var form = new Form
        {
            Text = text,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            KeyPreview = true,
            ClientSize = new Size(380, 80),
        };

        var frame = new OldStyleFramePanel
        {
            Left = 4,
            Top = 4,
            Width = form.ClientSize.Width - 8,
            Height = form.ClientSize.Height - 8,
            BackColor = Color.Black
        };

        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Black,
            ForeColor = GameRulesProvider.Current.DefaultColor,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };

        frame.Controls.Add(label);
        form.Controls.Add(frame);

        var timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, autoCloseMilliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            form.Close();
        };

        void Dismiss()
        {
            timer.Stop();
            form.Close();
        }

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Escape or Keys.Space)
                Dismiss();
        };
        label.MouseClick += (_, _) => Dismiss();
        frame.MouseClick += (_, _) => Dismiss();
        form.Shown += (_, _) => timer.Start();

        form.ShowDialog(owner);
        timer.Dispose();
    }
}
