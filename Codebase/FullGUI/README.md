# ALEX Robot Control GUI - Enhanced Data Collection Interface

## 📋 Overview

This is an enhanced Windows Forms GUI application for controlling and monitoring the ALEX (Arm Lexoskeleton) robotic rehabilitation system. The application provides real-time visualization of robot arm movements and comprehensive data logging capabilities at 50 Hz.

### Key Features

- ✅ **50 Hz Real-Time Monitoring** - Updates every 20ms for smooth visualization
- ✅ **Dual Arm Control** - Independent control of left and right robot arms
- ✅ **Multiple Data Tables** - Organized display of end effector and joint data
- ✅ **Comprehensive CSV Logging** - Records 61 data points per sample
- ✅ **Freeze/Unfreeze Display** - Pause screen updates while logging continues
- ✅ **Color-Coded Interface** - Blue for left arm, Green for right arm
- ✅ **Phase Monitoring** - Real-time display of robot arm states

---

## 🖥️ GUI Layout

### Top Status Bar
- **Mode Indicator** - Shows PREVIEW (not connected) or CONNECTED status
- **Connection Toggle** - Switch between preview and connected modes
- **Frequency Display** - Shows current update rate in Hz
- **Freeze/Unfreeze Button** - Pause display updates (⏸/▶)

### Data Display Tables (Left Side)

#### ◆ LEFT ARM - END EFFECTOR
3-column table showing position and velocity side-by-side:
- **X-axis** - Position (m) & Velocity (m/s)
- **Y-axis** - Position (m) & Velocity (m/s)
- **Z-axis** - Position (m) & Velocity (m/s)

#### ◆ RIGHT ARM - END EFFECTOR
3-column table showing position and velocity side-by-side:
- **X-axis** - Position (m) & Velocity (m/s)
- **Y-axis** - Position (m) & Velocity (m/s)
- **Z-axis** - Position (m) & Velocity (m/s)

**Update Frequency Display** - Separate label below tables showing current update rate (Hz)

#### ◆ LEFT ARM - SHOULDER JOINTS
- **Abduction** (Joint 0) - Position (rad/deg) & Velocity (rad/s)
- **Rotation** (Joint 1) - Position (rad/deg) & Velocity (rad/s)
- **Flexion** (Joint 4) - Position (rad/deg) & Velocity (rad/s)

#### ◆ RIGHT ARM - SHOULDER JOINTS
- **Abduction** (Joint 0) - Position (rad/deg) & Velocity (rad/s)
- **Rotation** (Joint 1) - Position (rad/deg) & Velocity (rad/s)
- **Flexion** (Joint 4) - Position (rad/deg) & Velocity (rad/s)

#### ◆ LEFT ARM - ELBOW/WRIST JOINTS
- **Elbow Flexion** (Joint 5) - Position (rad/deg) & Velocity (rad/s)
- **Prono Supination** (Joint 6) - Position (rad/deg) & Velocity (rad/s)
- **Wrist Flexion** (Joint 7) - Position (rad/deg) & Velocity (rad/s)

#### ◆ RIGHT ARM - ELBOW/WRIST JOINTS
- **Elbow Flexion** (Joint 5) - Position (rad/deg) & Velocity (rad/s)
- **Prono Supination** (Joint 6) - Position (rad/deg) & Velocity (rad/s)
- **Wrist Flexion** (Joint 7) - Position (rad/deg) & Velocity (rad/s)

### Control Panels (Right Side)

#### Left Arm Control
- **Start Wearing** - Activate left arm wearing mode
- **Start Rehab** - Begin rehabilitation mode
- **Stop Rehab** - Return to wearing mode
- **STOP DEVICE** - Emergency stop (returns to DRIVER_OFF)
- **Phase Display** - Shows current arm state

#### Right Arm Control
- **Start Wearing** - Activate right arm wearing mode
- **Start Rehab** - Begin rehabilitation mode
- **Stop Rehab** - Return to wearing mode
- **STOP DEVICE** - Emergency stop (returns to DRIVER_OFF)
- **Phase Display** - Shows current arm state

#### Data Collection
- **Start CSV Logging** - Begin recording data to CSV file
- **Stop CSV Logging** - Stop recording and save file
- **Status Display** - Shows current logging status and filename

---

## 📊 CSV Data Format

### File Naming Convention
Files are automatically saved as: `data_YYYYMMDD_HHmmss.csv`

