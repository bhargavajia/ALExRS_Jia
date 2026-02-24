import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
import os
import ruptures as rpt


"""
Simple PELT implementation for change point detection in time series data.

DATA HEADERS

Timestamp	
Left_EE_PosX_m, Left_EE_PosY_m, Left_EE_PosZ_m, Left_EE_VelX_mps, Left_EE_VelY_mps, Left_EE_VelZ_mps	
Left_J0_Abduction_rad, Left_J0_Abduction_deg, Left_J0_Abduction_vel_radps, 
Left_J1_Rotation_rad	Left_J1_Rotation_deg	Left_J1_Rotation_vel_radps	
Left_J2_rad	Left_J2_deg	Left_J2_vel_radps	(remove this column before giving to model: duplicate of J1 data)
Left_J3_rad	Left_J3_deg	Left_J3_vel_radps  (remove this column before giving to model: duplicate of J1 data, opposite sign)	
Left_J4_ShoulderFlexion_rad	Left_J4_ShoulderFlexion_deg	Left_J4_ShoulderFlexion_vel_radps	
Left_J5_ElbowFlexion_rad	Left_J5_ElbowFlexion_deg	Left_J5_ElbowFlexion_vel_radps	
Left_J6_PronoSupination_rad	Left_J6_PronoSupination_deg	Left_J6_PronoSupination_vel_radps	
Left_J7_WristFlexion_rad	Left_J7_WristFlexion_deg	Left_J7_WristFlexion_vel_radps	

Frequency_Hz (remove this column before giving to model: not used for CPD)

State (IDLE, GOING, RETURNING): Labels that are to be predicted by CPD

"""

DATA_DIR = '../Data/New_trial_data_20Feb'

def load_data(dir_path):
    """
    Load the dataset from a CSV file.
    Args:
        dir_path (str): Path to the directory containing CSV files.
    Returns:
        pd.DataFrame: Loaded dataset of all csv files.
    """
    all_files = [f for f in os.listdir(dir_path) if f.endswith('.csv')]
    data_frames = []
    for file in all_files:
        df = pd.read_csv(os.path.join(dir_path, file))
        data_frames.append(df)
    return pd.concat(data_frames, ignore_index=True)

def load_file(file_path):
    """
    Load a single CSV file into a DataFrame.
    Args:
        file_path (str): Path to the CSV file.
    Returns:
        pd.DataFrame: Loaded dataset from the specified file.
    """
    return pd.read_csv(file_path)


def remove_zero_velocity_segments(df, vel_cols, state_labels=None, threshold=0.01, min_continuous_steps=20):
    """
    Remove segments where velocity is ~0 for continuous timesteps, but ONLY if NOT in IDLE state.
    Args:
        df (pd.DataFrame): Dataset with velocity columns.
        vel_cols (list): Column names for velocity.
        state_labels (np.ndarray): State labels array. If provided, only remove zero-velocity segments NOT in IDLE.
        threshold (float): Velocity magnitude threshold for "zero". Default is 0.01 m/s.
        min_continuous_steps (int): Number of continuous zero steps to remove. Default is 20.
    Returns:
        tuple: (pd.DataFrame, np.ndarray) - Filtered dataset and array of kept indices.
    """
    # Calculate velocity magnitude across all velocity dimensions
    vel_magnitude = np.sqrt((df[vel_cols] ** 2).sum(axis=1))
    
    # Find segments where velocity is below threshold
    is_zero = vel_magnitude < threshold
    
    # Identify continuous zero segments
    mask = np.ones(len(is_zero), dtype=bool)  # Start by keeping everything
    
    i = 0
    while i < len(is_zero):
        if is_zero[i]:
            # Found start of zero segment
            zero_start = i
            # Find end of zero segment
            while i < len(is_zero) and is_zero[i]:
                i += 1
            zero_end = i
            zero_length = zero_end - zero_start
            
            # Only mark for removal if segment is long enough AND not in IDLE state
            if zero_length >= min_continuous_steps:
                # Check if this segment is in IDLE state
                if state_labels is not None:
                    # Check the state of the majority of samples in this segment
                    segment_states = state_labels[zero_start:zero_end]
                    is_idle = np.sum(segment_states == 'IDLE') > (zero_length * 0.5)
                    if not is_idle:
                        # Only remove if NOT in IDLE
                        mask[zero_start:zero_end] = False
                else:
                    # No state labels provided, remove all long zero segments
                    mask[zero_start:zero_end] = False
        else:
            i += 1
    
    # Build array of kept indices
    kept_indices = np.where(mask)[0]
    
    # Filter dataframe
    df_filtered = df.iloc[kept_indices].reset_index(drop=True)
    
    print(f"Removed {len(df) - len(df_filtered)} rows with continuous zero velocity segments (>={min_continuous_steps} steps, excluding IDLE state)")
    return df_filtered, kept_indices


