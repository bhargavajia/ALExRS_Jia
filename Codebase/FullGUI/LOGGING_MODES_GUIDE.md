# ALEX Robot GUI - Dual Logging Modes Guide

## Overview

The GUI now supports two distinct data collection modes:

### 1. **Record All Data Mode** (Default)
- Records data from both left and right robot arms simultaneously
- Logs all 61 data columns as before
- No state tracking

### 2. **Monolateral Acquisition Mode** (New)
- Records data from **either** left **or** right arm (user selects)
- Includes state tracking with automatic progression
- Adds a "State" column to CSV
- Automatically stops logging after completing state cycle

---

## Using Monolateral Acquisition Mode

### Setup

1. **Select Mode**: Choose "Monolateral Acquisition" radio button
2. **Select Arm**: Choose either "Left" or "Right" arm
3. **Click "Start CSV Logging"**: Begins data collection in IDLE state

### State Progression

The system cycles through three states:

| State | Description | Display Color | Next Action |
|-------|-------------|---------------|-------------|
| **IDLE** | Initial state, ready to begin | Dark Slate Gray | Click "Next State" → GOING |
| **GOING** | Movement phase (e.g., reaching forward) | Dark Orange | Click "Next State" → RETURNING |
| **RETURNING** | Return phase (e.g., returning to start) | Dark Blue | Click "Next State" → Stops logging |

### Workflow Example

```
1. Start CSV Logging → State: IDLE
2. Click "Next State" → State: GOING (arm moves forward)
3. Click "Next State" → State: RETURNING (arm returns)
4. Click "Next State" → Logging automatically stops, resets to IDLE
```

---

## CSV Output Formats

### Record All Data Mode

**Column Count**: 61 columns

```csv
Timestamp,
Left_EE_PosX_m, Left_EE_PosY_m, Left_EE_PosZ_m,
Left_EE_VelX_mps, Left_EE_VelY_mps, Left_EE_VelZ_mps,
Right_EE_PosX_m, Right_EE_PosY_m, Right_EE_PosZ_m,
Right_EE_VelX_mps, Right_EE_VelY_mps, Right_EE_VelZ_mps,
Left_J0...J7 (position rad/deg, velocity),
Right_J0...J7 (position rad/deg, velocity),
Frequency_Hz
```

### Monolateral Acquisition Mode

**Column Count**: 32 columns (31 data + 1 state)

**If Left Arm selected**:
```csv
Timestamp,
Left_EE_PosX_m, Left_EE_PosY_m, Left_EE_PosZ_m,
Left_EE_VelX_mps, Left_EE_VelY_mps, Left_EE_VelZ_mps,
Left_J0...J7 (position rad/deg, velocity),
Frequency_Hz,
State
```

**If Right Arm selected**:
```csv
Timestamp,
Right_EE_PosX_m, Right_EE_PosY_m, Right_EE_PosZ_m,
Right_EE_VelX_mps, Right_EE_VelY_mps, Right_EE_VelZ_mps,
Right_J0...J7 (position rad/deg, velocity),
Frequency_Hz,
State
```

**State Column Values**: 
- `IDLE`
- `GOING`
- `RETURNING`

---

## GUI Layout Changes

### Data Collection GroupBox (Expanded)

**New Controls Added:**

1. **Logging Mode Selection** (Radio buttons)
   - "Record All Data" (default)
   - "Monolateral Acquisition"

2. **Arm Selection** (Radio buttons - visible only in Monolateral mode)
   - "Left"
   - "Right"

3. **State Display** (visible only in Monolateral mode)
   - Label showing current state (IDLE, GOING, RETURNING)
   - Changes color based on state

4. **Next State Button** (visible only in Monolateral mode)
   - Advances through state progression
   - Automatically stops logging after RETURNING state
   - Only enabled during active logging

---

## Technical Implementation

### New Enums

```csharp
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
```

### Key Features

- **Mode Lock**: Mode and arm selection are disabled during active logging
- **Automatic Stop**: Logging stops automatically after completing RETURNING state
- **State Persistence**: State value is written to CSV with every data row
- **50 Hz Logging**: Both modes maintain 50 Hz (20ms) data collection rate

---

## Data Analysis Examples

### Loading Monolateral Data in Python

```python
import pandas as pd

# Load monolateral acquisition data
df = pd.read_csv('data_20240115_143022.csv')

# Filter by state
idle_data = df[df['State'] == 'IDLE']
going_data = df[df['State'] == 'GOING']
returning_data = df[df['State'] == 'RETURNING']

# Analyze movement phase
going_avg_velocity = going_data['Left_EE_VelX_mps'].mean()
```

### Loading in MATLAB

```matlab
data = readtable('data_20240115_143022.csv');

% Filter by state
idleData = data(strcmp(data.State, 'IDLE'), :);
goingData = data(strcmp(data.State, 'GOING'), :);
returningData = data(strcmp(data.State, 'RETURNING'), :);

% Compare phases
goingVel = mean(goingData.Left_EE_VelX_mps);
returnVel = mean(returningData.Left_EE_VelX_mps);
```

---

## Use Cases

### Record All Data Mode
✅ Bilateral coordination studies  
✅ Left-right comparison analysis  
✅ Full system monitoring  
✅ Long-term continuous recording  

### Monolateral Acquisition Mode
✅ Single-arm movement analysis  
✅ Task segmentation (idle → going → returning)  
✅ Reaching and grasping studies  
✅ Controlled movement trials with clear phases  

---

## Benefits

1. **Reduced Data Volume**: Monolateral mode generates ~50% less data
2. **Automatic Segmentation**: State column eliminates need for manual trial marking
3. **Simplified Analysis**: Pre-labeled movement phases
4. **Flexible Workflow**: Choose appropriate mode for each experiment
5. **Safety**: Automatic logging stop prevents accidental over-collection

---

## Notes

- Both modes run at 50 Hz (20ms interval)
- Mode selection can only be changed when not logging
- Monolateral state resets to IDLE when logging starts
- "Stop CSV Logging" button still available for manual stop in both modes
- Display freeze feature works independently in both modes

---

**Version**: 2.0  
**Last Updated**: January 2024  
**Compatibility**: .NET 10, Windows Forms
