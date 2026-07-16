using System;
using System.Windows.Forms;

namespace CK3MPS
{
    internal static class UiPreviewProgram
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UiPreviewForm());
        }
    }
}