def preprocess_data(df, use_ee_only=False, use_derivatives=False, remove_zero_velocity=True, vel_threshold=0.01, min_zero_steps=20):
    """
    Preprocess the dataset by removing unnecessary columns and casting to float.
    Args:
        df (pd.DataFrame): Raw dataset.
        use_ee_only (bool): If True, only use EE Y,Z position and velocity (exclude X). If False, include joints too. Default is False.
        use_derivatives (bool): If True, add velocity derivatives (acceleration) for EE Y,Z. Default is False.
        remove_zero_velocity (bool): If True, remove segments with ~zero velocity for 20+ continuous steps (except IDLE). Default is True.
        vel_threshold (float): Velocity magnitude threshold for "zero". Default is 0.01 m/s.
        min_zero_steps (int): Number of continuous zero steps to trigger removal. Default is 20.
    Returns:
        tuple: (np.ndarray, np.ndarray or None) - Preprocessed data and kept indices (if filtering applied).
    """
    # Extract state labels before removing State column (for zero-velocity filtering)
    state_labels = df['State'].values if 'State' in df.columns else None
    
    # Columns to remove (including all degree columns and duplicates)
    columns_to_remove = ['Timestamp', 'Left_J2_rad', 'Left_J2_deg', 'Left_J2_vel_radps', 
                         'Left_J3_rad', 'Left_J3_deg', 'Left_J3_vel_radps', 
                         'Frequency_Hz', 'State',
                         'Left_EE_PosX_m', 'Left_EE_VelX_mps',  # Remove X components
                         'Left_J0_Abduction_deg', 'Left_J1_Rotation_deg',  # Remove degree columns
                         'Left_J4_ShoulderFlexion_deg', 'Left_J5_ElbowFlexion_deg',
                         'Left_J6_PronoSupination_deg', 'Left_J7_WristFlexion_deg']
    
    # Remove columns that exist in the dataframe
    df_processed = df.drop(columns=[col for col in columns_to_remove if col in df.columns])
    
    # If using EE only, keep only EE Y,Z position and velocity columns
    if use_ee_only:
        ee_cols = ['Left_EE_PosY_m', 'Left_EE_PosZ_m',
                   'Left_EE_VelY_mps', 'Left_EE_VelZ_mps']
        df_processed = df_processed[ee_cols]
    # Otherwise keep all remaining columns (EE Y,Z + all joints in radians and their velocities)
    
    # Cast all remaining columns to float
    for col in df_processed.columns:
        df_processed[col] = pd.to_numeric(df_processed[col], errors='coerce')
    
    # Drop any rows with NaN values that may have resulted from conversion
    df_processed = df_processed.dropna()
    
    # Remove zero-velocity segments before derivatives (but keep IDLE state)
    kept_indices = None
    if remove_zero_velocity:
        vel_cols = [col for col in df_processed.columns if 'Vel' in col]
        df_processed, kept_indices = remove_zero_velocity_segments(df_processed, vel_cols, state_labels=state_labels, 
                                                                     threshold=vel_threshold, min_continuous_steps=min_zero_steps)
    
    # Add velocity derivatives (acceleration) if requested
    if use_derivatives:
        vel_cols = [col for col in df_processed.columns if 'Vel' in col]
        for col in vel_cols:
            derivative = np.diff(df_processed[col].values, prepend=df_processed[col].iloc[0])
            df_processed[f'{col}_derivative'] = derivative
    
    return df_processed.values, kept_indices


def calculate_reference_length(dir_path):
    """
    Calculate the average length of all CSV files to establish a reference length
    for time-agnostic penalty calculation.
    Args:
        dir_path (str): Path to the directory containing CSV files.
    Returns:
        int: Average length (number of samples) across all files.
    """
    all_files = [f for f in os.listdir(dir_path) if f.endswith('.csv')]
    lengths = []
    for file in all_files:
        df = pd.read_csv(os.path.join(dir_path, file))
        lengths.append(len(df))
    return int(np.mean(lengths))


