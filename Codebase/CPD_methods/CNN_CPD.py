"""
1D CNN for Change Point Detection (CPD)
Learns to output high probability at change points, low elsewhere.

Architecture:
- 1D CNN backbone to extract temporal features
- Binary output head: P(change point at each timestep)
- Loss: Binary Cross-Entropy (handles imbalanced 0/1 labels)
- Inference: Threshold probabilities to extract CP indices

Key insight:
- Labels are binary per timestep: mostly 0s, few 1s at actual CPs
- In 500 timesteps with ~1-2 CPs, only 1-2 values are 1
- Model learns to detect where transitions happen
"""

import os
import shutil
import numpy as np
import pandas as pd
import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader
from sklearn.preprocessing import StandardScaler
from sklearn.model_selection import train_test_split
import matplotlib.pyplot as plt
from scipy.spatial.distance import euclidean


# ============================================================================
# 1. DATA SPLITTING
# ============================================================================

def split_data_into_train_test(source_dir, train_dir, test_dir, test_size=0.2, seed=42):
    """
    Split CSV files from source_dir into train and test directories.
    
    Args:
        source_dir (str): Path to directory with all CSV files
        train_dir (str): Path to output train directory
        test_dir (str): Path to output test directory
        test_size (float): Fraction of files for testing
        seed (int): Random seed for reproducibility
    """
    np.random.seed(seed)
    torch.manual_seed(seed)
    
    # Create directories
    os.makedirs(train_dir, exist_ok=True)
    os.makedirs(test_dir, exist_ok=True)
    
    # Get all CSV files
    all_files = [f for f in os.listdir(source_dir) if f.endswith('.csv')]
    print(f"Found {len(all_files)} CSV files")
    
    # Split
    train_files, test_files = train_test_split(all_files, test_size=test_size, random_state=seed)
    
    # Copy files
    for file in train_files:
        src = os.path.join(source_dir, file)
        dst = os.path.join(train_dir, file)
        shutil.copy(src, dst)
    
    for file in test_files:
        src = os.path.join(source_dir, file)
        dst = os.path.join(test_dir, file)
        shutil.copy(src, dst)
    
    print(f"Train files: {len(train_files)} in {train_dir}")
    print(f"Test files: {len(test_files)} in {test_dir}")
    
    return train_files, test_files


# ============================================================================
# 2. PREPROCESSING
# ============================================================================

def load_file(file_path):
    """Load CSV file as DataFrame."""
    return pd.read_csv(file_path)


def preprocess_cpd_data(df, target_length=500):
    """
    Preprocess data: keep all useful columns, remove degree duplicates.
    Interpolate to fixed target length to handle variable-length sequences.
    
    Args:
        df (pd.DataFrame): Raw data from CSV
        target_length (int): Target sequence length (via interpolation)
        
    Returns:
        np.ndarray: Preprocessed features (target_length, n_features)
        np.ndarray: State labels (target_length,)
        np.ndarray: Original indices
    """
    # Remove columns that are duplicates or not useful
    cols_to_drop = [
        'Timestamp', 'Frequency_Hz',  # metadata
        'Left_EE_PosX_m',  # noisy X component
        'Left_EE_VelX_mps',  # noisy X component
        'Left_J0_Abduction_deg', 'Left_J1_Rotation_deg', 'Left_J2_deg', 'Left_J3_deg',
        'Left_J4_ShoulderFlexion_deg', 'Left_J5_ElbowFlexion_deg',
        'Left_J6_PronoSupination_deg', 'Left_J7_WristFlexion_deg',  # degree duplicates
        'Left_J2_rad', 'Left_J2_vel_radps', 'Left_J2_deg',  # duplicate of J1
        'Left_J3_rad', 'Left_J3_vel_radps', 'Left_J3_deg',  # duplicate of J1
    ]
    
    # Extract state labels
    states = df['State'].values if 'State' in df.columns else None
    
    # Drop unnecessary columns
    df_clean = df.drop(columns=[c for c in cols_to_drop if c in df.columns])
    
    # Convert to numeric and fill NaN
    for col in df_clean.columns:
        df_clean[col] = pd.to_numeric(df_clean[col], errors='coerce')
    df_clean = df_clean.fillna(0)
    
    # Extract features (all remaining columns except State)
    feature_cols = [c for c in df_clean.columns if c != 'State']
    features = df_clean[feature_cols].values.astype(np.float32)
    
    # Interpolate to target length
    original_length = len(features)
    if original_length != target_length:
        # Create interpolation indices
        old_indices = np.linspace(0, original_length - 1, original_length)
        new_indices = np.linspace(0, original_length - 1, target_length)
        
        # Interpolate each feature dimension
        features_interp = np.zeros((target_length, features.shape[1]), dtype=np.float32)
        for col_idx in range(features.shape[1]):
            features_interp[:, col_idx] = np.interp(new_indices, old_indices, features[:, col_idx])
        
        features = features_interp
        
        # Interpolate states (nearest neighbor for categorical data)
        state_indices = np.round(np.interp(new_indices, old_indices, np.arange(original_length))).astype(int)
        states = states[state_indices] if states is not None else None
    
    # Normalize features
    scaler = StandardScaler()
    features = scaler.fit_transform(features)
    
    return features, states, np.arange(len(features))


def get_change_points_from_states(states):
    """
    Extract change points from state sequence.
    
    Args:
        states (np.ndarray): State labels
        
    Returns:
        list: Indices where state changes
    """
    if states is None:
        return []
    
    change_points = []
    for i in range(1, len(states)):
        if states[i] != states[i-1]:
            change_points.append(i)
    return change_points


# ============================================================================
# 3. DATA AUGMENTATION
# ============================================================================

