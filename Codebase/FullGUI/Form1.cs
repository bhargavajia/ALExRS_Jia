using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using ALExGUI_4;

namespace CustomGUI
{
    /// <summary>
    /// Enhanced ALEX Robot Control GUI
    /// Features:
    /// - 50 Hz display update rate (20ms timer interval)
    /// - Multiple data tables: End Effector, Shoulder Joints, Elbow/Wrist Joints
    /// - Comprehensive CSV logging with all joint data (position in rad/deg, velocities)
    /// - Freeze/Unfreeze display toggle to pause screen updates while logging continues
    /// - Real-time visualization of 8 joints per arm (both left and right)
    /// - Two logging modes: Record All Data and Monolateral Acquisition with state tracking
    /// </summary>
    public class EnhancedRobotGUI : Form
    {
        // ── Enums for logging modes and states ────────────────────────────────
        private enum LoggingMode
        {
            RecordAllData,
            MonolateralAcquisition
        }

        private enum AcquisitionState
        {
            Idle,
            Going,
            Returning
        }

        private enum ArmSelection
        {
            Left,
            Right
        }

        // ── Shared memory (null until user connects) ──────────────────────────
        private SharedMemory? sharedMemory = null;

        // ── Timer ─────────────────────────────────────────────────────────────
        private System.Windows.Forms.Timer dataTimer;

        // ── Mode / logging state ──────────────────────────────────────────────
        private bool isConnectedMode = false;
        private bool isCollectingData = false;
        private bool headerWritten = false;

        private string? csvPath = null;
        private readonly string csvDirectory = @"C:\Users\franc\Documents\ALEX_Jia\Codebase\Data";

        // ── Logging mode and state tracking ───────────────────────────────────
        private LoggingMode currentLoggingMode = LoggingMode.RecordAllData;
        private AcquisitionState currentState = AcquisitionState.Idle;
        private ArmSelection selectedArmForMonolateral = ArmSelection.Left;

        // ── Cached sensor values (End Effector position and velocity only) ────
        private double x_left = 0, y_left = 0, z_left = 0;
        private double x_right = 0, y_right = 0, z_right = 0;
        private double vx_left = 0, vy_left = 0, vz_left = 0;
        private double vx_right = 0, vy_right = 0, vz_right = 0;
        private int frequency = 0;

        // ── Cached joint data (8 joints per arm) ──────────────────────────────
        private float[] jointPosRad_left = new float[8];   // Position in radians
        private float[] jointPosDeg_left = new float[8];   // Position in degrees
        private float[] jointVel_left = new float[8];      // Velocity in rad/s
        private float[] jointPosRad_right = new float[8];
        private float[] jointPosDeg_right = new float[8];
        private float[] jointVel_right = new float[8];

        // ── Display freeze state ──────────────────────────────────────────────
        private bool isDisplayFrozen = false;

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
        private Button btnStopDeviceLeft;
        private Button btnWearRight;
        private Button btnRehabRight;
        private Button btnStopRehabRight;
        private Button btnStopDeviceRight;
        private Button btnStartLog;
        private Button btnStopLog;
        private Button btnFreezeDisplay;
        private DataGridView dgvEELeft;      // End Effector Left
        private DataGridView dgvEERight;     // End Effector Right
        private DataGridView dgvShoulderLeft;
        private DataGridView dgvShoulderRight;
        private DataGridView dgvElbowWristLeft;
        private DataGridView dgvElbowWristRight;

        // Table title labels
        private Label lblEELeftTitle;
        private Label lblEERightTitle;
        private Label lblShoulderLeftTitle;
        private Label lblShoulderRightTitle;
        private Label lblElbowWristLeftTitle;
        private Label lblElbowWristRightTitle;
        private Label lblFrequencyDisplay;  // Frequency display label

        // ── New controls for logging mode and state ───────────────────────────
        private RadioButton rbRecordAllData;
        private RadioButton rbMonolateralAcquisition;
        private RadioButton rbLeftArm;
        private RadioButton rbRightArm;
        private Label lblArmSelection;
        private Panel pnlArmSelection;  // Panel to group arm selection radio buttons
        private Label lblStateDisplay;
        private Label lblStateValue;
        private Button btnNextState;

        // ── Colours for button feedback ───────────────────────────────────────
        private static readonly Color ColWearActive = Color.FromArgb(100, 180, 255);
        private static readonly Color ColWearIdle = Color.LightBlue;
        private static readonly Color ColRehabActive = Color.FromArgb(255, 120, 100);
        private static readonly Color ColRehabIdle = Color.LightCoral;
        private static readonly Color ColStopActive = Color.FromArgb(255, 200, 80);
        private static readonly Color ColStopIdle = Color.FromArgb(255, 235, 160);
        private static readonly Color ColResetActive = Color.FromArgb(255, 100, 100);
        private static readonly Color ColResetIdle = Color.FromArgb(255, 180, 180);

        // ─────────────────────────────────────────────────────────────────────
        public EnhancedRobotGUI()
        {
            InitializeComponent();
            InitializeDataGrid();
            EnsureCsvDirectory();

            dataTimer = new System.Windows.Forms.Timer();
            dataTimer.Interval = 20;  // 50 Hz (20ms interval)
            dataTimer.Tick += DataTimer_Tick;
            dataTimer.Start();

            isConnectedMode = false;
            UpdateModeUI();
        }