def pelt_change_point_detection(data, n_ref, min_distance=100, penalty_multiplier=1.2, cost_model="l2"):
    """
    Perform PELT-based change point detection using the ruptures library.
    
    Uses fixed penalty that scales with number of dimensions (d).
    Simpler and more aggressive than BIC, preventing overfitting.
    
    Args:
        data (pd.ndarray or pd.DataFrame): Preprocessed time series data (n_samples x d_features).
        n_ref (int): Reference length for time-agnostic penalty calculation (used for info only).
        min_distance (int): Minimum distance between change points. Default is 100 samples.
        penalty_multiplier (float): Multiplier for penalty strength (higher = fewer change points). Default is 1.2.
        cost_model (str): Cost function model ('l1', 'l2', or 'rbf'). Default is 'l2'.
    
    Returns:
        list: Indices of detected change points (excluding the endpoint n).
    """
    # Ensure data is a numpy array
    if isinstance(data, pd.DataFrame):
        data = data.values
    
    n_samples = data.shape[0]
    d_features = data.shape[1]
    
    # Use fixed penalty: penalty_multiplier * d_features
    # Much simpler and stronger than BIC. penalty_multiplier directly controls sensitivity.
    # Recommended values: 1.0-3.0 for aggressive detection, 3.0+ for conservative
    penalty = penalty_multiplier * d_features
    
    print(f"Data shape: {data.shape} (n_samples={n_samples}, d_features={d_features})")
    print(f"Reference length (n_ref): {n_ref}")
    print(f"Min distance between CPs: {min_distance} samples")
    print(f"Penalty multiplier: {penalty_multiplier}")
    print(f"Cost model: {cost_model}")
    print(f"Fixed Penalty: {penalty:.4f}")
    
    # Initialize PELT algorithm with specified cost model
    algo = rpt.Pelt(model=cost_model, min_size=min_distance, jump=1).fit(data)
    
    # Predict change points
    change_points = algo.predict(pen=penalty)
    
    # Remove the last index (which is always n by ruptures convention)
    change_points = [cp for cp in change_points if cp != n_samples]
    
    return change_points


def detect_change_points_single_file(file_path, n_ref, penalty_multiplier=2.0):
    """
    Detect change points in a single CSV file.
    Args:
        file_path (str): Path to the CSV file.
        n_ref (int): Reference length for time-agnostic penalty.
        penalty_multiplier (float): Penalty strength multiplier. Default is 2.0.
    Returns:
        tuple: (change_points, preprocessed_data, state_labels)
    """
    df = load_file(file_path)
    state_labels = df['State'].values if 'State' in df.columns else None
    
    data_processed, kept_indices = preprocess_data(df, use_ee_only=False, use_derivatives=False)
    change_points = pelt_change_point_detection(data_processed, n_ref, penalty_multiplier=penalty_multiplier)
    
    # Filter state labels to match kept indices
    if state_labels is not None and kept_indices is not None:
        state_labels = state_labels[kept_indices]
    
    return change_points, data_processed, state_labels


def detect_change_points_batch(dir_path):
    """
    Detect change points across all CSV files in a directory.
    
    Args:
        dir_path (str): Path to the directory containing CSV files.
    
    Returns:
        dict: Dictionary with filenames as keys and detected change points as values.
    """
    # Calculate reference length for time-agnostic penalty
    n_ref = calculate_reference_length(dir_path)
    print(f"Reference length calculated: {n_ref}\n")
    
    all_files = [f for f in os.listdir(dir_path) if f.endswith('.csv')]
    results = {}
    
    for file in sorted(all_files):
        file_path = os.path.join(dir_path, file)
        print(f"Processing: {file}")
        change_points, data, states = detect_change_points_single_file(file_path, n_ref)
        results[file] = {
            'change_points': change_points,
            'data_shape': data.shape,
            'true_states': states
        }
        print(f"  Change points detected: {change_points}\n")
    
    return results


def get_true_change_points(state_labels):
    """
    Extract true change points from state labels by finding state transitions.
    
    Args:
        state_labels (np.ndarray): Array of state labels (e.g., ['IDLE', 'GOING', 'RETURNING']).
    
    Returns:
        list: Indices where state transitions occur.
    """
    if state_labels is None:
        return []
    
    true_points = []
    for i in range(1, len(state_labels)):
        if state_labels[i] != state_labels[i-1]:
            true_points.append(i)
    
    return true_points


