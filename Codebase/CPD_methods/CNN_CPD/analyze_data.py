"""
Utility script to analyze raw data and determine optimal preprocessing parameters.
Run this ONCE to get statistics about your data, then use those values in prototypical_cpd.py
"""

import os
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
import torch
import sys
import pathlib

if __package__ is None:
    parent = str(pathlib.Path(__file__).resolve().parent.parent)
    if parent not in sys.path:
        sys.path.insert(0, parent)

from CNN_CPD.helpers import split_and_load_data, collate_cpd_batch, CPDNet
from CNN_CPD.evaluation import evaluate_iou_overlap
from torch.utils.data import DataLoader


def load_file(file_path):
    """Load a single CSV file."""
    return pd.read_csv(file_path)


def analyze_sequence_lengths(data_dir):
    """
    Scan all CSV files and analyze sequence lengths.
    
    Args:
        data_dir (str): Directory containing CSV files
        
    Returns:
        dict: Statistics including min, max, avg, median lengths
    """
    csv_files = [f for f in os.listdir(data_dir) if f.endswith('.csv')]
    
    if not csv_files:
        print(f"No CSV files found in {data_dir}")
        return None
    
    lengths = []
    print(f"\nAnalyzing {len(csv_files)} files in {data_dir}...\n")
    
    for file in sorted(csv_files):
        file_path = os.path.join(data_dir, file)
        try:
            df = load_file(file_path)
            length = len(df)
            lengths.append(length)
            print(f"  {file:<40} length: {length:4d}")
        except Exception as e:
            print(f"  {file:<40} ERROR: {e}")
    
    if not lengths:
        return None
    
    stats = {
        'count': len(lengths),
        'min': int(np.min(lengths)),
        'max': int(np.max(lengths)),
        'mean': float(np.mean(lengths)),
        'median': float(np.median(lengths)),
        'std': float(np.std(lengths)),
        'q25': float(np.percentile(lengths, 25)),
        'q75': float(np.percentile(lengths, 75)),
    }
    
    return stats


def print_analysis(data_dir):
    """Print analysis results for the data directory."""
    print("="*70)
    print("SEQUENCE LENGTH ANALYSIS")
    print("="*70)
    
    stats = analyze_sequence_lengths(data_dir)
    
    if stats is None:
        return
    
    print("\n" + "="*70)
    print("SUMMARY STATISTICS")
    print("="*70)
    print(f"Total files:      {stats['count']}")
    print(f"Min length:       {stats['min']}")
    print(f"Max length:       {stats['max']}")
    print(f"Mean length:      {stats['mean']:.1f}")
    print(f"Median length:    {stats['median']:.1f}")
    print(f"Std deviation:    {stats['std']:.1f}")
    print(f"25th percentile:  {stats['q25']:.1f}")
    print(f"75th percentile:  {stats['q75']:.1f}")
    
    print("\n" + "="*70)
    print("RECOMMENDATION FOR INTERPOLATION")
    print("="*70)
    print(f"\nUse TARGET_LENGTH = {stats['max']} in prototypical_cpd.py")
    print(f"  (This ensures no data truncation)")
    print(f"\nAlternatively, use TARGET_LENGTH = {int(stats['median'])} for ~50% compression")
    print(f"  (This would interpolate some up, resample some down)")
    print("="*70 + "\n")
    
    return stats


