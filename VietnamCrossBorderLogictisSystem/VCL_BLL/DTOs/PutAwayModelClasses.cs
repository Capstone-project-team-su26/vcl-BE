using Microsoft.ML.Data;

namespace VCL_BLL.DTOs;

public class PutAwayModelInput
{
    [ColumnName("Label")]
    public string BinId { get; set; } = string.Empty;

    public float Weight { get; set; }

    public float Volume { get; set; }

    public string Category { get; set; } = string.Empty;
}

public class PutAwayModelPrediction
{
    [ColumnName("PredictedLabel")]
    public string BinId { get; set; } = string.Empty;

    public float[] Score { get; set; } = System.Array.Empty<float>();
}
