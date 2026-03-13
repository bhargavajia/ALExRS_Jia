import os
import numpy as np
import torch
import matplotlib.pyplot as plt
from torch.utils.data import DataLoader
from model import CPDNet, WeightedBCELoss
from CNN_CPD.utils import safe_torch_load
from data import SubsetDataset, collate_cpd_batch
from evaluation import evaluate_iou_overlap, print_cv_summary, plot_best_worst_segmentation


def create_kfolds(n_samples, k=5):
    indices = np.arange(n_samples)
    fold_size = n_samples // k
    folds = []
    for fold_idx in range(k):
        val_start = fold_idx * fold_size
        val_end = val_start + fold_size if fold_idx < k - 1 else n_samples
        val_indices = indices[val_start:val_end].tolist()
        train_indices = np.concatenate([indices[:val_start], indices[val_end:]]).tolist()
        folds.append((train_indices, val_indices))
    return folds


def train_epoch(model, dataloader, optimizer, loss_fn, device):
    model.train()
    total_loss = 0.0
    for batch_idx, (features, cp_labels, _) in enumerate(dataloader):
        features = features.to(device)
        cp_labels = cp_labels.to(device)
        logits = model(features)
        loss = loss_fn(logits, cp_labels)
        optimizer.zero_grad()
        loss.backward()
        optimizer.step()
        total_loss += loss.item()
        if (batch_idx + 1) % 5 == 0:
            print(f"  Batch {batch_idx + 1}/{len(dataloader)}, Loss: {loss.item():.4f}")
    return total_loss / len(dataloader)


def validate(model, dataloader, loss_fn, device):
    model.eval()
    total_loss = 0.0
    with torch.no_grad():
        for features, cp_labels, _ in dataloader:
            features = features.to(device)
            cp_labels = cp_labels.to(device)
            logits = model(features)
            loss = loss_fn(logits, cp_labels)
            total_loss += loss.item()
    return total_loss / len(dataloader)


def train_fold(fold_idx, k, model, train_loader, val_loader, test_loader, device, epochs=50, learning_rate=0.001, patience=15, pos_weight=10.0, threshold=0.5, max_change_points=1, tolerance=10):
    print(f"\n" + "#" * 60)
    print(f"FOLD {fold_idx + 1}/{k}")
    print("#" * 60)
    print(f"Train size: {len(train_loader.dataset)}, Val size: {len(val_loader.dataset)}")
    optimizer = torch.optim.Adam(model.parameters(), lr=learning_rate)
    loss_fn = WeightedBCELoss(pos_weight=pos_weight)
    train_losses = []
    val_losses = []
    best_val_loss = float('inf')
    patience_counter = 0
    for epoch in range(1, epochs + 1):
        train_loss = train_epoch(model, train_loader, optimizer, loss_fn, device)
        val_loss = validate(model, val_loader, loss_fn, device)
        train_losses.append(train_loss)
        val_losses.append(val_loss)
        if (epoch % 10 == 0) or (epoch == 1):
            print(f"  Epoch {epoch}/{epochs} - Train Loss: {train_loss:.4f}, Val Loss: {val_loss:.4f}")
        if val_loss < best_val_loss:
            best_val_loss = val_loss
            patience_counter = 0
        else:
            patience_counter += 1
        if patience_counter >= patience:
            print(f"  Early stopping at epoch {epoch}")
            break
    val_metrics = evaluate_iou_overlap(model, val_loader, device, threshold=threshold, max_change_points=max_change_points, tolerance=tolerance)
    test_metrics = evaluate_iou_overlap(model, test_loader, device, threshold=threshold, max_change_points=max_change_points, tolerance=tolerance)
    print(f"\nFold {fold_idx + 1} - Validation Metrics:")
    print(f"  IoU: {val_metrics['average_iou']:.4f}, Per-state: GOING={val_metrics['per_state_iou']['GOING']:.4f}, RETURNING={val_metrics['per_state_iou']['RETURNING']:.4f}")
    print(f"  Detection (P/R/F1): {val_metrics['precision']:.4f} / {val_metrics['recall']:.4f} / {val_metrics['f1_score']:.4f}")
    print(f"Fold {fold_idx + 1} - Test Metrics:")
    print(f"  IoU: {test_metrics['average_iou']:.4f}, Per-state: GOING={test_metrics['per_state_iou']['GOING']:.4f}, RETURNING={test_metrics['per_state_iou']['RETURNING']:.4f}")
    print(f"  Detection (P/R/F1): {test_metrics['precision']:.4f} / {test_metrics['recall']:.4f} / {test_metrics['f1_score']:.4f}")
    return {'train_losses': train_losses, 'val_losses': val_losses, 'val_metrics': val_metrics, 'test_metrics': test_metrics, 'model': model}


