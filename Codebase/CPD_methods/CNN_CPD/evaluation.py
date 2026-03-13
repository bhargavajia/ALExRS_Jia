import os
import numpy as np
import pandas as pd
import torch
from model import CPDNet
from data import load_file
from CNN_CPD.utils import safe_torch_load


def _ordered_states_from_labels(labels):
    seen = set()
    ordered = []
    for label in labels:
        if label not in seen:
            ordered.append(label)
            seen.add(label)
    return ordered


def _labels_from_change_points(seq_len, change_points, ordered_states):
    if len(ordered_states) == 0:
        return np.array(['IDLE'] * seq_len)
    labels = np.array(['UNKNOWN'] * seq_len, dtype=object)
    change_points = sorted(change_points)
    expected_num_cps = len(ordered_states) - 1
    if len(change_points) == 0:
        labels[:] = ordered_states[0]
    else:
        valid_cps = change_points[:expected_num_cps]
        state_idx = 0
        if len(valid_cps) == 0:
            labels[:] = ordered_states[0]
        else:
            labels[:valid_cps[0]] = ordered_states[0]
            for i in range(len(valid_cps)):
                state_idx = i + 1
                if state_idx < len(ordered_states):
                    if i < len(valid_cps) - 1:
                        labels[valid_cps[i]:valid_cps[i+1]] = ordered_states[state_idx]
                    else:
                        if len(change_points) <= expected_num_cps:
                            labels[valid_cps[i]:] = ordered_states[state_idx]
                        else:
                            first_extra_cp = change_points[expected_num_cps]
                            labels[valid_cps[i]:first_extra_cp] = ordered_states[state_idx]
    return labels


def evaluate_detection(model, dataloader, device, threshold=0.5, tolerance=10, max_change_points=1):
    model.eval()
    all_tp = all_fp = all_fn = 0
    with torch.no_grad():
        for features, cp_labels, _ in dataloader:
            features = features.to(device)
            predicted_cps_list = model.predict_change_points(features, threshold=threshold, max_change_points=max_change_points)
            cp_labels_np = cp_labels.cpu().numpy()
            true_cps_list = []
            for batch_idx in range(cp_labels_np.shape[0]):
                true_cp_indices = np.where(cp_labels_np[batch_idx] == 1)[0].tolist()
                true_cps_list.append(true_cp_indices)
            for true_cps, pred_cps in zip(true_cps_list, predicted_cps_list):
                true_cps = np.array(true_cps)
                pred_cps = np.array(pred_cps)
                matched_pred = set()
                matched_true = set()
                for pred_idx in pred_cps:
                    if len(true_cps) == 0:
                        all_fp += 1
                    else:
                        distances = np.abs(true_cps - pred_idx)
                        nearest_true_idx = np.argmin(distances)
                        if distances[nearest_true_idx] <= tolerance:
                            matched_pred.add(pred_idx)
                            matched_true.add(nearest_true_idx)
                            all_tp += 1
                        else:
                            all_fp += 1
                all_fn += len(true_cps) - len(matched_true)
    precision = all_tp / (all_tp + all_fp) if (all_tp + all_fp) > 0 else 0.0
    recall = all_tp / (all_tp + all_fn) if (all_tp + all_fn) > 0 else 0.0
    f1 = 2 * (precision * recall) / (precision + recall) if (precision + recall) > 0 else 0.0
    return {'precision': precision, 'recall': recall, 'f1_score': f1, 'true_positives': all_tp, 'false_positives': all_fp, 'false_negatives': all_fn, 'tolerance': tolerance}