class TemporalAugmenter:
    """Apply temporal and spatial augmentations to time series data."""
    
    def __init__(self, seed=42):
        np.random.seed(seed)
        self.seed = seed
    
    def temporal_jitter(self, X, std=0.01):
        """
        Add Gaussian noise to features.
        
        Args:
            X (np.ndarray): Features (n_samples, n_features)
            std (float): Standard deviation of noise
            
        Returns:
            np.ndarray: Augmented features
        """
        noise = np.random.normal(0, std, X.shape)
        return X + noise
    
    def temporal_scaling(self, X, scale_range=(0.9, 1.1)):
        """
        Scale features independently (simulates amplitude variations).
        
        Args:
            X (np.ndarray): Features (n_samples, n_features)
            scale_range (tuple): Min and max scale factors
            
        Returns:
            np.ndarray: Augmented features
        """
        scale_factor = np.random.uniform(*scale_range, size=(1, X.shape[1]))
        return X * scale_factor
    
    def temporal_shift(self, X, max_shift=5):
        """
        Shift time series by random amount (circular shift).
        
        Args:
            X (np.ndarray): Features (n_samples, n_features)
            max_shift (int): Maximum shift in samples
            
        Returns:
            np.ndarray: Augmented features
        """
        shift = np.random.randint(-max_shift, max_shift + 1)
        return np.roll(X, shift, axis=0)
    
    def temporal_warping(self, X, warp_range=(0.8, 1.2)):
        """
        Resample time series (speed up/slow down motion).
        
        Args:
            X (np.ndarray): Features (n_samples, n_features)
            warp_range (tuple): Speed multiplier range
            
        Returns:
            np.ndarray: Warped features (may change length slightly)
        """
        speed = np.random.uniform(*warp_range)
        # Use simple interpolation for warping
        indices = np.arange(len(X))
        new_indices = np.linspace(0, len(X) - 1, int(len(X) / speed))
        
        augmented = np.zeros((len(new_indices), X.shape[1]))
        for feat_idx in range(X.shape[1]):
            augmented[:, feat_idx] = np.interp(new_indices, indices, X[:, feat_idx])
        
        # Pad or truncate to original length
        if len(augmented) < len(X):
            augmented = np.vstack([augmented, np.zeros((len(X) - len(augmented), X.shape[1]))])
        else:
            augmented = augmented[:len(X)]
        
        return augmented
    
    def frame_skipping(self, X, skip_prob=0.05):
        """
        Randomly skip (remove) frames with probability skip_prob.
        
        Args:
            X (np.ndarray): Features (n_samples, n_features)
            skip_prob (float): Probability of skipping each frame
            
        Returns:
            np.ndarray: Data with some frames skipped (resampled to original length)
        """
        mask = np.random.random(len(X)) > skip_prob
        if mask.sum() < 2:  # Need at least 2 points for interpolation
            return X
        
        kept_indices = np.where(mask)[0]
        X_skipped = X[kept_indices]
        
        # Resample to original length
        indices = np.linspace(0, len(X_skipped) - 1, len(X))
        X_resampled = np.zeros_like(X)
        
        for feat_idx in range(X.shape[1]):
            X_resampled[:, feat_idx] = np.interp(indices, np.arange(len(X_skipped)), X_skipped[:, feat_idx])
        
        return X_resampled
    
    def augment(self, X, augmentation_types=None):
        """
        Apply random augmentations.
        
        Args:
            X (np.ndarray): Features
            augmentation_types (list): Types to apply. Default: all
            
        Returns:
            np.ndarray: Augmented features
        """
        if augmentation_types is None:
            augmentation_types = ['jitter', 'scaling', 'shift', 'warping', 'skipping']
        
        X_aug = X.copy()
        
        for aug_type in augmentation_types:
            if np.random.random() > 0.5:  # Apply with 50% probability
                if aug_type == 'jitter':
                    X_aug = self.temporal_jitter(X_aug)
                elif aug_type == 'scaling':
                    X_aug = self.temporal_scaling(X_aug)
                elif aug_type == 'shift':
                    X_aug = self.temporal_shift(X_aug)
                elif aug_type == 'warping':
                    X_aug = self.temporal_warping(X_aug)
                elif aug_type == 'skipping':
                    X_aug = self.frame_skipping(X_aug)
        
        return X_aug


# ============================================================================
# 4. CUSTOM COLLATE FUNCTION FOR DATALOADERS
# ============================================================================

def collate_cpd_batch(batch):
    """
    Custom collate function to handle batching.
    
    Batches tensors normally but keeps states as a list of lists.
    
    Args:
        batch (list): List of (features, cp_labels, states) tuples
        
    Returns:
        Tuple of (features_batch, cp_labels_batch, states_list)
        - features_batch: Tensor (batch_size, n_features, seq_len) - ready for Conv1d
        - cp_labels_batch: Tensor (batch_size, seq_len)
        - states_list: List of state label lists
    """
    features_list = [item[0] for item in batch]
    cp_labels_list = [item[1] for item in batch]
    states_list = [item[2] for item in batch]
    
    # Stack tensors: features come in as (seq_len, n_features), stack to (batch, seq_len, n_features)
    features_batch = torch.stack(features_list, dim=0)  # (batch, seq_len, n_features)
    # Transpose to (batch, n_features, seq_len) for Conv1d
    features_batch = features_batch.transpose(1, 2)
    
    cp_labels_batch = torch.stack(cp_labels_list, dim=0)  # (batch, seq_len)
    
    return features_batch, cp_labels_batch, states_list


# ============================================================================
# 4. PYTORCH DATASET
# ============================================================================

class CPDDataset(Dataset):
    """
    Dataset for change point detection.
    Returns time series and corresponding change point labels.
    """
    
    def __init__(self, file_list, data_dir, augment=False, target_length=500):
        """
        Args:
            file_list (list): List of CSV filenames
            data_dir (str): Directory containing CSV files
            augment (bool): Apply augmentations
            target_length (int): Target sequence length (sequences interpolated to this length)
        """
        self.file_list = file_list
        self.data_dir = data_dir
        self.augment = augment
        self.augmenter = TemporalAugmenter()
        self.target_length = target_length
        
        # Load all data
        self.samples = []
        for file in file_list:
            file_path = os.path.join(data_dir, file)
            df = load_file(file_path)
            features, states, _ = preprocess_cpd_data(df, target_length=target_length)
            change_points = get_change_points_from_states(states)
            
            self.samples.append({
                'features': features,
                'states': states,
                'change_points': change_points,
                'file': file
            })
    
    def __len__(self):
        return len(self.samples)
    
    def __getitem__(self, idx):
        """
        Returns:
            features (torch.Tensor): Time series (n_samples, n_features)
            cp_labels (torch.Tensor): Binary labels (n_samples,) 
                - 1 at change point indices
                - 0 elsewhere
                - Typically only 1-2 ones in 500 timesteps
            states (list): State labels (for reference, not batched)
        """
        sample = self.samples[idx]
        features = sample['features'].copy()
        
        # Apply augmentation
        if self.augment:
            features = self.augmenter.augment(features)
        
        # Create binary CP labels: 1 at change points, 0 elsewhere
        cp_labels = np.zeros(len(features), dtype=np.float32)
        for cp in sample['change_points']:
            if cp < len(cp_labels):
                cp_labels[cp] = 1.0  # Mark the exact sample where state changes
        
        # Return states as list (not converted to tensor) - will be handled separately in collate
        return (
            torch.from_numpy(features).float(),
            torch.from_numpy(cp_labels).float(),
            sample['states'].tolist()  # Convert numpy array to list for safe unpacking
        )


class SubsetDataset(Dataset):
    """
    Wrapper for creating subsets of a dataset by indices.
    Used for k-fold cross-validation.
    """
    
    def __init__(self, dataset, indices):
        """
        Args:
            dataset (Dataset): Base dataset
            indices (list): Indices to include in subset
        """
        self.dataset = dataset
        self.indices = indices
    
    def __len__(self):
        return len(self.indices)
    
    def __getitem__(self, idx):
        return self.dataset[self.indices[idx]]