def run_kfold_training(full_train_dataset, test_loader, test_dataset, input_dim, device, k=5, epochs=50, learning_rate=0.001, save_dir='./saved_weights', weight_path=None, patience=15, pos_weight=10.0, threshold=0.5, hidden_dim=64, max_change_points=1, tolerance=10):
    os.makedirs(save_dir, exist_ok=True)
    print("\n" + "="*60)
    print("Training Configuration")
    print("="*60)
    print(f"Optimizer: Adam (lr={learning_rate})")
    print(f"Loss: Binary Cross-Entropy with pos_weight={pos_weight}")
    print(f"Epochs per fold: {epochs}")
    print(f"Early stopping patience: {patience}")
    print(f"Batch size: {test_loader.batch_size}")
    print(f"K-Folds: {k}")
    print(f"CP detection threshold: {threshold}")
    print(f"Save directory: {save_dir}")
    if weight_path:
        print(f"Continue training from: {weight_path}")
    else:
        print(f"Training from scratch")
    folds = create_kfolds(len(full_train_dataset), k=k)
    print(f"Number of folds: {k}")
    for fold_idx, (train_idx, val_idx) in enumerate(folds):
        print(f"  Fold {fold_idx + 1}: {len(train_idx)} train, {len(val_idx)} val")
    fold_results = {}
    all_fold_metrics = []
    for fold_idx, (train_indices, val_indices) in enumerate(folds):
        train_subset = SubsetDataset(full_train_dataset, train_indices)
        val_subset = SubsetDataset(full_train_dataset, val_indices)
        batch_size = test_loader.batch_size
        train_loader = DataLoader(train_subset, batch_size=batch_size, shuffle=True, collate_fn=collate_cpd_batch)
        val_loader = DataLoader(val_subset, batch_size=batch_size, shuffle=False, collate_fn=collate_cpd_batch)
        model = CPDNet(input_dim=input_dim, hidden_dim=hidden_dim).to(device)
        if weight_path:
            print(f"  Loading weights from {weight_path}...")
            state = safe_torch_load(weight_path, map_location=device)
            if isinstance(state, dict):
                if 'model_state_dict' in state:
                    state = state['model_state_dict']
                elif 'state_dict' in state:
                    state = state['state_dict']
            model.load_state_dict(state)
        result = train_fold(fold_idx, k, model, train_loader, val_loader, test_loader, device, epochs=epochs, learning_rate=learning_rate, patience=patience, pos_weight=pos_weight, threshold=threshold, max_change_points=max_change_points, tolerance=tolerance)
        fold_weight_path = os.path.join(save_dir, f'cpd_fold_{fold_idx + 1}.pt')
        torch.save(model.state_dict(), fold_weight_path)
        print(f"  Saved fold {fold_idx + 1} weights to {fold_weight_path}")
        fold_results[fold_idx] = result
        all_fold_metrics.append(result['test_metrics'])
    return fold_results, all_fold_metrics


def plot_training_curves(fold_results, k, save_path=None, display_time=0):
    """Plot training/validation loss curves for each fold.

    If `save_path` is provided the figure will be saved to that path. If
    `display_time` > 0 the plot will be shown non-blocking for that many
    seconds and then closed automatically; otherwise the plot is shown
    normally (blocking) when `save_path` is None.
    """
    print(f"\n" + "="*60)
    print("Plotting training curves")
    print("="*60)
    fig, axes = plt.subplots(1, k, figsize=(5*k, 4))
    if k == 1:
        axes = [axes]
    for fold_idx, (_, result) in enumerate(fold_results.items()):
        ax = axes[fold_idx]
        ax.plot(result['train_losses'], label='Train Loss', linewidth=2)
        ax.plot(result['val_losses'], label='Val Loss', linewidth=2)
        ax.set_xlabel('Epoch')
        ax.set_ylabel('Loss')
        ax.set_title(f'Fold {fold_idx + 1}/{k}')
        ax.legend()
        ax.grid(True, alpha=0.3)
    plt.tight_layout()
    # Decide whether the current matplotlib backend supports interactive display.
    try:
        import matplotlib
        backend = matplotlib.get_backend().lower()
    except Exception:
        backend = ''

    interactive_backends = ('qt', 'tk', 'wx', 'gtk', 'macosx')
    is_interactive = any(b in backend for b in interactive_backends)

    # If interactive and display_time requested, show briefly first, then save.
    if display_time and display_time > 0 and is_interactive:
        plt.show(block=False)
        plt.pause(display_time)
        if save_path:
            os.makedirs(os.path.dirname(save_path), exist_ok=True)
            fig.savefig(save_path, dpi=200)
        plt.close(fig)
        return

    # Non-interactive or no display requested: save if requested, otherwise show/block.
    if save_path:
        os.makedirs(os.path.dirname(save_path), exist_ok=True)
        fig.savefig(save_path, dpi=200)
        plt.close(fig)
    else:
        plt.show()


