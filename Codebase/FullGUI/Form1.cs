using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using ALExGUI_4;

namespace CustomGUI
{
    public class EnhancedRobotGUI : Form
    {
        // ── Shared memory (null until user connects) ──────────────────────────
        private SharedMemory? sharedMemory = null;

        // ── Timer ─────────────────────────────────────────────────────────────
        private System.Windows.Forms.Timer dataTimer;

        // ── Mode / logging state ──────────────────────────────────────────────
        private bool isConnectedMode = false;
        private bool isCollectingData = false;
        private bool headerWritten = false;

        // csvPath is set to null and only assigned when logging actually starts,
        // so the filename timestamp matches the moment the user clicks Start Log.
        private string? csvPath = null;
        private readonly string csvDirectory = @"C:\Users\franc\Documents\ALEX_Jia\Codebase\Data";

        // ── Arm state tracking (for button feedback labels) ───────────────────
        private enum ArmState { NotSet, Wearing, Rehab }
        private ArmState stateLeft = ArmState.NotSet;
        private ArmState stateRight = ArmState.NotSet;

        // ── Cached sensor values ──────────────────────────────────────────────
        private double x_left = 0, y_left = 0, z_left = 0;
        private double x_right = 0, y_right = 0, z_right = 0;
        private double vx_left = 0, vy_left = 0, vz_left = 0;
        private double vx_right = 0, vy_right = 0, vz_right = 0;
        private double fx_left = 0, fy_left = 0, fz_left = 0;
        private double fx_right = 0, fy_right = 0, fz_right = 0;
        private int frequency = 0;

        // ── UI controls ───────────────────────────────────────────────────────
        private Label lblMode;
        private Label lblFrequency;
        private Label lblCurrentState;
        private Label lblCsvStatus;
        private Label lblLeftArmState;
        private Label lblRightArmState;
        private Button btnToggleMode;
        private Button btnWearLeft;
        private Button btnRehabLeft;
        private Button btnStopRehabLeft;
        private Button btnWearRight;
        private Button btnRehabRight;
        private Button btnStopRehabRight;
        private Button btnStartLog;
        private Button btnStopLog;
        private DataGridView dgvData;

        // ── Colours used for button active/inactive feedback ──────────────────
        private static readonly Color ColWearActive = Color.FromArgb(100, 180, 255);  // stronger blue
        private static readonly Color ColWearIdle = Color.LightBlue;
        private static readonly Color ColRehabActive = Color.FromArgb(255, 120, 100);  // stronger red
        private static readonly Color ColRehabIdle = Color.LightCoral;
        private static readonly Color ColStopActive = Color.FromArgb(255, 200, 80);  // amber
        private static readonly Color ColStopIdle = Color.FromArgb(255, 235, 160);  // pale amber

        // ─────────────────────────────────────────────────────────────────────
        public EnhancedRobotGUI()
        {
            InitializeComponent();
            InitializeDataGrid();
            EnsureCsvDirectory();

            dataTimer = new System.Windows.Forms.Timer();
            dataTimer.Interval = 500;
            dataTimer.Tick += DataTimer_Tick;
            dataTimer.Start();

            isConnectedMode = false;
            UpdateModeUI();
        }

        // ── Build UI ──────────────────────────────────────────────────────────
        private void InitializeComponent()
        {
            this.Text = "ALEX Robot Control GUI";
            this.Width = 1300;
            this.Height = 760;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            // ── Top status bar ────────────────────────────────────────────────
            Panel topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 75,
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(10, 8, 10, 8)
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
                Location = new Point(10, 38),
                Size = new Size(215, 28),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.LightGreen,
                FlatStyle = FlatStyle.Flat
            };
            btnToggleMode.Click += BtnToggleMode_Click;

            lblFrequency = new Label
            {
                Text = "Frequency: 0 Hz",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.White,
                Location = new Point(245, 42),
                AutoSize = true
            };

            Label lblStateLabel = new Label
            {
                Text = "Current State:",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.Silver,
                Location = new Point(430, 42),
                AutoSize = true
            };

