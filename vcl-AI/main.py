import os
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import joblib
import numpy as np

# Automatically train the model if pkl files do not exist yet
model_dir = os.path.dirname(__file__)
model_path = os.path.join(model_dir, "warehouse_model.pkl")
cat_encoder_path = os.path.join(model_dir, "cat_encoder.pkl")
target_encoder_path = os.path.join(model_dir, "target_encoder.pkl")

if not (os.path.exists(model_path) and os.path.exists(cat_encoder_path) and os.path.exists(target_encoder_path)):
    print("Model files not found. Running training script first...")
    from train import train_model
    train_model()

# Load models and encoders
model = joblib.load(model_path)
cat_encoder = joblib.load(cat_encoder_path)
target_encoder = joblib.load(target_encoder_path)

app = FastAPI(title="VCL Warehouse AI Service")

class PredictRequest(BaseModel):
    weight: float
    volume: float
    category: str = "General"

@app.post("/predict_proba")
def predict_proba(request: PredictRequest):
    try:
        # Match category using encoder, fallback to default or first category if unknown
        cat_str = request.category
        if cat_str not in cat_encoder.classes_:
            # Fallback to closest match or default category
            print(f"Unknown category '{cat_str}'. Defaulting to general class.")
            cat_val = cat_encoder.transform([cat_encoder.classes_[0]])[0]
        else:
            cat_val = cat_encoder.transform([cat_str])[0]
            
        features = np.array([[request.weight, request.volume, cat_val]])
        
        # Get probability distributions
        probabilities = model.predict_proba(features)[0]
        
        # Map probabilities back to Bin ID strings
        predictions = []
        for class_idx, prob in enumerate(probabilities):
            bin_id = str(target_encoder.inverse_transform([class_idx])[0])
            predictions.append({
                "binId": bin_id,
                "probability": float(prob)
            })
            
        # Sort predictions by probability descending
        predictions = sorted(predictions, key=lambda x: x["probability"], reverse=True)
        
        return {
            "predictions": predictions
        }
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/health")
def health():
    return {"status": "healthy"}
