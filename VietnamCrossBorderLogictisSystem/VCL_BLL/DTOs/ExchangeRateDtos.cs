using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class ExchangeRateDto
    {
        public Guid Id { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public string? CurrencyName { get; set; }

        /// <summary>Số VND đổi được 1 đơn vị ngoại tệ.</summary>
        public decimal RateToVnd { get; set; }

        public bool IsActive { get; set; }
        public string? Note { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class CreateExchangeRateRequest
    {
        [Required(ErrorMessage = "Mã ngoại tệ không được để trống.")]
        [StringLength(10, ErrorMessage = "Mã ngoại tệ tối đa 10 ký tự.")]
        public string CurrencyCode { get; set; } = string.Empty;

        [StringLength(100)]
        public string? CurrencyName { get; set; }

        [Range(0.000001, double.MaxValue, ErrorMessage = "Tỷ giá phải lớn hơn 0.")]
        public decimal RateToVnd { get; set; }

        public bool IsActive { get; set; } = true;

        [StringLength(500)]
        public string? Note { get; set; }
    }

    public class UpdateExchangeRateRequest
    {
        [StringLength(100)]
        public string? CurrencyName { get; set; }

        [Range(0.000001, double.MaxValue, ErrorMessage = "Tỷ giá phải lớn hơn 0.")]
        public decimal RateToVnd { get; set; }

        public bool IsActive { get; set; } = true;

        [StringLength(500)]
        public string? Note { get; set; }
    }

    /// <summary>Kết quả quy đổi một số tiền ngoại tệ sang VND, kèm tỷ giá đã dùng.</summary>
    public class CurrencyConversionDto
    {
        public string Currency { get; set; } = string.Empty;
        public decimal ExchangeRate { get; set; }
        public decimal AmountOriginal { get; set; }
        public decimal AmountVnd { get; set; }
    }
}
