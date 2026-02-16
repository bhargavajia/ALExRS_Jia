using System;
using System.Windows.Forms;

namespace CustomGUI
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Create and run the enhanced form
            Application.Run(new EnhancedRobotGUI());
        }
    }
}
