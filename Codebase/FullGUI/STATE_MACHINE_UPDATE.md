# Monolateral Acquisition State Machine Update

## Summary of Changes

The Monolateral Acquisition mode state machine has been updated from **3 states** to **4 states** for better task segmentation.

---

## Previous State Flow (3 States)

```
IDLE → GOING → RETURNING → [Stop Logging]
```

### Issues:
- No way to mark when the return movement was complete
- IDLE was used for both "ready to start" and "not started"
- Missing explicit "end" marker for data analysis

---

## New State Flow (4 States)

```
START → GOING → RETURNING → END → [Stop Logging]
```

### State Descriptions:

| State | Description | Color | Purpose |
|-------|-------------|-------|---------|
| **START** | Ready to begin trial | Dark Slate Gray | Initial state before movement begins |
| **GOING** | Forward/outward movement | Dark Orange | Reaching, grasping, or extending motion |
| **RETURNING** | Return movement | Dark Blue | Coming back to starting position |
| **END** | Movement complete | Dark Red | Mark completion before stopping |

### Benefits:
✅ Clear distinction between "ready to start" (START) and "movement complete" (END)  
✅ Better data segmentation for analysis  
✅ Explicit marker for when return movement finishes  
✅ More intuitive workflow: START task → perform movements → END task  

---

## User Workflow

### Before (3 States):
```
1. Click "Start CSV Logging" → State: IDLE
2. Click "Next State" → State: GOING
3. Click "Next State" → State: RETURNING
4. Click "Next State" → Logging stops automatically
```

### After (4 States):
```
1. Click "Start CSV Logging" → State: START
2. Click "Next State" → State: GOING (perform outward movement)
3. Click "Next State" → State: RETURNING (perform return movement)
4. Click "Next State" → State: END (movement complete)
5. Click "Next State" → Logging stops automatically
```

---

## CSV Output Changes

### State Column Values

**Before:**
```csv
State
IDLE
GOING
RETURNING
```

**After:**
```csv
State
START
GOING
RETURNING
END
```

### Example CSV Analysis (Python)

```python
import pandas as pd

# Load data
df = pd.read_csv('data_20240115_143022.csv')

# Filter by state
start_phase = df[df['State'] == 'START']      # Before movement
going_phase = df[df['State'] == 'GOING']      # Outward movement
return_phase = df[df['State'] == 'RETURNING'] # Return movement
end_phase = df[df['State'] == 'END']          # After movement

# Calculate metrics
going_duration = len(going_phase) / 50  # seconds (50 Hz)
return_duration = len(return_phase) / 50

going_max_vel = going_phase['Left_EE_VelX_mps'].max()
return_max_vel = return_phase['Left_EE_VelX_mps'].max()

print(f"Going phase: {going_duration:.2f}s, Max velocity: {going_max_vel:.3f} m/s")
print(f"Return phase: {return_duration:.2f}s, Max velocity: {return_max_vel:.3f} m/s")
```

---

## Code Changes Made

### 1. Updated Enum
```csharp
// Before
private enum AcquisitionState
{
    Idle,
    Going,
    Returning
}

// After
private enum AcquisitionState
{
    Start,
    Going,
    Returning,
    End
}
```

### 2. Updated State Transitions
```csharp
// New transition logic
switch (currentState)
{
    case AcquisitionState.Start:
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
        currentState = AcquisitionState.End;
        lblStateValue.Text = "END";
        lblStateValue.ForeColor = Color.DarkRed;
        break;

    case AcquisitionState.End:
        // Stop logging and reset
        StopMonolateralLogging();
        break;
}
```

### 3. Updated All References
- Changed all `Idle` → `Start`
- Changed all `"IDLE"` → `"START"`
- Added END state handling
- Updated CSV logging to write correct state names

---

## Files Modified

1. **Form1.cs** - Main application logic
   - Updated enum definition
   - Updated state transitions
   - Updated button click handlers
   - Updated logging stop handlers

2. **LOGGING_MODES_GUIDE.md** - User documentation
   - Updated state table (3 → 4 states)
   - Updated workflow example
   - Updated Python/MATLAB code examples
   - Updated enum documentation

3. **STATE_MACHINE_UPDATE.md** - This file (new)
   - Comprehensive change documentation

---

## Backwards Compatibility

⚠️ **CSV files created with the old 3-state system will have different state values:**

- Old files: `IDLE`, `GOING`, `RETURNING`
- New files: `START`, `GOING`, `RETURNING`, `END`

**If you have existing data files:**
```python
# Convert old state names to new format
df['State'] = df['State'].replace({'IDLE': 'START'})
# Note: Old files won't have END state rows
```

---

## Testing Checklist

- [x] Code compiles successfully
- [ ] START state displays correctly on launch
- [ ] Pressing "Next State" transitions: START → GOING → RETURNING → END
- [ ] Pressing "Next State" in END stops logging
- [ ] CSV contains correct state names (START, GOING, RETURNING, END)
- [ ] Manual "Stop CSV Logging" button still works
- [ ] State resets to START when logging restarts
- [ ] Mode selection locked during logging

---

**Version**: 2.1  
**Date**: January 2025  
**Change Type**: Feature Enhancement  
**Breaking Change**: Yes (CSV state column values changed)
