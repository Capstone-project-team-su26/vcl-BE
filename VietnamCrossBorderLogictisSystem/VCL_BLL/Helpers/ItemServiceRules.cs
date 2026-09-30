using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Dịch vụ khách chọn theo từng kiện của đơn ký gửi: dịch vụ nào được chọn, tính phí thế nào
    /// và đọc lại dịch vụ của các kiện để trả cho màn hình.
    /// </summary>
    /// <remarks>
    /// Thùng gỗ không nằm ở đây: mỗi kiện chọn cấu hình thùng (PackageConfigurationId) và phí thùng
    /// lấy theo cấu hình đó. Rule đóng thùng gỗ trong PRICING_RULES vì vậy không cho chọn thêm,
    /// tránh tính hai lần cho cùng một cái thùng.
    /// </remarks>
    public static class ItemServiceRules
    {
        /// <summary>
        /// Rule HỆ THỐNG: chỉ là tham số tính toán (thuế suất, hệ số quy đổi, cân tối thiểu), không phải
        /// một khoản phí cộng vào báo giá. Vòng cộng phụ phí phải bỏ qua các rule này — nếu không, rule
        /// VOLUMETRIC_DIVISOR = 5000 bị cộng thành "phụ phí 5.000đ".
        /// </summary>
        private static readonly string[] SystemRuleKeywords =
        {
            "VAT", "IMPORT_TAX", "VOLUMETRIC_DIVISOR", "MIN_WEIGHT"
        };

        // Rule hệ thống (thuế, cước nội địa, hệ số quy đổi...) và rule của luồng mua hộ không phải
        // dịch vụ khách chọn cho kiện.
        private static readonly string[] NonSelectableKeywords =
            new[] { "DOMESTIC_FEE", "PURCHASE" }.Concat(SystemRuleKeywords).Concat(new[] { "WOOD", "CRATE" }).ToArray();

        /// <summary>Rule chỉ là tham số tính toán (VAT, thuế NK, hệ số DIM, cân tối thiểu) — không phải phụ phí.</summary>
        public static bool IsSystemRule(PricingRule rule)
        {
            if (rule == null) return false;
            var type = rule.RuleType?.ToUpperInvariant() ?? string.Empty;
            var code = rule.RuleCode?.ToUpperInvariant() ?? string.Empty;
            return SystemRuleKeywords.Any(k => type.Contains(k) || code.Contains(k));
        }

        public static bool IsSelectable(PricingRule rule)
        {
            if (rule == null || rule.IsRequired) return false;
            if (!string.Equals(rule.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)) return false;

            var type = rule.RuleType?.ToUpperInvariant() ?? string.Empty;
            var code = rule.RuleCode?.ToUpperInvariant() ?? string.Empty;
            return !NonSelectableKeywords.Any(k => type.Contains(k) || code.Contains(k));
        }

        public static bool IsInspection(PricingRule rule) =>
            string.Equals(rule.RuleType, "INSPECTION", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rule.ConditionType, "REQUIRES_INSPECTION", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Ngưỡng khai giá tối thiểu của rule, null nếu rule không đặt điều kiện này.
        /// Giá trị hỏng (không phải số) coi như không có điều kiện — dữ liệu danh mục sai thì
        /// chỉ mất phần kiểm, không chặn khách tạo đơn.
        /// </summary>
        public static decimal? GetMinDeclaredValue(PricingRule rule)
        {
            if (!string.Equals(rule?.ConditionType?.Trim(), "MIN_DECLARED_VALUE", StringComparison.OrdinalIgnoreCase))
                return null;

            return decimal.TryParse(
                rule.ConditionValue?.Trim(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var threshold) && threshold > 0m
                ? threshold
                : null;
        }

        /// <summary>
        /// Số tiền kiểu Việt Nam (5.000.000). Container chạy culture invariant nên ":N0" ra
        /// "5,000,000" — đọc sai kiểu số thập phân với người Việt.
        /// </summary>
        private static string FormatVnd(decimal amount) =>
            amount.ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".");

        private static bool IsPercentage(PricingRule rule) =>
            string.Equals(rule.CalculationType?.Trim(), "PERCENTAGE", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Kiểm dịch vụ khách chọn cho các kiện, trả về rule theo từng kiện (cùng thứ tự với
        /// <paramref name="items"/>).
        /// </summary>
        /// <param name="shippingPricingId">Dòng giá của phương án vận chuyển đơn đang chọn, để chặn rule chỉ áp cho phương án khác.</param>
        public static async Task<List<List<PricingRule>>> ResolveForItemsAsync(
            IUnitOfWork unitOfWork, IReadOnlyList<ConsignmentItemRequest> items, Guid? shippingPricingId)
        {
            var ruleMap = (await unitOfWork.Repository<PricingRule>().GetAllAsync()).ToDictionary(r => r.Id);
            var result = new List<List<PricingRule>>();

            foreach (var item in items)
            {
                var label = string.IsNullOrWhiteSpace(item.ProductName) ? "kiện hàng" : $"kiện \"{item.ProductName}\"";
                var chosen = new List<PricingRule>();

                foreach (var service in item.Services ?? new List<ConsignmentItemServiceRequest>())
                {
                    if (!ruleMap.TryGetValue(service.PricingRuleId, out var rule))
                        throw new ArgumentException($"Không tìm thấy dịch vụ với ID {service.PricingRuleId} ({label}).");

                    if (!IsSelectable(rule))
                        throw new ArgumentException(
                            $"\"{rule.RuleName}\" không phải dịch vụ chọn theo kiện ({label})."
                            + (rule.RuleType?.Contains("WOOD", StringComparison.OrdinalIgnoreCase) == true
                                ? " Thùng gỗ chọn qua cấu hình thùng (packageConfigurationId) của kiện."
                                : string.Empty));

                    if (rule.ServicePricingId.HasValue && rule.ServicePricingId != shippingPricingId)
                        throw new ArgumentException($"Dịch vụ \"{rule.RuleName}\" không áp dụng cho phương án vận chuyển đã chọn ({label}).");

                    if (chosen.Any(r => r.Id == rule.Id))
                        throw new ArgumentException($"Dịch vụ \"{rule.RuleName}\" bị chọn trùng ({label}).");

                    if (IsPercentage(rule) && (item.DeclaredValue ?? 0m) <= 0m)
                        throw new ArgumentException(
                            $"Dịch vụ \"{rule.RuleName}\" tính theo giá trị hàng nên {label} phải khai giá trị (declaredValue).");

                    // Ngưỡng khai giá của rule (bảo hiểm: MIN_DECLARED_VALUE = 5.000.000). Trước đây
                    // ngưỡng chỉ nằm trong mô tả nên không ai chặn, kiện khai 100.000 đ vẫn mua được
                    // bảo hiểm và phí rơi về mức sàn 10.000 đ.
                    var requiredDeclaredValue = GetMinDeclaredValue(rule);

                    if (requiredDeclaredValue.HasValue && (item.DeclaredValue ?? 0m) < requiredDeclaredValue.Value)
                        throw new ArgumentException(
                            $"Dịch vụ \"{rule.RuleName}\" chỉ áp dụng cho kiện khai giá từ {FormatVnd(requiredDeclaredValue.Value)} đ trở lên ({label}).");

                    chosen.Add(rule);
                }

                result.Add(chosen);
            }

            return result;
        }

        /// <summary>
        /// Phí một dịch vụ của một kiện. Kiện đã cân đo tại kho thì dùng cân nặng tính cước thật.
        /// </summary>
        public static decimal CalculateAmount(PricingRule rule, OrderItem item, Parcel? parcel)
        {
            var weight = parcel?.ChargeableWeight ?? parcel?.ActualWeight ?? item.Weight ?? 0m;
            var length = parcel?.Length ?? item.Length ?? 0m;
            var width = parcel?.Width ?? item.Width ?? 0m;
            var height = parcel?.Height ?? item.Height ?? 0m;

            decimal amount = rule.CalculationType?.Trim().ToUpperInvariant() switch
            {
                "FIXED" => rule.Value,
                "PERCENTAGE" => (item.DeclaredValue ?? 0m) * (rule.Value / 100m),
                "PER_KG" => weight * rule.Value,
                "PER_CBM" => length * width * height / 1000000m * rule.Value,
                "PER_PRODUCT" => (item.Quantity ?? 1) * rule.Value,
                _ => rule.Value
            };

            if (rule.MinAmount.HasValue && amount < rule.MinAmount.Value) amount = rule.MinAmount.Value;
            if (rule.MaxAmount.HasValue && amount > rule.MaxAmount.Value) amount = rule.MaxAmount.Value;

            return Math.Round(amount, 0);
        }

        public static ItemServiceDto ToDto(PricingRule rule) => new()
        {
            PricingRuleId = rule.Id,
            Code = rule.RuleCode ?? string.Empty,
            Name = rule.RuleName ?? string.Empty,
            RuleType = rule.RuleType ?? string.Empty,
            CalculationType = rule.CalculationType ?? string.Empty,
            Value = rule.Value,
            MinAmount = rule.MinAmount,
            MaxAmount = rule.MaxAmount,
            Description = rule.Description,
            ConditionType = rule.ConditionType,
            ConditionValue = rule.ConditionValue
        };

        /// <summary>Dịch vụ khách đã chọn của từng kiện, theo OrderItemId.</summary>
        public static async Task<Dictionary<Guid, List<PricingRule>>> LoadRulesAsync(
            IUnitOfWork unitOfWork, IEnumerable<Guid> orderItemIds)
        {
            var ids = orderItemIds.ToHashSet();
            if (ids.Count == 0) return new Dictionary<Guid, List<PricingRule>>();

            var ruleMap = (await unitOfWork.Repository<PricingRule>().GetAllAsync()).ToDictionary(r => r.Id);

            return (await unitOfWork.Repository<OrderItemService>().GetAllAsync())
                .Where(s => ids.Contains(s.OrderItemId) && ruleMap.ContainsKey(s.PricingRuleId))
                .OrderBy(s => s.CreatedAt)
                .GroupBy(s => s.OrderItemId)
                .ToDictionary(g => g.Key, g => g.Select(s => ruleMap[s.PricingRuleId]).ToList());
        }

        public static async Task<Dictionary<Guid, List<ItemServiceDto>>> LoadDtosAsync(
            IUnitOfWork unitOfWork, IEnumerable<Guid> orderItemIds) =>
            (await LoadRulesAsync(unitOfWork, orderItemIds))
                .ToDictionary(kv => kv.Key, kv => kv.Value.Select(ToDto).ToList());

        public static List<ItemServiceDto> For(IReadOnlyDictionary<Guid, List<ItemServiceDto>> map, Guid? orderItemId) =>
            orderItemId.HasValue && map.TryGetValue(orderItemId.Value, out var list) ? list : new List<ItemServiceDto>();

        public static PackageConfigurationResponseDto? MapPackageConfiguration(
            Guid? packageConfigurationId, IReadOnlyDictionary<Guid, PackageConfiguration> configMap)
        {
            if (!packageConfigurationId.HasValue || !configMap.TryGetValue(packageConfigurationId.Value, out var c))
                return null;

            return new PackageConfigurationResponseDto
            {
                Id = c.Id,
                ConfigCode = c.ConfigCode,
                ConfigName = c.ConfigName,
                Length = c.Length,
                Width = c.Width,
                Height = c.Height,
                MaxWeight = c.MaxWeight,
                PackageFee = c.PackageFee,
                Status = c.Status
            };
        }

        public static async Task<Dictionary<Guid, PackageConfiguration>> LoadPackageConfigurationsAsync(IUnitOfWork unitOfWork) =>
            (await unitOfWork.Repository<PackageConfiguration>().GetAllAsync()).ToDictionary(c => c.Id);
    }
}
