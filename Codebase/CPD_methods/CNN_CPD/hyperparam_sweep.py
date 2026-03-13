"""Simple hyperparameter sweep runner for the CNN CPD pipeline.

Usage:
  cd CNN_CPD
  python3 hyperparam_sweep.py

The script runs k-fold training for each configuration, records summary
metrics to `sweep_results.csv` and saves per-run weights under `sweep_results/`.

Note: runs may be slow; tune `k_folds` and `epochs` in the grid to shorten runs.
"""
import os
import csv
import itertools
from datetime import datetime
import pathlib
import sys

import torch
from torch.utils.data import DataLoader
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import pandas as pd
import random

if __package__ is None:
    parent = str(pathlib.Path(__file__).resolve().parent.parent)
    if parent not in sys.path:
        sys.path.insert(0, parent)

from params import (
    DATA_DIR, TRAIN_DIR, TEST_DIR, TARGET_LENGTH, BATCH_SIZE, K_FOLDS,
    LEARNING_RATE, POS_WEIGHT, HIDDEN_DIM, EPOCHS, MAX_CHANGE_POINTS, THRESHOLD,
    DETECTION_TOLERANCE
)
from CNN_CPD.helpers import split_and_load_data, CPDDataset, collate_cpd_batch
from CNN_CPD.training import train_and_save_pipeline
from CNN_CPD.evaluation import evaluate_iou_overlap
from CNN_CPD.model import CPDNet
from CNN_CPD.utils import safe_torch_load

# Device
if torch.backends.mps.is_available():
    DEVICE = torch.device('mps')
elif torch.cuda.is_available():
    DEVICE = torch.device('cuda')
else:
    DEVICE = torch.device('cpu')

# Load data once
full_train_dataset, test_dataset, train_files, test_files, input_dim = split_and_load_data(
    DATA_DIR, TRAIN_DIR, TEST_DIR, test_size=0.2, target_length=TARGET_LENGTH
)

test_loader = DataLoader(test_dataset, batch_size=BATCH_SIZE, shuffle=False, collate_fn=collate_cpd_batch)

out_dir = 'trials'
os.makedirs(out_dir, exist_ok=True)
results_csv = os.path.join(out_dir, 'sweep_results.csv')

# CLI mode: 'train' (default) or 'plot' to load saved weights and create best/worst plots
mode = sys.argv[1] if len(sys.argv) > 1 else 'train'


def ensure_dir(path):
    os.makedirs(path, exist_ok=True)


def process_run_plot(run_id, hidden_dim, eval_on='test'):
    weights_path = os.path.join(out_dir, 'weights', run_id, 'cpd_best_model.pt')
    if not os.path.exists(weights_path):
        print(f"Skipping {run_id}: weights not found at {weights_path}")
        return
    print(f"Plotting {run_id} -> loading weights from {weights_path}")

    # prepare dataset selection
    dataset = full_train_dataset if eval_on == 'train' else test_dataset

    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    model = CPDNet(input_dim=input_dim, hidden_dim=int(hidden_dim)).to(device)
    state = safe_torch_load(weights_path, map_location=device)
    if isinstance(state, dict):
        if 'model_state_dict' in state:
            state = state['model_state_dict']
        elif 'state_dict' in state:
            state = state['state_dict']
    model.load_state_dict(state)

    plots_dir = os.path.join(out_dir, 'plots', run_id)
    ensure_dir(plots_dir)

    try:
        # use existing evaluation helper to create a best/worst figure
        from CNN_CPD import evaluation as eval_mod
        eval_mod.plot_best_worst_segmentation(
            model,
            dataset,
            DATA_DIR,
            device,
            threshold=float(THRESHOLD),
            max_change_points=int(MAX_CHANGE_POINTS),
            hz=50,
        )
        fig = plt.gcf()
        out_path = os.path.join(plots_dir, 'best_worst.png')
        fig.savefig(out_path, dpi=150, bbox_inches='tight')
        plt.close(fig)
        print(f"Saved plot to {out_path}")
    except Exception as e:
        print(f"Error while plotting for {run_id}: {e}")


