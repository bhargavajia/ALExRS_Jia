using System;
using System.Windows.Forms;

namespace CustomGUI
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Create and run the form
            Application.Run(new CustomGUI());
        }
    }
}