def evaluate_cpd(predicted_points, true_points, tolerance=10):
    """
    Evaluate change point detection performance against ground truth labels.
    
    Args:
        predicted_points (list): Indices of predicted change points.
        true_points (list): Indices of true change points from labels.
        tolerance (int): Tolerance window in samples to consider a match. Default is 10.
    
    Returns:
        dict: Metrics including precision, recall, F1 score, and detection delay.
    """
    if len(true_points) == 0:
        return {
            'precision': 0.0,
            'recall': 0.0,
            'f1_score': 0.0,
            'true_positives': 0,
            'false_positives': len(predicted_points),
            'false_negatives': 0,
            'mean_detection_delay': 0.0,
            'error_message': 'No true change points found'
        }
    
    true_points = np.array(true_points)
    predicted_points = np.array(predicted_points)
    
    # Match predictions to ground truth within tolerance
    matched_true = set()
    matched_pred = set()
    detection_delays = []
    
    for pred_idx in predicted_points:
        # Find nearest true point
        distances = np.abs(true_points - pred_idx)
        nearest_true_idx = np.argmin(distances)
        
        if distances[nearest_true_idx] <= tolerance:
            matched_pred.add(pred_idx)
            matched_true.add(nearest_true_idx)
            detection_delays.append(distances[nearest_true_idx])
    
    true_positives = len(matched_pred)
    false_positives = len(predicted_points) - true_positives
    false_negatives = len(true_points) - len(matched_true)
    
    # Calculate metrics
    precision = true_positives / (true_positives + false_positives) if (true_positives + false_positives) > 0 else 0.0
    recall = true_positives / (true_positives + false_negatives) if (true_positives + false_negatives) > 0 else 0.0
    f1_score = 2 * (precision * recall) / (precision + recall) if (precision + recall) > 0 else 0.0
    mean_delay = np.mean(detection_delays) if detection_delays else 0.0
    
    return {
        'precision': precision,
        'recall': recall,
        'f1_score': f1_score,
        'true_positives': true_positives,
        'false_positives': false_positives,
        'false_negatives': false_negatives,
        'mean_detection_delay': mean_delay,
        'tolerance': tolerance
    }


