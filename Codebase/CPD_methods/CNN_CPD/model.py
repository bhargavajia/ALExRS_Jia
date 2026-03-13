import torch
import torch.nn as nn


class CNN1DBackbone(nn.Module):
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
        x = self.relu(self.bn1(self.conv1(x)))
        x = self.relu(self.bn2(self.conv2(x)))
        x = self.relu(self.bn3(self.conv3(x)))
        return x


class CPDNet(nn.Module):
    def __init__(self, input_dim=13, hidden_dim=64):
        super().__init__()
        self.backbone = CNN1DBackbone(input_dim, hidden_dim)
        self.cp_head = nn.Conv1d(hidden_dim, 1, kernel_size=1)

    def forward(self, x):
        features = self.backbone(x)
        logits = self.cp_head(features)
        return logits.squeeze(1)

    def predict_change_points(self, x, threshold=0.5, max_change_points=1, return_scores=False):
        logits = self.forward(x)
        probs = torch.sigmoid(logits)
        predicted_cps = []
        predicted_scores = []
        for batch_idx in range(probs.shape[0]):
            if max_change_points is None:
                cp_indices = torch.where(probs[batch_idx] > threshold)[0].cpu().numpy().tolist()
            else:
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


class WeightedBCELoss(nn.Module):
    def __init__(self, pos_weight=10.0):
        super().__init__()
        self.register_buffer('pos_weight', torch.tensor(float(pos_weight), dtype=torch.float32))

    def forward(self, logits, targets):
        return nn.functional.binary_cross_entropy_with_logits(
            logits,
            targets,
            pos_weight=self.pos_weight.to(logits.device)
        )