        // ── Build UI ──────────────────────────────────────────────────────────
        private void InitializeComponent()
        {
            this.Text = "ALEX Robot Control GUI";
            this.Width = 1600;  // Increased width for additional tables
            this.Height = 900;
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

            // ── Freeze/Unfreeze Display Button ───────────────────────────────
            btnFreezeDisplay = new Button
            {
                Text = "⏸ Freeze Display",
                Location = new Point(700, 38),
                Size = new Size(150, 28),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.LightYellow,
                FlatStyle = FlatStyle.Flat
            };
            btnFreezeDisplay.Click += BtnFreezeDisplay_Click;

            topPanel.Controls.AddRange(new Control[]
                { lblMode, btnToggleMode, lblFrequency, lblStateLabel, lblCurrentState, btnFreezeDisplay });

            // ── End Effector Tables (Left and Right) ──────────────────────────
            // Left End Effector Title
            lblEELeftTitle = new Label
            {
                Text = "◆ LEFT ARM - END EFFECTOR",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.DarkBlue,
                Location = new Point(10, 85),
                AutoSize = true
            };

            dgvEELeft = new DataGridView
            {
                Location = new Point(10, 110),
                Size = new Size(490, 180),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                RowHeadersVisible = false,
                Font = new Font("Consolas", 11),
                RowTemplate = { Height = 38 }
            };

            // Right End Effector Title
            lblEERightTitle = new Label
            {
                Text = "◆ RIGHT ARM - END EFFECTOR",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(510, 85),
                AutoSize = true
            };

            dgvEERight = new DataGridView
            {
                Location = new Point(510, 110),
                Size = new Size(490, 180),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                RowHeadersVisible = false,
                Font = new Font("Consolas", 11),
                RowTemplate = { Height = 38 }
            };

            // ── Frequency Display Label ───────────────────────────────────────
            lblFrequencyDisplay = new Label
            {
                Text = "Update Frequency: 0 Hz",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.Navy,
                Location = new Point(10, 300),
                AutoSize = true
            };

            // ── Shoulder Joint Tables (Left and Right) ────────────────────────
            lblShoulderLeftTitle = new Label
            {
                Text = "◆ LEFT ARM - SHOULDER JOINTS",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.DarkBlue,
                Location = new Point(10, 320),
                AutoSize = true
            };

            dgvShoulderLeft = new DataGridView
            {
                Location = new Point(10, 345),
                Size = new Size(490, 135),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                RowHeadersVisible = false,
                Font = new Font("Consolas", 11),
                RowTemplate = { Height = 38 }
            };

            lblShoulderRightTitle = new Label
            {
                Text = "◆ RIGHT ARM - SHOULDER JOINTS",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(510, 320),
                AutoSize = true
            };

            dgvShoulderRight = new DataGridView
            {
                Location = new Point(510, 345),
                Size = new Size(490, 135),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                RowHeadersVisible = false,
                Font = new Font("Consolas", 11),
                RowTemplate = { Height = 38 }
            };

            // ── Elbow/Wrist Joint Tables (Left and Right) ─────────────────────
            lblElbowWristLeftTitle = new Label
            {
                Text = "◆ LEFT ARM - ELBOW/WRIST JOINTS",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.DarkBlue,
                Location = new Point(10, 490),
                AutoSize = true
            };

            dgvElbowWristLeft = new DataGridView
            {
                Location = new Point(10, 515),
                Size = new Size(490, 135),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                RowHeadersVisible = false,
                Font = new Font("Consolas", 11),
                RowTemplate = { Height = 38 }
            };

            lblElbowWristRightTitle = new Label
            {
                Text = "◆ RIGHT ARM - ELBOW/WRIST JOINTS",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(510, 490),
                AutoSize = true
            };

            dgvElbowWristRight = new DataGridView
            {
                Location = new Point(510, 515),
                Size = new Size(490, 135),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                RowHeadersVisible = false,
                Font = new Font("Consolas", 11),
                RowTemplate = { Height = 38 }
            };

            // ── Right-side controls ───────────────────────────────────────────
            int rx = 1030, ry = 85;

            // ── Left Arm GroupBox ─────────────────────────────────────────────
            GroupBox gbLeft = new GroupBox
            {
                Text = "Left Arm",
                Location = new Point(rx, ry),
                Size = new Size(460, 195),
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

            // New: Stop Device button (resets to DRIVER_OFF)
            btnStopDeviceLeft = new Button
            {
                Text = "STOP DEVICE",
                Location = new Point(12, 73),
                Size = new Size(425, 35),
                BackColor = ColResetIdle,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            btnStopDeviceLeft.Click += BtnStopDeviceLeft_Click;

            lblLeftArmState = new Label
            {
                Text = "Phase: —",
                Location = new Point(12, 120),
                Size = new Size(430, 22),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.Gray
            };

            gbLeft.Controls.AddRange(new Control[]
                { btnWearLeft, btnRehabLeft, btnStopRehabLeft, btnStopDeviceLeft, lblLeftArmState });
            ry += 205;

            // ── Right Arm GroupBox ────────────────────────────────────────────
            GroupBox gbRight = new GroupBox
            {
                Text = "Right Arm",
                Location = new Point(rx, ry),
                Size = new Size(460, 195),
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

            btnStopDeviceRight = new Button
            {
                Text = "STOP DEVICE",
                Location = new Point(12, 73),
                Size = new Size(425, 35),
                BackColor = ColResetIdle,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            btnStopDeviceRight.Click += BtnStopDeviceRight_Click;

            lblRightArmState = new Label
            {
                Text = "Phase: —",
                Location = new Point(12, 120),
                Size = new Size(430, 22),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.Gray
            };

            gbRight.Controls.AddRange(new Control[]
                { btnWearRight, btnRehabRight, btnStopRehabRight, btnStopDeviceRight, lblRightArmState });
            ry += 205;

            // ── Data Logging GroupBox ─────────────────────────────────────────
            GroupBox gbLog = new GroupBox
            {
                Text = "Data Collection",
                Location = new Point(rx, ry),
                Size = new Size(460, 270),  // Increased height for new controls
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };

            // Logging Mode Selection
            Label lblLoggingMode = new Label
            {
                Text = "Logging Mode:",
                Location = new Point(12, 25),
                Size = new Size(110, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };

            rbRecordAllData = new RadioButton
            {
                Text = "Record All Data",
                Location = new Point(12, 48),
                Size = new Size(135, 20),
                Checked = true,
                Font = new Font("Segoe UI", 8)
            };
            rbRecordAllData.CheckedChanged += RbLoggingMode_CheckedChanged;

            rbMonolateralAcquisition = new RadioButton
            {
                Text = "Monolateral Acquisition",
                Location = new Point(155, 48),
                Size = new Size(160, 20),
                Font = new Font("Segoe UI", 8)
            };
            rbMonolateralAcquisition.CheckedChanged += RbLoggingMode_CheckedChanged;

            // Panel for Arm Selection (creates independent radio button group)
            pnlArmSelection = new Panel
            {
                Location = new Point(325, 20),
                Size = new Size(120, 50),
                Visible = false
            };

            // Arm Selection Label
            lblArmSelection = new Label
            {
                Text = "Select Arm:",
                Location = new Point(0, 5),
                Size = new Size(80, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };

            // Arm Selection Radio Buttons (coordinates relative to Panel)
            rbLeftArm = new RadioButton
            {
                Text = "Left",
                Location = new Point(0, 28),
                Size = new Size(55, 20),
                Checked = true,
                Font = new Font("Segoe UI", 8)
            };

            rbRightArm = new RadioButton
            {
                Text = "Right",
                Location = new Point(60, 28),
                Size = new Size(60, 20),
                Font = new Font("Segoe UI", 8)
            };

            // Add arm selection controls to Panel
            pnlArmSelection.Controls.Add(lblArmSelection);
            pnlArmSelection.Controls.Add(rbLeftArm);
            pnlArmSelection.Controls.Add(rbRightArm);

            // CSV Logging Buttons
            btnStartLog = new Button
            {
                Text = "Start CSV Logging",
                Location = new Point(12, 78),
                Size = new Size(200, 35),
                BackColor = Color.LightGreen,
                FlatStyle = FlatStyle.Flat
            };
            btnStartLog.Click += BtnStartLog_Click;

            btnStopLog = new Button
            {
                Text = "Stop CSV Logging",
                Location = new Point(230, 78),
                Size = new Size(200, 35),
                BackColor = Color.LightSalmon,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            btnStopLog.Click += BtnStopLog_Click;

            // State Display (for Monolateral Acquisition)
            lblStateDisplay = new Label
            {
                Text = "State:",
                Location = new Point(12, 125),
                Size = new Size(50, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Visible = false
            };

            lblStateValue = new Label
            {
                Text = "IDLE",
                Location = new Point(65, 125),
                Size = new Size(150, 25),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.DarkSlateGray,
                Visible = false
            };

            // Next State Button (for Monolateral Acquisition)
            btnNextState = new Button
            {
                Text = "Next State",
                Location = new Point(230, 120),
                Size = new Size(200, 35),
                BackColor = Color.LightSkyBlue,
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Visible = false,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            btnNextState.Click += BtnNextState_Click;

            // CSV Status Label
            lblCsvStatus = new Label
            {
                Text = "Not logging",
                Location = new Point(12, 165),
                Size = new Size(430, 18),
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.DimGray
            };

            gbLog.Controls.AddRange(new Control[] { 
                lblLoggingMode, rbRecordAllData, rbMonolateralAcquisition,
                pnlArmSelection,
                btnStartLog, btnStopLog, 
                lblStateDisplay, lblStateValue, btnNextState,
                lblCsvStatus 
            });

            this.Controls.Add(topPanel);
            this.Controls.Add(lblEELeftTitle);
            this.Controls.Add(dgvEELeft);
            this.Controls.Add(lblEERightTitle);
            this.Controls.Add(dgvEERight);
            this.Controls.Add(lblFrequencyDisplay);
            this.Controls.Add(lblShoulderLeftTitle);
            this.Controls.Add(dgvShoulderLeft);
            this.Controls.Add(lblShoulderRightTitle);
            this.Controls.Add(dgvShoulderRight);
            this.Controls.Add(lblElbowWristLeftTitle);
            this.Controls.Add(dgvElbowWristLeft);
            this.Controls.Add(lblElbowWristRightTitle);
            this.Controls.Add(dgvElbowWristRight);
            this.Controls.Add(gbLeft);
            this.Controls.Add(gbRight);
            this.Controls.Add(gbLog);
        }

        private void InitializeDataGrid()
        {
            // ── Left End Effector Data Grid ───────────────────────────────────
            dgvEELeft.Columns.Add("Axis", "Axis");
            dgvEELeft.Columns.Add("Position", "Position (m)");
            dgvEELeft.Columns.Add("Velocity", "Velocity (m/s)");

            // Set column width proportions (FillWeight)
            dgvEELeft.Columns[0].FillWeight = 25;   // Axis - narrower
            dgvEELeft.Columns[1].FillWeight = 37.5f;  // Position
            dgvEELeft.Columns[2].FillWeight = 37.5f;  // Velocity

            dgvEELeft.Rows.Add("X", "0.0000", "0.0000");
            dgvEELeft.Rows.Add("Y", "0.0000", "0.0000");
            dgvEELeft.Rows.Add("Z", "0.0000", "0.0000");

            dgvEELeft.ColumnHeadersDefaultCellStyle.BackColor = Color.DarkBlue;
            dgvEELeft.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvEELeft.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            dgvEELeft.EnableHeadersVisualStyles = false;
            dgvEELeft.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(240, 245, 255);

            // ── Right End Effector Data Grid ──────────────────────────────────
            dgvEERight.Columns.Add("Axis", "Axis");
            dgvEERight.Columns.Add("Position", "Position (m)");
            dgvEERight.Columns.Add("Velocity", "Velocity (m/s)");

            // Set column width proportions (FillWeight)
            dgvEERight.Columns[0].FillWeight = 25;   // Axis - narrower
            dgvEERight.Columns[1].FillWeight = 37.5f;  // Position
            dgvEERight.Columns[2].FillWeight = 37.5f;  // Velocity

            dgvEERight.Rows.Add("X", "0.0000", "0.0000");
            dgvEERight.Rows.Add("Y", "0.0000", "0.0000");
            dgvEERight.Rows.Add("Z", "0.0000", "0.0000");

            dgvEERight.ColumnHeadersDefaultCellStyle.BackColor = Color.DarkGreen;
            dgvEERight.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvEERight.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            dgvEERight.EnableHeadersVisualStyles = false;
            dgvEERight.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(240, 255, 245);

            // ── Shoulder Joint Tables ─────────────────────────────────────────
            InitializeJointGrid(dgvShoulderLeft, "SHOULDER", Color.DarkBlue);
            InitializeJointGrid(dgvShoulderRight, "SHOULDER", Color.DarkGreen);

            // ── Elbow/Wrist Joint Tables ──────────────────────────────────────
            InitializeJointGrid(dgvElbowWristLeft, "ELBOW_WRIST", Color.DarkBlue);
            InitializeJointGrid(dgvElbowWristRight, "ELBOW_WRIST", Color.DarkGreen);
        }

        private void InitializeJointGrid(DataGridView grid, string type, Color headerColor)
        {
            grid.Columns.Add("Joint", "Joint");
            grid.Columns.Add("PosRad", "Position (rad)");
            grid.Columns.Add("PosDeg", "Position (deg)");
            grid.Columns.Add("Velocity", "Velocity (rad/s)");

            // Set column width proportions (FillWeight) - make first column wider for long joint names
            grid.Columns[0].FillWeight = 38;    // Joint name - wider for "Prono Supination"
            grid.Columns[1].FillWeight = 21;    // Position (rad)
            grid.Columns[2].FillWeight = 21;    // Position (deg)
            grid.Columns[3].FillWeight = 20;    // Velocity

            if (type == "SHOULDER")
            {
                grid.Rows.Add("Abduction", "0.0000", "0.00", "0.000");       // Index 0
                grid.Rows.Add("Rotation", "0.0000", "0.00", "0.000");       // Index 1
                grid.Rows.Add("Flexion", "0.0000", "0.00", "0.000");        // Index 4
            }
            else // Elbow/Wrist
            {
                grid.Rows.Add("Elbow Flexion", "0.0000", "0.00", "0.000");   // Index 5
                grid.Rows.Add("Prono Supination", "0.0000", "0.00", "0.000"); // Index 6
                grid.Rows.Add("Wrist Flexion", "0.0000", "0.00", "0.000");    // Index 7
            }

            grid.ColumnHeadersDefaultCellStyle.BackColor = headerColor;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            grid.EnableHeadersVisualStyles = false;

            if (headerColor == Color.DarkBlue)
                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(240, 245, 255);
            else
                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(240, 255, 245);
        }

        private void EnsureCsvDirectory()
        {
            if (!Directory.Exists(csvDirectory))
                Directory.CreateDirectory(csvDirectory);
        }

        // ── Phase code to name mapping ────────────────────────────────────────
        // Converts numeric phase codes from the robot controller to readable names
        private string GetPhaseName(float phaseCode)
        {
            int code = (int)phaseCode;
            return code switch
            {
                -100 => "STARTING_UP",
                0 => "DRIVER_OFF",
                1 => "DEACT_DRIVER",
                2 => "DEACT_CONTROL",
                5 => "ACT_DRIVER",
                6 => "ACT_CONTROL",
                7 => "MOTOR_RESET",
                8 => "ENCODER_RESET",
                10 => "WEARING",
                15 => "PRE_REHAB",
                20 => "REHAB",
                100 => "FAULT",
                _ => $"UNKNOWN ({code})"
            };
        }

        // Returns a color for UI display based on phase
        private Color GetPhaseColor(float phaseCode)
        {
            int code = (int)phaseCode;
            return code switch
            {
                10 => Color.SteelBlue,      // Wearing mode
                20 => Color.Firebrick,      // Rehab mode
                15 => Color.DarkOrange,     // Pre-rehab
                100 => Color.Red,           // Fault
                0 => Color.Gray,            // Driver off
                _ => Color.DarkSlateGray
            };
        }

        // ── Timer tick ────────────────────────────────────────────────────────
        // Called every 20ms (50 Hz) to update GUI and log data
        private void DataTimer_Tick(object? sender, EventArgs e)
        {
            if (isConnectedMode && sharedMemory != null)
                ReadFromSharedMemory();
            else
                FillPreviewZeros();

            UpdateTable();
            lblFrequency.Text = $"Frequency: {frequency} Hz";

            if (isConnectedMode && sharedMemory != null)
                UpdateArmPhaseLabels();
            else
                ResetArmPhaseLabels();

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
                sharedMemory.readGuiDataInStruct();

                // Read end effector position and velocity (no force)
                x_left = sharedMemory.AppDataInStruct.armLeft.EE_Pos[0];
                y_left = sharedMemory.AppDataInStruct.armLeft.EE_Pos[1];
                z_left = sharedMemory.AppDataInStruct.armLeft.EE_Pos[2];
                vx_left = sharedMemory.AppDataInStruct.armLeft.EE_Speed[0];
                vy_left = sharedMemory.AppDataInStruct.armLeft.EE_Speed[1];
                vz_left = sharedMemory.AppDataInStruct.armLeft.EE_Speed[2];

                x_right = sharedMemory.AppDataInStruct.armRight.EE_Pos[0];
                y_right = sharedMemory.AppDataInStruct.armRight.EE_Pos[1];
                z_right = sharedMemory.AppDataInStruct.armRight.EE_Pos[2];
                vx_right = sharedMemory.AppDataInStruct.armRight.EE_Speed[0];
                vy_right = sharedMemory.AppDataInStruct.armRight.EE_Speed[1];
                vz_right = sharedMemory.AppDataInStruct.armRight.EE_Speed[2];

                // Read joint data (positions in radians, already converted to degrees in JointPosL/R)
                for (int i = 0; i < 8; i++)
                {
                    jointPosRad_left[i] = sharedMemory.AppDataInStruct.armLeft.Joint_Pos[i];
                    jointPosDeg_left[i] = sharedMemory.JointPosL[i];  // Already converted to degrees
                    jointVel_left[i] = sharedMemory.AppDataInStruct.armLeft.Joint_Speed[i];

                    jointPosRad_right[i] = sharedMemory.AppDataInStruct.armRight.Joint_Pos[i];
                    jointPosDeg_right[i] = sharedMemory.JointPosR[i];  // Already converted to degrees
                    jointVel_right[i] = sharedMemory.AppDataInStruct.armRight.Joint_Speed[i];
                }

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
            x_right = y_right = z_right = 0;
            vx_right = vy_right = vz_right = 0;
            frequency = 0;

            // Zero out joint data
            for (int i = 0; i < 8; i++)
            {
                jointPosRad_left[i] = 0;
                jointPosDeg_left[i] = 0;
                jointVel_left[i] = 0;
                jointPosRad_right[i] = 0;
                jointPosDeg_right[i] = 0;
                jointVel_right[i] = 0;
            }
        }

        private void UpdateTable()
        {
            // Skip updating the display if frozen (but data collection continues)
            if (isDisplayFrozen) return;

            // Update Left End Effector table (Position and Velocity columns)
            dgvEELeft.Rows[0].Cells[1].Value = x_left.ToString("F4");      // X Position
            dgvEELeft.Rows[0].Cells[2].Value = vx_left.ToString("F4");     // X Velocity
            dgvEELeft.Rows[1].Cells[1].Value = y_left.ToString("F4");      // Y Position
            dgvEELeft.Rows[1].Cells[2].Value = vy_left.ToString("F4");     // Y Velocity
            dgvEELeft.Rows[2].Cells[1].Value = z_left.ToString("F4");      // Z Position
            dgvEELeft.Rows[2].Cells[2].Value = vz_left.ToString("F4");     // Z Velocity

            // Update Right End Effector table (Position and Velocity columns)
            dgvEERight.Rows[0].Cells[1].Value = x_right.ToString("F4");    // X Position
            dgvEERight.Rows[0].Cells[2].Value = vx_right.ToString("F4");   // X Velocity
            dgvEERight.Rows[1].Cells[1].Value = y_right.ToString("F4");    // Y Position
            dgvEERight.Rows[1].Cells[2].Value = vy_right.ToString("F4");   // Y Velocity
            dgvEERight.Rows[2].Cells[1].Value = z_right.ToString("F4");    // Z Position
            dgvEERight.Rows[2].Cells[2].Value = vz_right.ToString("F4");   // Z Velocity

            // Update Frequency Display (separate label)
            lblFrequencyDisplay.Text = $"Update Frequency: {frequency} Hz";

            // Update Shoulder Joint tables
            // Left Shoulder: Abduction (0), Rotation (1), Flexion (4)
            UpdateJointRow(dgvShoulderLeft, 0, jointPosRad_left[0], jointPosDeg_left[0], jointVel_left[0]);
            UpdateJointRow(dgvShoulderLeft, 1, jointPosRad_left[1], jointPosDeg_left[1], jointVel_left[1]);
            UpdateJointRow(dgvShoulderLeft, 2, jointPosRad_left[4], jointPosDeg_left[4], jointVel_left[4]);

            // Right Shoulder: Abduction (0), Rotation (1), Flexion (4)
            UpdateJointRow(dgvShoulderRight, 0, jointPosRad_right[0], jointPosDeg_right[0], jointVel_right[0]);
            UpdateJointRow(dgvShoulderRight, 1, jointPosRad_right[1], jointPosDeg_right[1], jointVel_right[1]);
            UpdateJointRow(dgvShoulderRight, 2, jointPosRad_right[4], jointPosDeg_right[4], jointVel_right[4]);

            // Update Elbow/Wrist Joint tables
            // Left Elbow/Wrist: Elbow Flexion (5), Prono Supination (6), Wrist Flexion (7)
            UpdateJointRow(dgvElbowWristLeft, 0, jointPosRad_left[5], jointPosDeg_left[5], jointVel_left[5]);
            UpdateJointRow(dgvElbowWristLeft, 1, jointPosRad_left[6], jointPosDeg_left[6], jointVel_left[6]);
            UpdateJointRow(dgvElbowWristLeft, 2, jointPosRad_left[7], jointPosDeg_left[7], jointVel_left[7]);

            // Right Elbow/Wrist: Elbow Flexion (5), Prono Supination (6), Wrist Flexion (7)
            UpdateJointRow(dgvElbowWristRight, 0, jointPosRad_right[5], jointPosDeg_right[5], jointVel_right[5]);
            UpdateJointRow(dgvElbowWristRight, 1, jointPosRad_right[6], jointPosDeg_right[6], jointVel_right[6]);
            UpdateJointRow(dgvElbowWristRight, 2, jointPosRad_right[7], jointPosDeg_right[7], jointVel_right[7]);
        }

        // Helper method to update a single joint row
        // ToString("F3") formats the number to 3 decimal places for better readability
        private void UpdateJointRow(DataGridView grid, int rowIndex, float posRad, float posDeg, float vel)
        {
            grid.Rows[rowIndex].Cells[1].Value = posRad.ToString("F4");
            grid.Rows[rowIndex].Cells[2].Value = posDeg.ToString("F2");
            grid.Rows[rowIndex].Cells[3].Value = vel.ToString("F3");
        }

        // ── Update arm phase labels from ControlPhase ─────────────────────────
        private void UpdateArmPhaseLabels()
        {
            if (sharedMemory == null) return;

            float phaseLeft = sharedMemory.GuiDataInStruct.Exos.armLeft.Status.ControlPhase;
            float phaseRight = sharedMemory.GuiDataInStruct.Exos.armRight.Status.ControlPhase;

            string nameLeft = GetPhaseName(phaseLeft);
            string nameRight = GetPhaseName(phaseRight);

            lblLeftArmState.Text = $"Phase: {nameLeft}";
            lblLeftArmState.ForeColor = GetPhaseColor(phaseLeft);

            lblRightArmState.Text = $"Phase: {nameRight}";
            lblRightArmState.ForeColor = GetPhaseColor(phaseRight);

            UpdateButtonHighlight(phaseLeft, btnWearLeft, btnRehabLeft);
            UpdateButtonHighlight(phaseRight, btnWearRight, btnRehabRight);
        }

        private void ResetArmPhaseLabels()
        {
            lblLeftArmState.Text = "Phase: —";
            lblLeftArmState.ForeColor = Color.Gray;
            lblRightArmState.Text = "Phase: —";
            lblRightArmState.ForeColor = Color.Gray;

            btnWearLeft.BackColor = ColWearIdle;
            btnRehabLeft.BackColor = ColRehabIdle;
            btnWearRight.BackColor = ColWearIdle;
            btnRehabRight.BackColor = ColRehabIdle;
        }

        private void UpdateButtonHighlight(float phase, Button wearBtn, Button rehabBtn)
        {
            int code = (int)phase;

            wearBtn.BackColor = ColWearIdle;
            rehabBtn.BackColor = ColRehabIdle;

            if (code == 10)
                wearBtn.BackColor = ColWearActive;
            else if (code == 20 || code == 15)
                rehabBtn.BackColor = ColRehabActive;
        }

        // ── Logging Mode Selection ────────────────────────────────────────────
        private void RbLoggingMode_CheckedChanged(object? sender, EventArgs e)
        {
            if (rbMonolateralAcquisition.Checked)
            {
                currentLoggingMode = LoggingMode.MonolateralAcquisition;

                // Show arm selection panel (contains lblArmSelection, rbLeftArm, rbRightArm)
                pnlArmSelection.Visible = true;

                // Show state controls (but keep button disabled until logging starts)
                lblStateDisplay.Visible = true;
                lblStateValue.Visible = true;
                btnNextState.Visible = true;
            }
            else
            {
                currentLoggingMode = LoggingMode.RecordAllData;

                // Hide arm selection panel
                pnlArmSelection.Visible = false;

                // Hide state controls
                lblStateDisplay.Visible = false;
                lblStateValue.Visible = false;
                btnNextState.Visible = false;
            }
        }

        // ── Next State Button (for Monolateral Acquisition) ───────────────────
        private void BtnNextState_Click(object? sender, EventArgs e)
        {
            switch (currentState)
            {
                case AcquisitionState.Idle:
                    currentState = AcquisitionState.Going;
                    lblStateValue.Text = "GOING";
                    lblStateValue.ForeColor = Color.DarkOrange;
                    break;

                case AcquisitionState.Going:
                    currentState = AcquisitionState.Returning;
                    lblStateValue.Text = "RETURNING";
                    lblStateValue.ForeColor = Color.DarkBlue;
                    break;

                case AcquisitionState.Returning:
                    // Stop logging and reset
                    StopMonolateralLogging();
                    break;
            }
        }

        private void StopMonolateralLogging()
        {
            // Stop data collection
            isCollectingData = false;
            btnStartLog.Enabled = true;
            btnStopLog.Enabled = false;
            btnNextState.Enabled = false;

            // Reset state
            currentState = AcquisitionState.Idle;
            lblStateValue.Text = "IDLE";
            lblStateValue.ForeColor = Color.DarkSlateGray;

            lblCsvStatus.Text = $"Last saved: {Path.GetFileName(csvPath ?? "—")}";
            lblCsvStatus.ForeColor = Color.DimGray;
        }

        // ── CSV logging ───────────────────────────────────────────────────────
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
                    if (currentLoggingMode == LoggingMode.RecordAllData)
                    {
                        // Original headers for all data
                        sw.WriteLine("Timestamp," +
                                     // Left Arm End Effector (position and velocity)
                                     "Left_EE_PosX_m,Left_EE_PosY_m,Left_EE_PosZ_m," +
                                     "Left_EE_VelX_mps,Left_EE_VelY_mps,Left_EE_VelZ_mps," +
                                     // Right Arm End Effector (position and velocity)
                                     "Right_EE_PosX_m,Right_EE_PosY_m,Right_EE_PosZ_m," +
                                     "Right_EE_VelX_mps,Right_EE_VelY_mps,Right_EE_VelZ_mps," +
                                     // Left Arm Joints (all 8 joints with position and velocity)
                                     "Left_J0_Abduction_rad,Left_J0_Abduction_deg,Left_J0_Abduction_vel_radps," +
                                     "Left_J1_Rotation_rad,Left_J1_Rotation_deg,Left_J1_Rotation_vel_radps," +
                                     "Left_J2_rad,Left_J2_deg,Left_J2_vel_radps," +
                                     "Left_J3_rad,Left_J3_deg,Left_J3_vel_radps," +
                                     "Left_J4_ShoulderFlexion_rad,Left_J4_ShoulderFlexion_deg,Left_J4_ShoulderFlexion_vel_radps," +
                                     "Left_J5_ElbowFlexion_rad,Left_J5_ElbowFlexion_deg,Left_J5_ElbowFlexion_vel_radps," +
                                     "Left_J6_PronoSupination_rad,Left_J6_PronoSupination_deg,Left_J6_PronoSupination_vel_radps," +
                                     "Left_J7_WristFlexion_rad,Left_J7_WristFlexion_deg,Left_J7_WristFlexion_vel_radps," +
                                     // Right Arm Joints (all 8 joints with position and velocity)
                                     "Right_J0_Abduction_rad,Right_J0_Abduction_deg,Right_J0_Abduction_vel_radps," +
                                     "Right_J1_Rotation_rad,Right_J1_Rotation_deg,Right_J1_Rotation_vel_radps," +
                                     "Right_J2_rad,Right_J2_deg,Right_J2_vel_radps," +
                                     "Right_J3_rad,Right_J3_deg,Right_J3_vel_radps," +
                                     "Right_J4_ShoulderFlexion_rad,Right_J4_ShoulderFlexion_deg,Right_J4_ShoulderFlexion_vel_radps," +
                                     "Right_J5_ElbowFlexion_rad,Right_J5_ElbowFlexion_deg,Right_J5_ElbowFlexion_vel_radps," +
                                     "Right_J6_PronoSupination_rad,Right_J6_PronoSupination_deg,Right_J6_PronoSupination_vel_radps," +
                                     "Right_J7_WristFlexion_rad,Right_J7_WristFlexion_deg,Right_J7_WristFlexion_vel_radps," +
                                     "Frequency_Hz");
                    }
                    else // MonolateralAcquisition
                    {
                        // Get arm prefix based on selection
                        string armPrefix = rbLeftArm.Checked ? "Left" : "Right";

                        // Headers for selected arm only + State column
                        sw.WriteLine("Timestamp," +
                                     $"{armPrefix}_EE_PosX_m,{armPrefix}_EE_PosY_m,{armPrefix}_EE_PosZ_m," +
                                     $"{armPrefix}_EE_VelX_mps,{armPrefix}_EE_VelY_mps,{armPrefix}_EE_VelZ_mps," +
                                     $"{armPrefix}_J0_Abduction_rad,{armPrefix}_J0_Abduction_deg,{armPrefix}_J0_Abduction_vel_radps," +
                                     $"{armPrefix}_J1_Rotation_rad,{armPrefix}_J1_Rotation_deg,{armPrefix}_J1_Rotation_vel_radps," +
                                     $"{armPrefix}_J2_rad,{armPrefix}_J2_deg,{armPrefix}_J2_vel_radps," +
                                     $"{armPrefix}_J3_rad,{armPrefix}_J3_deg,{armPrefix}_J3_vel_radps," +
                                     $"{armPrefix}_J4_ShoulderFlexion_rad,{armPrefix}_J4_ShoulderFlexion_deg,{armPrefix}_J4_ShoulderFlexion_vel_radps," +
                                     $"{armPrefix}_J5_ElbowFlexion_rad,{armPrefix}_J5_ElbowFlexion_deg,{armPrefix}_J5_ElbowFlexion_vel_radps," +
                                     $"{armPrefix}_J6_PronoSupination_rad,{armPrefix}_J6_PronoSupination_deg,{armPrefix}_J6_PronoSupination_vel_radps," +
                                     $"{armPrefix}_J7_WristFlexion_rad,{armPrefix}_J7_WristFlexion_deg,{armPrefix}_J7_WristFlexion_vel_radps," +
                                     "Frequency_Hz,State");
                    }
                    headerWritten = true;
                }

                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                if (currentLoggingMode == LoggingMode.RecordAllData)
                {
                    // Build joint data strings for both arms
                    string jointsLeftData = "";
                    string jointsRightData = "";
                    for (int i = 0; i < 8; i++)
                    {
                        jointsLeftData += $"{jointPosRad_left[i]},{jointPosDeg_left[i]},{jointVel_left[i]},";
                        jointsRightData += $"{jointPosRad_right[i]},{jointPosDeg_right[i]},{jointVel_right[i]},";
                    }

                    sw.WriteLine($"{ts}," +
                                 $"{x_left},{y_left},{z_left}," +
                                 $"{vx_left},{vy_left},{vz_left}," +
                                 $"{x_right},{y_right},{z_right}," +
                                 $"{vx_right},{vy_right},{vz_right}," +
                                 $"{jointsLeftData}" +
                                 $"{jointsRightData}" +
                                 $"{frequency}");
                }
                else // MonolateralAcquisition
                {
                    // Select data based on chosen arm
                    double x, y, z, vx, vy, vz;
                    float[] jointPosRad, jointPosDeg, jointVel;

                    if (rbLeftArm.Checked)
                    {
                        x = x_left; y = y_left; z = z_left;
                        vx = vx_left; vy = vy_left; vz = vz_left;
                        jointPosRad = jointPosRad_left;
                        jointPosDeg = jointPosDeg_left;
                        jointVel = jointVel_left;
                        selectedArmForMonolateral = ArmSelection.Left;
                    }
                    else
                    {
                        x = x_right; y = y_right; z = z_right;
                        vx = vx_right; vy = vy_right; vz = vz_right;
                        jointPosRad = jointPosRad_right;
                        jointPosDeg = jointPosDeg_right;
                        jointVel = jointVel_right;
                        selectedArmForMonolateral = ArmSelection.Right;
                    }

                    // Build joint data string for selected arm
                    string jointsData = "";
                    for (int i = 0; i < 8; i++)
                    {
                        jointsData += $"{jointPosRad[i]},{jointPosDeg[i]},{jointVel[i]},";
                    }

                    // Get current state as string
                    string stateName = currentState.ToString().ToUpper();

                    sw.WriteLine($"{ts}," +
                                 $"{x},{y},{z}," +
                                 $"{vx},{vy},{vz}," +
                                 $"{jointsData}" +
                                 $"{frequency},{stateName}");
                }
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

                ResetArmPhaseLabels();
            }

            btnWearLeft.Enabled = isConnectedMode;
            btnRehabLeft.Enabled = isConnectedMode;
            btnStopRehabLeft.Enabled = isConnectedMode;
            btnStopDeviceLeft.Enabled = isConnectedMode;
            btnWearRight.Enabled = isConnectedMode;
            btnRehabRight.Enabled = isConnectedMode;
            btnStopRehabRight.Enabled = isConnectedMode;
            btnStopDeviceRight.Enabled = isConnectedMode;
            btnStartLog.Enabled = isConnectedMode;
        }

        // ── Arm command buttons ───────────────────────────────────────────────

        private void BtnWearLeft_Click(object? sender, EventArgs e)
        {
            if (sharedMemory == null) return;
            try
            {
                sharedMemory.SelectedArm = 1;
                sharedMemory.startWearing();
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending Rehab command (Left):\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Stop Rehab - transitions back to wearing mode
        private void BtnStopRehabLeft_Click(object? sender, EventArgs e)
        {
            if (sharedMemory == null) return;
            try
            {
                sharedMemory.SelectedArm = 1;
                // First stop rehab
                sharedMemory.stopRehab();
                // Small delay to let the command process
                System.Threading.Thread.Sleep(100);
                // Then start wearing
                sharedMemory.startWearing();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending Stop Rehab command (Left):\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Stop Device - returns arm to DRIVER_OFF state
        private void BtnStopDeviceLeft_Click(object? sender, EventArgs e)
        {
            if (sharedMemory == null) return;
            try
            {
                sharedMemory.SelectedArm = 1;
                sharedMemory.stopWearing();  // Sends STOP_DEVICE command
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending Stop Device command (Left):\n{ex.Message}",
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending Rehab command (Right):\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnStopRehabRight_Click(object? sender, EventArgs e)
        {
            if (sharedMemory == null) return;
            try
            {
                sharedMemory.SelectedArm = 3;
                sharedMemory.stopRehab();
                System.Threading.Thread.Sleep(100);
                sharedMemory.startWearing();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending Stop Rehab command (Right):\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnStopDeviceRight_Click(object? sender, EventArgs e)
        {
            if (sharedMemory == null) return;
            try
            {
                sharedMemory.SelectedArm = 3;
                sharedMemory.stopWearing();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending Stop Device command (Right):\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── CSV logging buttons ───────────────────────────────────────────────
        private void BtnStartLog_Click(object? sender, EventArgs e)
        {
            StartNewCsvSession();
            isCollectingData = true;
            btnStartLog.Enabled = false;
            btnStopLog.Enabled = true;

            // If in Monolateral Acquisition mode
            if (currentLoggingMode == LoggingMode.MonolateralAcquisition)
            {
                // Reset state to idle and enable Next State button
                currentState = AcquisitionState.Idle;
                lblStateValue.Text = "IDLE";
                lblStateValue.ForeColor = Color.DarkSlateGray;
                btnNextState.Enabled = true;

                // Disable mode selection during logging
                rbRecordAllData.Enabled = false;
                rbMonolateralAcquisition.Enabled = false;
                rbLeftArm.Enabled = false;
                rbRightArm.Enabled = false;
            }
        }

        private void BtnStopLog_Click(object? sender, EventArgs e)
        {
            isCollectingData = false;
            btnStartLog.Enabled = true;
            btnStopLog.Enabled = false;
            lblCsvStatus.Text = $"Last saved: {Path.GetFileName(csvPath ?? "—")}";
            lblCsvStatus.ForeColor = Color.DimGray;

            // If in Monolateral Acquisition mode
            if (currentLoggingMode == LoggingMode.MonolateralAcquisition)
            {
                // Reset state and disable Next State button
                currentState = AcquisitionState.Idle;
                lblStateValue.Text = "IDLE";
                lblStateValue.ForeColor = Color.DarkSlateGray;
                btnNextState.Enabled = false;

                // Re-enable mode selection
                rbRecordAllData.Enabled = true;
                rbMonolateralAcquisition.Enabled = true;
                rbLeftArm.Enabled = true;
                rbRightArm.Enabled = true;
            }
        }

        // ── Freeze/Unfreeze Display Toggle ────────────────────────────────────
        private void BtnFreezeDisplay_Click(object? sender, EventArgs e)
        {
            isDisplayFrozen = !isDisplayFrozen;

            if (isDisplayFrozen)
            {
                btnFreezeDisplay.Text = "▶ Unfreeze Display";
                btnFreezeDisplay.BackColor = Color.LightCoral;
            }
            else
            {
                btnFreezeDisplay.Text = "⏸ Freeze Display";
                btnFreezeDisplay.BackColor = Color.LightYellow;
            }
        }

        // ── Cleanup ───────────────────────────────────────────────────────────
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            dataTimer.Stop();
            base.OnFormClosing(e);
        }
    }
}