# Build the one-at-a-time run list using defaults from params.py
defaults = {
    'learning_rate': LEARNING_RATE,
    'pos_weight': POS_WEIGHT,
    'hidden_dim': HIDDEN_DIM,
    'k_folds': K_FOLDS,
    'epochs': EPOCHS,
    'threshold': THRESHOLD,
    'max_change_points': MAX_CHANGE_POINTS,
}

# User-specified sequence: zeroth default, then single-parameter changes
run_configs = []
# # Zeroth run: defaults
# run_configs.append((defaults.copy(), 'none', None))
# # 1) vary lr -> 1e-4
# c = defaults.copy(); c['learning_rate'] = 1e-4; run_configs.append((c, 'learning_rate', 1e-4))
# # 2) Combined run: learning_rate=1e-4 and epochs=100
# c = defaults.copy(); c['learning_rate'] = 1e-4; c['epochs'] = 100; run_configs.append((c, 'learning_rate+epochs', '1e-4,100'))
# # 3) pos_weight -> 5 then 20
# c = defaults.copy(); c['pos_weight'] = 5.0; run_configs.append((c, 'pos_weight', 5.0))
# c = defaults.copy(); c['pos_weight'] = 20.0; run_configs.append((c, 'pos_weight', 20.0))
# # 4) hidden dim -> 128 then 32
# c = defaults.copy(); c['hidden_dim'] = 128; run_configs.append((c, 'hidden_dim', 128))
# c = defaults.copy(); c['hidden_dim'] = 32; run_configs.append((c, 'hidden_dim', 32))
# # 5) folds -> 3
# c = defaults.copy(); c['k_folds'] = 3; run_configs.append((c, 'k_folds', 3))
# # 6) epochs -> 100
# c = defaults.copy(); c['epochs'] = 100; run_configs.append((c, 'epochs', 100))

# # 7) epochs -> 200
# c = defaults.copy(); c['epochs'] = 200; run_configs.append((c, 'epochs', 200))
# # 8) hidden dim -> 256
# c = defaults.copy(); c['hidden_dim'] = 256; run_configs.append((c, 'hidden_dim', 256))


# 9) Combined run: epochs=100 and hidden_dim=128
c = defaults.copy(); c['epochs'] = 100; c['hidden_dim'] = 128; run_configs.append((c, 'epochs+hidden_dim', '100,128'))
# 10) Combined run: epochs=100, hidden_dim=128, pos_weight=5
c = defaults.copy(); c['epochs'] = 100; c['hidden_dim'] = 128; c['pos_weight'] = 5.0; run_configs.append((c, 'epochs+hidden_dim+pos_weight', '100,128,5.0'))

# If the CSV exists, detect the last run index so we can append. Otherwise create it with header.
if os.path.exists(results_csv):
    # read last non-empty row to find last run id
    last_run_num = 0
    try:
        with open(results_csv, 'r', newline='') as csvfile:
            reader = list(csv.reader(csvfile))
            # find last non-empty data row (skip header)
            for row in reversed(reader):
                if row and row[0].strip().lower().startswith('run_'):
                    try:
                        last_run_num = int(row[0].strip().split('_')[-1])
                        break
                    except Exception:
                        continue
    except Exception:
        last_run_num = 0
    start_index = last_run_num + 1
    write_header = False
else:
    start_index = 1
    write_header = True

if write_header:
    with open(results_csv, 'w', newline='') as csvfile:
        writer = csv.writer(csvfile)
        header = [
            'run_id', 'timestamp', 'varied_param', 'varied_value',
            'learning_rate', 'pos_weight', 'hidden_dim', 'k_folds', 'epochs',
            'avg_iou', 'avg_precision', 'std_iou', 'std_precision',
            'avg_detection_delay_ms'
        ]
        writer.writerow(header)