def train_and_save_pipeline(full_train_dataset, test_loader, test_dataset, input_dim, device, k_folds, epochs, learning_rate, save_dir, early_stop_patience, pos_weight, threshold, hidden_dim, max_change_points, tolerance, weight_path=None, plots_dir=None, display_time=0):
    if weight_path:
        print(f"Continuing training from weights: {weight_path}")
    fold_results, all_fold_metrics = run_kfold_training(full_train_dataset, test_loader, test_dataset, input_dim, device, k=k_folds, epochs=epochs, learning_rate=learning_rate, save_dir=save_dir, weight_path=weight_path, patience=early_stop_patience, pos_weight=pos_weight, threshold=threshold, hidden_dim=hidden_dim, max_change_points=max_change_points, tolerance=tolerance)
    avg_iou, avg_precision, avg_recall, avg_f1, std_iou, std_precision = print_cv_summary(all_fold_metrics, k=k_folds)
    print(f"\nCross‑fold variability: IoU std = {std_iou:.4f}, Precision std = {std_precision:.4f}")
    # Save training curves to plots directory if provided
    if plots_dir:
        os.makedirs(plots_dir, exist_ok=True)
        plot_path = os.path.join(plots_dir, 'training_curves.png')
        plot_training_curves(fold_results, k=k_folds, save_path=plot_path, display_time=display_time)
    else:
        plot_training_curves(fold_results, k=k_folds, display_time=display_time)
    print("\n" + "="*60)
    print("Saving best model")
    print("="*60)
    best_fold_idx = np.argmax([m['f1_score'] for m in all_fold_metrics])
    best_model = fold_results[best_fold_idx]['model']
    best_model_path = os.path.join(save_dir, 'cpd_best_model.pt')
    torch.save(best_model.state_dict(), best_model_path)
    print(f"Best model (Fold {best_fold_idx + 1}, F1={all_fold_metrics[best_fold_idx]['f1_score']:.4f}) saved to {best_model_path}")
    # Fine-tune the best-fold model on the full training set (no validation split)
    try:
        print("\nFine-tuning best-fold weights on full training set...")
        final_model = CPDNet(input_dim=input_dim, hidden_dim=hidden_dim).to(device)
        state = safe_torch_load(best_model_path, map_location=device)
        if isinstance(state, dict):
            if 'model_state_dict' in state:
                state = state['model_state_dict']
            elif 'state_dict' in state:
                state = state['state_dict']
        # strip DataParallel prefix if present
        if isinstance(state, dict) and any(k.startswith('module.') for k in state.keys()):
            state = {k.replace('module.', ''): v for k, v in state.items()}
        final_model.load_state_dict(state)
        batch_size = test_loader.batch_size if test_loader is not None else 4
        full_loader = DataLoader(full_train_dataset, batch_size=batch_size, shuffle=True, collate_fn=collate_cpd_batch)
        optimizer = torch.optim.Adam(final_model.parameters(), lr=learning_rate)
        loss_fn = WeightedBCELoss(pos_weight=pos_weight)
        for epoch in range(1, epochs + 1):
            train_loss = train_epoch(final_model, full_loader, optimizer, loss_fn, device)
            if epoch % 10 == 0 or epoch == 1:
                print(f"  Fine-tune Epoch {epoch}/{epochs} - Train Loss: {train_loss:.4f}")
        final_model_path = os.path.join(save_dir, 'cpd_final_model.pt')
        torch.save(final_model.state_dict(), final_model_path)
        print(f"Fine-tuned final model saved to {final_model_path}")
    except Exception as e:
        print(f"Warning: fine-tuning on full dataset failed: {e}")
    return fold_results, all_fold_metrics