# ============================================================================
# 5. CNN MODEL FOR CHANGE POINT DETECTION
# ============================================================================

class CNN1DBackbone(nn.Module):
    """1D CNN backbone for temporal feature extraction."""
    
    def __init__(self, input_dim=13, hidden_dim=64):
        super().__init__()
        
        self.conv1 = nn.Conv1d(input_dim, hidden_dim, kernel_size=5, padding=2)
        self.bn1 = nn.BatchNorm1d(hidden_dim)
        
        self.conv2 = nn.Conv1d(hidden_dim, hidden_dim * 2, kernel_size=5, padding=2)
        self.bn2 = nn.BatchNorm1d(hidden_dim * 2)
        
        self.conv3 = nn.Conv1d(hidden_dim * 2, hidden_dim, kernel_size=5, padding=2)
        self.bn3 = nn.BatchNorm1d(hidden_dim)
        
        self.relu = nn.ReLU()
    
    def forward(self, x):
        """
        Args:
            x (torch.Tensor): (batch, n_features, seq_len)
            
        Returns:
            torch.Tensor: Features (batch, hidden_dim, seq_len)
        """
        x = self.relu(self.bn1(self.conv1(x)))
        x = self.relu(self.bn2(self.conv2(x)))
        x = self.relu(self.bn3(self.conv3(x)))
        return x


class CPDNet(nn.Module):
    """
    CNN-based Change Point Detection Network.
    
    Input: Time series (batch, n_features, seq_len)
    Output: Per-timestep probability of change point (batch, seq_len)
    
    The model learns: P(change point at timestep t)
    """
    
    def __init__(self, input_dim=13, hidden_dim=64):
        super().__init__()
        
        self.backbone = CNN1DBackbone(input_dim, hidden_dim)
        
        # Output layer: predict logit at each timestep
        self.cp_head = nn.Conv1d(hidden_dim, 1, kernel_size=1)
    
    def forward(self, x):
        """
        Args:
            x (torch.Tensor): Time series (batch, n_features, seq_len)
            
        Returns:
            torch.Tensor: CP logits (batch, seq_len)
        """
        features = self.backbone(x)  # (batch, hidden_dim, seq_len)
        logits = self.cp_head(features)  # (batch, 1, seq_len)
        return logits.squeeze(1)  # (batch, seq_len)
    
    def predict_change_points(self, x, threshold=0.5, max_change_points=1, return_scores=False):
        """
        Predict change point indices from probabilities.
        
        Args:
            x (torch.Tensor): Time series (batch, n_features, seq_len)
            threshold (float): Probability threshold for detection
            max_change_points (int | None):
                - int: return top-k CP indices per sample
                - None: use thresholding (variable number of CPs)
            return_scores (bool): If True, also return probability score(s)
                of the selected CP indices for each sample
            
        Returns:
            list or tuple:
                - predicted_cps (list): CP indices per sample
                - (optional) predicted_scores (list): probability scores at selected CPs
        """
        logits = self.forward(x)  # (batch, seq_len)
        probs = torch.sigmoid(logits)
        
        predicted_cps = []
        predicted_scores = []
        for batch_idx in range(probs.shape[0]):
            if max_change_points is None:
                # Variable-number detection via threshold
                cp_indices = torch.where(probs[batch_idx] > threshold)[0].cpu().numpy().tolist()
            else:
                # Fixed-number detection via top-k
                k = max(1, min(int(max_change_points), probs.shape[1]))
                topk = torch.topk(probs[batch_idx], k=k).indices.cpu().numpy().tolist()
                cp_indices = sorted(int(i) for i in topk)
            predicted_cps.append(cp_indices)

            if return_scores:
                if len(cp_indices) == 0:
                    predicted_scores.append([])
                else:
                    cp_scores = probs[batch_idx, cp_indices].detach().cpu().numpy().tolist()
                    predicted_scores.append([float(s) for s in cp_scores])

        if return_scores:
            return predicted_cps, predicted_scores
        return predicted_cps



# ============================================================================
# 6. LOSS FUNCTION - BINARY CROSS ENTROPY WITH CLASS WEIGHTING
# ============================================================================

class WeightedBCELoss(nn.Module):
    """
    Binary Cross-Entropy with class weighting to handle imbalance.
    
    Since ~99% of samples are 0 (no CP) and ~1% are 1 (CP),
    we weight the positive class more heavily.
    """
    
    def __init__(self, pos_weight=10.0):
        super().__init__()
        self.register_buffer('pos_weight', torch.tensor(float(pos_weight), dtype=torch.float32))
    
    def forward(self, logits, targets):
        """
        Args:
            logits (torch.Tensor): Predicted logits (batch, seq_len)
            targets (torch.Tensor): Target binary labels (batch, seq_len)
                - 0 = no change point
                - 1 = change point
                
        Returns:
            torch.Tensor: Loss value
        """
        return nn.functional.binary_cross_entropy_with_logits(
            logits,
            targets,
            pos_weight=self.pos_weight.to(logits.device)
        )


# ============================================================================
# 7. TRAINING LOOP
# ============================================================================

def train_epoch(model, dataloader, optimizer, loss_fn, device):
    """
    Train for one epoch.
    
    Args:
        model (nn.Module): CPD network
        dataloader (DataLoader): Training data
        optimizer (Optimizer): Optimizer
        loss_fn (nn.Module): Loss function
        device (torch.device): CPU or GPU
        
    Returns:
        float: Average loss
    """
    model.train()
    total_loss = 0.0
    
    for batch_idx, (features, cp_labels, _) in enumerate(dataloader):
        features = features.to(device)  # (batch, n_features, seq_len)
        cp_labels = cp_labels.to(device)  # (batch, seq_len)
        
        # Forward pass
        logits = model(features)  # Already in (batch, n_features, seq_len) from collate
        
        # Compute loss
        loss = loss_fn(logits, cp_labels)
        
        # Backward
        optimizer.zero_grad()
        loss.backward()
        optimizer.step()
        
        total_loss += loss.item()
        
        if (batch_idx + 1) % 5 == 0:
            print(f"  Batch {batch_idx + 1}/{len(dataloader)}, Loss: {loss.item():.4f}")
    
    return total_loss / len(dataloader)


def validate(model, dataloader, loss_fn, device):
    """
    Validate model.
    
    Args:
        model (nn.Module): CPD network
        dataloader (DataLoader): Validation data
        loss_fn (nn.Module): Loss function
        device (torch.device): CPU or GPU
        
    Returns:
        float: Average loss
    """
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


# ============================================================================
# 8. EVALUATION - DETECTION-BASED METRICS
# ============================================================================