Example: `data_20240115_143022.csv`

### Storage Location
```
C:\Users\franc\Documents\ALEX_Jia\Codebase\Data\
```

### CSV Structure (61 Columns Total)

#### Column Headers

```csv
Timestamp,
Left_EE_PosX_m,Left_EE_PosY_m,Left_EE_PosZ_m,
Left_EE_VelX_mps,Left_EE_VelY_mps,Left_EE_VelZ_mps,
Right_EE_PosX_m,Right_EE_PosY_m,Right_EE_PosZ_m,
Right_EE_VelX_mps,Right_EE_VelY_mps,Right_EE_VelZ_mps,
Left_J0_Abduction_rad,Left_J0_Abduction_deg,Left_J0_Abduction_vel_radps,
Left_J1_Rotation_rad,Left_J1_Rotation_deg,Left_J1_Rotation_vel_radps,
Left_J2_rad,Left_J2_deg,Left_J2_vel_radps,
Left_J3_rad,Left_J3_deg,Left_J3_vel_radps,
Left_J4_ShoulderFlexion_rad,Left_J4_ShoulderFlexion_deg,Left_J4_ShoulderFlexion_vel_radps,
Left_J5_ElbowFlexion_rad,Left_J5_ElbowFlexion_deg,Left_J5_ElbowFlexion_vel_radps,
Left_J6_PronoSupination_rad,Left_J6_PronoSupination_deg,Left_J6_PronoSupination_vel_radps,
Left_J7_WristFlexion_rad,Left_J7_WristFlexion_deg,Left_J7_WristFlexion_vel_radps,
Right_J0_Abduction_rad,Right_J0_Abduction_deg,Right_J0_Abduction_vel_radps,
Right_J1_Rotation_rad,Right_J1_Rotation_deg,Right_J1_Rotation_vel_radps,
Right_J2_rad,Right_J2_deg,Right_J2_vel_radps,
Right_J3_rad,Right_J3_deg,Right_J3_vel_radps,
Right_J4_ShoulderFlexion_rad,Right_J4_ShoulderFlexion_deg,Right_J4_ShoulderFlexion_vel_radps,
Right_J5_ElbowFlexion_rad,Right_J5_ElbowFlexion_deg,Right_J5_ElbowFlexion_vel_radps,
Right_J6_PronoSupination_rad,Right_J6_PronoSupination_deg,Right_J6_PronoSupination_vel_radps,
Right_J7_WristFlexion_rad,Right_J7_WristFlexion_deg,Right_J7_WristFlexion_vel_radps,
Frequency_Hz
```

### Column Abbreviations Legend

| Abbreviation | Meaning |
|--------------|---------|
| `EE` | End Effector |
| `Pos` | Position |
| `Vel` | Velocity |
| `m` | meters |
| `mps` | meters per second |
| `rad` | radians |
| `deg` | degrees |
| `radps` | radians per second |
| `Hz` | Hertz (frequency) |
| `J0-J7` | Joint indices (0 through 7) |

### Data Breakdown

**Per Row (every 20ms at 50 Hz):**
1. **1 Timestamp** - Format: `yyyy-MM-dd HH:mm:ss.fff`
2. **6 Left End Effector Values** - Position (X, Y, Z) + Velocity (X, Y, Z)
3. **6 Right End Effector Values** - Position (X, Y, Z) + Velocity (X, Y, Z)
4. **24 Left Joint Values** - 8 joints × 3 values (rad, deg, vel)
5. **24 Right Joint Values** - 8 joints × 3 values (rad, deg, vel)
6. **1 Frequency Value** - Current update frequency in Hz

**Total: 61 columns per row**

---

## 🎯 Joint Mapping Reference

### Joint Index to Name Mapping

| Joint Index | Joint Name | Description |
|-------------|------------|-------------|
| **J0** | Shoulder Abduction | Arm movement away from body |
| **J1** | Shoulder Rotation | Internal/external rotation |
| **J2** | *(Intermediate)* | Not displayed in GUI |
| **J3** | *(Intermediate)* | Not displayed in GUI |
| **J4** | Shoulder Flexion | Forward/backward arm movement |
| **J5** | Elbow Flexion | Elbow bend/extension |
| **J6** | Prono Supination | Forearm rotation |
| **J7** | Wrist Flexion | Wrist bend |

### Displayed Joints (6 per arm)

