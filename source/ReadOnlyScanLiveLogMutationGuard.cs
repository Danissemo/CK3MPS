using System;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private static readonly bool ReadOnlyScanLiveLogMutationGuardInstalled = InstallReadOnlyScanLiveLogMutationGuard();
        private static ReadOnlyScanMessageFilter readOnlyScanMessageFilter;
        private bool readOnlyScanButtonPreClickGuardAttached;

        private sealed class ReadOnlyScanMessageFilter : IMessageFilter
        {
            private const int WM_LBUTTONDOWN = 0x0201;
            private const int WM_LBUTTONDBLCLK = 0x0203;
            private const int WM_KEYDOWN = 0x0100;

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_LBUTTONDOWN && m.Msg != WM_LBUTTONDBLCLK && m.Msg != WM_KEYDOWN)
                    return false;

                foreach (Form form in Application.OpenForms)
                {
                    MainForm main = form as MainForm;
                    if (main == null)
                        continue;

                    if (m.Msg == WM_KEYDOWN)
                    {
                        Keys key = (Keys)((int)m.WParam) & Keys.KeyCode;
                        if (key == Keys.Enter || key == Keys.Space)
                            main.PrepareReadOnlyScanButtonBeforeClick(IntPtr.Zero, true);
                    }
                    else
                    {
                        main.PrepareReadOnlyScanButtonBeforeClick(m.HWnd, false);
                    }
                }

                return false;
            }
        }

        private static bool InstallReadOnlyScanLiveLogMutationGuard()
        {
            try
            {
                if (readOnlyScanMessageFilter == null)
                {
                    readOnlyScanMessageFilter = new ReadOnlyScanMessageFilter();
                    Application.AddMessageFilter(readOnlyScanMessageFilter);
                }

                Application.Idle += delegate
                {
                    foreach (Form form in Application.OpenForms)
                    {
                        MainForm main = form as MainForm;
                        if (main != null)
                        {
                            main.AttachReadOnlyScanButtonPreClickGuard();
                            main.GuardReadOnlyScanLiveLogState();
                        }
                    }
                };
            }
            catch
            {
            }
            return true;
        }

        private void AttachReadOnlyScanButtonPreClickGuard()
        {
            if (readOnlyScanButtonPreClickGuardAttached || checkButton == null)
                return;

            readOnlyScanButtonPreClickGuardAttached = true;
            checkButton.MouseDown += delegate { PrepareReadOnlyScanButtonBeforeClick(checkButton.Handle, false); };
            checkButton.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                    PrepareReadOnlyScanButtonBeforeClick(checkButton.Handle, true);
            };
        }

        private void PrepareReadOnlyScanButtonBeforeClick(IntPtr targetHandle, bool keyboard)
        {
            if (checkButton == null || busyUi)
                return;

            bool isScanButton = keyboard
                ? checkButton.Focused
                : targetHandle == checkButton.Handle;
            if (!isScanButton)
                return;

            if (!String.Equals(checkButton.Text, "Scan Settings", StringComparison.OrdinalIgnoreCase))
                return;

            // This runs before the button Click event. If another runtime module rebound the button
            // back to the legacy Scan handler, replace it now and disable file-backed LiveLog output
            // before read-only Scan can append to CK3MPS\\LiveLogs.
            ConfigureScanExportRuntimeFix();
            liveLogWritesEnabled = false;
            liveLogBuffer.Length = 0;
        }

        private void GuardReadOnlyScanLiveLogState()
        {
            if (!readOnlyScanMode)
                return;

            liveLogWritesEnabled = false;
            liveLogBuffer.Length = 0;
        }
    }
}
