using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Hệ số quy đổi thể tích (DIM): kg quy đổi = dài × rộng × cao (cm) / hệ số.
    /// <para>
    /// MỘT nguồn đọc duy nhất cho báo giá, check-in, tách/gộp kiện, chi tiết đơn, phiếu nhập kho và
    /// tất toán VN — trước đây mỗi chỗ tự đọc một kiểu (có chỗ chỉ đọc file cấu hình), nên Admin sửa
    /// rule trong bảng giá thì mỗi màn hình ra một số cân khác nhau.
    /// </para>
    /// Thứ tự: rule VOLUMETRIC_DIVISOR đang ACTIVE gắn đúng bảng giá (ServicePricingId) → rule chung
    /// (không gắn bảng giá) → PricingSettings:VolumetricDivisor → 5000.
    /// </summary>
    public static class VolumetricDivisorResolver
    {
        public const decimal Default = 5000m;
        public const string RuleKey = "VOLUMETRIC_DIVISOR";
        public const string ConfigKey = "PricingSettings:VolumetricDivisor";

        public static bool IsDivisorRule(PricingRule rule) =>
            rule != null
            && (string.Equals(rule.RuleCode?.Trim(), RuleKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(rule.RuleType?.Trim(), RuleKey, StringComparison.OrdinalIgnoreCase));

        public static decimal Resolve(IEnumerable<PricingRule>? rules, IConfiguration? configuration, Guid? servicePricingId = null)
        {
            var candidates = (rules ?? Enumerable.Empty<PricingRule>())
                .Where(r => IsDivisorRule(r) && r.Value > 0m
                            && (string.IsNullOrWhiteSpace(r.Status) || string.Equals(r.Status.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (servicePricingId.HasValue)
            {
                var specific = candidates.FirstOrDefault(r => r.ServicePricingId == servicePricingId.Value);
                if (specific != null) return specific.Value;
            }

            var general = candidates.FirstOrDefault(r => r.ServicePricingId == null);
            if (general != null) return general.Value;

            return FromConfiguration(configuration);
        }

        public static async Task<decimal> ResolveAsync(IUnitOfWork unitOfWork, IConfiguration? configuration, Guid? servicePricingId = null)
        {
            var rules = await unitOfWork.Repository<PricingRule>().GetAllAsync();
            return Resolve(rules, configuration, servicePricingId);
        }

        public static decimal FromConfiguration(IConfiguration? configuration) =>
            decimal.TryParse(configuration?[ConfigKey], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var configured) && configured > 0m
                ? configured
                : Default;
    }
}
