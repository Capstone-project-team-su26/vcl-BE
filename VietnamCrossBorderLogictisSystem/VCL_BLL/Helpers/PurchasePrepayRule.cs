using System;
using System.Collections.Generic;
using VCL_BLL.DTOs;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Quy tắc TRẢ TRƯỚC của báo giá mua hộ — một nguồn duy nhất cho số tiền khách trả khi xác nhận báo giá.
    /// confirm-and-pay tạo khoản thu đúng <see cref="PurchaseQuotation.PrepayAmount"/>; mọi response báo giá
    /// trả sẵn số đó ở <see cref="PurchaseQuotationResponseDto.DepositAmount"/> để web khách hiện đúng số,
    /// không tự tính lại theo công thức cọc đời cũ.
    /// </summary>
    public static class PurchasePrepayRule
    {
        public const string Description =
            "Trả trước 100% tiền hàng + phí mua hộ + ship nội địa + phụ phí (+ VAT phần phí). "
            + "Cước quốc tế, VAT cước và thuế nhập khẩu là tạm tính, thu khi hàng về VN theo cân đo thật.";

        public const string LegacyMessage =
            "Báo giá này lập theo cách tính cũ. Vui lòng liên hệ Sale để lập lại báo giá.";

        /// <summary>
        /// Thuế NK (theo loại hàng, thu ở chặng VN) và VAT (theo rule VAT) do backend tự tính khi lập báo giá.
        /// Sale/FE gửi kèm các rule này như "phụ phí" thì bị tính HAI lần và thuế NK bị thu trước.
        /// </summary>
        private static readonly HashSet<string> AutoComputedTaxCodes = new(StringComparer.OrdinalIgnoreCase)
        { "IMPORT_TAX", "VAT" };

        public static bool IsAutoComputedTax(PricingRule? rule, string? feeType) =>
            AutoComputedTaxCodes.Contains(rule?.RuleCode?.Trim() ?? string.Empty)
            || AutoComputedTaxCodes.Contains(rule?.RuleType?.Trim() ?? string.Empty)
            || AutoComputedTaxCodes.Contains(feeType?.Trim() ?? string.Empty);

        private static readonly HashSet<string> DomesticShippingCodes = new(StringComparer.OrdinalIgnoreCase)
        { "DOMESTIC_FEE", "DOMESTIC_SHIPPING" };

        /// <summary>
        /// Phí ship nội địa nước ngoài (NCC → kho nguồn): rule DOMESTIC_FEE hoặc phụ phí gắn loại DOMESTIC_*.
        /// Báo giá mua hộ có ô riêng DomesticShippingFee — phụ phí loại này không được cộng thêm lần nữa.
        /// </summary>
        public static bool IsDomesticShippingFee(PricingRule? rule, string? feeType) =>
            DomesticShippingCodes.Contains(rule?.RuleCode?.Trim() ?? string.Empty)
            || DomesticShippingCodes.Contains(rule?.RuleType?.Trim() ?? string.Empty)
            || DomesticShippingCodes.Contains(rule?.ConditionType?.Trim() ?? string.Empty)
            || DomesticShippingCodes.Contains(feeType?.Trim() ?? string.Empty);

        /// <summary>Điền phần trả trước / tạm tính / số khách phải trả khi xác nhận vào DTO báo giá.</summary>
        public static void Fill(PurchaseQuotationResponseDto dto, PurchaseQuotation quotation)
        {
            var isLegacy = !quotation.PrepayAmount.HasValue;
            dto.IsLegacy = isLegacy;
            dto.PrepayAmount = quotation.PrepayAmount ?? 0m;
            dto.EstimatedLaterAmount = quotation.EstimatedLaterAmount ?? 0m;
            dto.CanPayOnline = !isLegacy && dto.PrepayAmount > 0m;
            dto.DepositAmount = isLegacy ? 0m : dto.PrepayAmount;
            dto.RemainingAmount = isLegacy ? 0m : dto.EstimatedLaterAmount;
            dto.DepositDescription = isLegacy ? LegacyMessage : Description;
        }
    }
}
