namespace GopCadApp;

internal sealed class LoginForm : Form
{
    private readonly TextBox _email = new() { Font = new Font("Segoe UI", 10f) };
    private readonly Button _login = new() { Text = "Đăng nhập" };
    private readonly Label _error = new() { ForeColor = Color.Firebrick, AutoSize = false };

    public LoginForm()
    {
        Text = "BIMLab DWG Merge - Đăng nhập";
        ClientSize = new Size(420, 210);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        Label label = new() { Text = "Nhập Gmail đã được cấp quyền:", AutoSize = true, Left = 20, Top = 20 };

        _email.Left = 20;
        _email.Top = 44;
        _email.Width = 380;
        _email.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = TryLoginAsync();
            }
        };

        _error.Left = 20;
        _error.Top = 78;
        _error.Width = 380;
        _error.Height = 40;

        _login.Left = 20;
        _login.Top = 130;
        _login.Width = 380;
        _login.Height = 34;
        _login.Click += (_, _) => _ = TryLoginAsync();

        Controls.AddRange([label, _email, _error, _login]);
        AcceptButton = _login;
    }

    private async Task TryLoginAsync()
    {
        _login.Enabled = false;
        _email.Enabled = false;
        _login.Text = "Đang kiểm tra...";
        _error.Text = "";

        (bool ok, string _, string error) = await AuthGate.TryLoginAsync(_email.Text);

        if (ok)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _error.Text = error;
        _login.Enabled = true;
        _email.Enabled = true;
        _login.Text = "Đăng nhập";
    }
}
