namespace AeroficsLauncher;

public sealed class InstallDialog : Form
{
    public InstallDialog(string description)
    {
        Text = "安装 aerofics 优化器到桌面？";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(510, 240);
        BackColor = Color.FromArgb(19, 33, 55);
        ForeColor = Color.FromArgb(230, 240, 255);
        Font = new Font("Microsoft YaHei UI", 10);
        var heading = new Label { Text = "安装 aerofics 优化器到桌面？", Font = new Font(Font.FontFamily, 13, FontStyle.Bold), Bounds = new Rectangle(24, 25, 465, 40) };
        var detail = new Label { Text = description, Bounds = new Rectangle(24, 78, 465, 80) };
        var cancel = new Button { Name = "CancelInstall", Text = "取消", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(233, 179, 106, 40), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(28, 43, 65) };
        var confirm = new Button { Name = "ConfirmInstall", Text = "同意并复制", DialogResult = DialogResult.OK, Bounds = new Rectangle(351, 179, 135, 40), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(128, 218, 249), ForeColor = Color.FromArgb(16, 33, 54) };
        Controls.AddRange(new Control[] { heading, detail, cancel, confirm });
        CancelButton = cancel;
        // Keep Enter on Cancel until the user deliberately selects the affirmative action.
        ActiveControl = cancel;
    }
}
