using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using ALExGUI_4;   // Namespace of SharedMemory.cs

namespace CustomGUI
{
    public class EnhancedRobotGUI : Form
    {
        // Shared Memory
        private SharedMemory? sharedMemory = null;

        // Timers
        private System.Windows.Forms.Timer dataTimer;

        // Mode control
        private bool isConnectedMode = false;
        private bool isCollectingData = false;

        // CSV logging
        private string csvPath = $@"C:\Users\franc\Documents\ALEX_Jia\Codebase\Data\data_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        private bool headerWritten = false;

        // UI Controls
        private Button btnToggleMode;
        private Button btnStartWearingLeft;
        private Button btnStartWearingRight;
        private Button btnStartRehabLeft;
        private Button btnStartRehabRight;
        private Button btnStartDataCollection;
        private Button btnStopDataCollection;
        private Label lblCurrentState;
        private Label lblMode;
        private Label lblFrequency;
        private DataGridView dgvRobotData;
        private GroupBox gbLeftArm;
        private GroupBox gbRightArm;
        private GroupBox gbDataCollection;
        private GroupBox gbStatus;

        // Data storage for preview mode
        private double x_EE_left = 0.0;
        private double y_EE_left = 0.0;
        private double z_EE_left = 0.0;
        private double vel_z_EE_left = 0.0;
        private double x_EE_right = 0.0;
        private double y_EE_right = 0.0;
        private double z_EE_right = 0.0;
        private double vel_z_EE_right = 0.0;
        private int frequency = 0;

        public EnhancedRobotGUI()
        {
            // DO NOT initialize shared memory here - only when switching to connected mode

            // Setup GUI
            InitializeComponent();

            // Ensure CSV directory exists
            EnsureCsvDirectory();

            // Create timer (500 ms)
            dataTimer = new System.Windows.Forms.Timer();
            dataTimer.Interval = 500;
            dataTimer.Tick += DataTimer_Tick;
            dataTimer.Start();

            // Initialize data grid
            InitializeDataGrid();

            // Start in preview mode (default)
            isConnectedMode = false;
            UpdateModeUI();
        }

        private void InitializeComponent()
        {
            // Form settings
            this.Text = "ALEX Robot Control GUI";
            this.Width = 1400;
            this.Height = 900;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            // Main layout panel
            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(10)
            };