def analyze_precision_vs_tolerance(
    data_dir='../Data/Trial_data_27Feb',
    train_dir='../Data/CPD_train',
    test_dir='../Data/CPD_test',
    weight_path='./saved_weights/cpd_best_model.pt',
    target_length=500,
    batch_size=4,
    hidden_dim=64,
    threshold=0.2,
    max_change_points=1,
    hz=50,
    tolerances=range(5, 26, 5),
    plot_kind='bar',
):
    """
    Evaluate precision (hit rate for 1 CP) across multiple detection tolerances.

    Args:
        data_dir (str): Source data directory with all CSVs
        train_dir (str): Train split directory
        test_dir (str): Test split directory
        weight_path (str): Path to trained model weights
        target_length (int): Interpolated sequence length
        batch_size (int): Dataloader batch size
        hidden_dim (int): Model hidden dim
        threshold (float): Threshold used when max_change_points=None
        max_change_points (int | None): Fixed number of CPs (use 1 for one-CP task)
        hz (int): Sampling rate in Hz
        tolerances (iterable): Tolerance windows in samples
        plot_kind (str): 'bar' or 'scatter'

    Returns:
        list[dict]: [{'tolerance_samples', 'window_ms', 'precision'}]
    """
    if not os.path.exists(weight_path):
        print(f"Weights not found: {weight_path}")
        return []

    # Device
    if torch.backends.mps.is_available():
        device = torch.device('mps')
    elif torch.cuda.is_available():
        device = torch.device('cuda')
    else:
        device = torch.device('cpu')

    # Reuse CNN_CPD data pipeline
    _, test_dataset, _, _, input_dim = split_and_load_data(
        data_dir, train_dir, test_dir, test_size=0.2, target_length=target_length
    )
    test_loader = DataLoader(test_dataset, batch_size=batch_size, shuffle=False, collate_fn=collate_cpd_batch)

    # Load trained model once
    model = CPDNet(input_dim=input_dim, hidden_dim=hidden_dim).to(device)
    from CNN_CPD.utils import safe_torch_load
    state = safe_torch_load(weight_path, map_location=device)
    if isinstance(state, dict):
        if 'model_state_dict' in state:
            state = state['model_state_dict']
        elif 'state_dict' in state:
            state = state['state_dict']
    model.load_state_dict(state)
    model.eval()

    rows = []
    for tol in tolerances:
        metrics = evaluate_iou_overlap(
            model,
            test_loader,
            device,
            threshold=threshold,
            max_change_points=max_change_points,
            tolerance=int(tol),
        )
        window_ms = (int(tol) * 1000.0) / float(hz)
        rows.append({
            'tolerance_samples': int(tol),
            'window_ms': float(window_ms),
            'precision': float(metrics['precision']),
        })

    # Print table
    print("\n" + "=" * 70)
    print("PRECISION VS DETECTION TOLERANCE")
    print("=" * 70)
    print(f"{'Tol (samples)':<15} {'Window (ms)':<15} {'Precision':<10}")
    print("-" * 70)
    for r in rows:
        print(f"{r['tolerance_samples']:<15} {r['window_ms']:<15.1f} {r['precision']:<10.4f}")

    # Plot
    x_ms = [r['window_ms'] for r in rows]
    y_prec = [r['precision'] for r in rows]

    plt.figure(figsize=(8, 4.5))
    if plot_kind == 'scatter':
        plt.scatter(x_ms, y_prec, s=60)
    else:
        bars = plt.bar(x_ms, y_prec, width=0.6 * (x_ms[1] - x_ms[0]) if len(x_ms) > 1 else 10)
        for bar, y in zip(bars, y_prec):
            plt.text(
                bar.get_x() + bar.get_width() / 2,
                y + 0.015,
                f"{y:.3f}",
                ha='center',
                va='bottom',
                fontsize=12
            )

    plt.xlabel('Window in ms around CP', fontsize=12)
    plt.ylabel('Precision (hit rate for 1 CP)', fontsize=12)
    plt.title('Precision vs Detection Tolerance', fontsize=14)
    plt.ylim(0.0, 1.0)
    plt.grid(alpha=0.3, axis='y')
    plt.tight_layout()
    plt.show()

    return rows


if __name__ == "__main__":
    DATA_DIR = '../Data/Trial_data_27Feb'
    TRAIN_DIR = '../Data/CPD_train'
    TEST_DIR = '../Data/CPD_test'
    WEIGHT_PATH = './saved_weights/cpd_best_model.pt'

    RUN_LENGTH_ANALYSIS = True
    RUN_TOLERANCE_SWEEP = False
    
    if os.path.exists(DATA_DIR):
        if RUN_LENGTH_ANALYSIS:
            stats = print_analysis(DATA_DIR)

        if RUN_TOLERANCE_SWEEP:
            analyze_precision_vs_tolerance(
                data_dir=DATA_DIR,
                train_dir=TRAIN_DIR,
                test_dir=TEST_DIR,
                weight_path=WEIGHT_PATH,
                target_length=500,
                batch_size=4,
                hidden_dim=64,
                threshold=0.2,
                max_change_points=1,
                hz=50,
                tolerances=range(5, 26, 5),
                plot_kind='bar',
            )
    else:
        print(f"Data directory not found: {DATA_DIR}")
        print("\nTrying to find data directory...")
        # Try alternative paths
        alt_paths = [
            './Data/Trial_data_27Feb',
            '../../Data/Trial_data_27Feb',
        ]
        for path in alt_paths:
            if os.path.exists(path):
                print(f"Found data at: {path}")
                if RUN_LENGTH_ANALYSIS:
                    stats = print_analysis(path)

                if RUN_TOLERANCE_SWEEP:
                    analyze_precision_vs_tolerance(
                        data_dir=path,
                        train_dir=TRAIN_DIR,
                        test_dir=TEST_DIR,
                        weight_path=WEIGHT_PATH,
                        target_length=500,
                        batch_size=4,
                        hidden_dim=64,
                        threshold=0.2,
                        max_change_points=1,
                        hz=50,
                        tolerances=range(5, 26, 5),
                        plot_kind='bar',
                    )
                break