**Shoulder Group:**
- J0 (Abduction)
- J1 (Rotation)
- J4 (Flexion)

**Elbow/Wrist Group:**
- J5 (Elbow Flexion)
- J6 (Prono Supination)
- J7 (Wrist Flexion)

---

## 🚀 Getting Started

### Prerequisites

- **Operating System:** Windows 10/11
- **.NET Runtime:** .NET 10.0 or higher
- **ALEX Robot System:** Must be running with shared memory interface active

### Installation

1. Clone or download the repository
2. Open the solution in Visual Studio 2022 or later
3. Build the solution (Ctrl+Shift+B)
4. Run the application (F5)

### First Time Setup

1. **Launch the Application**
   - The GUI starts in PREVIEW mode (not connected)
   - All controls are disabled except the connection toggle

2. **Ensure Data Directory Exists**
   - Default: `C:\Users\franc\Documents\ALEX_Jia\Codebase\Data`
   - Directory is created automatically if missing

3. **Connect to Robot**
   - Click "Switch to CONNECTED Mode"
   - Ensure ALEX robot controller is running
   - Connection status will update in top bar

---

## 📖 Usage Guide

### Basic Operation

#### 1. Connecting to the Robot

```
1. Start the ALEX robot controller software
2. Launch the GUI application
3. Click "Switch to CONNECTED Mode" button
4. Verify connection status turns green
5. All control buttons should now be enabled
```

#### 2. Starting Data Collection

```
1. Ensure robot is connected (green status)
2. Click "Start CSV Logging" button
3. Logging status shows filename
4. Data is recorded at 50 Hz (every 20ms)
5. Click "Stop CSV Logging" to finish
```

#### 3. Controlling Robot Arms

**Wearing Mode (Initial Setup):**
```
1. Click "Start Wearing" for desired arm
2. Wait for phase to show "WEARING"
3. Button will highlight in blue
4. Robot arm is now in passive mode
```

**Rehabilitation Mode:**
```
1. Arm must be in WEARING mode first
2. Click "Start Rehab" for desired arm
3. Wait for phase to show "REHAB"
4. Button will highlight in red/orange
5. Robot provides active assistance
```

**Stopping Operations:**
```
Stop Rehab: Returns to WEARING mode
STOP DEVICE: Emergency stop to DRIVER_OFF state
```

#### 4. Using Freeze/Unfreeze Display

```
Purpose: Pause screen to read/note values
1. Click "⏸ Freeze Display" button
2. Screen updates pause (data logging continues)
3. Note desired values from frozen display
4. Click "▶ Unfreeze Display" to resume
```

### Understanding Robot Phases

| Phase Code | Phase Name | Description | Color |
|------------|------------|-------------|-------|
| -100 | STARTING_UP | System initialization | Dark Gray |
| 0 | DRIVER_OFF | Motors disabled | Gray |
| 1-8 | Activation Phases | Motor/encoder setup | Dark Gray |
| 10 | **WEARING** | Passive mode active | Steel Blue |
| 15 | PRE_REHAB | Preparing for rehab | Orange |
| 20 | **REHAB** | Active rehabilitation | Firebrick Red |
| 100 | **FAULT** | Error state | Red |

---

## 🔧 Technical Specifications

### Performance

- **Update Rate:** 50 Hz (20ms interval)
- **Data Resolution:** 
  - Position: 4 decimal places (0.0001 units)
  - Velocity: 3 decimal places (0.001 units)
  - Degrees: 2 decimal places (0.01°)
- **Timestamp Resolution:** Milliseconds (fff)

### Data Types

```csharp
End Effector Position: double (meters)
End Effector Velocity: double (meters/second)
Joint Position (rad): float (radians)
Joint Position (deg): float (degrees)
Joint Velocity: float (radians/second)
Frequency: int (Hz)
```

### Memory Interface

- **Shared Memory Segments:**
  - `ALEX32_DATA_IN` - Robot sensor data
  - `ALEX32_DATA_OUT` - Robot commands
  - `ALEX32_GUI_IN` - GUI status data
  - `ALEX32_GUI_OUT` - GUI commands
  - `ALEX32_BASE_CHANNEL` - Communication channel
  - `ALEX32_BASE_COMMAND` - Command interface
  - `ALEX32_BASE_STATUS` - Status interface
  - `SYSTEM_FAULT` - Fault reporting

---

## 📁 Project Structure

