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
        public string? Gateway { get; set; }

        [JsonPropertyName("transactionDate")]
        public string? TransactionDate { get; set; }

        [JsonPropertyName("accountNumber")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("transferAmount")]
        public decimal TransferAmount { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("referenceCode")]
        public string? ReferenceCode { get; set; }
    }
}