def evaluate_detection(model, dataloader, device, threshold=0.5, tolerance=10, max_change_points=1):
    """
    Evaluate change point detection (not per-sample classification).
    
    Matches predicted CPs to true CPs with tolerance.
    
    Args:
        model (nn.Module): Trained model
        dataloader (DataLoader): Test data
        device (torch.device): CPU or GPU
        threshold (float): Probability threshold for CP detection
        tolerance (int): Tolerance in samples for matching CPs
        
    Returns:
        dict: Detection metrics (precision, recall, F1, TP, FP, FN)
    """
    model.eval()
    
    all_tp = 0
    all_fp = 0
    all_fn = 0
    
    with torch.no_grad():
        for features, cp_labels, _ in dataloader:
            features = features.to(device)
            
            # Get predicted CP indices
            predicted_cps_list = model.predict_change_points(
                features,
                threshold=threshold,
                max_change_points=max_change_points
            )
            
            # Get true CP indices from labels
            cp_labels_np = cp_labels.cpu().numpy()
            true_cps_list = []
            for batch_idx in range(cp_labels_np.shape[0]):
                true_cp_indices = np.where(cp_labels_np[batch_idx] == 1)[0].tolist()
                true_cps_list.append(true_cp_indices)
            
            # Match predictions to true CPs
            for true_cps, pred_cps in zip(true_cps_list, predicted_cps_list):
                true_cps = np.array(true_cps)
                pred_cps = np.array(pred_cps)
                
                matched_pred = set()
                matched_true = set()
                
                # Greedy matching: for each predicted CP, find nearest true CP
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
                
                # Unmatched true CPs are false negatives
                all_fn += len(true_cps) - len(matched_true)
    
    # Compute metrics
    precision = all_tp / (all_tp + all_fp) if (all_tp + all_fp) > 0 else 0.0
    recall = all_tp / (all_tp + all_fn) if (all_tp + all_fn) > 0 else 0.0
    f1 = 2 * (precision * recall) / (precision + recall) if (precision + recall) > 0 else 0.0
    
    return {
        'precision': precision,
        'recall': recall,
        'f1_score': f1,
        'true_positives': all_tp,
        'false_positives': all_fp,
        'false_negatives': all_fn,
        'tolerance': tolerance,
    }


def _ordered_states_from_labels(labels):
    """Extract unique states in order of first appearance."""
    seen = set()
    ordered = []
    for label in labels:
        if label not in seen:
            ordered.append(label)
            seen.add(label)
    return ordered


def _labels_from_change_points(seq_len, change_points, ordered_states):
    """
    Generate state labels from change point indices.
    
    Extra CPs beyond expected states are marked as 'UNKNOWN' to properly penalize over-segmentation.
    
    Args:
        seq_len (int): Length of sequence
        change_points (list): Indices of change points
        ordered_states (list): States in order of appearance
        
    Returns:
        np.ndarray: State labels for each timestep
    """
    if len(ordered_states) == 0:
        return np.array(['IDLE'] * seq_len)
    
    labels = np.array(['UNKNOWN'] * seq_len, dtype=object)
    change_points = sorted(change_points)
    
    # Number of expected CPs = number of states - 1
    # E.g., 2 states (GOING, RETURNING) = 1 expected CP
    expected_num_cps = len(ordered_states) - 1
    
    if len(change_points) == 0:
        # No change points: assign first state to all
        labels[:] = ordered_states[0]
    else:
        # Only use CPs up to expected number
        valid_cps = change_points[:expected_num_cps]
        
        # Assign states based on valid change point boundaries
        state_idx = 0
        
        # Before first CP or if no CPs: first state
        if len(valid_cps) == 0:
            labels[:] = ordered_states[0]
        else:
            # Assign first state before first CP
            labels[:valid_cps[0]] = ordered_states[0]
            
            # Assign intermediate states
            for i in range(len(valid_cps)):
                state_idx = i + 1
                if state_idx < len(ordered_states):
                    if i < len(valid_cps) - 1:
                        # Between two CPs
                        labels[valid_cps[i]:valid_cps[i+1]] = ordered_states[state_idx]
                    else:
                        # After last valid CP: assign last state only if no over-segmentation
                        if len(change_points) <= expected_num_cps:
                            labels[valid_cps[i]:] = ordered_states[state_idx]
                        else:
                            # Over-segmentation: mark region after extra CPs as UNKNOWN
                            # Last valid state from last valid CP to first extra CP
                            first_extra_cp = change_points[expected_num_cps]
                            labels[valid_cps[i]:first_extra_cp] = ordered_states[state_idx]
                            # Everything after stays UNKNOWN
    
    return labels


