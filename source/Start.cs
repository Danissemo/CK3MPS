using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CK3MPS
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
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