            lblCurrentState = new Label
            {
                Text = "UNAVAILABLE",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.Gray,
                Location = new Point(550, 42),
                AutoSize = true
            };

            topPanel.Controls.AddRange(new Control[]
                { lblMode, btnToggleMode, lblFrequency, lblStateLabel, lblCurrentState });

            // ── Data table ────────────────────────────────────────────────────
            dgvData = new DataGridView
            {
                Location = new Point(10, 85),
                Size = new Size(780, 620),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                RowHeadersVisible = false,
                Font = new Font("Consolas", 10)
            };

            // ── Right-side controls ───────────────────────────────────────────
            int rx = 805, ry = 85;

            // ── Left Arm GroupBox ─────────────────────────────────────────────
            GroupBox gbLeft = new GroupBox
            {
                Text = "Left Arm",
                Location = new Point(rx, ry),
                Size = new Size(460, 155),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            btnWearLeft = new Button
            {
                Text = "Start Wearing",
                Location = new Point(12, 28),
                Size = new Size(135, 35),
                BackColor = ColWearIdle,
                FlatStyle = FlatStyle.Flat
            };
            btnWearLeft.Click += BtnWearLeft_Click;

            btnRehabLeft = new Button
            {
                Text = "Start Rehab",
                Location = new Point(157, 28),
                Size = new Size(135, 35),
                BackColor = ColRehabIdle,
                FlatStyle = FlatStyle.Flat
            };
            btnRehabLeft.Click += BtnRehabLeft_Click;

            btnStopRehabLeft = new Button
            {
                Text = "Stop Rehab",
                Location = new Point(302, 28),
                Size = new Size(135, 35),
                BackColor = ColStopIdle,
                FlatStyle = FlatStyle.Flat
            };
            btnStopRehabLeft.Click += BtnStopRehabLeft_Click;

            // State feedback label
            lblLeftArmState = new Label
            {
                Text = "State: NOT SET",
                Location = new Point(12, 75),
                Size = new Size(430, 22),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.Gray
            };

            gbLeft.Controls.AddRange(new Control[]
                { btnWearLeft, btnRehabLeft, btnStopRehabLeft, lblLeftArmState });
            ry += 165;

            // ── Right Arm GroupBox ────────────────────────────────────────────
            GroupBox gbRight = new GroupBox
            {
                Text = "Right Arm",
                Location = new Point(rx, ry),
                Size = new Size(460, 155),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            btnWearRight = new Button
            {
                Text = "Start Wearing",
                Location = new Point(12, 28),
                Size = new Size(135, 35),
                BackColor = ColWearIdle,
                FlatStyle = FlatStyle.Flat
            };
            btnWearRight.Click += BtnWearRight_Click;

            btnRehabRight = new Button
            {
                Text = "Start Rehab",
                Location = new Point(157, 28),
                Size = new Size(135, 35),
                BackColor = ColRehabIdle,
                FlatStyle = FlatStyle.Flat
            };
            btnRehabRight.Click += BtnRehabRight_Click;

            btnStopRehabRight = new Button
            {
                Text = "Stop Rehab",
                Location = new Point(302, 28),
                Size = new Size(135, 35),
                BackColor = ColStopIdle,
                FlatStyle = FlatStyle.Flat
            };
            btnStopRehabRight.Click += BtnStopRehabRight_Click;

            lblRightArmState = new Label
            {
                Text = "State: NOT SET",
                Location = new Point(12, 75),
                Size = new Size(430, 22),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.Gray
            };

            gbRight.Controls.AddRange(new Control[]
                { btnWearRight, btnRehabRight, btnStopRehabRight, lblRightArmState });
            ry += 165;

            // ── Data Logging GroupBox ─────────────────────────────────────────
            GroupBox gbLog = new GroupBox
            {
                Text = "Data Collection",
                Location = new Point(rx, ry),
                Size = new Size(460, 110),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            btnStartLog = new Button
            {
                Text = "Start CSV Logging",
                Location = new Point(12, 28),
                Size = new Size(200, 35),
                BackColor = Color.LightGreen,
                FlatStyle = FlatStyle.Flat
            };
            btnStartLog.Click += BtnStartLog_Click;

            btnStopLog = new Button
            {
                Text = "Stop CSV Logging",
                Location = new Point(230, 28),
                Size = new Size(200, 35),
                BackColor = Color.LightSalmon,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            btnStopLog.Click += BtnStopLog_Click;

            // Shows the active CSV filename (or idle message)
            lblCsvStatus = new Label
            {
                Text = "Not logging",
                Location = new Point(12, 72),
                Size = new Size(430, 18),
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.DimGray
            };

            gbLog.Controls.AddRange(new Control[] { btnStartLog, btnStopLog, lblCsvStatus });

            // Add all controls to form
            this.Controls.Add(topPanel);
            this.Controls.Add(dgvData);
            this.Controls.Add(gbLeft);
            this.Controls.Add(gbRight);
            this.Controls.Add(gbLog);
        }

        private void InitializeDataGrid()
        {
            dgvData.Columns.Add("Parameter", "Parameter");
            dgvData.Columns.Add("LeftArm", "Left Arm");
            dgvData.Columns.Add("RightArm", "Right Arm");
            dgvData.Columns.Add("Unit", "Unit");

            dgvData.Rows.Add("EE Position X", "0.0000", "0.0000", "m");
            dgvData.Rows.Add("EE Position Y", "0.0000", "0.0000", "m");
            dgvData.Rows.Add("EE Position Z", "0.0000", "0.0000", "m");
            dgvData.Rows.Add("EE Speed X", "0.0000", "0.0000", "m/s");
            dgvData.Rows.Add("EE Speed Y", "0.0000", "0.0000", "m/s");
            dgvData.Rows.Add("EE Speed Z", "0.0000", "0.0000", "m/s");
            dgvData.Rows.Add("EE Force X", "0.0000", "0.0000", "N");
            dgvData.Rows.Add("EE Force Y", "0.0000", "0.0000", "N");
            dgvData.Rows.Add("EE Force Z", "0.0000", "0.0000", "N");
            dgvData.Rows.Add("Handle Pressure", "0.0000", "0.0000", "-");
            dgvData.Rows.Add("Frequency", "0", "—", "Hz");

            dgvData.ColumnHeadersDefaultCellStyle.BackColor = Color.Navy;
            dgvData.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvData.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            dgvData.EnableHeadersVisualStyles = false;
            dgvData.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
        }

        private void EnsureCsvDirectory()
        {
            if (!Directory.Exists(csvDirectory))
                Directory.CreateDirectory(csvDirectory);
        }

        // ── Timer tick ────────────────────────────────────────────────────────
        private void DataTimer_Tick(object? sender, EventArgs e)
        {
            if (isConnectedMode && sharedMemory != null)
                ReadFromSharedMemory();
            else
                FillPreviewZeros();

            UpdateTable();
            lblFrequency.Text = $"Frequency: {frequency} Hz";

            if (isCollectingData && isConnectedMode && sharedMemory != null)
                LogToCsv();
        }

        // ── Read shared memory ────────────────────────────────────────────────
        private void ReadFromSharedMemory()
        {
            try
            {
                sharedMemory!.readAppDataInStruct();
                sharedMemory.readStatusDevice();

                x_left = sharedMemory.AppDataInStruct.armLeft.EE_Pos[0];
                y_left = sharedMemory.AppDataInStruct.armLeft.EE_Pos[1];
                z_left = sharedMemory.AppDataInStruct.armLeft.EE_Pos[2];
                vx_left = sharedMemory.AppDataInStruct.armLeft.EE_Speed[0];
                vy_left = sharedMemory.AppDataInStruct.armLeft.EE_Speed[1];
                vz_left = sharedMemory.AppDataInStruct.armLeft.EE_Speed[2];
                fx_left = sharedMemory.AppDataInStruct.armLeft.EE_Force[0];
                fy_left = sharedMemory.AppDataInStruct.armLeft.EE_Force[1];
                fz_left = sharedMemory.AppDataInStruct.armLeft.EE_Force[2];

                x_right = sharedMemory.AppDataInStruct.armRight.EE_Pos[0];
                y_right = sharedMemory.AppDataInStruct.armRight.EE_Pos[1];
                z_right = sharedMemory.AppDataInStruct.armRight.EE_Pos[2];
                vx_right = sharedMemory.AppDataInStruct.armRight.EE_Speed[0];
                vy_right = sharedMemory.AppDataInStruct.armRight.EE_Speed[1];
                vz_right = sharedMemory.AppDataInStruct.armRight.EE_Speed[2];
                fx_right = sharedMemory.AppDataInStruct.armRight.EE_Force[0];
                fy_right = sharedMemory.AppDataInStruct.armRight.EE_Force[1];
                fz_right = sharedMemory.AppDataInStruct.armRight.EE_Force[2];

                frequency = sharedMemory.Frequency();
            }
            catch (Exception ex)
            {
                isConnectedMode = false;
                UpdateModeUI();
                MessageBox.Show($"Lost connection to shared memory:\n{ex.Message}",
                    "Connection Lost", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void FillPreviewZeros()
        {
            x_left = y_left = z_left = 0;
            vx_left = vy_left = vz_left = 0;
            fx_left = fy_left = fz_left = 0;
            x_right = y_right = z_right = 0;
            vx_right = vy_right = vz_right = 0;
            fx_right = fy_right = fz_right = 0;
            frequency = 0;
        }

        private void UpdateTable()
        {
            dgvData.Rows[0].Cells[1].Value = x_left.ToString("F4");
            dgvData.Rows[0].Cells[2].Value = x_right.ToString("F4");
            dgvData.Rows[1].Cells[1].Value = y_left.ToString("F4");
            dgvData.Rows[1].Cells[2].Value = y_right.ToString("F4");
            dgvData.Rows[2].Cells[1].Value = z_left.ToString("F4");
            dgvData.Rows[2].Cells[2].Value = z_right.ToString("F4");
            dgvData.Rows[3].Cells[1].Value = vx_left.ToString("F4");
            dgvData.Rows[3].Cells[2].Value = vx_right.ToString("F4");
            dgvData.Rows[4].Cells[1].Value = vy_left.ToString("F4");
            dgvData.Rows[4].Cells[2].Value = vy_right.ToString("F4");
            dgvData.Rows[5].Cells[1].Value = vz_left.ToString("F4");
            dgvData.Rows[5].Cells[2].Value = vz_right.ToString("F4");
            dgvData.Rows[6].Cells[1].Value = fx_left.ToString("F4");
            dgvData.Rows[6].Cells[2].Value = fx_right.ToString("F4");
            dgvData.Rows[7].Cells[1].Value = fy_left.ToString("F4");
            dgvData.Rows[7].Cells[2].Value = fy_right.ToString("F4");
            dgvData.Rows[8].Cells[1].Value = fz_left.ToString("F4");
            dgvData.Rows[8].Cells[2].Value = fz_right.ToString("F4");

            if (isConnectedMode && sharedMemory != null)
            {
                dgvData.Rows[9].Cells[1].Value =
                    sharedMemory.AppDataInStruct.armLeft.Handle_Pressure.ToString("F4");
                dgvData.Rows[9].Cells[2].Value =
                    sharedMemory.AppDataInStruct.armRight.Handle_Pressure.ToString("F4");
            }
            else
            {
                dgvData.Rows[9].Cells[1].Value = "0.0000";
                dgvData.Rows[9].Cells[2].Value = "0.0000";
            }

            dgvData.Rows[10].Cells[1].Value = frequency.ToString();
            dgvData.Rows[10].Cells[2].Value = "—";
        }

        // ── CSV logging ───────────────────────────────────────────────────────
        // csvPath is set here so the filename timestamp is the moment logging starts,
        // not the moment the application launched.
        private void StartNewCsvSession()
        {
            csvPath = Path.Combine(csvDirectory,
                                $"data_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            headerWritten = false;
            lblCsvStatus.Text = $"Logging → {Path.GetFileName(csvPath)}";
            lblCsvStatus.ForeColor = Color.DarkGreen;
        }

        private void LogToCsv()
        {
            if (csvPath == null) return;
            try
            {
                using StreamWriter sw = new StreamWriter(csvPath, append: true);
                if (!headerWritten)
                {
                    sw.WriteLine("Time," +
                                 "X_L,Y_L,Z_L,VX_L,VY_L,VZ_L,FX_L,FY_L,FZ_L," +
                                 "X_R,Y_R,Z_R,VX_R,VY_R,VZ_R,FX_R,FY_R,FZ_R,Frequency");
                    headerWritten = true;
                }
                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                sw.WriteLine($"{ts}," +
                             $"{x_left},{y_left},{z_left}," +
                             $"{vx_left},{vy_left},{vz_left}," +
                             $"{fx_left},{fy_left},{fz_left}," +
                             $"{x_right},{y_right},{z_right}," +
                             $"{vx_right},{vy_right},{vz_right}," +
                             $"{fx_right},{fy_right},{fz_right}," +
                             $"{frequency}");
            }
            catch (Exception ex)
            {
                isCollectingData = false;
                btnStartLog.Enabled = true;
                btnStopLog.Enabled = false;
                lblCsvStatus.Text = "Logging stopped (write error)";
                lblCsvStatus.ForeColor = Color.Red;
                MessageBox.Show($"CSV write error:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── Mode toggle ───────────────────────────────────────────────────────
        private void BtnToggleMode_Click(object? sender, EventArgs e)
        {
            if (!isConnectedMode)
            {
                try
                {
                    sharedMemory = new SharedMemory();
                    isConnectedMode = true;
                    UpdateModeUI();
                }
                catch (Exception ex)
                {
                    sharedMemory = null;
                    isConnectedMode = false;
                    MessageBox.Show(
                        $"Could not open shared memory segments.\n" +
                        $"Make sure the robot controller is running.\n\n{ex.Message}",
                        "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                isConnectedMode = false;
                UpdateModeUI();
            }
        }

        private void UpdateModeUI()
        {
            if (isConnectedMode)
            {
                lblMode.Text = "Mode: CONNECTED (Reading from Shared Memory)";
                lblMode.ForeColor = Color.LimeGreen;
                btnToggleMode.Text = "Switch to PREVIEW Mode";
                btnToggleMode.BackColor = Color.LightCoral;
            }
            else
            {
                lblMode.Text = "Mode: PREVIEW (Not Connected)";
                lblMode.ForeColor = Color.DarkOrange;
                btnToggleMode.Text = "Switch to CONNECTED Mode";
                btnToggleMode.BackColor = Color.LightGreen;

                if (isCollectingData)
                {
                    isCollectingData = false;
                    btnStartLog.Enabled = true;
                    btnStopLog.Enabled = false;
                    lblCsvStatus.Text = "Not logging";
                    lblCsvStatus.ForeColor = Color.DimGray;
                }

                // Reset arm states visually when disconnecting
                SetArmState(ref stateLeft, ArmState.NotSet);
                SetArmState(ref stateRight, ArmState.NotSet);
                UpdateArmStateUI();
            }

            btnWearLeft.Enabled = isConnectedMode;
            btnRehabLeft.Enabled = isConnectedMode;
            btnStopRehabLeft.Enabled = isConnectedMode;
            btnWearRight.Enabled = isConnectedMode;
            btnRehabRight.Enabled = isConnectedMode;
            btnStopRehabRight.Enabled = isConnectedMode;
            btnStartLog.Enabled = isConnectedMode;
        }

        // ── Arm state helpers ─────────────────────────────────────────────────
        private void SetArmState(ref ArmState field, ArmState newState)
        {
            field = newState;
        }

        // Refreshes button highlights and state label text for both arms.
        private void UpdateArmStateUI()
        {
            RefreshArmButtons(stateLeft,
                btnWearLeft, btnRehabLeft, btnStopRehabLeft, lblLeftArmState, "Left");
            RefreshArmButtons(stateRight,
                btnWearRight, btnRehabRight, btnStopRehabRight, lblRightArmState, "Right");
        }

        private void RefreshArmButtons(ArmState state,
            Button wear, Button rehab, Button stopRehab, Label stateLabel, string side)
        {
            // Reset all to idle colours first
            wear.BackColor = ColWearIdle;
            rehab.BackColor = ColRehabIdle;
            stopRehab.BackColor = ColStopIdle;

            switch (state)
            {
                case ArmState.Wearing:
                    wear.BackColor = ColWearActive;
                    stateLabel.Text = $"State: WEARING";
                    stateLabel.ForeColor = Color.SteelBlue;
                    break;

                case ArmState.Rehab:
                    rehab.BackColor = ColRehabActive;
                    stateLabel.Text = $"State: REHAB";
                    stateLabel.ForeColor = Color.Firebrick;
                    break;

                case ArmState.NotSet:
                default:
                    stateLabel.Text = "State: NOT SET";
                    stateLabel.ForeColor = Color.Gray;
                    break;
            }
        }

        // ── Arm command buttons ───────────────────────────────────────────────

        private void BtnWearLeft_Click(object? sender, EventArgs e)
        {
            if (sharedMemory == null) return;
            try
            {
                sharedMemory.SelectedArm = 1;
                sharedMemory.startWearing();
                stateLeft = ArmState.Wearing;
                UpdateArmStateUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending Wearing command (Left):\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnRehabLeft_Click(object? sender, EventArgs e)
        {
            if (sharedMemory == null) return;
            try
            {
                sharedMemory.SelectedArm = 1;
                sharedMemory.startRehab();
                stateLeft = ArmState.Rehab;
                UpdateArmStateUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending Rehab command (Left):\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Stop Rehab → sends stopRehab() then startWearing() to return to wearing state
        private void BtnStopRehabLeft_Click(object? sender, EventArgs e)
        {
            if (sharedMemory == null) return;
            try
            {
                sharedMemory.SelectedArm = 1;
                sharedMemory.stopRehab();
                sharedMemory.startWearing();
                stateLeft = ArmState.Wearing;
                UpdateArmStateUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending Stop Rehab command (Left):\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnWearRight_Click(object? sender, EventArgs e)
        {
            if (sharedMemory == null) return;
            try
            {
                sharedMemory.SelectedArm = 3;
                sharedMemory.startWearing();
                stateRight = ArmState.Wearing;
                UpdateArmStateUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending Wearing command (Right):\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnRehabRight_Click(object? sender, EventArgs e)
        {
            if (sharedMemory == null) return;
            try
            {
                sharedMemory.SelectedArm = 3;
                sharedMemory.startRehab();
                stateRight = ArmState.Rehab;
                UpdateArmStateUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending Rehab command (Right):\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Stop Rehab → sends stopRehab() then startWearing() to return to wearing state
        private void BtnStopRehabRight_Click(object? sender, EventArgs e)
        {
            if (sharedMemory == null) return;
            try
            {
                sharedMemory.SelectedArm = 3;
                sharedMemory.stopRehab();
                sharedMemory.startWearing();
                stateRight = ArmState.Wearing;
                UpdateArmStateUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending Stop Rehab command (Right):\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── CSV logging buttons ───────────────────────────────────────────────
        private void BtnStartLog_Click(object? sender, EventArgs e)
        {
            StartNewCsvSession();         // sets csvPath to NOW timestamp
            isCollectingData = true;
            btnStartLog.Enabled = false;
            btnStopLog.Enabled = true;
        }

        private void BtnStopLog_Click(object? sender, EventArgs e)
        {
            isCollectingData = false;
            btnStartLog.Enabled = true;
            btnStopLog.Enabled = false;
            lblCsvStatus.Text = $"Last saved: {Path.GetFileName(csvPath ?? "—")}";
            lblCsvStatus.ForeColor = Color.DimGray;
        }

        // ── Cleanup ───────────────────────────────────────────────────────────
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            dataTimer.Stop();
            base.OnFormClosing(e);
        }
    }
}