            // Set column styles
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));

            // Set row styles
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 70F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 30F));

            // ========== TOP PANEL - Mode and Status ==========
            Panel topPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle
            };

            lblMode = new Label
            {
                Text = "Mode: PREVIEW (Not Connected)",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.DarkOrange,
                Location = new Point(10, 10),
                AutoSize = true
            };

            btnToggleMode = new Button
            {
                Text = "Switch to CONNECTED Mode",
                Location = new Point(10, 40),
                Size = new Size(220, 30),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.LightGreen
            };
            btnToggleMode.Click += BtnToggleMode_Click;

            lblFrequency = new Label
            {
                Text = "Frequency: 0 Hz",
                Font = new Font("Segoe UI", 10),
                Location = new Point(250, 45),
                AutoSize = true
            };

            topPanel.Controls.Add(lblMode);
            topPanel.Controls.Add(btnToggleMode);
            topPanel.Controls.Add(lblFrequency);

            mainLayout.Controls.Add(topPanel, 0, 0);
            mainLayout.SetColumnSpan(topPanel, 2);

            // ========== DATA TABLE PANEL ==========
            Panel dataPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };

            dgvRobotData = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                Font = new Font("Consolas", 10)
            };

            dataPanel.Controls.Add(dgvRobotData);
            mainLayout.Controls.Add(dataPanel, 0, 1);
            mainLayout.SetRowSpan(dataPanel, 2);

            // ========== RIGHT PANEL - Controls ==========
            Panel controlPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5),
                AutoScroll = true
            };

            int yPos = 10;

            // Left Arm Group Box
            gbLeftArm = new GroupBox
            {
                Text = "Left Arm Control",
                Location = new Point(10, yPos),
                Size = new Size(480, 120),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            btnStartWearingLeft = new Button
            {
                Text = "Start Wearing Mode",
                Location = new Point(15, 30),
                Size = new Size(210, 35),
                Font = new Font("Segoe UI", 9),
                BackColor = Color.LightBlue
            };
            btnStartWearingLeft.Click += BtnStartWearingLeft_Click;

            btnStartRehabLeft = new Button
            {
                Text = "Start Rehab Mode",
                Location = new Point(240, 30),
                Size = new Size(210, 35),
                Font = new Font("Segoe UI", 9),
                BackColor = Color.LightCoral
            };
            btnStartRehabLeft.Click += BtnStartRehabLeft_Click;

            Label lblLeftStatus = new Label
            {
                Text = "Status: Ready",
                Location = new Point(15, 75),
                Size = new Size(450, 25),
                Font = new Font("Segoe UI", 9)
            };

            gbLeftArm.Controls.Add(btnStartWearingLeft);
            gbLeftArm.Controls.Add(btnStartRehabLeft);
            gbLeftArm.Controls.Add(lblLeftStatus);

            controlPanel.Controls.Add(gbLeftArm);
            yPos += 130;

            // Right Arm Group Box
            gbRightArm = new GroupBox
            {
                Text = "Right Arm Control",
                Location = new Point(10, yPos),
                Size = new Size(480, 120),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            btnStartWearingRight = new Button
            {
                Text = "Start Wearing Mode",
                Location = new Point(15, 30),
                Size = new Size(210, 35),
                Font = new Font("Segoe UI", 9),
                BackColor = Color.LightBlue
            };
            btnStartWearingRight.Click += BtnStartWearingRight_Click;

            btnStartRehabRight = new Button
            {
                Text = "Start Rehab Mode",
                Location = new Point(240, 30),
                Size = new Size(210, 35),
                Font = new Font("Segoe UI", 9),
                BackColor = Color.LightCoral
            };
            btnStartRehabRight.Click += BtnStartRehabRight_Click;

            Label lblRightStatus = new Label
            {
                Text = "Status: Ready",
                Location = new Point(15, 75),
                Size = new Size(450, 25),
                Font = new Font("Segoe UI", 9)
            };

            gbRightArm.Controls.Add(btnStartWearingRight);
            gbRightArm.Controls.Add(btnStartRehabRight);
            gbRightArm.Controls.Add(lblRightStatus);

            controlPanel.Controls.Add(gbRightArm);
            yPos += 130;

            // Data Collection Group Box
            gbDataCollection = new GroupBox
            {
                Text = "Data Collection",
                Location = new Point(10, yPos),
                Size = new Size(480, 100),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            btnStartDataCollection = new Button
            {
                Text = "Start CSV Logging",
                Location = new Point(15, 30),
                Size = new Size(210, 35),
                Font = new Font("Segoe UI", 9),
                BackColor = Color.LightGreen
            };
            btnStartDataCollection.Click += BtnStartDataCollection_Click;

            btnStopDataCollection = new Button
            {
                Text = "Stop CSV Logging",
                Location = new Point(240, 30),
                Size = new Size(210, 35),
                Font = new Font("Segoe UI", 9),
                BackColor = Color.LightSalmon,
                Enabled = false
            };
            btnStopDataCollection.Click += BtnStopDataCollection_Click;

            gbDataCollection.Controls.Add(btnStartDataCollection);
            gbDataCollection.Controls.Add(btnStopDataCollection);

            controlPanel.Controls.Add(gbDataCollection);
            yPos += 110;

            // Status Group Box
            gbStatus = new GroupBox
            {
                Text = "System Status",
                Location = new Point(10, yPos),
                Size = new Size(480, 100),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            Label lblStateTitle = new Label
            {
                Text = "Current State:",
                Location = new Point(15, 30),
                Size = new Size(120, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };

            lblCurrentState = new Label
            {
                Text = "UNAVAILABLE",
                Location = new Point(140, 30),
                Size = new Size(320, 25),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.Gray
            };

            Label lblCSVPath = new Label
            {
                Text = $"CSV Path: {csvPath}",
                Location = new Point(15, 60),
                Size = new Size(450, 25),
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.DarkGray
            };

            gbStatus.Controls.Add(lblStateTitle);
            gbStatus.Controls.Add(lblCurrentState);
            gbStatus.Controls.Add(lblCSVPath);

            controlPanel.Controls.Add(gbStatus);

            mainLayout.Controls.Add(controlPanel, 1, 1);
            mainLayout.SetRowSpan(controlPanel, 2);

            // Add main layout to form
            this.Controls.Add(mainLayout);
        }

        private void InitializeDataGrid()
        {
            // Add columns
            dgvRobotData.Columns.Add("Parameter", "Parameter");
            dgvRobotData.Columns.Add("LeftArm", "Left Arm");
            dgvRobotData.Columns.Add("RightArm", "Right Arm");
            dgvRobotData.Columns.Add("Unit", "Unit");

            // Set column widths
            dgvRobotData.Columns["Parameter"].Width = 200;
            dgvRobotData.Columns["LeftArm"].Width = 150;
            dgvRobotData.Columns["RightArm"].Width = 150;
            dgvRobotData.Columns["Unit"].Width = 80;

            // Add rows for data
            dgvRobotData.Rows.Add("EE Position X", "0.000", "0.000", "m");
            dgvRobotData.Rows.Add("EE Position Y", "0.000", "0.000", "m");
            dgvRobotData.Rows.Add("EE Position Z", "0.000", "0.000", "m");
            dgvRobotData.Rows.Add("EE Velocity Z", "0.000", "0.000", "m/s");
            dgvRobotData.Rows.Add("EE Speed X", "0.000", "0.000", "m/s");
            dgvRobotData.Rows.Add("EE Speed Y", "0.000", "0.000", "m/s");
            dgvRobotData.Rows.Add("EE Speed Z", "0.000", "0.000", "m/s");
            dgvRobotData.Rows.Add("EE Force X", "0.000", "0.000", "N");
            dgvRobotData.Rows.Add("EE Force Y", "0.000", "0.000", "N");
            dgvRobotData.Rows.Add("EE Force Z", "0.000", "0.000", "N");

            // Style the header
            dgvRobotData.ColumnHeadersDefaultCellStyle.BackColor = Color.Navy;
            dgvRobotData.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvRobotData.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            dgvRobotData.EnableHeadersVisualStyles = false;

            // Alternate row colors
            dgvRobotData.AlternatingRowsDefaultCellStyle.BackColor = Color.LightGray;
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
            if (isConnectedMode)
            {
                ReadRobotDataFromSharedMemory();
            }
            else
            {
                // Preview mode - just update with zero values
                UpdateDataGridPreview();
            }

            // Update frequency display
            lblFrequency.Text = $"Frequency: {frequency} Hz";

            // Log to CSV if collection is active
            if (isCollectingData && isConnectedMode)
            {
                LogDataToCSV();
            }
        }

        private void ReadRobotDataFromSharedMemory()
        {
            // Safety check - if shared memory not initialized, don't try to read
            if (sharedMemory == null)
            {
                UpdateDataGridPreview();
                return;
            }

            try
            {
                // Read shared memory
                sharedMemory.readAppDataInStruct();
                sharedMemory.readStatusDevice();

                // Left Arm Data
                x_EE_left = sharedMemory.AppDataInStruct.armLeft.EE_Pos[0];
                y_EE_left = sharedMemory.AppDataInStruct.armLeft.EE_Pos[2];
                z_EE_left = sharedMemory.AppDataInStruct.armLeft.EE_Pos[1];
                vel_z_EE_left = sharedMemory.AppDataInStruct.armLeft.EE_Speed[2];

                double vel_x_EE_left = sharedMemory.AppDataInStruct.armLeft.EE_Speed[0];
                double vel_y_EE_left = sharedMemory.AppDataInStruct.armLeft.EE_Speed[1];

                double force_x_EE_left = sharedMemory.AppDataInStruct.armLeft.EE_Force[0];
                double force_y_EE_left = sharedMemory.AppDataInStruct.armLeft.EE_Force[1];
                double force_z_EE_left = sharedMemory.AppDataInStruct.armLeft.EE_Force[2];

                // Right Arm Data
                x_EE_right = sharedMemory.AppDataInStruct.armRight.EE_Pos[0];
                y_EE_right = sharedMemory.AppDataInStruct.armRight.EE_Pos[2];
                z_EE_right = sharedMemory.AppDataInStruct.armRight.EE_Pos[1];
                vel_z_EE_right = sharedMemory.AppDataInStruct.armRight.EE_Speed[2];

                double vel_x_EE_right = sharedMemory.AppDataInStruct.armRight.EE_Speed[0];
                double vel_y_EE_right = sharedMemory.AppDataInStruct.armRight.EE_Speed[1];

                double force_x_EE_right = sharedMemory.AppDataInStruct.armRight.EE_Force[0];
                double force_y_EE_right = sharedMemory.AppDataInStruct.armRight.EE_Force[1];
                double force_z_EE_right = sharedMemory.AppDataInStruct.armRight.EE_Force[2];

                frequency = sharedMemory.Frequency();

                // Update data grid
                dgvRobotData.Rows[0].Cells[1].Value = x_EE_left.ToString("F3");
                dgvRobotData.Rows[0].Cells[2].Value = x_EE_right.ToString("F3");

                dgvRobotData.Rows[1].Cells[1].Value = y_EE_left.ToString("F3");
                dgvRobotData.Rows[1].Cells[2].Value = y_EE_right.ToString("F3");

                dgvRobotData.Rows[2].Cells[1].Value = z_EE_left.ToString("F3");
                dgvRobotData.Rows[2].Cells[2].Value = z_EE_right.ToString("F3");

                dgvRobotData.Rows[3].Cells[1].Value = vel_z_EE_left.ToString("F3");
                dgvRobotData.Rows[3].Cells[2].Value = vel_z_EE_right.ToString("F3");

                dgvRobotData.Rows[4].Cells[1].Value = vel_x_EE_left.ToString("F3");
                dgvRobotData.Rows[4].Cells[2].Value = vel_x_EE_right.ToString("F3");

                dgvRobotData.Rows[5].Cells[1].Value = vel_y_EE_left.ToString("F3");
                dgvRobotData.Rows[5].Cells[2].Value = vel_y_EE_right.ToString("F3");

                dgvRobotData.Rows[6].Cells[1].Value = vel_z_EE_left.ToString("F3");
                dgvRobotData.Rows[6].Cells[2].Value = vel_z_EE_right.ToString("F3");

                dgvRobotData.Rows[7].Cells[1].Value = force_x_EE_left.ToString("F3");
                dgvRobotData.Rows[7].Cells[2].Value = force_x_EE_right.ToString("F3");

                dgvRobotData.Rows[8].Cells[1].Value = force_y_EE_left.ToString("F3");
                dgvRobotData.Rows[8].Cells[2].Value = force_y_EE_right.ToString("F3");

                dgvRobotData.Rows[9].Cells[1].Value = force_z_EE_left.ToString("F3");
                dgvRobotData.Rows[9].Cells[2].Value = force_z_EE_right.ToString("F3");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading shared memory: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateDataGridPreview()
        {
            // In preview mode, just show zeros
            for (int i = 0; i < dgvRobotData.Rows.Count; i++)
            {
                dgvRobotData.Rows[i].Cells[1].Value = "0.000";
                dgvRobotData.Rows[i].Cells[2].Value = "0.000";
            }
        }

        private void LogDataToCSV()
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(csvPath, true))
                {
                    if (!headerWritten)
                    {
                        sw.WriteLine("Time,X_EE_Left,Y_EE_Left,Z_EE_Left,VelZ_EE_Left," +
                                   "X_EE_Right,Y_EE_Right,Z_EE_Right,VelZ_EE_Right,Frequency");
                        headerWritten = true;
                    }

                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    sw.WriteLine($"{timestamp},{x_EE_left},{y_EE_left},{z_EE_left},{vel_z_EE_left}," +
                               $"{x_EE_right},{y_EE_right},{z_EE_right},{vel_z_EE_right},{frequency}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error writing to CSV: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ========== Button Event Handlers ==========

        private void BtnToggleMode_Click(object? sender, EventArgs e)
        {
            if (!isConnectedMode)
            {
                // Switching TO connected mode - initialize shared memory now
                try
                {
                    sharedMemory = new SharedMemory();
                    isConnectedMode = true;
                    UpdateModeUI();
                    MessageBox.Show("Connected to shared memory successfully!", "Connected",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to connect to shared memory:\n{ex.Message}\n\nStaying in Preview mode.",
                        "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    isConnectedMode = false;
                }
            }
            else
            {
                // Switching TO preview mode - just disable connected mode
                isConnectedMode = false;
                UpdateModeUI();
            }
        }

        private void UpdateModeUI()
        {
            if (isConnectedMode)
            {
                lblMode.Text = "Mode: CONNECTED (Reading from Shared Memory)";
                lblMode.ForeColor = Color.Green;
                btnToggleMode.Text = "Switch to PREVIEW Mode";
                btnToggleMode.BackColor = Color.LightCoral;

                // Enable control buttons
                btnStartWearingLeft.Enabled = true;
                btnStartWearingRight.Enabled = true;
                btnStartRehabLeft.Enabled = true;
                btnStartRehabRight.Enabled = true;
            }
            else
            {
                lblMode.Text = "Mode: PREVIEW (Not Connected)";
                lblMode.ForeColor = Color.DarkOrange;
                btnToggleMode.Text = "Switch to CONNECTED Mode";
                btnToggleMode.BackColor = Color.LightGreen;

                // Disable control buttons in preview mode
                btnStartWearingLeft.Enabled = false;
                btnStartWearingRight.Enabled = false;
                btnStartRehabLeft.Enabled = false;
                btnStartRehabRight.Enabled = false;

                // Reset data
                frequency = 0;
                UpdateDataGridPreview();
            }
        }

        private void BtnStartWearingLeft_Click(object? sender, EventArgs e)
        {
            if (!isConnectedMode || sharedMemory == null)
            {
                MessageBox.Show("Please switch to CONNECTED mode first!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Command to start wearing mode for left arm
                // ALEX32_COMMAND_EXOS_START_DEVICE = 1
                sharedMemory.GuiDataOutStruct.Exos.armLeft.Command = 1;
                sharedMemory.writeGuiDataOutStruct();

                MessageBox.Show("Started Wearing Mode for LEFT ARM", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting wearing mode (Left): {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnStartWearingRight_Click(object? sender, EventArgs e)
        {
            if (!isConnectedMode || sharedMemory == null)
            {
                MessageBox.Show("Please switch to CONNECTED mode first!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Command to start wearing mode for right arm
                // ALEX32_COMMAND_EXOS_START_DEVICE = 1
                sharedMemory.GuiDataOutStruct.Exos.armRight.Command = 1;
                sharedMemory.writeGuiDataOutStruct();

                MessageBox.Show("Started Wearing Mode for RIGHT ARM", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting wearing mode (Right): {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnStartRehabLeft_Click(object? sender, EventArgs e)
        {
            if (!isConnectedMode || sharedMemory == null)
            {
                MessageBox.Show("Please switch to CONNECTED mode first!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Command to start rehab mode for left arm
                // ALEX32_COMMAND_EXOS_START_REHAB = 3
                sharedMemory.GuiDataOutStruct.Exos.armLeft.Command = 3;
                sharedMemory.writeGuiDataOutStruct();

                MessageBox.Show("Started Rehab Mode for LEFT ARM", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting rehab mode (Left): {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnStartRehabRight_Click(object? sender, EventArgs e)
        {
            if (!isConnectedMode || sharedMemory == null)
            {
                MessageBox.Show("Please switch to CONNECTED mode first!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Command to start rehab mode for right arm
                // ALEX32_COMMAND_EXOS_START_REHAB = 3
                sharedMemory.GuiDataOutStruct.Exos.armRight.Command = 3;
                sharedMemory.writeGuiDataOutStruct();

                MessageBox.Show("Started Rehab Mode for RIGHT ARM", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting rehab mode (Right): {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnStartDataCollection_Click(object? sender, EventArgs e)
        {
            if (!isConnectedMode || sharedMemory == null)
            {
                MessageBox.Show("Data collection only works in CONNECTED mode!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            isCollectingData = true;
            btnStartDataCollection.Enabled = false;
            btnStopDataCollection.Enabled = true;

            MessageBox.Show($"Started CSV logging to:\n{csvPath}", "Data Collection Started",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnStopDataCollection_Click(object? sender, EventArgs e)
        {
            isCollectingData = false;
            btnStartDataCollection.Enabled = true;
            btnStopDataCollection.Enabled = false;

            MessageBox.Show("CSV logging stopped", "Data Collection Stopped",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            dataTimer.Stop();
            base.OnFormClosing(e);
        }
    }
}