def evaluate_iou_overlap(model, dataloader, device, threshold=0.5, max_change_points=1, tolerance=10):
    model.eval()
    all_ious = []
    all_precisions = []
    all_recalls = []
    all_f1s = []
    per_state_ious = {'GOING': [], 'RETURNING': []}
    num_predicted_cps = []
    num_true_cps = []
    selected_cp_probs = []
    all_detection_delays_samples = []
    with torch.no_grad():
        for features, cp_labels, states in dataloader:
            features = features.to(device)
            states_list = states
            seq_len = features.shape[2]
            predicted_cps_list, predicted_scores_list = model.predict_change_points(features, threshold=threshold, max_change_points=max_change_points, return_scores=True)
            cp_labels_np = cp_labels.cpu().numpy()
            true_cps_list = []
            for batch_idx in range(cp_labels_np.shape[0]):
                true_cp_indices = np.where(cp_labels_np[batch_idx] == 1)[0].tolist()
                true_cps_list.append(true_cp_indices)
            for sample_idx, (true_cps, pred_cps) in enumerate(zip(true_cps_list, predicted_cps_list)):
                true_states = np.array(states_list[sample_idx])
                num_predicted_cps.append(len(pred_cps))
                num_true_cps.append(len(true_cps))
                sample_scores = predicted_scores_list[sample_idx]
                if len(sample_scores) > 0:
                    selected_cp_probs.append(float(np.max(sample_scores)))
                pred_labels = _labels_from_change_points(seq_len, pred_cps, _ordered_states_from_labels(true_states))
                sample_ious = []
                for state in ['GOING', 'RETURNING']:
                    true_mask = (true_states == state)
                    pred_mask = (pred_labels == state)
                    union = np.sum(true_mask | pred_mask)
                    intersection = np.sum(true_mask & pred_mask)
                    iou = (intersection / union) if union > 0 else 0.0
                    sample_ious.append(iou)
                    per_state_ious[state].append(iou)
                unknown_count = np.sum(pred_labels == 'UNKNOWN')
                over_seg_penalty = unknown_count / seq_len if seq_len > 0 else 0.0
                sample_avg_iou = np.mean(sample_ious) * (1.0 - over_seg_penalty) if sample_ious else 0.0
                all_ious.append(sample_avg_iou)
                true_cps = np.array(true_cps)
                pred_cps = np.array(pred_cps)
                if len(pred_cps) == 0 and len(true_cps) == 0:
                    tp = fp = fn = 0
                elif len(pred_cps) == 0:
                    tp, fp, fn = 0, 0, len(true_cps)
                elif len(true_cps) == 0:
                    tp, fp, fn = 0, len(pred_cps), 0
                else:
                    matched_pred = set()
                    matched_true = set()
                    sample_delays = []
                    for pred_idx in pred_cps:
                        distances = np.abs(true_cps - pred_idx)
                        nearest_true_idx = np.argmin(distances)
                        if distances[nearest_true_idx] <= tolerance and nearest_true_idx not in matched_true:
                            matched_pred.add(pred_idx)
                            matched_true.add(nearest_true_idx)
                            sample_delays.append(int(pred_idx - true_cps[nearest_true_idx]))
                    tp = len(matched_true)
                    fp = len(pred_cps) - tp
                    fn = len(true_cps) - tp
                precision = tp / (tp + fp) if (tp + fp) > 0 else 0.0
                recall = tp / (tp + fn) if (tp + fn) > 0 else 0.0
                f1 = 2 * (precision * recall) / (precision + recall) if (precision + recall) > 0 else 0.0
                all_precisions.append(precision)
                all_recalls.append(recall)
                all_f1s.append(f1)
                if sample_delays:
                    all_detection_delays_samples.extend(sample_delays)
    avg_iou = float(np.mean(all_ious)) if all_ious else 0.0
    avg_precision = float(np.mean(all_precisions)) if all_precisions else 0.0
    avg_recall = float(np.mean(all_recalls)) if all_recalls else 0.0
    avg_f1 = float(np.mean(all_f1s)) if all_f1s else 0.0
    per_state_summary = {}
    for state in ['GOING', 'RETURNING']:
        per_state_summary[state] = float(np.mean(per_state_ious[state])) if per_state_ious[state] else 0.0
    cp_stats = {
        'min_predicted_cps': int(np.min(num_predicted_cps)) if num_predicted_cps else 0,
        'max_predicted_cps': int(np.max(num_predicted_cps)) if num_predicted_cps else 0,
        'avg_predicted_cps': float(np.mean(num_predicted_cps)) if num_predicted_cps else 0.0,
        'min_true_cps': int(np.min(num_true_cps)) if num_true_cps else 0,
        'max_true_cps': int(np.max(num_true_cps)) if num_true_cps else 0,
        'avg_true_cps': float(np.mean(num_true_cps)) if num_true_cps else 0.0,
        'max_selected_cp_prob': float(np.max(selected_cp_probs)) if selected_cp_probs else 0.0,
        'min_selected_cp_prob': float(np.min(selected_cp_probs)) if selected_cp_probs else 0.0,
        'avg_selected_cp_prob': float(np.mean(selected_cp_probs)) if selected_cp_probs else 0.0,
        'avg_detection_delay_samples': float(np.mean(np.abs(all_detection_delays_samples))) if all_detection_delays_samples else 0.0,
        'avg_detection_delay_ms': (float(np.mean(np.abs(all_detection_delays_samples))) * (1000.0 / 50.0)) if all_detection_delays_samples else 0.0,
    }
    return {'average_iou': avg_iou, 'per_state_iou': per_state_summary, 'precision': avg_precision, 'recall': avg_recall, 'f1_score': avg_f1, 'cp_stats': cp_stats}