def train_sweep():
    """Run the training sweep and append results to CSV."""
    import numpy as np
    for offset, (cfg_dict, varied_param, varied_value) in enumerate(run_configs):
        run_idx = start_index + offset
        run_id = f'run_{run_idx}'
        ts = datetime.now().isoformat(timespec='seconds')
        # create per-run directories
        weights_root = os.path.join(out_dir, 'weights')
        weights_dir = os.path.join(weights_root, run_id)
        os.makedirs(weights_dir, exist_ok=True)
        print(f"\n=== RUN {run_idx}/{len(run_configs)}: varied={varied_param} value={varied_value} ===")

        plots_root = os.path.join(out_dir, 'plots')
        plots_dir = os.path.join(plots_root, run_id)
        os.makedirs(plots_dir, exist_ok=True)

        fold_results, all_fold_metrics = train_and_save_pipeline(
            full_train_dataset=full_train_dataset,
            test_loader=test_loader,
            test_dataset=test_dataset,
            input_dim=input_dim,
            device=DEVICE,
            k_folds=cfg_dict['k_folds'],
            epochs=cfg_dict['epochs'],
            learning_rate=cfg_dict['learning_rate'],
            save_dir=weights_dir,
            early_stop_patience=10,
            pos_weight=cfg_dict['pos_weight'],
            threshold=cfg_dict.get('threshold', THRESHOLD),
            hidden_dim=cfg_dict['hidden_dim'],
            max_change_points=cfg_dict.get('max_change_points', MAX_CHANGE_POINTS),
            tolerance=DETECTION_TOLERANCE,
            weight_path=None,
            plots_dir=plots_dir,
            display_time=5,
        )

        # Summarize
        avg_iou = float(np.mean([m['average_iou'] for m in all_fold_metrics]))
        avg_precision = float(np.mean([m['precision'] for m in all_fold_metrics]))
        std_iou = float(np.std([m['average_iou'] for m in all_fold_metrics]))
        std_precision = float(np.std([m['precision'] for m in all_fold_metrics]))

        # Evaluate best saved model for detection delay
        best_model_path = os.path.join(weights_dir, 'cpd_best_model.pt')
        avg_detection_delay_ms = float('nan')
        if os.path.exists(best_model_path):
            try:
                model = CPDNet(input_dim=input_dim, hidden_dim=cfg_dict['hidden_dim']).to(DEVICE)
                state = safe_torch_load(best_model_path, map_location=DEVICE)
                if isinstance(state, dict):
                    if 'model_state_dict' in state:
                        state = state['model_state_dict']
                    elif 'state_dict' in state:
                        state = state['state_dict']
                model.load_state_dict(state)
                model.eval()
                test_metrics = evaluate_iou_overlap(model, test_loader, DEVICE, threshold=cfg_dict.get('threshold', THRESHOLD), max_change_points=cfg_dict.get('max_change_points', MAX_CHANGE_POINTS), tolerance=DETECTION_TOLERANCE)
                avg_detection_delay_ms = float(test_metrics['cp_stats'].get('avg_detection_delay_ms', float('nan')))
            except Exception as e:
                print('Failed to load/evaluate best model:', e)

        with open(results_csv, 'a', newline='') as csvfile:
            writer = csv.writer(csvfile)
            row = [
                run_id, ts, varied_param, varied_value,
                cfg_dict['learning_rate'], cfg_dict['pos_weight'], cfg_dict['hidden_dim'], cfg_dict['k_folds'], cfg_dict['epochs'],
                avg_iou, avg_precision, std_iou, std_precision,
                avg_detection_delay_ms,
            ]
            writer.writerow(row)

        print(f"Run {run_id} complete. avg_iou={avg_iou:.4f}, precision={avg_precision:.4f}")

    print('\nSweep complete. Trials saved to', results_csv)


