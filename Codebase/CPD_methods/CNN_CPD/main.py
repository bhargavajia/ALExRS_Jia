"""Main runner for the CNN CPD pipeline.

Imports parameters from `params.py` and helper/training functions from
`helpers.py` and `train_eval.py`. This file contains the original `__main__`
logic extracted from `CNN_CPD.py` but wired to the split modules.
"""

import os
import sys
import pathlib
import torch
from torch.utils.data import DataLoader

# When running `python main.py` from inside the CNN_CPD directory, make sure
# the parent directory is on sys.path so absolute package imports (`CNN_CPD.*`)
# work and relative imports inside the package resolve correctly.
if __package__ is None:
    parent = str(pathlib.Path(__file__).resolve().parent.parent)
    if parent not in sys.path:
        sys.path.insert(0, parent)

from CNN_CPD.params import (
    K_FOLDS, EPOCHS, LEARNING_RATE, BATCH_SIZE, EARLY_STOP_PATIENCE, POS_WEIGHT,
    TARGET_LENGTH, HIDDEN_DIM, THRESHOLD, MAX_CHANGE_POINTS, DETECTION_TOLERANCE,
    PLOT_BEST_WORST_SEGMENTATION, MODE, EVAL_ON, DATA_DIR, TRAIN_DIR, TEST_DIR,
    SAVE_DIR, WEIGHT_PATH
)
from CNN_CPD.helpers import split_and_load_data, CPDNet, CPDDataset, collate_cpd_batch
from CNN_CPD.utils import safe_torch_load
from CNN_CPD.train_eval import train_and_save_pipeline, evaluate_only, print_cv_summary, plot_best_worst_segmentation


def main():
    # Device selection
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

    # Step 1: Split and load
    print("="*60)
    print("STEP 1: Data Loading")
    print("="*60)

    full_train_dataset, test_dataset, train_files, test_files, input_dim = split_and_load_data(
        DATA_DIR, TRAIN_DIR, TEST_DIR, test_size=0.2, target_length=TARGET_LENGTH
    )

    test_loader = DataLoader(test_dataset, batch_size=BATCH_SIZE, shuffle=False, collate_fn=collate_cpd_batch)

    # create an unaugmented training dataset for evaluation
    train_eval_dataset = CPDDataset(train_files, TRAIN_DIR, augment=False, target_length=TARGET_LENGTH)
    train_eval_loader = DataLoader(train_eval_dataset, batch_size=BATCH_SIZE, shuffle=False, collate_fn=collate_cpd_batch)

    print(f"Full train samples: {len(full_train_dataset)}")
    print(f"Test samples: {len(test_dataset)}")
    print(f"Input dimension (features): {input_dim}")
    print(f"Sequence length: {TARGET_LENGTH} (all sequences interpolated to this length)")

    # Run selected mode
    trained_best_weight = None
    if MODE == 'train':
        fold_results, all_fold_metrics = train_and_save_pipeline(
            full_train_dataset, test_loader, test_dataset, input_dim, DEVICE,
            k_folds=K_FOLDS, epochs=EPOCHS, learning_rate=LEARNING_RATE, save_dir=SAVE_DIR,
            early_stop_patience=EARLY_STOP_PATIENCE, pos_weight=POS_WEIGHT,
            threshold=THRESHOLD, hidden_dim=HIDDEN_DIM, max_change_points=MAX_CHANGE_POINTS,
            tolerance=DETECTION_TOLERANCE,
            weight_path=WEIGHT_PATH
        )
        # after training, use final model for plotting/evaluation if available
        candidate = os.path.join(SAVE_DIR, 'cpd_final_model.pt')
        if os.path.exists(candidate):
            trained_best_weight = candidate
        else:
            trained_best_weight = None

    elif MODE == 'evaluate':
        all_fold_metrics = evaluate_only(
            test_loader,
            input_dim,
            DEVICE,
            weight_path=WEIGHT_PATH,
            threshold=THRESHOLD,
            hidden_dim=HIDDEN_DIM,
            max_change_points=MAX_CHANGE_POINTS,
            tolerance=DETECTION_TOLERANCE,
            train_loader=train_eval_loader,
            eval_on=EVAL_ON
        )
    else:
        raise ValueError(f"Invalid MODE: {MODE}. Choose 'train' or 'evaluate'.")

    # print_cv_summary(all_fold_metrics, k=K_FOLDS)

    if PLOT_BEST_WORST_SEGMENTATION:
        # choose which weight file to use: trained best model after training, or provided WEIGHT_PATH for evaluate
        weight_to_use = None
        if MODE == 'train' and 'trained_best_weight' in locals() and trained_best_weight is not None:
            weight_to_use = trained_best_weight
        elif MODE == 'evaluate' and WEIGHT_PATH is not None:
            weight_to_use = WEIGHT_PATH

        if weight_to_use is None or not os.path.exists(weight_to_use):
            print("No weight file available for plotting best/worst segmentation.")
        else:
            # choose dataset for plotting: if evaluating on train, plot train set; otherwise plot test set
            if MODE == 'evaluate' and EVAL_ON == 'train':
                dataset_to_plot = train_eval_dataset
                data_dir_to_plot = TRAIN_DIR
            else:
                dataset_to_plot = test_dataset
                data_dir_to_plot = TEST_DIR

            plot_model = CPDNet(input_dim=input_dim, hidden_dim=HIDDEN_DIM).to(DEVICE)
            state = safe_torch_load(weight_to_use, map_location=DEVICE)
            # handle common wrapper keys
            if isinstance(state, dict):
                if 'model_state_dict' in state:
                    state = state['model_state_dict']
                elif 'state_dict' in state:
                    state = state['state_dict']
            plot_model.load_state_dict(state)
            plot_best_worst_segmentation(
                model=plot_model,
                dataset=dataset_to_plot,
                data_dir=data_dir_to_plot,
                device=DEVICE,
                threshold=THRESHOLD,
                max_change_points=MAX_CHANGE_POINTS,
                hz=50
            )


if __name__ == '__main__':
    main()
