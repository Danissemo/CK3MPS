using System;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private static readonly bool ReadOnlyScanLiveLogMutationGuardInstalled = InstallReadOnlyScanLiveLogMutationGuard();

        private static bool InstallReadOnlyScanLiveLogMutationGuard()
        {
            try
            {
                Application.Idle += delegate
                {
                    foreach (Form form in Application.OpenForms)
                    {
                        MainForm main = form as MainForm;
                        if (main != null)
                            main.GuardReadOnlyScanLiveLogState();
                    }
                };
            }
            catch
            {
            }
            return true;
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
