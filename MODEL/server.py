import os
import socket
import time
import numpy as np
import torch
import torch.nn as nn
from collections import Counter

# ==========================================
# 1. UDP SETUP (Connection to Unity)
# ==========================================
UDP_IP = "127.0.0.1" 
UDP_PORT = 5005
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# 2. SETUP DEVICE
device = "cuda" if torch.cuda.is_available() else "cpu"

# 3. LABEL MAP
label_map = {
    0: "no_motion", 1: "radial_deviation", 2: "ulnar_deviation",
    3: "wrist_flexion", 4: "wrist_extension", 5: "supination",
    6: "pronation", 7: "power_grip", 8: "open_hand",
    9: "chuck_grip", 10: "pinch_grip"
}
num_classes = len(label_map)

# 4. HELPER FUNCTIONS
def normalize(x):
    x = np.nan_to_num(x)
    mean = x.mean(axis=0)
    std = x.std(axis=0) + 1e-8
    return (x - mean) / std

# ==========================================
# 5. NEW MODEL ARCHITECTURE
# ==========================================
class StrongEMGTransformer(nn.Module):
    def __init__(self, input_dim=10, d_model=128, nhead=8, num_layers=4, seq_len=500):
        super().__init__()

        # 1. CNN STEM: Extracts local temporal patterns & filters noise
        self.cnn_stem = nn.Sequential(
            nn.Conv1d(input_dim, d_model, kernel_size=5, padding=2),
            nn.BatchNorm1d(d_model),
            nn.ReLU(),
            nn.Dropout(0.2)
        )

        # 2. CLS TOKEN: Learnable summary vector
        self.cls_token = nn.Parameter(torch.zeros(1, 1, d_model))

        # 3. LEARNED POSITIONAL ENCODING: Adds temporal order context
        self.pos_embed = nn.Parameter(torch.zeros(1, seq_len + 1, d_model))

        # 4. TRANSFORMER ENCODER
        encoder_layer = nn.TransformerEncoderLayer(
            d_model=d_model,
            nhead=nhead,
            dim_feedforward=d_model * 4,
            dropout=0.2, 
            batch_first=True
        )
        self.transformer = nn.TransformerEncoder(encoder_layer, num_layers=num_layers)

        # 5. CLASSIFIER HEAD
        self.classifier = nn.Linear(d_model, num_classes)
        
        # Initialize weights
        nn.init.trunc_normal_(self.pos_embed, std=0.02)
        nn.init.trunc_normal_(self.cls_token, std=0.02)

    def forward(self, x):
        # x shape: (Batch, Seq_Len, Channels)
        
        # CNN Step: Requires (Batch, Channels, Seq_Len)
        x = x.transpose(1, 2)
        x = self.cnn_stem(x)
        x = x.transpose(1, 2) # Back to (Batch, Seq_Len, d_model)

        # Prepend CLS Token
        b = x.shape[0]
        cls_tokens = self.cls_token.expand(b, -1, -1)
        x = torch.cat((cls_tokens, x), dim=1) # (Batch, Seq_Len + 1, d_model)

        # Add Positional Context
        x = x + self.pos_embed

        # Transformer Processing
        x = self.transformer(x)

        # Extract only the CLS token output (index 0) for classification
        x = x[:, 0] 
        
        return self.classifier(x)

# ==========================================
# 6. STATEFUL PREDICTION CLASS (The Fix!)
# ==========================================
class StreamingPredictor:
    def __init__(self, model):
        self.model = model
        self.model.eval()
        
        # State variables persist across files
        self.last_raw_guess = None
        self.consecutive_count = 0
        self.current_verified_motion = "no_motion"

    def process_signal(self, signal, window=500, stride=100):
        signal = normalize(signal)

        with torch.no_grad():
            for i in range(0, len(signal) - window, stride):
                window_data = signal[i:i+window]
                x = torch.tensor(window_data, dtype=torch.float32).unsqueeze(0).to(device)
                out = self.model(x)
                pred_idx = torch.argmax(out, dim=1).item()
                raw_guess_name = label_map[pred_idx]

                # --- PERSISTENT CONSECUTIVE LOGIC ---
                if raw_guess_name == self.last_raw_guess:
                    self.consecutive_count += 1
                else:
                    self.consecutive_count = 1
                    self.last_raw_guess = raw_guess_name

                if self.consecutive_count >= 3: 
                    self.current_verified_motion = raw_guess_name
                
                # --- SEND TO UNITY ---
                try:
                    sock.sendto(self.current_verified_motion.encode(), (UDP_IP, UDP_PORT))
                except Exception as e:
                    print(f"UDP Error: {e}")

                print(f"Window: {i//stride:2} | Guess: {raw_guess_name:15} | Count: {self.consecutive_count} | Active: {self.current_verified_motion}")
                
                # Maintained your 0.5s delay from the new script
                time.sleep(0.5) 

        return self.current_verified_motion

# =========================================================
# 7. HOW TO USE IT (Continuous Stream over all files)
# =========================================================
if __name__ == "__main__":
    MODEL_WEIGHTS = "best_emg_transformer.pth.zip" 
    
    # 1. Initialize and load the model ONCE
    loaded_model = StrongEMGTransformer().to(device)
    
    if os.path.exists(MODEL_WEIGHTS):
        checkpoint = torch.load(MODEL_WEIGHTS, map_location=device)
        if "model_state_dict" in checkpoint:
            loaded_model.load_state_dict(checkpoint["model_state_dict"])
        else:
            loaded_model.load_state_dict(checkpoint)
        print("Model loaded successfully.")
    else:
        print(f"Warning: Model weights not found at {MODEL_WEIGHTS}. Using untrained weights.")

    # 2. Initialize the stateful predictor ONCE
    stream_predictor = StreamingPredictor(loaded_model)
    
    # 3. Process all files in the directory as a continuous stream
    test_dir = "SignalTestingSet"
    if os.path.exists(test_dir):
        signal_files = [f for f in os.listdir(test_dir) if f.endswith(".txt")]
        signal_files.sort()
        
        for file in signal_files:
            file_path = os.path.join(test_dir, file)
            print("-" * 60)
            print(f" PROCESSING: {file}")
            print("-" * 60)
            
            raw_signal = np.loadtxt(file_path, delimiter=",")
            emg_only = raw_signal[:, :10] 
            
            # This calls the persistent object, maintaining stability rules across files!
            final_motion = stream_predictor.process_signal(emg_only)
            
            print(f"--- File Ended. Current Stable Motion: {final_motion} ---")
    else:
        print(f"Directory '{test_dir}' not found.")