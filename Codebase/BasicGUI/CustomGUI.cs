using System;
using System.IO;
using System.Windows.Forms;
using ALExGUI_4;   // Namespace of SharedMemory.cs

namespace CustomGUI
{
    public class CustomGUI : Form
    {
        private SharedMemory sharedMemory;
        private System.Windows.Forms.Timer dataTimer;
        private TextBox textBoxOutput = null!;   // <--- Added output textbox

        private string csvPath = $@"C:\Users\franc\Documents\ALEX_Jia\Codebase\Data\data_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        private bool headerWritten = false;

        public CustomGUI()
        {
            // Initialize shared memory
            sharedMemory = new SharedMemory();

            // Setup GUI
            InitializeComponent();

            EnsureCsvDirectory();

            // Create timer (500 ms)
            dataTimer = new System.Windows.Forms.Timer();
            dataTimer.Interval = 500;
            dataTimer.Tick += DataTimer_Tick;
            dataTimer.Start();
        }

        private void EnsureCsvDirectory()
        {
            string? dir = Path.GetDirectoryName(csvPath);

            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private void DataTimer_Tick(object? sender, EventArgs e)
        {
            ReadRobotData();
        }

        private void InitializeComponent()
        {
            // Initialize the output TextBox
            textBoxOutput = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                ReadOnly = true
            };

            // Add the TextBox to the form
            this.Controls.Add(textBoxOutput);

            // Set basic form properties
            this.Text = "Custom GUI";
            this.Width = 1200;
            this.Height = 800;
        }

        private void ReadRobotData()
        {
            try
            {
                // Read shared memory
                sharedMemory.readAppDataInStruct();
                sharedMemory.readStatusDevice(); 

                double x_EE_sx = sharedMemory.AppDataInStruct.armLeft.EE_Pos[0];
                double y_EE_sx = sharedMemory.AppDataInStruct.armLeft.EE_Pos[2];
                double z_EE_sx = sharedMemory.AppDataInStruct.armLeft.EE_Pos[1];
                double vel_z_EE_sx = sharedMemory.AppDataInStruct.armLeft.EE_Speed[2];

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                int frequency = sharedMemory.Frequency();

                // Write to CSV
                using (StreamWriter sw = new StreamWriter(csvPath, true))
                {
                    if (!headerWritten)
                    {
                        sw.WriteLine("Time,X_EE,Y_EE,Z_EE,Vel_Z_EE,Frequency");
                        headerWritten = true;
                    }

                    sw.WriteLine($"{timestamp},{x_EE_sx},{y_EE_sx},{z_EE_sx},{vel_z_EE_sx},{frequency}");
                }

                // Append to TextBox output
                // Text font size is set to 12
                textBoxOutput.Font = new System.Drawing.Font(textBoxOutput.Font.FontFamily, 12);
                textBoxOutput.AppendText($"Time = {timestamp},\t X = {x_EE_sx},\t Y = {y_EE_sx},\t Z = {z_EE_sx},\t VelZ = {vel_z_EE_sx},\t Frequency = {frequency} {Environment.NewLine}");
            }
            catch (Exception ex)
            {
                textBoxOutput.AppendText("Error reading shared memory: " + ex.Message + Environment.NewLine);
            }
        }
    }
}
