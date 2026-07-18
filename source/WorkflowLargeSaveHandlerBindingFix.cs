using System;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private bool workflowLargeSaveBindingWatcherConfigured;
        private bool workflowLargeSaveBindingRefreshQueued;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ConfigureLargeSaveBindingWatcher();
        }

        private void ConfigureLargeSaveBindingWatcher()
        {
            if (workflowLargeSaveBindingWatcherConfigured || workflowApplySafeStartButton == null)
                return;

            workflowLargeSaveBindingWatcherConfigured = true;
            workflowApplySafeStartButton.TextChanged += delegate { QueueLargeSaveBindingRefresh(); };
            workflowApplySafeStartButton.EnabledChanged += delegate { QueueLargeSaveBindingRefresh(); };
            workflowApplySafeStartButton.MouseDown += delegate { ConfigureAnalyzerOverlaySaveHostFixHandler(); };
            workflowApplySafeStartButton.KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                    ConfigureAnalyzerOverlaySaveHostFixHandler();
            };
            if (workflowModeBox != null)
                workflowModeBox.SelectedIndexChanged += delegate { QueueLargeSaveBindingRefresh(); };
            Activated += delegate { QueueLargeSaveBindingRefresh(); };
            Shown += delegate { QueueLargeSaveBindingRefresh(); };
            QueueLargeSaveBindingRefresh();
        }

        private void QueueLargeSaveBindingRefresh()
        {
            if (workflowLargeSaveBindingRefreshQueued || IsDisposed || !IsHandleCreated)
                return;

            workflowLargeSaveBindingRefreshQueued = true;
            BeginInvoke((MethodInvoker)delegate
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    workflowLargeSaveBindingRefreshQueued = false;
                    if (IsDisposed || workflowApplySafeStartButton == null || workflowSaveHostFixInProgress)
                        return;

                    ConfigureAnalyzerOverlaySaveHostFixHandler();
                });
            });
        }
    }
}