def evaluate_iou_overlap(model, dataloader, device, threshold=0.5, max_change_points=1, tolerance=10):
    """
    Evaluate change point detection using IoU (Intersection over Union) overlap metrics.
    
    Computes per-state IoU by comparing predicted segments (regions between CPs)
    with true segments based on state labels.
    
    Args:
        model (nn.Module): Trained model
        dataloader (DataLoader): Test data
        device (torch.device): CPU or GPU
        threshold (float): Probability threshold for CP detection
        
    Returns:
        dict: Overlap metrics (average_iou, per_state_iou, precision, recall, f1)
    """
    model.eval()
    
    all_ious = []
    all_precisions = []
    all_recalls = []
    all_f1s = []
    per_state_ious = {'GOING': [], 'RETURNING': []}
    
    # Track CP detection statistics
    num_predicted_cps = []
    num_true_cps = []
    selected_cp_probs = []
    
    with torch.no_grad():
        for features, cp_labels, states in dataloader:
            features = features.to(device)
            # states is now a list of lists (from collate_cpd_batch)
            # Convert to numpy array format for processing
            states_list = states  # Already a list of state label lists
            seq_len = features.shape[2]  # (batch, n_features, seq_len)
            
            # Get predicted CP indices
            predicted_cps_list, predicted_scores_list = model.predict_change_points(
                features,
                threshold=threshold,
                max_change_points=max_change_points,
                return_scores=True
            )
            
            # Get true CP indices from labels
            cp_labels_np = cp_labels.cpu().numpy()
            true_cps_list = []
            for batch_idx in range(cp_labels_np.shape[0]):
                true_cp_indices = np.where(cp_labels_np[batch_idx] == 1)[0].tolist()
                true_cps_list.append(true_cp_indices)
            
            # Compute IoU for each sample
            for sample_idx, (true_cps, pred_cps) in enumerate(zip(true_cps_list, predicted_cps_list)):
                true_states = np.array(states_list[sample_idx])  # Convert back to numpy
                
                # Track CP counts
                num_predicted_cps.append(len(pred_cps))
                num_true_cps.append(len(true_cps))
                sample_scores = predicted_scores_list[sample_idx]
                if len(sample_scores) > 0:
                    selected_cp_probs.append(float(np.max(sample_scores)))
                
                # Create predicted state labels from predicted CPs
                pred_labels = _labels_from_change_points(seq_len, pred_cps, 
                                                         _ordered_states_from_labels(true_states))
                
                # Compute per-state IoU (only for expected states, excluding UNKNOWN)
                sample_ious = []
                for state in ['GOING', 'RETURNING']:
                    true_mask = (true_states == state)
                    pred_mask = (pred_labels == state)
                    
                    # Union and intersection
                    union = np.sum(true_mask | pred_mask)
                    intersection = np.sum(true_mask & pred_mask)
                    iou = (intersection / union) if union > 0 else 0.0
                    sample_ious.append(iou)
                    per_state_ious[state].append(iou)
                
                # Penalty for over-segmentation: count UNKNOWN predictions as error
                unknown_count = np.sum(pred_labels == 'UNKNOWN')
                over_seg_penalty = unknown_count / seq_len if seq_len > 0 else 0.0
                
                # Average IoU for this sample (penalized by over-segmentation)
                sample_avg_iou = np.mean(sample_ious) * (1.0 - over_seg_penalty) if sample_ious else 0.0
                all_ious.append(sample_avg_iou)
                
                # Also compute detection metrics (TP/FP/FN) for reference
                true_cps = np.array(true_cps)
                pred_cps = np.array(pred_cps)
                
                if len(pred_cps) == 0 and len(true_cps) == 0:
                    tp, fp, fn = 0, 0, 0
                elif len(pred_cps) == 0:
                    tp, fp, fn = 0, 0, len(true_cps)
                elif len(true_cps) == 0:
                    tp, fp, fn = 0, len(pred_cps), 0
                else:
                    matched_pred = set()
                    matched_true = set()
                    
                    # Greedy matching with configurable tolerance window
                    for pred_idx in pred_cps:
                        distances = np.abs(true_cps - pred_idx)
                        nearest_true_idx = np.argmin(distances)
                        
                        if distances[nearest_true_idx] <= tolerance and nearest_true_idx not in matched_true:
                            matched_pred.add(pred_idx)
                            matched_true.add(nearest_true_idx)
                    
                    tp = len(matched_true)
                    fp = len(pred_cps) - tp
                    fn = len(true_cps) - tp
                
                # Detection metrics for this sample
                precision = tp / (tp + fp) if (tp + fp) > 0 else 0.0
                recall = tp / (tp + fn) if (tp + fn) > 0 else 0.0
                f1 = 2 * (precision * recall) / (precision + recall) if (precision + recall) > 0 else 0.0
                
                all_precisions.append(precision)
                all_recalls.append(recall)
                all_f1s.append(f1)
    
    # Aggregate metrics
    avg_iou = float(np.mean(all_ious)) if all_ious else 0.0
    avg_precision = float(np.mean(all_precisions)) if all_precisions else 0.0
    avg_recall = float(np.mean(all_recalls)) if all_recalls else 0.0
    avg_f1 = float(np.mean(all_f1s)) if all_f1s else 0.0
    
    per_state_summary = {}
    for state in ['GOING', 'RETURNING']:
        per_state_summary[state] = float(np.mean(per_state_ious[state])) if per_state_ious[state] else 0.0
    
    # CP detection statistics
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
    }
    
    return {
        'average_iou': avg_iou,
        'per_state_iou': per_state_summary,
        'precision': avg_precision,
        'recall': avg_recall,
        'f1_score': avg_f1,
        'cp_stats': cp_stats
    }


# ============================================================================
# 9. K-FOLD CROSS-VALIDATION UTILITIES
# ============================================================================

def create_kfolds(n_samples, k=5):
    """
    Create k-fold indices for cross-validation.
    
    Args:
        n_samples (int): Total number of samples
        k (int): Number of folds
        
    Returns:
        list: List of (train_indices, val_indices) tuples for each fold
    """
    indices = np.arange(n_samples)
    fold_size = n_samples // k
    folds = []
    
    for fold_idx in range(k):
        val_start = fold_idx * fold_size
        val_end = val_start + fold_size if fold_idx < k - 1 else n_samples
        
        val_indices = indices[val_start:val_end].tolist()
        train_indices = np.concatenate([
            indices[:val_start],
            indices[val_end:]
        ]).tolist()
        
        folds.append((train_indices, val_indices))
    
    return folds


# ============================================================================
# 10. HELPER FUNCTIONS FOR MAIN EXECUTION
# ============================================================================

def split_and_load_data(data_dir, train_dir, test_dir, test_size=0.2, target_length=500):
    """
    Split data into train and test directories, then load into datasets.
    
    Args:
        data_dir (str): Source data directory
        train_dir (str): Train output directory
        test_dir (str): Test output directory
        test_size (float): Test fraction
        target_length (int): Target sequence length (interpolated)
        
    Returns:
        tuple: (full_train_dataset, test_dataset, train_files, test_files, input_dim)
    """
    # Split if directories don't exist
    if not os.path.exists(train_dir) or not os.path.exists(test_dir):
        print("Creating train/test split...")
        train_files, test_files = split_data_into_train_test(
            data_dir, train_dir, test_dir, test_size=test_size, seed=42
        )
    else:
        print("Train/test directories already exist. Using existing split.")
        train_files = sorted([f for f in os.listdir(train_dir) if f.endswith('.csv')])
        test_files = sorted([f for f in os.listdir(test_dir) if f.endswith('.csv')])
        print(f"Train files: {len(train_files)} in {train_dir}")
        print(f"Test files: {len(test_files)} in {test_dir}")
    
    # Load datasets
    full_train_dataset = CPDDataset(train_files, train_dir, augment=True, target_length=target_length)
    test_dataset = CPDDataset(test_files, test_dir, augment=False, target_length=target_length)
    
    # Get input dimension
    sample_features, _, _ = full_train_dataset[0]
    input_dim = sample_features.shape[1]
    
    return full_train_dataset, test_dataset, train_files, test_files, input_dim


