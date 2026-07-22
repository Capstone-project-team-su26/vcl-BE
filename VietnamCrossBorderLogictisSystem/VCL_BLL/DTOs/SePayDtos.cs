using System.Text.Json.Serialization;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// DTO đại diện cho dữ liệu giao dịch gửi từ SePay Webhook.
    /// </summary>
    public class SePayWebhookDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("gateway")]
        public string Gateway { get; set; } = string.Empty;

        [JsonPropertyName("transactionDate")]
        public string TransactionDate { get; set; } = string.Empty;

        [JsonPropertyName("accountNumber")]
        public string AccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("transferAmount")]
        public decimal TransferAmount { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("referenceCode")]
        public string ReferenceCode { get; set; } = string.Empty;
    }
}
