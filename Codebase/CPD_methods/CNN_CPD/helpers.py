"""Helper re-exports for the CNN CPD pipeline.

This module re-exports preprocessing, dataset, model and small helpers from
the original `CNN_CPD.py` file so code can be split without changing logic.
"""

import torch

from data import (
    load_file,
    preprocess_cpd_data,
    get_change_points_from_states,
    TemporalAugmenter,
    collate_cpd_batch,
    CPDDataset,
    SubsetDataset,
    split_and_load_data,
    split_data_into_train_test,
)
from model import CNN1DBackbone, CPDNet
from evaluation import _ordered_states_from_labels, _labels_from_change_points

__all__ = [
    'load_file', 'preprocess_cpd_data', 'get_change_points_from_states',
    'TemporalAugmenter', 'collate_cpd_batch', 'CPDDataset', 'SubsetDataset',
    'CNN1DBackbone', 'CPDNet', '_ordered_states_from_labels',
    '_labels_from_change_points', 'split_and_load_data', 'split_data_into_train_test'
]


# Note: `safe_torch_load` was moved to `CNN_CPD.utils` to avoid circular
# import issues (evaluation imports helpers; keeping the loader in a tiny
# independent module breaks the cycle).