def plot_best_worst_segmentation(model, dataset, data_dir, device, threshold=0.2, max_change_points=1, hz=50, plot_cols=None):
    if plot_cols is None:
        plot_cols = ['Left_EE_PosZ_m', 'Left_EE_PosY_m', 'Left_J5_ElbowFlexion_rad']
    if len(dataset) == 0:
        print("Dataset is empty; cannot plot best/worst segmentation.")
        return
    model.eval()
    sample_infos = []
    with torch.no_grad():
        for sample in dataset.samples:
            features_np = sample['features']
            true_states = np.asarray(sample['states'])
            true_cps = list(sample['change_points'])
            seq_len = features_np.shape[0]
            x = torch.from_numpy(features_np).float().unsqueeze(0).transpose(1, 2).to(device)
            pred_cps_list = model.predict_change_points(x, threshold=threshold, max_change_points=max_change_points)
            pred_cps = pred_cps_list[0]
            matched_delays_samples = []
            if len(pred_cps) > 0 and len(true_cps) > 0:
                pred_sorted = sorted([int(p) for p in pred_cps])
                true_sorted = sorted([int(t) for t in true_cps])
                unused_true = set(range(len(true_sorted)))
                for p in pred_sorted:
                    if not unused_true:
                        break
                    best_idx = min(unused_true, key=lambda i: abs(true_sorted[i] - p))
                    unused_true.remove(best_idx)
                    matched_delays_samples.append(p - true_sorted[best_idx])
            if matched_delays_samples:
                avg_abs_delay_samples = float(np.mean(np.abs(matched_delays_samples)))
                mean_signed_delay_samples = float(np.mean(matched_delays_samples))
            else:
                avg_abs_delay_samples = float('nan')
                mean_signed_delay_samples = float('nan')
            sample_period_ms = 1000.0 / float(hz)
            avg_abs_delay_ms = avg_abs_delay_samples * sample_period_ms if not np.isnan(avg_abs_delay_samples) else float('nan')
            mean_signed_delay_ms = mean_signed_delay_samples * sample_period_ms if not np.isnan(mean_signed_delay_samples) else float('nan')
            ordered_states = _ordered_states_from_labels(true_states)
            pred_labels = _labels_from_change_points(seq_len, pred_cps, ordered_states)
            sample_ious = []
            for state in ['GOING', 'RETURNING']:
                true_mask = (true_states == state)
                pred_mask = (pred_labels == state)
                union = np.sum(true_mask | pred_mask)
                inter = np.sum(true_mask & pred_mask)
                iou = (inter / union) if union > 0 else 0.0
                sample_ious.append(iou)
            unknown_count = np.sum(pred_labels == 'UNKNOWN')
            over_seg_penalty = unknown_count / seq_len if seq_len > 0 else 0.0
            sample_iou = float(np.mean(sample_ious) * (1.0 - over_seg_penalty)) if sample_ious else 0.0
            sample_infos.append({'file': sample['file'], 'seq_len': seq_len, 'true_cps': true_cps, 'pred_cps': pred_cps, 'iou': sample_iou, 'avg_abs_delay_ms': avg_abs_delay_ms, 'mean_signed_delay_ms': mean_signed_delay_ms})
    best = max(sample_infos, key=lambda s: s['iou'])
    worst = min(sample_infos, key=lambda s: s['iou'])
    def _fmt_delay(v):
        return f"{v:.1f} ms" if not np.isnan(v) else "N/A"
    print("\n" + "=" * 60)
    print(f"BEST segmentation:  {best['file']} (IoU={best['iou']:.4f}, avg |delay|={_fmt_delay(best['avg_abs_delay_ms'])})")
    print(f"WORST segmentation: {worst['file']} (IoU={worst['iou']:.4f}, avg |delay|={_fmt_delay(worst['avg_abs_delay_ms'])})")
    print("=" * 60)
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(2, 3, figsize=(18, 8))
    fig.suptitle('Best vs Worst Segmentation by IoU', fontsize=14, fontweight='bold')
    sample_period_ms = 1000.0 / float(hz)
    plot_pairs = [(best, 'BEST'), (worst, 'WORST')]
    for row_idx, (info, prefix) in enumerate(plot_pairs):
        file_path = os.path.join(data_dir, info['file'])
        df = load_file(file_path)
        available_cols = [c for c in plot_cols if c in df.columns]
        if len(available_cols) < 3:
            numeric_cols = [c for c in df.columns if c != 'State']
            for c in numeric_cols:
                if c not in available_cols:
                    available_cols.append(c)
                if len(available_cols) == 3:
                    break
        available_cols = available_cols[:3]
        old_idx = np.linspace(0, len(df) - 1, len(df))
        new_idx = np.linspace(0, len(df) - 1, info['seq_len'])
        time_ms = np.arange(info['seq_len']) * sample_period_ms
        true_points_ms = [cp * sample_period_ms for cp in info['true_cps']]
        pred_points_ms = [cp * sample_period_ms for cp in info['pred_cps']]
        overlap_points_ms = sorted(set(true_points_ms).intersection(set(pred_points_ms)))
        for col_idx in range(3):
            ax = axes[row_idx, col_idx]
            col_name = available_cols[col_idx] if col_idx < len(available_cols) else None
            if col_name is not None:
                series = pd.to_numeric(df[col_name], errors='coerce').fillna(0).values
                interp_series = np.interp(new_idx, old_idx, series)
            else:
                col_name = f'feature_{col_idx}'
                interp_series = np.zeros(info['seq_len'])
            ax.plot(time_ms, interp_series, linewidth=1.5, color='steelblue', label=col_name)
            for cp_ms in overlap_points_ms:
                ax.axvline(cp_ms, color='purple', linestyle='-', linewidth=2.2, alpha=0.9, label='Overlap (True=Detected)' if col_idx == 0 else '', zorder=4)
            true_only_ms = [cp for cp in true_points_ms if cp not in overlap_points_ms]
            pred_only_ms = [cp for cp in pred_points_ms if cp not in overlap_points_ms]
            for cp_ms in true_only_ms:
                ax.axvline(cp_ms, color='green', linestyle='--', linewidth=1.8, label='True CP' if col_idx == 0 else '', zorder=3)
            for cp_ms in pred_only_ms:
                ax.axvline(cp_ms, color='red', linestyle='-.', linewidth=1.8, label='Predicted CP' if col_idx == 0 else '', zorder=5)
            ax.set_xlabel('Time (ms)', fontsize=9)
            ax.set_ylabel(col_name, fontsize=9)
            ax.grid(True, alpha=0.3)
            if col_idx == 0:
                ax.set_title(f"{prefix}: {info['file']}\nIoU={info['iou']:.4f}, avg |delay|={_fmt_delay(info['avg_abs_delay_ms'])}", fontsize=10, fontweight='bold')
            if row_idx == 0 and col_idx == 0:
                handles, labels = ax.get_legend_handles_labels()
                uniq = dict(zip(labels, handles))
                ax.legend(uniq.values(), uniq.keys(), loc='upper right', fontsize=8)
    plt.tight_layout()
    plt.show()


