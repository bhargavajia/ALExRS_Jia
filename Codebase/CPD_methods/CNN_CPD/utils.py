import torch


def safe_torch_load(path, map_location=None):
    """Load a torch file preferring weights-only mode when available.

    Falls back to legacy behaviour if the installed torch doesn't support
    the `weights_only` kwarg.
    Returns the loaded object (often a state_dict) or raises the underlying
    exception.
    """
    try:
        return torch.load(path, map_location=map_location, weights_only=True)
    except TypeError:
        return torch.load(path, map_location=map_location)
