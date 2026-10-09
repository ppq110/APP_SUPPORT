using System;
using System.Windows.Forms;

namespace GopCadApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            AuthGate.Prewarm();

            ApplicationConfiguration.Initialize();
            Application.ThreadException += (_, e) => ShowStartupError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    WriteStartupLog(ex);
                }
            };

            using (LoginForm login = new())
            {
                if (login.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
            }

            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            ShowStartupError(ex);
        }
    }

    private static void ShowStartupError(Exception ex)
    {
        WriteStartupLog(ex);
        MessageBox.Show(
            ex.ToString(),
            "BIMLab DWG Merge - Loi khoi dong",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static void WriteStartupLog(Exception ex)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BIMLab DWG Merge");
            Directory.CreateDirectory(folder);
            File.WriteAllText(
                Path.Combine(folder, "startup_error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{ex}",
                System.Text.Encoding.UTF8);
        }
        catch
        {
            // Do not let diagnostic logging hide the original startup error.
        }
    }
}