def print_cv_summary(all_fold_metrics, k):
    print(f"\n" + "="*60)
    print("Evaluation Summary")
    print("="*60)
    avg_iou = np.mean([m['average_iou'] for m in all_fold_metrics])
    avg_precision = np.mean([m['precision'] for m in all_fold_metrics])
    avg_recall = np.mean([m['recall'] for m in all_fold_metrics])
    avg_f1 = np.mean([m['f1_score'] for m in all_fold_metrics])
    std_iou = np.std([m['average_iou'] for m in all_fold_metrics])
    std_precision = np.std([m['precision'] for m in all_fold_metrics])
    std_recall = np.std([m['recall'] for m in all_fold_metrics])
    std_f1 = np.std([m['f1_score'] for m in all_fold_metrics])
    going_ious = [m['per_state_iou']['GOING'] for m in all_fold_metrics]
    returning_ious = [m['per_state_iou']['RETURNING'] for m in all_fold_metrics]
    print(f"\n{'Metric':<20} {'Value':<20}")
    print("-" * 42)
    print(f"{'Average IoU':<20} {avg_iou:.4f} ± {std_iou:.4f}")
    print(f"{'IoU (GOING)':<20} {np.mean(going_ious):.4f} ± {np.std(going_ious):.4f}")
    print(f"{'IoU (RETURNING)':<20} {np.mean(returning_ious):.4f} ± {np.std(returning_ious):.4f}")
    print("-" * 42)
    print(f"{'Precision':<20} {avg_precision:.4f} ± {std_precision:.4f}")
    print(f"{'Recall':<20} {avg_recall:.4f} ± {std_recall:.4f}")
    print(f"{'F1 Score':<20} {avg_f1:.4f} ± {std_f1:.4f}")
    all_cp_stats = [m['cp_stats'] for m in all_fold_metrics]
    avg_predicted_cps = np.mean([s['avg_predicted_cps'] for s in all_cp_stats])
    avg_true_cps = np.mean([s['avg_true_cps'] for s in all_cp_stats])
    min_predicted = int(np.min([s['min_predicted_cps'] for s in all_cp_stats]))
    max_predicted = int(np.max([s['max_predicted_cps'] for s in all_cp_stats]))
    avg_selected_cp_prob = np.mean([s['avg_selected_cp_prob'] for s in all_cp_stats])
    min_selected_cp_prob = float(np.min([s['min_selected_cp_prob'] for s in all_cp_stats]))
    max_selected_cp_prob = float(np.max([s['max_selected_cp_prob'] for s in all_cp_stats]))
    print("-" * 42)
    print("Change Point Detection Statistics:")
    print(f"  True CPs (avg):      {avg_true_cps:.2f}")
    print(f"  Predicted CPs (avg): {avg_predicted_cps:.2f}")
    print(f"  Predicted CPs (min): {min_predicted}")
    print(f"  Predicted CPs (max): {max_predicted}")
    print(f"  Selected CP prob (avg): {avg_selected_cp_prob:.4f}")
    print(f"  Selected CP prob (min): {min_selected_cp_prob:.4f}")
    print(f"  Selected CP prob (max): {max_selected_cp_prob:.4f}")
    return avg_iou, avg_precision, avg_recall, avg_f1, std_iou, std_precision


