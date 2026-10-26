using System;
using System.Windows.Forms;

namespace CleanCache1C
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // --profile <папка профиля>: открыть произвольный профиль, например скопированный с другой машины
            string profileDir = null;
            int i = Array.FindIndex(args, a => a.Equals("--profile", StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && i + 1 < args.Length)
                profileDir = args[i + 1];

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(profileDir));
        }
    }
}