def plot_change_points(file_path, n_ref, penalty_multiplier=0.2):
    """
    Plot end-effector Y,Z position and velocity with overlaid detected and true change points.
    
    Args:
        file_path (str): Path to the CSV file.
        n_ref (int): Reference length for penalty calculation.
        penalty_multiplier (float): Penalty strength multiplier. Default is 0.2.
                                   Try values: 0.1-0.3 (aggressive), 0.3-0.5 (moderate), 0.5+ (conservative)
    """
    # Load and preprocess data
    df = load_file(file_path)
    state_labels = df['State'].values if 'State' in df.columns else None
    
    # Extract timestamps before preprocessing (convert to string if needed)
    timestamps = None
    if 'Timestamp' in df.columns:
        timestamps = df['Timestamp'].astype(str).values
    
    data_processed, kept_indices = preprocess_data(df, use_ee_only=False, use_derivatives=False)
    
    # Filter state labels and timestamps to match kept indices
    if state_labels is not None and kept_indices is not None:
        state_labels = state_labels[kept_indices]
    if timestamps is not None and kept_indices is not None:
        timestamps = timestamps[kept_indices]
    
    # Detect change points
    predicted_points = pelt_change_point_detection(data_processed, n_ref, penalty_multiplier=penalty_multiplier)
    true_points = get_true_change_points(state_labels)
    
    print(f"\nDEBUG: predicted_points = {predicted_points}")
    print(f"DEBUG: true_points = {true_points}")
    print(f"DEBUG: data_processed shape = {data_processed.shape}")
    print(f"DEBUG: kept_indices length = {len(kept_indices) if kept_indices is not None else 'None'}")
    
    # Extract timestamps for change points
    if timestamps is not None:
        print(f"\n{'='*60}")
        print("TIMESTAMP ANALYSIS")
        print(f"{'='*60}")
        
        print(f"\nTrue Change Points (indices and timestamps):")
        true_timestamps = []
        for i, cp_idx in enumerate(true_points, 1):
            if cp_idx < len(timestamps):
                ts = timestamps[cp_idx]
                true_timestamps.append(ts)
                print(f"  CP {i}: Index {cp_idx:5d} -> Timestamp {ts}")
        
        print(f"\nDetected Change Points (indices and timestamps):")
        detected_timestamps = []
        for i, cp_idx in enumerate(predicted_points, 1):
            if cp_idx < len(timestamps):
                ts = timestamps[cp_idx]
                detected_timestamps.append(ts)
                print(f"  CP {i}: Index {cp_idx:5d} -> Timestamp {ts}")
        
        # Calculate time differences (convert back to float for calculation)
        print(f"\nTime Differences (Detected - True):")
        for i in range(max(len(true_timestamps), len(detected_timestamps))):
            true_ts = true_timestamps[i] if i < len(true_timestamps) else None
            detected_ts = detected_timestamps[i] if i < len(detected_timestamps) else None
            
            if true_ts is not None and detected_ts is not None:
                try:
                    diff = float(detected_ts) - float(true_ts)
                    print(f"  CP {i+1}: Δt = {diff:+.6f} s ({diff*1000:+.2f} ms)")
                except:
                    print(f"  CP {i+1}: True={true_ts}, Detected={detected_ts}")
            elif true_ts is not None:
                print(f"  CP {i+1}: MISSED (True = {true_ts})")
            elif detected_ts is not None:
                print(f"  CP {i+1}: FALSE POSITIVE (Detected = {detected_ts})")
        
        print(f"{'='*60}\n")
    
    # Evaluate performance
    metrics = evaluate_cpd(predicted_points, true_points)
    
    # Extract EE Y,Z positions and velocities and filter by kept indices
    ee_pos_columns = ['Left_EE_PosY_m', 'Left_EE_PosZ_m']
    ee_vel_columns = ['Left_EE_VelY_mps', 'Left_EE_VelZ_mps']
    
    ee_pos_data = df[ee_pos_columns].copy()
    ee_vel_data = df[ee_vel_columns].copy()
    
    if kept_indices is not None:
        ee_pos_data = ee_pos_data.iloc[kept_indices].reset_index(drop=True)
        ee_vel_data = ee_vel_data.iloc[kept_indices].reset_index(drop=True)
    
    print(f"DEBUG: ee_pos_data shape = {ee_pos_data.shape}")
    print(f"DEBUG: ee_vel_data shape = {ee_vel_data.shape}")
    
    # Create figure with 2x2 grid
    fig, axes = plt.subplots(2, 2, figsize=(16, 10))
    fig.suptitle(f'Change Point Detection: {os.path.basename(file_path)}\nF1={metrics["f1_score"]:.3f}, Precision={metrics["precision"]:.3f}, Recall={metrics["recall"]:.3f}', 
                 fontsize=12, fontweight='bold')
    
    all_columns = ee_pos_columns + ee_vel_columns
    all_data = [ee_pos_data['Left_EE_PosY_m'], ee_pos_data['Left_EE_PosZ_m'],
                ee_vel_data['Left_EE_VelY_mps'], ee_vel_data['Left_EE_VelZ_mps']]
    
    # Flatten axes for easier iteration
    axes = axes.flatten()
    
    # Plot positions and velocities
    for idx, (col_name, data_col) in enumerate(zip(all_columns, all_data)):
        ax = axes[idx]
        label = 'Position' if 'Pos' in col_name else 'Velocity'
        color = 'steelblue' if 'Pos' in col_name else 'darkorange'
        ax.plot(data_col.values, label=f'EE {label}', linewidth=1.5, color=color)
        
        # Plot true change points
        for true_cp in true_points:
            if true_cp < len(data_col):
                ax.axvline(x=true_cp, color='green', linestyle='--', linewidth=2, label='True CP' if idx == 0 else '')
        
        # Plot predicted change points
        for pred_cp in predicted_points:
            if pred_cp < len(data_col):
                ax.axvline(x=pred_cp, color='red', linestyle='--', linewidth=2, label='Detected CP' if idx == 0 else '')
        
        ax.set_ylabel(col_name, fontsize=10)
        ax.set_xlabel('Sample Index', fontsize=10)
        ax.grid(True, alpha=0.3)
    
    # Add legend only to the first subplot
    handles, labels = axes[0].get_legend_handles_labels()
    by_label = dict(zip(labels, handles))
    axes[0].legend(by_label.values(), by_label.keys(), loc='upper right', fontsize=10)
    
    plt.tight_layout()
    plt.show()
    
    # Print metrics
    print("\n" + "="*60)
    print(f"Change Point Detection Metrics for: {os.path.basename(file_path)}")
    print("="*60)
    print(f"True change points:      {true_points}")
    print(f"Detected change points:  {predicted_points}")
    print(f"\nPerformance Metrics:")
    print(f"  F1 Score:              {metrics['f1_score']:.4f}")
    print(f"  Precision:             {metrics['precision']:.4f}")
    print(f"  Recall:                {metrics['recall']:.4f}")
    print(f"  True Positives:        {metrics['true_positives']}")
    print(f"  False Positives:       {metrics['false_positives']}")
    print(f"  False Negatives:       {metrics['false_negatives']}")
    print(f"  Mean Detection Delay:  {metrics['mean_detection_delay']:.2f} samples")
    print(f"  Tolerance:             ±{metrics['tolerance']} samples")
    print("="*60 + "\n")
    
    return metrics


# RUN CPD for single file with plotting
if __name__ == "__main__":
    n_ref = calculate_reference_length(DATA_DIR)
    
    file = os.path.join(DATA_DIR, 'data_20260220_163422.csv')  # Change to your specific file
    plot_change_points(file, n_ref)
    