def evaluate_only(test_loader, input_dim, device, weight_path, threshold=0.5, hidden_dim=64, max_change_points=1, tolerance=10, train_loader=None, eval_on='test'):
    print("="*60)
    print("Evaluation Only Mode")
    print("="*60)
    print(f"Loading weights from: {weight_path}")
    print(f"CP detection threshold: {threshold}")
    print(f"Evaluating on: {eval_on.upper()} set")
    model = CPDNet(input_dim=input_dim, hidden_dim=hidden_dim).to(device)
    state = safe_torch_load(weight_path, map_location=device)
    if isinstance(state, dict):
        if 'model_state_dict' in state:
            state = state['model_state_dict']
        elif 'state_dict' in state:
            state = state['state_dict']
    model.load_state_dict(state)
    if eval_on == 'train':
        if train_loader is None:
            raise ValueError("train_loader must be provided when eval_on='train'")
        loader = train_loader
    else:
        loader = test_loader
    test_metrics = evaluate_iou_overlap(model, loader, device, threshold=threshold, max_change_points=max_change_points, tolerance=tolerance)
    print(f"\n{eval_on.capitalize()} Set Metrics:")
    print(f"  Average IoU: {test_metrics['average_iou']:.4f}")
    print(f"  Per-state IoU - GOING: {test_metrics['per_state_iou']['GOING']:.4f}, RETURNING: {test_metrics['per_state_iou']['RETURNING']:.4f}")
    print(f"  Detection Metrics (P/R/F1): {test_metrics['precision']:.4f} / {test_metrics['recall']:.4f} / {test_metrics['f1_score']:.4f}")
    avg_delay_samples = test_metrics['cp_stats'].get('avg_detection_delay_samples', None)
    avg_delay_ms = test_metrics['cp_stats'].get('avg_detection_delay_ms', None)
    if avg_delay_samples is not None:
        print(f"  Average detection delay: {avg_delay_samples:.2f} samples ({avg_delay_ms:.2f} ms)")
    return [test_metrics]
