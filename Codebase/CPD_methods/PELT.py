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

DATA_DIR = '../Data/Trial_data_27Feb'

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
    Preprocess the dataset to extract EE Y, Z positions and velocities.
    Args:
        df (pd.DataFrame): Raw dataset.
    Returns:
        np.ndarray: Preprocessed data (n_samples x 4).
    """
    # Select EE Y,Z positions and velocities
    cols = ['Left_EE_PosY_m', 'Left_EE_PosZ_m', 'Left_EE_VelY_mps', 'Left_EE_VelZ_mps']
    df_processed = df[cols].copy()
    
    # Cast to float
    for col in df_processed.columns:
        df_processed[col] = pd.to_numeric(df_processed[col], errors='coerce')
    
    # Drop any rows with NaN values
    df_processed = df_processed.dropna()
    
    return df_processed.values


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


def pelt_change_point_detection(data, n_ref, min_distance=100, penalty_multiplier=0.5, cost_model="rbf"):
    """
    Perform PELT-based change point detection using the ruptures library.
    
    Args:
        data (np.ndarray): Preprocessed time series data (n_samples x d_features).
        n_ref (int): Reference length for penalty scaling.
        min_distance (int): Minimum distance between change points. Default is 100.
        penalty_multiplier (float): Penalty strength multiplier. Default is 0.5.
        cost_model (str): Cost function model ('l1', 'l2', or 'rbf'). Default is 'rbf'.
    
    Returns:
        list: Indices of detected change points.
    """
    n_samples, d_features = data.shape
    penalty = penalty_multiplier * n_ref * d_features
    
    algo = rpt.Pelt(model=cost_model, min_size=min_distance, jump=1).fit(data)
    change_points = algo.predict(pen=penalty)
    
    # Remove the last index (always n by ruptures convention)
    change_points = [cp for cp in change_points if cp != n_samples]
    
    return change_points


def detect_change_points_single_file(file_path, n_ref, penalty_multiplier=0.5):
    """
    Detect change points in a single CSV file.
    Args:
        file_path (str): Path to the CSV file.
        n_ref (int): Reference length.
        penalty_multiplier (float): Penalty strength multiplier. Default is 0.5.
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




def _ordered_states_from_labels(state_labels):
    """
    Build ordered unique states from consecutive labels.
    Example: [IDLE, IDLE, GOING, GOING, RETURNING] -> [IDLE, GOING, RETURNING]
    """
    if state_labels is None or len(state_labels) == 0:
        return []

    ordered = [state_labels[0]]
    for s in state_labels[1:]:
        if s != ordered[-1]:
            ordered.append(s)
    return ordered


def _labels_from_change_points(n_samples, change_points, ordered_states):
    """
    Convert CP indices into per-sample predicted labels using the given ordered states.
    If CPs create more segments than available states, the last state is repeated.
    """
    if n_samples <= 0:
        return np.array([])

    if ordered_states is None or len(ordered_states) == 0:
        return np.array(["UNKNOWN"] * n_samples)

    cps = sorted([int(cp) for cp in change_points if 0 < int(cp) < n_samples])
    boundaries = [0] + cps + [n_samples]

    pred = np.empty(n_samples, dtype=object)
    for seg_idx in range(len(boundaries) - 1):
        start, end = boundaries[seg_idx], boundaries[seg_idx + 1]
        state_idx = min(seg_idx, len(ordered_states) - 1)
        pred[start:end] = ordered_states[state_idx]
    return pred


def segmentation_overlap_metrics(predicted_points, true_state_labels, target_states=('GOING', 'RETURNING')):
    """
    Segmentation metric based on label overlap (IoU) between predicted and true states.

    Returns:
        dict with per-state IoU and average IoU over requested target states.
    """
    if true_state_labels is None or len(true_state_labels) == 0:
        return {
            'per_state_overlap': {},
            'average_overlap': 0.0,
            'ordered_states': [],
            'predicted_labels': np.array([])
        }

    true_labels = np.asarray(true_state_labels)
    n = len(true_labels)
    ordered_states = _ordered_states_from_labels(true_labels)
    pred_labels = _labels_from_change_points(n, predicted_points, ordered_states)

    per_state = {}
    for st in target_states:
        true_mask = (true_labels == st)
        pred_mask = (pred_labels == st)
        union = np.sum(true_mask | pred_mask)
        inter = np.sum(true_mask & pred_mask)
        per_state[st] = (inter / union) if union > 0 else np.nan

    valid = [v for v in per_state.values() if not np.isnan(v)]
    avg_overlap = float(np.mean(valid)) if valid else 0.0

    return {
        'per_state_overlap': per_state,
        'average_overlap': avg_overlap,
        'ordered_states': ordered_states,
        'predicted_labels': pred_labels
    }


