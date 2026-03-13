import os
import shutil
import numpy as np
import pandas as pd
import torch
from torch.utils.data import Dataset
from sklearn.preprocessing import StandardScaler


def split_data_into_train_test(source_dir, train_dir, test_dir, test_size=0.2, seed=42):
    np.random.seed(seed)
    torch.manual_seed(seed)
    os.makedirs(train_dir, exist_ok=True)
    os.makedirs(test_dir, exist_ok=True)
    all_files = [f for f in os.listdir(source_dir) if f.endswith('.csv')]
    train_files = []
    test_files = []
    if len(all_files) > 0:
        from sklearn.model_selection import train_test_split
        train_files, test_files = train_test_split(all_files, test_size=test_size, random_state=seed)
        for file in train_files:
            shutil.copy(os.path.join(source_dir, file), os.path.join(train_dir, file))
        for file in test_files:
            shutil.copy(os.path.join(source_dir, file), os.path.join(test_dir, file))
    return train_files, test_files


def load_file(file_path):
    return pd.read_csv(file_path)


def preprocess_cpd_data(df, target_length=500):
    cols_to_drop = [
        'Timestamp', 'Frequency_Hz',
        'Left_EE_PosX_m', 'Left_EE_VelX_mps',
        'Left_J0_Abduction_deg', 'Left_J1_Rotation_deg', 'Left_J2_deg', 'Left_J3_deg',
        'Left_J4_ShoulderFlexion_deg', 'Left_J5_ElbowFlexion_deg',
        'Left_J6_PronoSupination_deg', 'Left_J7_WristFlexion_deg',
        'Left_J2_rad', 'Left_J2_vel_radps', 'Left_J2_deg',
        'Left_J3_rad', 'Left_J3_vel_radps', 'Left_J3_deg',
    ]
    states = df['State'].values if 'State' in df.columns else None
    df_clean = df.drop(columns=[c for c in cols_to_drop if c in df.columns])
    for col in df_clean.columns:
        df_clean[col] = pd.to_numeric(df_clean[col], errors='coerce')
    df_clean = df_clean.fillna(0)
    feature_cols = [c for c in df_clean.columns if c != 'State']
    features = df_clean[feature_cols].values.astype(np.float32)
    original_length = len(features)
    if original_length != target_length:
        old_indices = np.linspace(0, original_length - 1, original_length)
        new_indices = np.linspace(0, original_length - 1, target_length)
        features_interp = np.zeros((target_length, features.shape[1]), dtype=np.float32)
        for col_idx in range(features.shape[1]):
            features_interp[:, col_idx] = np.interp(new_indices, old_indices, features[:, col_idx])
        features = features_interp
        state_indices = np.round(np.interp(new_indices, old_indices, np.arange(original_length))).astype(int)
        states = states[state_indices] if states is not None else None
    scaler = StandardScaler()
    features = scaler.fit_transform(features)
    return features, states, np.arange(len(features))


def get_change_points_from_states(states):
    if states is None:
        return []
    change_points = []
    for i in range(1, len(states)):
        if states[i] != states[i-1]:
            change_points.append(i)
    return change_points