```
FullGUI/
├── Form1.cs              # Main GUI implementation
├── SharedMemory.cs       # Robot interface (do not modify)
├── Program.cs            # Application entry point
├── README.md             # This file
└── Data/                 # CSV output directory (auto-created)
    └── data_*.csv        # Logged data files
```

---

## 🐛 Troubleshooting

### Connection Issues

**Problem:** "Could not open shared memory segments" error

**Solutions:**
1. Ensure ALEX robot controller is running
2. Check that controller has created shared memory segments
3. Verify you have read/write permissions
4. Restart both controller and GUI

### Data Logging Issues

**Problem:** CSV logging button is disabled

**Solution:**
- Must be in CONNECTED mode first
- Click "Switch to CONNECTED Mode" before starting logging

**Problem:** CSV file not found

**Solution:**
- Check default directory: `C:\Users\franc\Documents\ALEX_Jia\Codebase\Data`
- Directory is created automatically on first run
- Verify write permissions to parent directory

### Display Issues

**Problem:** Tables show all zeros

**Solution:**
- Normal in PREVIEW mode (not connected)
- Connect to robot to see live data
- Check robot controller is sending data

**Problem:** Display frozen but didn't click freeze button

**Solution:**
- Check connection status
- Robot may have disconnected
- GUI auto-disconnects on memory read errors
- Reconnect using toggle button

---

## 🎨 GUI Color Coding

### Arm Identification
- **Dark Blue** = Left Arm components
- **Dark Green** = Right Arm components

### Status Indicators
- **Green** = Connected/Active/Running
- **Orange** = Preview/Warning
- **Red** = Error/Fault/Emergency Stop
- **Gray** = Disabled/Inactive
- **Light Blue** = Wearing mode active
- **Salmon/Coral** = Rehab mode active

---

## 📝 Data Analysis Tips

### Loading CSV in Python

```python
import pandas as pd

# Load CSV file
df = pd.read_csv('data_20240115_143022.csv')

# Access specific columns
left_ee_x = df['Left_EE_PosX_m']
right_elbow = df['Right_J5_ElbowFlexion_deg']

# Calculate statistics
mean_velocity = df['Left_EE_VelX_mps'].mean()
max_angle = df['Left_J4_ShoulderFlexion_deg'].max()
```

### Loading CSV in MATLAB

```matlab
% Load CSV file
data = readtable('data_20240115_143022.csv');

% Access specific columns
leftEEx = data.Left_EE_PosX_m;
rightElbow = data.Right_J5_ElbowFlexion_deg;

% Calculate statistics
meanVel = mean(data.Left_EE_VelX_mps);
maxAngle = max(data.Left_J4_ShoulderFlexion_deg);
```

### Time-Series Analysis

The timestamp column can be used to:
- Calculate exact time intervals between samples
- Synchronize with external sensors
- Detect data gaps or logging interruptions
- Calculate derivative values (acceleration, jerk)

---

## ⚠️ Important Notes

### Safety

- **STOP DEVICE** button provides emergency stop functionality
- Returns robot to DRIVER_OFF state (motors disabled)
- Use immediately if any unexpected behavior occurs
- Cannot resume operation without restarting wearing mode

### Data Integrity

- **Freeze Display** does NOT pause data logging
- Use when you need to read exact values from screen
- Data continues recording in background at 50 Hz
- Unfreeze to resume display updates

### File Management

- New CSV file created for each logging session
- Files named with timestamp: `data_YYYYMMDD_HHmmss.csv`
- Old files are never overwritten
- Manually delete old files to free disk space

---

## 🔮 Future Enhancements (Potential)

- [ ] Real-time plotting of joint angles
- [ ] Configurable data logging frequency
- [ ] Export to additional formats (HDF5, MAT)
- [ ] Trajectory playback visualization
- [ ] Multi-session data comparison
- [ ] Automatic data backup

---

## 📞 Support

For issues, questions, or contributions related to this GUI:

1. Check this README for solutions
2. Review troubleshooting section
3. Check robot controller documentation
4. Contact ALEX system administrator

---

## 📄 License

This software is part of the ALEX (Arm Lexoskeleton) rehabilitation system project.

---

## 🙏 Acknowledgments

Developed for the ALEX robotic rehabilitation system research project.

---

**Version:** 1.0  
**Last Updated:** January 2024  
**Platform:** Windows .NET 10  
**Update Frequency:** 50 Hz
