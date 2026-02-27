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

DATA_DIR = '../Data/Trial_data_24Feb'

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


def preprocess_data(df):
    """
    Preprocess the dataset to extract 2 columns: EE Y, EE Z.
    Computes first derivatives (velocities) for sharper change detection.
    Args:
        df (pd.DataFrame): Raw dataset.
    Returns:
        np.ndarray: Preprocessed data (n_samples x 2) - first derivatives.
    """
    # Select only the 2 columns we need
    cols = ['Left_EE_PosY_m', 'Left_EE_PosZ_m']
    df_processed = df[cols].copy()
    
    # Cast to float
    for col in df_processed.columns:
        df_processed[col] = pd.to_numeric(df_processed[col], errors='coerce')
    
    # Drop any rows with NaN values
    df_processed = df_processed.dropna()
    
    # Compute first derivatives (velocity) for sharper transitions
    data = df_processed.values
    data_diff = np.diff(data, axis=0)
    
    return data_diff


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


def pelt_change_point_detection(data, n_ref, min_distance=100, penalty_multiplier=0.005, cost_model="rbf"):
    """
    Perform PELT-based change point detection.
    
    Args:
        data (np.ndarray): Preprocessed time series data (n_samples x 3).
        n_ref (int): Reference length for penalty scaling.
        min_distance (int): Minimum distance between change points. Default is 100.
        penalty_multiplier (float): Penalty strength multiplier. Default is 0.005.
        cost_model (str): Cost function model ('l1', 'l2', or 'rbf'). Default is 'rbf'.
    
    Returns:
        list: Indices of detected change points.
    """
    n_samples, d_features = data.shape
    penalty = penalty_multiplier * n_ref * d_features
    print(f"dimensions: {d_features}, samples: {n_samples}, penalty: {penalty}")
    
    algo = rpt.Pelt(model=cost_model, min_size=min_distance, jump=1).fit(data)
    change_points = algo.predict(pen=penalty)
    
    # Remove the last index (always n by ruptures convention)
    change_points = [cp for cp in change_points if cp != n_samples]
    
    return change_points


def detect_change_points_single_file(file_path, n_ref, penalty_multiplier=0.005):
    """
    Detect change points in a single CSV file.
    Args:
        file_path (str): Path to the CSV file.
        n_ref (int): Reference length.
        penalty_multiplier (float): Penalty strength multiplier. Default is 0.005.
    Returns:
        tuple: (change_points, preprocessed_data, state_labels)
    """
    df = load_file(file_path)
    state_labels = df['State'].values if 'State' in df.columns else None
    
    data_processed = preprocess_data(df)
    change_points = pelt_change_point_detection(data_processed, n_ref, penalty_multiplier=penalty_multiplier)
    
    return change_points, data_processed, state_labels


def detect_change_points_batch(dir_path):
    """
    Detect change points across all CSV files in a directory.
    
    Args:
        dir_path (str): Path to the directory containing CSV files.
    
    Returns:
        dict: Dictionary with filenames as keys and detected change points as values.
    """
    n_ref = calculate_reference_length(dir_path)
    all_files = [f for f in os.listdir(dir_path) if f.endswith('.csv')]
    results = {}
    
    for file in sorted(all_files):
        file_path = os.path.join(dir_path, file)
        change_points, data, states = detect_change_points_single_file(file_path, n_ref)
        results[file] = {
            'change_points': change_points,
            'data_shape': data.shape,
            'true_states': states
        }
    
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


def plot_change_points(file_path, n_ref, penalty_multiplier=0.02):
    """
    Plot EE Y, Z and Elbow Flexion with detected and true change points.
    
    Args:
        file_path (str): Path to the CSV file.
        n_ref (int): Reference length.
        penalty_multiplier (float): Penalty strength multiplier. Default is 0.5.
    """
    df = load_file(file_path)
    state_labels = df['State'].values if 'State' in df.columns else None
    
    data_processed = preprocess_data(df)
    predicted_points = pelt_change_point_detection(data_processed, n_ref, penalty_multiplier=penalty_multiplier)
    true_points = get_true_change_points(state_labels)
    
    metrics = evaluate_cpd(predicted_points, true_points)
    
    # Extract the 3 columns for plotting
    plot_cols = ['Left_EE_PosZ_m', 'Left_EE_PosY_m', 'Left_J5_ElbowFlexion_rad']
    series_list = []
    for col in plot_cols:
        s = df[col].copy()
        series_list.append(pd.to_numeric(s, errors='coerce').fillna(0))

    fig, axes = plt.subplots(1, 3, figsize=(18, 4))
    fig.suptitle(f'Change Point Detection: {os.path.basename(file_path)}\nF1={metrics["f1_score"]:.3f}, Precision={metrics["precision"]:.3f}, Recall={metrics["recall"]:.3f}', fontsize=12, fontweight='bold')
    labels = ['EE Pos Z', 'EE Pos Y', 'Elbow Flexion (rad)']
    colors = ['steelblue', 'steelblue', 'seagreen']

    for idx, (col_name, series, label, color) in enumerate(zip(plot_cols, series_list, labels, colors)):
        ax = axes[idx]
        ax.plot(series.values, label=label, color=color, linewidth=1.5)
        for true_cp in true_points:
            if true_cp < len(series):
                ax.axvline(x=true_cp, color='green', linestyle='--', linewidth=1.5, label='True CP' if idx == 0 else '')
        for pred_cp in predicted_points:
            if pred_cp < len(series):
                ax.axvline(x=pred_cp, color='red', linestyle='--', linewidth=1.5, label='Detected CP' if idx == 0 else '')
        ax.set_ylabel(col_name, fontsize=10)
        ax.set_xlabel('Sample Index', fontsize=10)
        ax.grid(True, alpha=0.3)

    handles, lbls = axes[0].get_legend_handles_labels()
    by_label = dict(zip(lbls, handles))
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
    
    file = os.path.join(DATA_DIR, 'data_20260224_153327.csv')  # Change to your specific file
    plot_change_points(file, n_ref)
    