def cp_delay_metrics_ms(predicted_points, true_points, sample_period_ms=20.0):
    """
    Match predicted CPs to true CPs by nearest neighbor and report delays in ms.
    Returns signed delays (pred - true) and average absolute delay in ms.
    """
    if len(predicted_points) == 0 or len(true_points) == 0:
        return {
            'delays_ms': [],
            'average_abs_delay_ms': 0.0
        }

    pred_sorted = sorted([int(p) for p in predicted_points])
    true_sorted = sorted([int(t) for t in true_points])

    unused_true = set(range(len(true_sorted)))
    delays_ms = []

    for p in pred_sorted:
        if not unused_true:
            break
        best_idx = min(unused_true, key=lambda i: abs(true_sorted[i] - p))
        unused_true.remove(best_idx)
        delay_samples = p - true_sorted[best_idx]
        delays_ms.append(delay_samples * sample_period_ms)

    avg_abs = float(np.mean(np.abs(delays_ms))) if delays_ms else 0.0
    return {
        'delays_ms': delays_ms,
        'average_abs_delay_ms': avg_abs
    }
def evaluate_cpd(predicted_points, true_points, tolerance=10):
    """
    Evaluate change point detection performance against ground truth labels.
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
            'tolerance': tolerance
        }

    true_points = np.array(true_points)
    predicted_points = np.array(predicted_points)

    matched_true = set()
    matched_pred = set()
    detection_delays = []

    for pred_idx in predicted_points:
        distances = np.abs(true_points - pred_idx)
        nearest_true_idx = np.argmin(distances)

        if distances[nearest_true_idx] <= tolerance:
            matched_pred.add(pred_idx)
            matched_true.add(nearest_true_idx)
            detection_delays.append(distances[nearest_true_idx])

    true_positives = len(matched_pred)
    false_positives = len(predicted_points) - true_positives
    false_negatives = len(true_points) - len(matched_true)

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


def detect_change_points_batch(dir_path, penalty_multiplier=0.06):
    """
    Run CPD on all files and report segmentation overlap and delay metrics.
    Only includes samples with correct CP count in averages.
    """
    n_ref = calculate_reference_length(dir_path)
    all_files = [f for f in os.listdir(dir_path) if f.endswith('.csv')]
    results = {}

    all_avg_overlaps = []
    all_avg_abs_delays_ms = []
    correct_count_samples = []
    total_samples = 0

    print("\nFILE\tDETECTED_CP\tTRUE_CP\tMATCH\tAVG_OVERLAP\tAVG_DELAY_MS")

    for file in sorted(all_files):
        file_path = os.path.join(dir_path, file)
        change_points, data, states = detect_change_points_single_file(
            file_path, n_ref, penalty_multiplier=penalty_multiplier
        )

        true_points = get_true_change_points(states)
        total_samples += 1
        
        num_detected = len(change_points)
        num_true = len(true_points)
        match = "YES" if num_detected == num_true else "NO"
        
        overlap = segmentation_overlap_metrics(change_points, states, target_states=('GOING', 'RETURNING'))
        delay = cp_delay_metrics_ms(change_points, true_points, sample_period_ms=20.0)

        per_state_overlap = overlap['per_state_overlap']
        avg_overlap = overlap['average_overlap']
        avg_abs_delay_ms = delay['average_abs_delay_ms']

        # Only include in averages if CP count matches
        if num_detected == num_true:
            correct_count_samples.append(file)
            if not np.isnan(avg_overlap):
                all_avg_overlaps.append(avg_overlap)
            all_avg_abs_delays_ms.append(avg_abs_delay_ms)

        results[file] = {
            'change_points': change_points,
            'num_detected': num_detected,
            'true_change_points': true_points,
            'num_true': num_true,
            'count_match': num_detected == num_true,
            'data_shape': data.shape,
            'true_states': states,
            'overlap_per_state': per_state_overlap,
            'average_overlap': avg_overlap,
            'delays_ms_each': delay['delays_ms'],
            'average_abs_delay_ms': avg_abs_delay_ms
        }

        if num_detected == num_true:
            print(f"{file}\t{num_detected}\t{num_true}\t{match}\t{avg_overlap:.4f}\t{avg_abs_delay_ms:.2f}")
        else:
            print(f"{file}\t{num_detected}\t{num_true}\t{match}\t-\t-")

    batch_avg_overlap = float(np.mean(all_avg_overlaps)) if all_avg_overlaps else 0.0
    batch_avg_delay_ms = float(np.mean(all_avg_abs_delays_ms)) if all_avg_abs_delays_ms else 0.0
    percent_correct = (len(correct_count_samples) / total_samples * 100) if total_samples > 0 else 0.0

    print(f"\n--- SUMMARY (Only samples with correct CP count) ---")
    print(f"Samples with correct CP count: {len(correct_count_samples)}/{total_samples} ({percent_correct:.1f}%)")
    print(f"Average Overlap: {batch_avg_overlap:.4f}")
    print(f"Average Delay (ms): {batch_avg_delay_ms:.2f}")

    return results


def plot_change_points(file_path, n_ref, penalty_multiplier=0.06):
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
    
    # Create time axis in milliseconds (50 Hz = 20 ms per sample)
    time_ms = np.arange(len(df)) * 20  # Each sample is 20 ms apart
    
    # Convert change point indices to time (ms)
    true_points_ms = [cp * 20 for cp in true_points]
    predicted_points_ms = [cp * 20 for cp in predicted_points]
    overlap_points_ms = sorted(set(true_points_ms).intersection(set(predicted_points_ms)))

    fig, axes = plt.subplots(1, 3, figsize=(18, 4))
    fig.suptitle(f'Change Point Detection: {os.path.basename(file_path)}\nF1={metrics["f1_score"]:.3f}, Precision={metrics["precision"]:.3f}, Recall={metrics["recall"]:.3f}', fontsize=12, fontweight='bold')
    labels = ['EE Pos Z', 'EE Pos Y', 'Elbow Flexion (rad)']
    colors = ['steelblue', 'steelblue', 'seagreen']

    for idx, (col_name, series, label, color) in enumerate(zip(plot_cols, series_list, labels, colors)):
        ax = axes[idx]
        ax.plot(time_ms, series.values, label=label, color=color, linewidth=1.5)

        # Plot overlap first so it's visually obvious when true and detected are identical
        for cp_ms in overlap_points_ms:
            if cp_ms <= time_ms[-1]:
                ax.axvline(x=cp_ms, color='purple', linestyle='-', linewidth=2.2, alpha=0.9,
                           label='Overlap (True=Detected)' if idx == 0 else '', zorder=4)

        # Plot true-only and detected-only CPs
        true_only_ms = [cp for cp in true_points_ms if cp not in overlap_points_ms]
        pred_only_ms = [cp for cp in predicted_points_ms if cp not in overlap_points_ms]

        for true_cp_ms in true_only_ms:
            if true_cp_ms <= time_ms[-1]:
                ax.axvline(x=true_cp_ms, color='green', linestyle='--', linewidth=1.8,
                           label='True CP' if idx == 0 else '', zorder=3)
        for pred_cp_ms in pred_only_ms:
            if pred_cp_ms <= time_ms[-1]:
                ax.axvline(x=pred_cp_ms, color='red', linestyle='-.', linewidth=1.8,
                           label='Detected CP' if idx == 0 else '', zorder=5)
        ax.set_ylabel(col_name, fontsize=10)
        ax.set_xlabel('Time (ms)', fontsize=10)
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
    print(f"True change points:      {true_points} (indices) = {true_points_ms} (ms)")
    print(f"Detected change points:  {predicted_points} (indices) = {predicted_points_ms} (ms)")
    print(f"Exact overlaps (ms):     {overlap_points_ms}")
    print(f"\nPerformance Metrics:")
    print(f"  F1 Score:              {metrics['f1_score']:.4f}")
    print(f"  Precision:             {metrics['precision']:.4f}")
    print(f"  Recall:                {metrics['recall']:.4f}")
    print(f"  True Positives:        {metrics['true_positives']}")
    print(f"  False Positives:       {metrics['false_positives']}")
    print(f"  False Negatives:       {metrics['false_negatives']}")
    print(f"  Mean Detection Delay:  {metrics['mean_detection_delay']:.2f} samples ({metrics['mean_detection_delay']*20:.1f} ms)")
    print(f"  Tolerance:             ±{metrics['tolerance']} samples (±{metrics['tolerance']*20} ms)")
    print("="*60 + "\n")
    
    return metrics


def plot_best_worst_overlap(results, dir_path, n_ref, penalty_multiplier=0.06):
    """
    Plot the best and worst overlap samples from those with correct CP count.
    
    Args:
        results (dict): Results from detect_change_points_batch.
        dir_path (str): Path to data directory.
        n_ref (int): Reference length.
        penalty_multiplier (float): Penalty strength multiplier.
    """
    # Filter for samples with correct CP count
    correct_samples = {file: res for file, res in results.items() if res['count_match']}
    
    if len(correct_samples) == 0:
        print("No samples with correct CP count found!")
        return
    
    # Find best and worst by overlap
    valid_samples = {file: res for file, res in correct_samples.items() if not np.isnan(res['average_overlap'])}
    
    if len(valid_samples) == 0:
        print("No valid overlap values found!")
        return
    
    best_file = max(valid_samples, key=lambda f: valid_samples[f]['average_overlap'])
    worst_file = min(valid_samples, key=lambda f: valid_samples[f]['average_overlap'])
    
    best_overlap = valid_samples[best_file]['average_overlap']
    worst_overlap = valid_samples[worst_file]['average_overlap']
    
    print(f"\n{'='*60}")
    print(f"BEST overlap: {best_file} (overlap={best_overlap:.4f})")
    print(f"WORST overlap: {worst_file} (overlap={worst_overlap:.4f})")
    print(f"{'='*60}\n")
    
    # Plot both
    fig, axes = plt.subplots(2, 3, figsize=(18, 8))
    fig.suptitle('Best vs Worst Overlap (Correct CP Count Only)', fontsize=14, fontweight='bold')
    
    for row_idx, (file, title_prefix) in enumerate([(best_file, f'BEST'), (worst_file, f'WORST')]):
        file_path = os.path.join(dir_path, file)
        df = load_file(file_path)
        data_processed = preprocess_data(df)
        predicted_points = pelt_change_point_detection(data_processed, n_ref, penalty_multiplier=penalty_multiplier)
        state_labels = df['State'].values if 'State' in df.columns else None
        true_points = get_true_change_points(state_labels)
        
        overlap_metric = results[file]['average_overlap']
        delay_metric = results[file]['average_abs_delay_ms']
        
        # Extract plot columns
        plot_cols = ['Left_EE_PosZ_m', 'Left_EE_PosY_m', 'Left_J5_ElbowFlexion_rad']
        series_list = []
        for col in plot_cols:
            s = df[col].copy()
            series_list.append(pd.to_numeric(s, errors='coerce').fillna(0))
        
        # Time axis
        time_ms = np.arange(len(df)) * 20
        true_points_ms = [cp * 20 for cp in true_points]
        predicted_points_ms = [cp * 20 for cp in predicted_points]
        overlap_points_ms = sorted(set(true_points_ms).intersection(set(predicted_points_ms)))
        
        labels = ['EE Pos Z', 'EE Pos Y', 'Elbow Flexion']
        colors = ['steelblue', 'steelblue', 'seagreen']
        
        for col_idx, (col_name, series, label, color) in enumerate(zip(plot_cols, series_list, labels, colors)):
            ax = axes[row_idx, col_idx]
            ax.plot(time_ms, series.values, label=label, color=color, linewidth=1.5)
            
            # Plot overlaps
            for cp_ms in overlap_points_ms:
                if cp_ms <= time_ms[-1]:
                    ax.axvline(x=cp_ms, color='purple', linestyle='-', linewidth=2.2, alpha=0.9,
                               label='Overlap' if col_idx == 0 else '', zorder=4)
            
            # Plot true-only and detected-only
            true_only_ms = [cp for cp in true_points_ms if cp not in overlap_points_ms]
            pred_only_ms = [cp for cp in predicted_points_ms if cp not in overlap_points_ms]
            
            for true_cp_ms in true_only_ms:
                if true_cp_ms <= time_ms[-1]:
                    ax.axvline(x=true_cp_ms, color='green', linestyle='--', linewidth=1.8,
                               label='True CP' if col_idx == 0 else '', zorder=3)
            for pred_cp_ms in pred_only_ms:
                if pred_cp_ms <= time_ms[-1]:
                    ax.axvline(x=pred_cp_ms, color='red', linestyle='-.', linewidth=1.8,
                               label='Detected CP' if col_idx == 0 else '', zorder=5)
            
            ax.set_ylabel(col_name, fontsize=9)
            ax.set_xlabel('Time (ms)', fontsize=9)
            ax.grid(True, alpha=0.3)
            
            if col_idx == 0:
                ax.set_title(f'{title_prefix}: {file}\nOverlap={overlap_metric:.4f}, Delay={delay_metric:.2f}ms', 
                             fontsize=10, fontweight='bold')
            
            if row_idx == 0 and col_idx == 0:
                handles, lbls = ax.get_legend_handles_labels()
                by_label = dict(zip(lbls, handles))
                ax.legend(by_label.values(), by_label.keys(), loc='upper right', fontsize=8)
    
    plt.tight_layout()
    plt.show()


# RUN CPD
if __name__ == "__main__":
    n_ref = calculate_reference_length(DATA_DIR)

    # 1) Batch run on all files with requested penalty
    results = detect_change_points_batch(DATA_DIR, penalty_multiplier=0.018)
    
    # 2) Plot best and worst overlap
    plot_best_worst_overlap(results, DATA_DIR, n_ref, penalty_multiplier=0.018)

    # 3) Optional single-file plot
    # plot_change_points(file, n_ref, penalty_multiplier=0.06)
    