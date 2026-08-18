using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public partial class OrderService
    {
        /// <summary>
        /// Nhãn tiếng Việt của nguyện vọng khách về hướng xử lý tại kho VN.
        /// Trả null khi khách chưa chọn, để FE tự phân biệt "chưa chọn" với "chọn giao ngay".
        /// </summary>
        public static string? DescribeDestinationHandling(string? handling) =>
            handling?.Trim().ToUpperInvariant() switch
            {
                "DIRECT_DELIVERY" => "Giao ngay khi về VN",
                "STORE_AT_VN" => "Gửi lại kho VN",
                _ => null,
            };

        public static string MapRouteToDisplayName(string? route)
        {
            if (string.IsNullOrWhiteSpace(route)) return string.Empty;
            var trimmed = route.Trim();
            if (trimmed.Equals("CN-VN", StringComparison.OrdinalIgnoreCase) || 
                trimmed.Equals("CN", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("TQ-VN", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("TQ", StringComparison.OrdinalIgnoreCase))
            {
                return "Trung quốc --> Việt Nam";
            }
            if (trimmed.Equals("KR-VN", StringComparison.OrdinalIgnoreCase) || 
                trimmed.Equals("KR", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("HQ-VN", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("HQ", StringComparison.OrdinalIgnoreCase))
            {
                return "Hàn Quốc --> Việt Nam";
            }
            if (trimmed.Equals("JP-VN", StringComparison.OrdinalIgnoreCase) || 
                trimmed.Equals("JP", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("NB-VN", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("NB", StringComparison.OrdinalIgnoreCase))
            {
                return "Nhật Bản --> Việt Nam";
            }
            return trimmed;
        }

        public static string NormalizeRoute(string? route)
        {
            if (string.IsNullOrWhiteSpace(route)) return string.Empty;
            var trimmed = route.Trim().ToUpperInvariant();
            if (trimmed.Contains("CN") || trimmed.Contains("TQ") || trimmed.Contains("CHINA") || trimmed.Contains("TRUNG QUỐC") || trimmed.Contains("TRUNG QUOC"))
            {
                return "CN-VN";
            }
            if (trimmed.Contains("KR") || trimmed.Contains("HQ") || trimmed.Contains("KOREA") || trimmed.Contains("HÀN QUỐC") || trimmed.Contains("HAN QUOC"))
            {
                return "KR-VN";
            }
            if (trimmed.Contains("JP") || trimmed.Contains("NB") || trimmed.Contains("JAPAN") || trimmed.Contains("NHẬT BẢN") || trimmed.Contains("NHAT BAN"))
            {
                return "JP-VN";
            }
            return trimmed;
        }

        private async Task<string> GenerateUniqueConsignmentCodeAsync()
        {
            var allOrders = await _unitOfWork.Repository<Order>().GetAllAsync();
            var existingCodes = allOrders
                .Where(o => !string.IsNullOrEmpty(o.ConsignmentCode))
                .Select(o => o.ConsignmentCode!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var random = new Random();
            string code;
            do
            {
                code = $"VCL-{DateTime.UtcNow:yyyyMMddHHmmss}-{random.Next(100000, 999999)}";
            } while (existingCodes.Contains(code));

            return code;
        }

        public async Task<List<Guid>> ResolveServiceFlagRuleIdsAsync(
            bool requiresInspection, bool requiresPacking, bool requiresWoodenCrate, bool requiresInsurance)
        {
            var rules = new List<PricingRule>();
            await AddRulesFromServiceFlagsAsync(rules, requiresInspection, requiresPacking, requiresWoodenCrate, requiresInsurance);
            return rules.Select(r => r.Id).ToList();
        }

        private async Task AddRulesFromServiceFlagsAsync(
            List<PricingRule> selectedRules,
            bool requiresInspection, bool requiresPacking, bool requiresWoodenCrate, bool requiresInsurance)
        {
            if (!requiresInspection && !requiresPacking && !requiresWoodenCrate && !requiresInsurance)
                return;

            var activeRules = (await _unitOfWork.Repository<PricingRule>().GetAllAsync())
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            bool MatchType(PricingRule r, params string[] keywords)
            {
                var ruleType = r.RuleType?.ToUpperInvariant() ?? string.Empty;
                var condType = r.ConditionType?.ToUpperInvariant() ?? string.Empty;
                var code = r.RuleCode?.ToUpperInvariant() ?? string.Empty;
                return keywords.Any(k => ruleType.Contains(k) || condType.Contains(k) || code.Contains(k));
            }

            void AddMatches(Func<PricingRule, bool> predicate)
            {
                foreach (var rule in activeRules.Where(predicate))
                {
                    if (selectedRules.All(x => x.Id != rule.Id))
                        selectedRules.Add(rule);
                }
            }

            if (requiresInspection) AddMatches(r => MatchType(r, "INSPECTION"));
            if (requiresPacking) AddMatches(r => MatchType(r, "PACKING", "REPACK", "CARTON"));
            if (requiresWoodenCrate) AddMatches(r => MatchType(r, "WOODEN_BOX", "WOODEN", "CRATE"));
            if (requiresInsurance) AddMatches(r => MatchType(r, "INSURANCE"));
        }

        private bool IsBoxRule(PricingRule rule)
        {
            if (rule == null) return false;
            var code = rule.RuleCode?.ToUpperInvariant() ?? string.Empty;
            var name = rule.RuleName?.ToUpperInvariant() ?? string.Empty;
            var ruleType = rule.RuleType?.ToUpperInvariant() ?? string.Empty;
            var condType = rule.ConditionType?.ToUpperInvariant() ?? string.Empty;

            return ruleType.Contains("BOX") || ruleType.Contains("CRATE") ||
                   condType.Contains("BOX") ||
                   code.Contains("SMALL") || code.Contains("MEDIUM") || code.Contains("LARGE") || code.Contains("BUBBLE") ||
                   name.Contains("SMALL") || name.Contains("MEDIUM") || name.Contains("LARGE") || name.Contains("BUBBLE") ||
                   name.Contains("NHỎ") || name.Contains("VỪA") || name.Contains("LỚN") || name.Contains("XỐP HƠI") || name.Contains("BÓP NỔ") || name.Contains("MÀNG XỐP") || name.Contains("THÙNG GỖ") || name.Contains("ĐÔNG GỖ") || name.Contains("ĐÓNG GỖ");
        }

        private async Task<Guid?> SuggestPackageConfigurationIdAsync(decimal? length, decimal? width, decimal? height, decimal? weight)
        {
            var len = length ?? 0m;
            var w = width ?? 0m;
            var h = height ?? 0m;
            var wt = weight ?? 0m;

            var configs = await _unitOfWork.Repository<PackageConfiguration>().GetAllAsync();
            var activeConfigs = configs.Where(c => string.Equals(c.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)).ToList();

            var sortedDims = new[] { len, w, h }.OrderBy(d => d).ToList();

            var fittingConfig = activeConfigs
                .Where(c => !string.Equals(c.ConfigCode, "CUSTOM", StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Length * c.Width * c.Height)
                .FirstOrDefault(c =>
                {
                    var boxDims = new[] { c.Length, c.Width, c.Height }.OrderBy(d => d).ToList();
                    return sortedDims[0] <= boxDims[0] &&
                           sortedDims[1] <= boxDims[1] &&
                           sortedDims[2] <= boxDims[2] &&
                           wt <= c.MaxWeight;
                });

            if (fittingConfig != null) return fittingConfig.Id;

            var customConfig = activeConfigs.FirstOrDefault(c => string.Equals(c.ConfigCode, "CUSTOM", StringComparison.OrdinalIgnoreCase));
            return customConfig?.Id;
        }
    }
}
