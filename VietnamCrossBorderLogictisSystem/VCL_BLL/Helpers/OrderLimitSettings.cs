using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Mô tả một giới hạn tạo đơn mà Admin chỉnh được: khoá lưu ở SYSTEM_SETTINGS, nhãn/đơn vị để
    /// web hiển thị, giá trị mặc định (= số cứng trước đây) và khoảng hợp lệ khi Admin sửa.
    /// </summary>
    public sealed record OrderLimitDefinition(
        string Key,
        string Scope,
        string Label,
        string Unit,
        decimal? DefaultValue,
        bool AllowUnlimited,
        bool IsInteger,
        decimal MinValue,
        decimal MaxValue,
        string Description);

    /// <summary>Giá trị giới hạn đang áp dụng (null = không giới hạn).</summary>
    public sealed class OrderLimits
    {
        // Ký gửi — mỗi kiện
        public decimal? MaxParcelWeightKg { get; init; }
        public decimal? MaxParcelLengthCm { get; init; }
        public decimal? MaxParcelWidthCm { get; init; }
        public decimal? MaxParcelHeightCm { get; init; }
        public int? MaxParcelQuantity { get; init; }
        public decimal? MaxItemDeclaredValue { get; init; }

        // Ký gửi — cả đơn
        public decimal? MaxTotalWeightKg { get; init; }
        public decimal? MaxTotalDeclaredValue { get; init; }
        public int MaxPackages { get; init; }

        // Mua hộ
        public int MaxPurchaseItems { get; init; }
        public int? MaxPurchaseItemQuantity { get; init; }
    }

    /// <summary>
    /// GIỚI HẠN TẠO ĐƠN KÝ GỬI / MUA HỘ — Admin chỉnh trên web (bảng SYSTEM_SETTINGS, nhóm ORDER_LIMITS).
    /// <para>
    /// Mặc định giữ đúng các số cứng trước đây (web khách + Sale và <c>ConsignmentSettings:Limits</c>),
    /// nên chưa ai sửa thì hành vi không đổi. Khoá chưa có dòng trong DB thì dùng mặc định.
    /// </para>
    /// <para>
    /// Không liên quan tới trần cân <c>ParcelService.MaxParcelWeightKg</c> (1000 kg) lúc kho nhập kiện:
    /// đó là chặn gõ nhầm của kho, còn đây là hạn mức khách/Sale khai lúc tạo đơn. Trần cân mỗi kiện
    /// ở đây không cho đặt quá 1000 kg để hai số không mâu thuẫn.
    /// </para>
    /// </summary>
    public static class OrderLimitSettings
    {
        public const string Group = "ORDER_LIMITS";
        public const string ScopeConsignment = "CONSIGNMENT";
        public const string ScopePurchase = "PURCHASE";

        public const string MaxParcelWeightKgKey = "ORDER_MAX_PARCEL_WEIGHT_KG";
        public const string MaxParcelLengthCmKey = "ORDER_MAX_PARCEL_LENGTH_CM";
        public const string MaxParcelWidthCmKey = "ORDER_MAX_PARCEL_WIDTH_CM";
        public const string MaxParcelHeightCmKey = "ORDER_MAX_PARCEL_HEIGHT_CM";
        public const string MaxParcelQuantityKey = "ORDER_MAX_PARCEL_QUANTITY";
        public const string MaxItemDeclaredValueKey = "ORDER_MAX_ITEM_DECLARED_VALUE";
        public const string MaxTotalWeightKgKey = "ORDER_MAX_TOTAL_WEIGHT_KG";
        public const string MaxTotalDeclaredValueKey = "ORDER_MAX_TOTAL_DECLARED_VALUE";
        public const string MaxPackagesKey = "ORDER_MAX_PACKAGES";
        public const string MaxPurchaseItemsKey = "PURCHASE_MAX_ITEMS";
        public const string MaxPurchaseItemQuantityKey = "PURCHASE_MAX_ITEM_QUANTITY";

        /// <summary>Trần cứng số kiện / số dòng một yêu cầu — chặn payload khổng lồ, Admin không nâng quá.</summary>
        public const int HardMaxItemsPerRequest = InputLimits.HardMaxItemsPerRequest;

        private const decimal MaxMoney = 100_000_000_000m;   // 100 tỷ đ
        private const decimal MaxParcelKg = 1000m;            // khớp ParcelService.MaxParcelWeightKg
        private const decimal MaxOrderKg = 100_000m;
        private const decimal MaxDimensionCm = 1000m;

        public static readonly IReadOnlyList<OrderLimitDefinition> Definitions = new List<OrderLimitDefinition>
        {
            new(MaxTotalWeightKgKey, ScopeConsignment, "Tổng cân nặng tối đa của đơn ký gửi", "kg",
                5m, true, false, 0.01m, MaxOrderKg,
                "Cộng cân nặng khai báo của mọi kiện trong một đơn ký gửi."),
            new(MaxTotalDeclaredValueKey, ScopeConsignment, "Tổng giá trị khai báo tối đa của đơn ký gửi", "đ",
                10_000_000m, true, true, 1m, MaxMoney,
                "Cộng giá trị khai báo của mọi kiện trong một đơn ký gửi."),
            new(MaxParcelWeightKgKey, ScopeConsignment, "Cân nặng tối đa mỗi kiện", "kg",
                3m, true, false, 0.01m, MaxParcelKg,
                "Cân nặng khách/Sale khai cho một kiện. Không quá 1.000 kg (trần cân khi kho nhập kiện)."),
            new(MaxItemDeclaredValueKey, ScopeConsignment, "Giá trị khai báo tối đa mỗi kiện", "đ",
                6_000_000m, true, true, 1m, MaxMoney,
                "Giá trị hàng khai báo cho một kiện."),
            new(MaxParcelQuantityKey, ScopeConsignment, "Số sản phẩm tối đa mỗi kiện", "sản phẩm",
                5m, true, true, 1m, InputLimits.HardMaxConsignmentItemQuantity,
                "Số lượng sản phẩm đóng trong một kiện."),
            new(MaxParcelLengthCmKey, ScopeConsignment, "Chiều dài tối đa mỗi kiện", "cm",
                100m, true, false, 0.1m, MaxDimensionCm,
                "Chiều dài khai báo của một kiện."),
            new(MaxParcelWidthCmKey, ScopeConsignment, "Chiều rộng tối đa mỗi kiện", "cm",
                200m, true, false, 0.1m, MaxDimensionCm,
                "Chiều rộng khai báo của một kiện."),
            new(MaxParcelHeightCmKey, ScopeConsignment, "Chiều cao tối đa mỗi kiện", "cm",
                50m, true, false, 0.1m, MaxDimensionCm,
                "Chiều cao khai báo của một kiện."),
            new(MaxPackagesKey, ScopeConsignment, "Số kiện tối đa của một đơn ký gửi", "kiện",
                InputLimits.MaxItemsPerRequest, false, true, 1m, HardMaxItemsPerRequest,
                $"Bắt buộc có giới hạn, tối đa {HardMaxItemsPerRequest} kiện."),
            new(MaxPurchaseItemsKey, ScopePurchase, "Số dòng sản phẩm tối đa của một yêu cầu mua hộ", "sản phẩm",
                InputLimits.MaxItemsPerRequest, false, true, 1m, HardMaxItemsPerRequest,
                $"Bắt buộc có giới hạn, tối đa {HardMaxItemsPerRequest} dòng."),
            new(MaxPurchaseItemQuantityKey, ScopePurchase, "Số lượng tối đa mỗi sản phẩm mua hộ", "cái",
                InputLimits.MaxPurchaseItemQuantity, true, true, 1m, InputLimits.HardMaxPurchaseItemQuantity,
                "Số lượng khách đặt cho một dòng sản phẩm."),
        };

        private static readonly Dictionary<string, OrderLimitDefinition> ByKey =
            Definitions.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

        private static readonly CultureInfo Vi = new("vi-VN");

        public static OrderLimitDefinition? Find(string? key) =>
            key != null && ByKey.TryGetValue(key.Trim(), out var def) ? def : null;

        /// <summary>Đọc giới hạn đang áp dụng. Khoá chưa có dòng trong DB → mặc định.</summary>
        public static async Task<OrderLimits> LoadAsync(IUnitOfWork unitOfWork)
        {
            var rows = await unitOfWork.Repository<SystemSetting>().FindAsync(s => s.SettingGroup == Group);
            return Resolve(rows);
        }

        public static OrderLimits Resolve(IEnumerable<SystemSetting> rows)
        {
            var values = ResolveValues(rows);

            decimal? Dec(string key) => values[key];
            int? Int(string key) => values[key].HasValue ? (int)Math.Min(int.MaxValue, Math.Floor(values[key]!.Value)) : null;
            int Required(string key) => Int(key) ?? (int)Find(key)!.DefaultValue!.Value;

            return new OrderLimits
            {
                MaxParcelWeightKg = Dec(MaxParcelWeightKgKey),
                MaxParcelLengthCm = Dec(MaxParcelLengthCmKey),
                MaxParcelWidthCm = Dec(MaxParcelWidthCmKey),
                MaxParcelHeightCm = Dec(MaxParcelHeightCmKey),
                MaxParcelQuantity = Int(MaxParcelQuantityKey),
                MaxItemDeclaredValue = Dec(MaxItemDeclaredValueKey),
                MaxTotalWeightKg = Dec(MaxTotalWeightKgKey),
                MaxTotalDeclaredValue = Dec(MaxTotalDeclaredValueKey),
                MaxPackages = Required(MaxPackagesKey),
                MaxPurchaseItems = Required(MaxPurchaseItemsKey),
                MaxPurchaseItemQuantity = Int(MaxPurchaseItemQuantityKey),
            };
        }

        /// <summary>Khoá → giá trị đang áp dụng (null = không giới hạn), đủ mọi khoá đã định nghĩa.</summary>
        public static Dictionary<string, decimal?> ResolveValues(IEnumerable<SystemSetting> rows)
        {
            var stored = (rows ?? Enumerable.Empty<SystemSetting>())
                .Where(r => r?.SettingKey != null)
                .GroupBy(r => r.SettingKey.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
            foreach (var def in Definitions)
            {
                if (!stored.TryGetValue(def.Key, out var row))
                {
                    result[def.Key] = def.DefaultValue;
                    continue;
                }

                var value = row.DecimalValue;
                // Dòng bị sửa tay thành null / số âm cho khoá bắt buộc → quay về mặc định, không để hở.
                if (value.HasValue && value.Value <= 0) value = def.AllowUnlimited ? null : def.DefaultValue;
                if (!value.HasValue && !def.AllowUnlimited) value = def.DefaultValue;
                result[def.Key] = value;
            }
            return result;
        }

        /// <summary>
        /// Kiểm một giá trị Admin gửi lên. Trả về câu lỗi tiếng Việt, hoặc null nếu hợp lệ.
        /// </summary>
        public static string? Validate(OrderLimitDefinition def, decimal? value)
        {
            if (!value.HasValue)
                return def.AllowUnlimited ? null : $"\"{def.Label}\" bắt buộc phải có giới hạn.";

            var v = value.Value;
            if (v <= 0)
                return $"\"{def.Label}\" phải lớn hơn 0 (để trống nếu muốn không giới hạn).";
            if (def.IsInteger && decimal.Truncate(v) != v)
                return $"\"{def.Label}\" phải là số nguyên.";
            if (decimal.Round(v, 2) != v)
                return $"\"{def.Label}\" tối đa 2 chữ số thập phân.";
            if (v < def.MinValue)
                return $"\"{def.Label}\" tối thiểu {FormatValue(def, def.MinValue)}.";
            if (v > def.MaxValue)
                return $"\"{def.Label}\" tối đa {FormatValue(def, def.MaxValue)}.";
            return null;
        }

        public static string FormatValue(OrderLimitDefinition def, decimal value) =>
            def.Unit == "đ"
                ? $"{value.ToString("#,##0", Vi)} đ"
                : $"{value.ToString("#,##0.##", Vi)} {def.Unit}";

        public static string Kg(decimal value) => value.ToString("#,##0.##", Vi);

        public static string Cm(decimal value) => value.ToString("#,##0.##", Vi);

        public static string Money(decimal value) => value.ToString("#,##0", Vi);
    }
}