def plot_sweep(run_ids=None):
    """Load saved weights and produce best/worst plots for runs in CSV or provided list.

    If `run_ids` is None the function reads `results_csv` and processes all rows.
    """
    df = None
    if run_ids is None:
        if not os.path.exists(results_csv):
            raise FileNotFoundError(f"Sweep CSV not found at {results_csv}")
        df = pd.read_csv(results_csv)
        rows = df.to_dict(orient='records')
    else:
        rows = [{'run_id': rid, 'hidden_dim': HIDDEN_DIM} for rid in run_ids]

    eval_on = 'train' if 'EVAL_ON' in globals() and globals()['EVAL_ON'] == 'train' else 'test'
    for row in rows:
        run_id = row.get('run_id')
        hidden_dim = row.get('hidden_dim', HIDDEN_DIM)
        process_run_plot(run_id, hidden_dim, eval_on=eval_on)


def repeat_runs(run_ids, n_seeds=5):
    """Re-run the specified run_ids `n_seeds` times with different seeds.

    For each run_id the function will create per-seed weight folders under
    `trials/weights/<run_id>/seed_<seed>/` and append per-seed results to
    `trials/weights/<run_id>/repeats.csv`.
    """
    if not os.path.exists(results_csv):
        raise FileNotFoundError(f"Sweep CSV not found at {results_csv}")
    df = pd.read_csv(results_csv)
    to_process = []
    for rid in run_ids:
        row = df[df['run_id'] == rid]
        if row.empty:
            print(f"Warning: run_id {rid} not found in {results_csv}; skipping")
            continue
        r = row.iloc[0].to_dict()
        # coerce types
        cfg = {
            'learning_rate': float(r.get('learning_rate', LEARNING_RATE)),
            'pos_weight': float(r.get('pos_weight', POS_WEIGHT)),
            'hidden_dim': int(r.get('hidden_dim', HIDDEN_DIM)),
            'k_folds': int(r.get('k_folds', K_FOLDS)),
            'epochs': int(r.get('epochs', EPOCHS)),
            'threshold': float(r.get('threshold', THRESHOLD)),
            'max_change_points': int(r.get('max_change_points', MAX_CHANGE_POINTS)),
        }
        to_process.append((rid, cfg))

    for run_id, cfg in to_process:
        weights_root = os.path.join(out_dir, 'weights')
        run_weights_dir = os.path.join(weights_root, run_id)
        os.makedirs(run_weights_dir, exist_ok=True)
        repeats_csv = os.path.join(run_weights_dir, 'repeats.csv')
        # write header if missing
        if not os.path.exists(repeats_csv):
            with open(repeats_csv, 'w', newline='') as f:
                writer = csv.writer(f)
                header = ['timestamp', 'seed', 'learning_rate', 'pos_weight', 'hidden_dim', 'k_folds', 'epochs', 'avg_iou', 'avg_precision', 'std_iou', 'std_precision', 'avg_detection_delay_ms']
                writer.writerow(header)

        print(f"Repeating {run_id} for {n_seeds} seeds")
        for seed in range(n_seeds):
            s = seed + 1
            # deterministic-ish seeding
            np = __import__('numpy')
            np.random.seed(s)
            random.seed(s)
            torch.manual_seed(s)
            if torch.cuda.is_available():
                torch.cuda.manual_seed_all(s)
            torch.backends.cudnn.deterministic = True
            torch.backends.cudnn.benchmark = False

            seed_weights_dir = os.path.join(run_weights_dir, f'seed_{s}')
            os.makedirs(seed_weights_dir, exist_ok=True)

            print(f"  Seed {s}: training -> saving to {seed_weights_dir}")
            fold_results, all_fold_metrics = train_and_save_pipeline(
                full_train_dataset=full_train_dataset,
                test_loader=test_loader,
                test_dataset=test_dataset,
                input_dim=input_dim,
                device=DEVICE,
                k_folds=cfg['k_folds'],
                epochs=cfg['epochs'],
                learning_rate=cfg['learning_rate'],
                save_dir=seed_weights_dir,
                early_stop_patience=10,
                pos_weight=cfg['pos_weight'],
                threshold=cfg.get('threshold', THRESHOLD),
                hidden_dim=cfg['hidden_dim'],
                max_change_points=cfg.get('max_change_points', MAX_CHANGE_POINTS),
                tolerance=DETECTION_TOLERANCE,
                weight_path=None,
                plots_dir=os.path.join(out_dir, 'plots', run_id, f'seed_{s}'),
                display_time=0,
            )

            import numpy as _np
            avg_iou = float(_np.mean([m['average_iou'] for m in all_fold_metrics]))
            avg_precision = float(_np.mean([m['precision'] for m in all_fold_metrics]))
            std_iou = float(_np.std([m['average_iou'] for m in all_fold_metrics]))
            std_precision = float(_np.std([m['precision'] for m in all_fold_metrics]))

            # Evaluate saved best model for delay
            best_model_path = os.path.join(seed_weights_dir, 'cpd_best_model.pt')
            avg_detection_delay_ms = float('nan')
            if os.path.exists(best_model_path):
                try:
                    model = CPDNet(input_dim=input_dim, hidden_dim=cfg['hidden_dim']).to(DEVICE)
                    state = safe_torch_load(best_model_path, map_location=DEVICE)
                    if isinstance(state, dict):
                        if 'model_state_dict' in state:
                            state = state['model_state_dict']
                        elif 'state_dict' in state:
                            state = state['state_dict']
                    model.load_state_dict(state)
                    model.eval()
                    test_metrics = evaluate_iou_overlap(model, test_loader, DEVICE, threshold=cfg.get('threshold', THRESHOLD), max_change_points=cfg.get('max_change_points', MAX_CHANGE_POINTS), tolerance=DETECTION_TOLERANCE)
                    avg_detection_delay_ms = float(test_metrics['cp_stats'].get('avg_detection_delay_ms', float('nan')))
                except Exception as e:
                    print('Failed to load/evaluate best model for seed:', e)

            ts = datetime.now().isoformat(timespec='seconds')
            with open(repeats_csv, 'a', newline='') as f:
                writer = csv.writer(f)
                row = [ts, s, cfg['learning_rate'], cfg['pos_weight'], cfg['hidden_dim'], cfg['k_folds'], cfg['epochs'], avg_iou, avg_precision, std_iou, std_precision, avg_detection_delay_ms]
                writer.writerow(row)

        print(f"Completed repeats for {run_id}; results in {repeats_csv}")


if __name__ == '__main__':
    if mode == 'train':
        train_sweep()
    elif mode == 'plot':
        # optional: accept a comma-separated list of run ids as argv[2]
        run_list = None
        if len(sys.argv) > 2:
            run_list = sys.argv[2].split(',')
        plot_sweep(run_ids=run_list)
    elif mode == 'repeat':
        # usage: python3 hyperparam_sweep.py repeat run_9,run_6 5
        # if no run list provided, repeat for all runs in CSV
        run_list = None
        if len(sys.argv) > 2:
            run_list = sys.argv[2].split(',') if sys.argv[2].strip() else None
        else:
            # read all run_ids from CSV
            if os.path.exists(results_csv):
                df_all = pd.read_csv(results_csv)
                run_list = df_all['run_id'].tolist()
        seeds = 5
        if len(sys.argv) > 3:
            try:
                seeds = int(sys.argv[3])
            except Exception:
                pass
        if not run_list:
            raise ValueError('No run_ids provided and sweep CSV is empty')
        repeat_runs(run_list, n_seeds=seeds)
    else:
        raise ValueError(f"Unknown mode: {mode}. Use 'train' or 'plot'.")
