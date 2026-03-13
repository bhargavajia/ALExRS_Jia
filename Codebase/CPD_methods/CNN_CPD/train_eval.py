"""Training and evaluation re-exports for CNN CPD pipeline.

This module re-exports training, loss, evaluation and plotting functions from
the original `CNN_CPD.py` so callers can import them from here.
"""

from model import WeightedBCELoss
from training import train_epoch, validate, train_fold, run_kfold_training, train_and_save_pipeline, plot_training_curves
from evaluation import evaluate_detection, evaluate_iou_overlap, print_cv_summary, plot_best_worst_segmentation, evaluate_only

__all__ = [
    'WeightedBCELoss', 'train_epoch', 'validate', 'train_fold',
    'run_kfold_training', 'train_and_save_pipeline', 'evaluate_detection',
    'evaluate_iou_overlap', 'print_cv_summary', 'plot_training_curves',
    'plot_best_worst_segmentation', 'evaluate_only'
]