class TemporalAugmenter:
    def __init__(self, seed=42):
        np.random.seed(seed)
        self.seed = seed

    def temporal_jitter(self, X, std=0.01):
        noise = np.random.normal(0, std, X.shape)
        return X + noise

    def temporal_scaling(self, X, scale_range=(0.9, 1.1)):
        scale_factor = np.random.uniform(*scale_range, size=(1, X.shape[1]))
        return X * scale_factor

    def temporal_shift(self, X, max_shift=5):
        shift = np.random.randint(-max_shift, max_shift + 1)
        return np.roll(X, shift, axis=0)

    def temporal_warping(self, X, warp_range=(0.8, 1.2)):
        speed = np.random.uniform(*warp_range)
        indices = np.arange(len(X))
        new_indices = np.linspace(0, len(X) - 1, int(len(X) / speed))
        augmented = np.zeros((len(new_indices), X.shape[1]))
        for feat_idx in range(X.shape[1]):
            augmented[:, feat_idx] = np.interp(new_indices, indices, X[:, feat_idx])
        if len(augmented) < len(X):
            augmented = np.vstack([augmented, np.zeros((len(X) - len(augmented), X.shape[1]))])
        else:
            augmented = augmented[:len(X)]
        return augmented

    def frame_skipping(self, X, skip_prob=0.05):
        mask = np.random.random(len(X)) > skip_prob
        if mask.sum() < 2:
            return X
        kept_indices = np.where(mask)[0]
        X_skipped = X[kept_indices]
        indices = np.linspace(0, len(X_skipped) - 1, len(X))
        X_resampled = np.zeros_like(X)
        for feat_idx in range(X.shape[1]):
            X_resampled[:, feat_idx] = np.interp(indices, np.arange(len(X_skipped)), X_skipped[:, feat_idx])
        return X_resampled

    def augment(self, X, augmentation_types=None):
        if augmentation_types is None:
            augmentation_types = ['jitter', 'scaling', 'shift', 'warping', 'skipping']
        X_aug = X.copy()
        for aug_type in augmentation_types:
            if np.random.random() > 0.5:
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


def collate_cpd_batch(batch):
    import torch
    features_list = [item[0] for item in batch]
    cp_labels_list = [item[1] for item in batch]
    states_list = [item[2] for item in batch]
    features_batch = torch.stack(features_list, dim=0).transpose(1, 2)
    cp_labels_batch = torch.stack(cp_labels_list, dim=0)
    return features_batch, cp_labels_batch, states_list


class CPDDataset(Dataset):
    def __init__(self, file_list, data_dir, augment=False, target_length=500):
        self.file_list = file_list
        self.data_dir = data_dir
        self.augment = augment
        self.augmenter = TemporalAugmenter()
        self.target_length = target_length
        self.samples = []
        for file in file_list:
            file_path = os.path.join(data_dir, file)
            df = load_file(file_path)
            features, states, _ = preprocess_cpd_data(df, target_length=target_length)
            change_points = get_change_points_from_states(states)
            self.samples.append({'features': features, 'states': states, 'change_points': change_points, 'file': file})

    def __len__(self):
        return len(self.samples)

    def __getitem__(self, idx):
        sample = self.samples[idx]
        features = sample['features'].copy()
        if self.augment:
            features = self.augmenter.augment(features)
        cp_labels = np.zeros(len(features), dtype=np.float32)
        for cp in sample['change_points']:
            if cp < len(cp_labels):
                cp_labels[cp] = 1.0
        return torch.from_numpy(features).float(), torch.from_numpy(cp_labels).float(), sample['states'].tolist()


class SubsetDataset(Dataset):
    def __init__(self, dataset, indices):
        self.dataset = dataset
        self.indices = indices

    def __len__(self):
        return len(self.indices)

    def __getitem__(self, idx):
        return self.dataset[self.indices[idx]]


def split_and_load_data(data_dir, train_dir, test_dir, test_size=0.2, target_length=500):
    if not os.path.exists(train_dir) or not os.path.exists(test_dir):
        train_files, test_files = split_data_into_train_test(data_dir, train_dir, test_dir, test_size=test_size)
    else:
        train_files = sorted([f for f in os.listdir(train_dir) if f.endswith('.csv')])
        test_files = sorted([f for f in os.listdir(test_dir) if f.endswith('.csv')])
    full_train_dataset = CPDDataset(train_files, train_dir, augment=True, target_length=target_length)
    test_dataset = CPDDataset(test_files, test_dir, augment=False, target_length=target_length)
    sample_features, _, _ = full_train_dataset[0]
    input_dim = sample_features.shape[1]
    return full_train_dataset, test_dataset, train_files, test_files, input_dim
