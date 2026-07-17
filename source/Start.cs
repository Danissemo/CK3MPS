using System;
using System.Diagnostics;
using System.Windows.Forms;
using System.Security.Principal;

namespace CK3MPS
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                if (SafeUpdater.TryHandleCommandLine(args))
                    return;
            }
            catch (Exception ex)
            {
                if (!SuppressUpdaterUi())
                {
                    try
                    {
                        MessageBox.Show("CK3MPS updater failed:\r\n" + ex.Message, "CK3MPS updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch { }
                }
                Environment.ExitCode = 1;
                return;
            }

            // The stabilizer edits firewall, registry, adapter, launcher, and game settings.
            // Request elevation at startup so every selected action has a predictable privilege level.
            if (!SkipElevationForTestRun() && !IsAdministrator())
            {
                try
                {
                    ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath);
                    info.UseShellExecute = true;
                    info.Verb = "runas";
                    Process.Start(info);
                    return;
                }
                catch
                {
                    MessageBox.Show("Administrator rights are required. Start the program again and approve the Windows UAC prompt.", "CK3MPS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static bool SuppressUpdaterUi()
        {
            string value = Environment.GetEnvironmentVariable("CK3MPS_SUPPRESS_UPDATER_UI");
            return String.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SkipElevationForTestRun()
        {
            string value = Environment.GetEnvironmentVariable("CK3MPS_SKIP_ELEVATION");
            return String.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
