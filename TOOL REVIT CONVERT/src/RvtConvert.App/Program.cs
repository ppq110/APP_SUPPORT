using System;
using System.Windows.Forms;

namespace RvtConvert.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Nhúng OpenMcdf.dll trong exe để tool chỉ là 1 file duy nhất.
            AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
            {
                if (!a.Name.StartsWith("OpenMcdf", StringComparison.OrdinalIgnoreCase)) return null;
                using (var st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("OpenMcdf.dll"))
                {
                    if (st == null) return null;
                    var buf = new byte[st.Length]; st.Read(buf, 0, buf.Length);
                    return System.Reflection.Assembly.Load(buf);
                }
            };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }
}


