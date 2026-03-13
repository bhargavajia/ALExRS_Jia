"""Hyperparameters and paths for CNN CPD pipeline.

Update paths here if moving the code folder. This file is intended to be
imported by the main runner so that the code logic doesn't need editing.
"""

# Training hyperparameters
K_FOLDS = 5
EPOCHS = 50
LEARNING_RATE = 1e-3
BATCH_SIZE = 4
EARLY_STOP_PATIENCE = 15
POS_WEIGHT = 10.0

# Data hyperparameters
TARGET_LENGTH = 500

# Model hyperparameters
HIDDEN_DIM = 64
THRESHOLD = 0.5
MAX_CHANGE_POINTS = 1
DETECTION_TOLERANCE = 20
PLOT_BEST_WORST_SEGMENTATION = True

# Modes
MODE = 'evaluate'  # 'train' or 'evaluate'
EVAL_ON = 'test' # Evaluate on train or test set (only used if MODE='evaluate')

# Paths
DATA_DIR = '../../Data/Trial_data_27Feb'
TRAIN_DIR = '../../Data/CPD_train'
TEST_DIR = '../../Data/CPD_test'
SAVE_DIR = './saved_weights'
# WEIGHT_PATH = None  # Set to None for training from scratch
WEIGHT_PATH = './saved_weights/cpd_final_model_50.pt'