def train_fold(fold_idx, k, model, train_loader, val_loader, test_loader, device, 
               epochs=50, learning_rate=0.001, patience=15, pos_weight=10.0, threshold=0.5,
               max_change_points=1, tolerance=10):
    """
    Train a single fold.
    
    Args:
        fold_idx (int): Fold index
        k (int): Total number of folds
        model (nn.Module): Model to train
        train_loader (DataLoader): Training data
        val_loader (DataLoader): Validation data
        test_loader (DataLoader): Test data
        device (torch.device): CPU or GPU
        epochs (int): Number of epochs
        learning_rate (float): Learning rate
        patience (int): Early stopping patience
        pos_weight (float): Positive class weight for BCE loss
        threshold (float): CP detection threshold for evaluation
        
    Returns:
        dict: Results with losses and metrics
    """
    print(f"\n" + "#" * 60)
    print(f"FOLD {fold_idx + 1}/{k}")
    print("#" * 60)
    print(f"Train size: {len(train_loader.dataset)}, Val size: {len(val_loader.dataset)}")
    
    optimizer = optim.Adam(model.parameters(), lr=learning_rate)
    loss_fn = WeightedBCELoss(pos_weight=pos_weight)
    
    train_losses = []
    val_losses = []
    best_val_loss = float('inf')
    patience_counter = 0
    
    print(f"Training Fold {fold_idx + 1}...")
    
    for epoch in range(1, epochs + 1):
        train_loss = train_epoch(model, train_loader, optimizer, loss_fn, device)
        val_loss = validate(model, val_loader, loss_fn, device)
        
        train_losses.append(train_loss)
        val_losses.append(val_loss)
        
        if (epoch % 10 == 0) or (epoch == 1):
            print(f"  Epoch {epoch}/{epochs} - Train Loss: {train_loss:.4f}, Val Loss: {val_loss:.4f}")
        
        # Early stopping
        if val_loss < best_val_loss:
            best_val_loss = val_loss
            patience_counter = 0
        else:
            patience_counter += 1
        
        if patience_counter >= patience:
            print(f"  Early stopping at epoch {epoch}")
            break
    
    # Evaluate
    val_metrics = evaluate_iou_overlap(
        model,
        val_loader,
        device,
        threshold=threshold,
        max_change_points=max_change_points,
        tolerance=tolerance
    )
    test_metrics = evaluate_iou_overlap(
        model,
        test_loader,
        device,
        threshold=threshold,
        max_change_points=max_change_points,
        tolerance=tolerance
    )
    
    print(f"\nFold {fold_idx + 1} - Validation Metrics:")
    print(f"  IoU: {val_metrics['average_iou']:.4f}, Per-state: GOING={val_metrics['per_state_iou']['GOING']:.4f}, RETURNING={val_metrics['per_state_iou']['RETURNING']:.4f}")
    print(f"  Detection (P/R/F1): {val_metrics['precision']:.4f} / {val_metrics['recall']:.4f} / {val_metrics['f1_score']:.4f}")
    
    print(f"Fold {fold_idx + 1} - Test Metrics:")
    print(f"  IoU: {test_metrics['average_iou']:.4f}, Per-state: GOING={test_metrics['per_state_iou']['GOING']:.4f}, RETURNING={test_metrics['per_state_iou']['RETURNING']:.4f}")
    print(f"  Detection (P/R/F1): {test_metrics['precision']:.4f} / {test_metrics['recall']:.4f} / {test_metrics['f1_score']:.4f}")
    
    return {
        'train_losses': train_losses,
        'val_losses': val_losses,
        'val_metrics': val_metrics,
        'test_metrics': test_metrics,
        'model': model
    }


def run_kfold_training(full_train_dataset, test_loader, test_dataset, input_dim, device, 
                       k=5, epochs=50, learning_rate=0.001, save_dir='./saved_weights',
                       weight_path=None, patience=15, pos_weight=10.0, threshold=0.5,
                       hidden_dim=64, max_change_points=1, tolerance=10):
    """
    Run complete k-fold cross-validation training pipeline.
    
    Args:
        full_train_dataset (Dataset): Full training dataset
        test_loader (DataLoader): Test loader
        test_dataset (Dataset): Test dataset
        input_dim (int): Input dimension
        device (torch.device): CPU or GPU
        k (int): Number of folds
        epochs (int): Epochs per fold
        learning_rate (float): Learning rate
        save_dir (str): Directory to save weights
        weight_path (str): Path to load weights from (None = train from scratch)
        patience (int): Early stopping patience
        pos_weight (float): Positive class weight for BCE loss
        threshold (float): CP detection threshold for evaluation
        hidden_dim (int): Hidden dimension for CNN
        
    Returns:
        tuple: (fold_results, all_fold_metrics)
    """
    # Create save directory
    os.makedirs(save_dir, exist_ok=True)
    
    # Print configuration
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
    
    # Create folds
    print("\n" + "="*60)
    print("K-Fold Setup")
    print("="*60)
    folds = create_kfolds(len(full_train_dataset), k=k)
    print(f"Number of folds: {k}")
    for fold_idx, (train_idx, val_idx) in enumerate(folds):
        print(f"  Fold {fold_idx + 1}: {len(train_idx)} train, {len(val_idx)} val")
    
    # Run k-fold training
    print("\n" + "="*60)
    print("K-Fold Training")
    print("="*60)
    
    fold_results = {}
    all_fold_metrics = []
    
    for fold_idx, (train_indices, val_indices) in enumerate(folds):
        # Create train/val subsets
        train_subset = SubsetDataset(full_train_dataset, train_indices)
        val_subset = SubsetDataset(full_train_dataset, val_indices)
        
        # Use batch size from test_loader for consistency
        batch_size = test_loader.batch_size
        train_loader = DataLoader(train_subset, batch_size=batch_size, shuffle=True, collate_fn=collate_cpd_batch)
        val_loader = DataLoader(val_subset, batch_size=batch_size, shuffle=False, collate_fn=collate_cpd_batch)
        
        # Create model
        model = CPDNet(input_dim=input_dim, hidden_dim=hidden_dim).to(device)
        
        # Load weights if provided
        if weight_path:
            print(f"  Loading weights from {weight_path}...")
            model.load_state_dict(torch.load(weight_path, map_location=device))
        
        # Train fold
        result = train_fold(fold_idx, k, model, train_loader, val_loader, test_loader, 
                           device, epochs=epochs, learning_rate=learning_rate, 
                           patience=patience, pos_weight=pos_weight, threshold=threshold,
                           max_change_points=max_change_points, tolerance=tolerance)
        
        # Save fold weights
        fold_weight_path = os.path.join(save_dir, f'cpd_fold_{fold_idx + 1}.pt')
        torch.save(model.state_dict(), fold_weight_path)
        print(f"  Saved fold {fold_idx + 1} weights to {fold_weight_path}")
        
        fold_results[fold_idx] = result
        all_fold_metrics.append(result['test_metrics'])
    
    return fold_results, all_fold_metrics


