import os
import pandas as pd
import joblib
from sklearn.ensemble import RandomForestClassifier
from sklearn.preprocessing import LabelEncoder

def train_model():
    print("Starting training process...")
    
    # 1. Prepare sample training data representing history
    # If a csv file exists, use it, otherwise create a sample dataframe
    csv_path = os.path.join(os.path.dirname(__file__), "history.csv")
    if os.path.exists(csv_path):
        print(f"Loading history data from {csv_path}")
        data = pd.read_csv(csv_path)
    else:
        print("No history.csv found. Generating synthetic training data...")
        # Generates training samples: weight, volume, category -> BinId (as string guid)
        # Using real seeded bin ID from our DB context: 33333333-3333-3333-3333-333333333333
        # and some mock bin IDs for classifier diversity
        bin_a = "33333333-3333-3333-3333-333333333333"
        bin_b = "44444444-4444-4444-4444-444444444444"
        bin_c = "55555555-5555-5555-5555-555555555555"
        
        records = [
            # Shoes/Clothing usually go to Bin A
            {"Weight": 1.2, "Volume": 0.005, "Category": "Shoes", "Bin": bin_a},
            {"Weight": 0.8, "Volume": 0.003, "Category": "Shoes", "Bin": bin_a},
            {"Weight": 2.5, "Volume": 0.015, "Category": "Clothes", "Bin": bin_a},
            
            # Electronics go to Bin B
            {"Weight": 15.0, "Volume": 0.08, "Category": "Electronics", "Bin": bin_b},
            {"Weight": 25.0, "Volume": 0.12, "Category": "Electronics", "Bin": bin_b},
            {"Weight": 10.5, "Volume": 0.05, "Category": "Electronics", "Bin": bin_b},
            
            # Fragile/Heavy items go to Bin C
            {"Weight": 35.0, "Volume": 0.20, "Category": "Fragile", "Bin": bin_c},
            {"Weight": 45.0, "Volume": 0.25, "Category": "Fragile", "Bin": bin_c},
            {"Weight": 30.0, "Volume": 0.18, "Category": "Fragile", "Bin": bin_c},
        ]
        
        # Multiply records to give scikit-learn enough samples
        records = records * 10
        data = pd.DataFrame(records)
        data.to_csv(csv_path, index=False)
        print(f"Generated synthetic history.csv with {len(data)} rows.")

    # 2. Preprocess data
    # Encode categorical variable 'Category'
    cat_encoder = LabelEncoder()
    data['Category_Encoded'] = cat_encoder.fit_transform(data['Category'])
    
    # Encode Target 'Bin'
    target_encoder = LabelEncoder()
    data['Bin_Encoded'] = target_encoder.fit_transform(data['Bin'])
    
    X = data[['Weight', 'Volume', 'Category_Encoded']]
    y = data['Bin_Encoded']
    
    # 3. Train RandomForest Model
    print("Training RandomForest model...")
    model = RandomForestClassifier(n_estimators=100, random_state=42)
    model.fit(X, y)
    
    # 4. Save model and encoders
    model_dir = os.path.dirname(__file__)
    joblib.dump(model, os.path.join(model_dir, "warehouse_model.pkl"))
    joblib.dump(cat_encoder, os.path.join(model_dir, "cat_encoder.pkl"))
    joblib.dump(target_encoder, os.path.join(model_dir, "target_encoder.pkl"))
    
    print("Model and Encoders saved successfully.")

if __name__ == "__main__":
    train_model()