def plot_training_curves(fold_results, k):
    """
    Plot training curves for all folds.
    
    Args:
        fold_results (dict): Results from all folds
        k (int): Number of folds
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
    plt.show()


def plot_best_worst_segmentation(model, dataset, data_dir, device,
                                 threshold=0.2, max_change_points=1,
                                 hz=50, plot_cols=None):
    """
    Plot best and worst segmentation samples based on per-sample IoU.

    Style is aligned with PELT plotting: 2 rows (best/worst) x 3 signals,
    with true and predicted CP vertical markers.

    Args:
        model (nn.Module): Trained CPD model
        dataset (CPDDataset): Dataset to evaluate (typically test_dataset)
        data_dir (str): Directory where the original CSV files are located
        device (torch.device): CPU/GPU device
        threshold (float): Threshold used when max_change_points=None
        max_change_points (int | None): Fixed number of CPs or None for threshold mode
        hz (int): Sampling rate for x-axis conversion (samples -> ms)
        plot_cols (list | None): Preferred columns to plot from raw CSV
    """
    if plot_cols is None:
        plot_cols = ['Left_EE_PosZ_m', 'Left_EE_PosY_m', 'Left_J5_ElbowFlexion_rad']

    if len(dataset) == 0:
        print("Dataset is empty; cannot plot best/worst segmentation.")
        return

    model.eval()
    sample_infos = []

    with torch.no_grad():
        for sample in dataset.samples:
            features_np = sample['features']  # (seq_len, n_features)
            true_states = np.asarray(sample['states'])
            true_cps = list(sample['change_points'])
            seq_len = features_np.shape[0]

            x = torch.from_numpy(features_np).float().unsqueeze(0).transpose(1, 2).to(device)
            pred_cps_list = model.predict_change_points(
                x,
                threshold=threshold,
                max_change_points=max_change_points,
            )

            pred_cps = pred_cps_list[0]

            # Delay metrics (pred - true), in samples and ms
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

            # Per-state IoU
            sample_ious = []
            for state in ['GOING', 'RETURNING']:
                true_mask = (true_states == state)
                pred_mask = (pred_labels == state)
                union = np.sum(true_mask | pred_mask)
                inter = np.sum(true_mask & pred_mask)
                iou = (inter / union) if union > 0 else 0.0
                sample_ious.append(iou)

            # Over-segmentation penalty
            unknown_count = np.sum(pred_labels == 'UNKNOWN')
            over_seg_penalty = unknown_count / seq_len if seq_len > 0 else 0.0
            sample_iou = float(np.mean(sample_ious) * (1.0 - over_seg_penalty)) if sample_ious else 0.0

            sample_infos.append({
                'file': sample['file'],
                'seq_len': seq_len,
                'true_cps': true_cps,
                'pred_cps': pred_cps,
                'iou': sample_iou,
                'avg_abs_delay_ms': avg_abs_delay_ms,
                'mean_signed_delay_ms': mean_signed_delay_ms,
            })

    best = max(sample_infos, key=lambda s: s['iou'])
    worst = min(sample_infos, key=lambda s: s['iou'])

    def _fmt_delay(v):
        return f"{v:.1f} ms" if not np.isnan(v) else "N/A"

    print("\n" + "=" * 60)
    print(f"BEST segmentation:  {best['file']} (IoU={best['iou']:.4f}, avg |delay|={_fmt_delay(best['avg_abs_delay_ms'])})")
    print(f"WORST segmentation: {worst['file']} (IoU={worst['iou']:.4f}, avg |delay|={_fmt_delay(worst['avg_abs_delay_ms'])})")
    print("=" * 60)

    fig, axes = plt.subplots(2, 3, figsize=(18, 8))
    fig.suptitle('Best vs Worst Segmentation by IoU', fontsize=14, fontweight='bold')

    sample_period_ms = 1000.0 / float(hz)
    plot_pairs = [(best, 'BEST'), (worst, 'WORST')]

    for row_idx, (info, prefix) in enumerate(plot_pairs):
        file_path = os.path.join(data_dir, info['file'])
        df = load_file(file_path)

        # Build exactly 3 series (fallback to numeric columns if preferred ones unavailable)
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

            # Overlap first
            for cp_ms in overlap_points_ms:
                ax.axvline(cp_ms, color='purple', linestyle='-', linewidth=2.2, alpha=0.9,
                           label='Overlap (True=Detected)' if col_idx == 0 else '', zorder=4)

            true_only_ms = [cp for cp in true_points_ms if cp not in overlap_points_ms]
            pred_only_ms = [cp for cp in pred_points_ms if cp not in overlap_points_ms]

            for cp_ms in true_only_ms:
                ax.axvline(cp_ms, color='green', linestyle='--', linewidth=1.8,
                           label='True CP' if col_idx == 0 else '', zorder=3)
            for cp_ms in pred_only_ms:
                ax.axvline(cp_ms, color='red', linestyle='-.', linewidth=1.8,
                           label='Predicted CP' if col_idx == 0 else '', zorder=5)

            ax.set_xlabel('Time (ms)', fontsize=9)
            ax.set_ylabel(col_name, fontsize=9)
            ax.grid(True, alpha=0.3)

            if col_idx == 0:
                ax.set_title(
                    f"{prefix}: {info['file']}\nIoU={info['iou']:.4f}, avg |delay|={_fmt_delay(info['avg_abs_delay_ms'])}",
                    fontsize=10,
                    fontweight='bold'
                )

            if row_idx == 0 and col_idx == 0:
                handles, labels = ax.get_legend_handles_labels()
                uniq = dict(zip(labels, handles))
                ax.legend(uniq.values(), uniq.keys(), loc='upper right', fontsize=8)

    plt.tight_layout()
    plt.show()


def print_cv_summary(all_fold_metrics, k):
    """
    Print cross-validation summary - aggregate test set metrics across all folds.
    
    Args:
        all_fold_metrics (list): Metrics from all folds
        k (int): Number of folds
    """
    print(f"\n" + "="*60)
    print("Test Set Metrics - Overall Summary")
    print("="*60)
    
    # Calculate statistics across all folds
    avg_iou = np.mean([m['average_iou'] for m in all_fold_metrics])
    avg_precision = np.mean([m['precision'] for m in all_fold_metrics])
    avg_recall = np.mean([m['recall'] for m in all_fold_metrics])
    avg_f1 = np.mean([m['f1_score'] for m in all_fold_metrics])
    
    std_iou = np.std([m['average_iou'] for m in all_fold_metrics])
    std_precision = np.std([m['precision'] for m in all_fold_metrics])
    std_recall = np.std([m['recall'] for m in all_fold_metrics])
    std_f1 = np.std([m['f1_score'] for m in all_fold_metrics])
    
    # Per-state IoU
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
    
    # CP detection statistics
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


def evaluate_only(test_loader, input_dim, device, weight_path, threshold=0.5, hidden_dim=64,
                  max_change_points=1, tolerance=10):
    """
    Load model from weights and evaluate on test set only.
    
    Args:
        test_loader (DataLoader): Test data
        input_dim (int): Input dimension
        device (torch.device): CPU or GPU
        weight_path (str): Path to saved weights
        threshold (float): Probability threshold for CP detection
        hidden_dim (int): Hidden dimension for CNN
        
    Returns:
        list: List with single test metrics dict (for consistency with training return format)
    """
    print("="*60)
    print("Evaluation Only Mode")
    print("="*60)
    print(f"Loading weights from: {weight_path}")
    print(f"CP detection threshold: {threshold}")
    
    # Create model and load weights
    model = CPDNet(input_dim=input_dim, hidden_dim=hidden_dim).to(device)
    model.load_state_dict(torch.load(weight_path, map_location=device))
    
    # Evaluate
    test_metrics = evaluate_iou_overlap(
        model,
        test_loader,
        device,
        threshold=threshold,
        max_change_points=max_change_points,
        tolerance=tolerance
    )
    
    print(f"\nTest Set Metrics:")
    print(f"  Average IoU: {test_metrics['average_iou']:.4f}")
    print(f"  Per-state IoU - GOING: {test_metrics['per_state_iou']['GOING']:.4f}, RETURNING: {test_metrics['per_state_iou']['RETURNING']:.4f}")
    print(f"  Detection Metrics (P/R/F1): {test_metrics['precision']:.4f} / {test_metrics['recall']:.4f} / {test_metrics['f1_score']:.4f}")
    print(f"\n  CP Statistics:")
    print(f"    True CPs (avg): {test_metrics['cp_stats']['avg_true_cps']:.2f}")
    print(f"    Predicted CPs: min={test_metrics['cp_stats']['min_predicted_cps']}, "
          f"max={test_metrics['cp_stats']['max_predicted_cps']}, avg={test_metrics['cp_stats']['avg_predicted_cps']:.2f}")
    print(f"    Selected CP prob: min={test_metrics['cp_stats']['min_selected_cp_prob']:.4f}, "
          f"max={test_metrics['cp_stats']['max_selected_cp_prob']:.4f}, avg={test_metrics['cp_stats']['avg_selected_cp_prob']:.4f}")
    
    # Return as list of 1 item for consistency with training return format
    return [test_metrics]


def train_and_save_pipeline(full_train_dataset, test_loader, test_dataset, input_dim, device,
                            k_folds, epochs, learning_rate, save_dir, early_stop_patience,
                            pos_weight, threshold, hidden_dim, max_change_points, tolerance):
    """
    Run full training pipeline (k-fold training, summary, plots, best model saving).

    Keep this separate from main so evaluation-only mode can be toggled cleanly.
    """
    fold_results, all_fold_metrics = run_kfold_training(
        full_train_dataset, test_loader, test_dataset, input_dim, device,
        k=k_folds, epochs=epochs, learning_rate=learning_rate, save_dir=save_dir,
        weight_path=None, patience=early_stop_patience, pos_weight=pos_weight,
        threshold=threshold, hidden_dim=hidden_dim, max_change_points=max_change_points,
        tolerance=tolerance
    )

    # Report results
    print_cv_summary(all_fold_metrics, k=k_folds)

    # Plot curves
    plot_training_curves(fold_results, k=k_folds)

    # Save best model
    print("\n" + "="*60)
    print("Saving best model")
    print("="*60)

    best_fold_idx = np.argmax([m['f1_score'] for m in all_fold_metrics])
    best_model = fold_results[best_fold_idx]['model']
    best_model_path = os.path.join(save_dir, 'cpd_best_model.pt')
    torch.save(best_model.state_dict(), best_model_path)
    print(f"Best model (Fold {best_fold_idx + 1}, F1={all_fold_metrics[best_fold_idx]['f1_score']:.4f}) saved to {best_model_path}")

    return fold_results, all_fold_metrics


# ============================================================================
# 12. MAIN EXECUTION WITH K-FOLD CROSS-VALIDATION
# ============================================================================

if __name__ == "__main__":
    # Setup - Use Metal GPU on M1 Macs
    if torch.backends.mps.is_available():
        DEVICE = torch.device('mps')
        print("Using Metal Performance Shaders (MPS) GPU acceleration")
    elif torch.cuda.is_available():
        DEVICE = torch.device('cuda')
        print("Using CUDA GPU acceleration")
    else:
        DEVICE = torch.device('cpu')
        print("Using CPU")
    print(f"Device: {DEVICE}\n")
    
    # ========== HYPERPARAMETERS (TUNE HERE) ==========
    # Training hyperparameters
    K_FOLDS = 5
    EPOCHS = 50
    LEARNING_RATE = 0.001
    BATCH_SIZE = 4
    EARLY_STOP_PATIENCE = 15
    POS_WEIGHT = 10.0
    
    # Data hyperparameters
    TARGET_LENGTH = 500  # All sequences interpolated to this length
    
    # Model hyperparameters
    HIDDEN_DIM = 64
    THRESHOLD = 0.5
    MAX_CHANGE_POINTS = 1  # set to 2, 3... in future; set None for threshold-based variable count
    DETECTION_TOLERANCE = 20  # +/- samples for TP matching in precision/recall/F1
    PLOT_BEST_WORST_SEGMENTATION = True
    
    # Paths
    DATA_DIR = '../Data/Trial_data_27Feb'
    TRAIN_DIR = '../Data/CPD_train'
    TEST_DIR = '../Data/CPD_test'
    SAVE_DIR = './saved_weights'
    
    # ===== STEP 1: Split and load data =====
    print("="*60)
    print("STEP 1: Data Loading")
    print("="*60)
    
    full_train_dataset, test_dataset, train_files, test_files, input_dim = split_and_load_data(
        DATA_DIR, TRAIN_DIR, TEST_DIR, test_size=0.2, target_length=TARGET_LENGTH
    )
    
    test_loader = DataLoader(test_dataset, batch_size=BATCH_SIZE, shuffle=False, collate_fn=collate_cpd_batch)
    print(f"Full train samples: {len(full_train_dataset)}")
    print(f"Test samples: {len(test_dataset)}")
    print(f"Input dimension (features): {input_dim}")
    print(f"Sequence length: {TARGET_LENGTH} (all sequences interpolated to this length)")
    
    # ===== CHOOSE: TRAIN OR EVALUATE ONLY =====
    # Uncomment only one block.

    # Option 1: Train mode (includes fold training + summary + plots + best model save)
    # fold_results, all_fold_metrics = train_and_save_pipeline(
    #     full_train_dataset, test_loader, test_dataset, input_dim, DEVICE,
    #     k_folds=K_FOLDS, epochs=EPOCHS, learning_rate=LEARNING_RATE, save_dir=SAVE_DIR,
    #     early_stop_patience=EARLY_STOP_PATIENCE, pos_weight=POS_WEIGHT,
    #     threshold=THRESHOLD, hidden_dim=HIDDEN_DIM, max_change_points=MAX_CHANGE_POINTS,
    #     tolerance=DETECTION_TOLERANCE
    # )

    # Option 2: Evaluate only (clean path; no training artifacts needed)
    all_fold_metrics = evaluate_only(
        test_loader,
        input_dim,
        DEVICE,
        weight_path='./saved_weights/cpd_best_model.pt',
        threshold=THRESHOLD,
        hidden_dim=HIDDEN_DIM,
        max_change_points=MAX_CHANGE_POINTS,
        tolerance=DETECTION_TOLERANCE
    )

    print_cv_summary(all_fold_metrics, k=K_FOLDS)

    if PLOT_BEST_WORST_SEGMENTATION:
        plot_model = CPDNet(input_dim=input_dim, hidden_dim=HIDDEN_DIM).to(DEVICE)
        plot_model.load_state_dict(torch.load('./saved_weights/cpd_best_model.pt', map_location=DEVICE))
        plot_best_worst_segmentation(
            model=plot_model,
            dataset=test_dataset,
            data_dir=TEST_DIR,
            device=DEVICE,
            threshold=THRESHOLD,
            max_change_points=MAX_CHANGE_POINTS,
            hz=50
        )
