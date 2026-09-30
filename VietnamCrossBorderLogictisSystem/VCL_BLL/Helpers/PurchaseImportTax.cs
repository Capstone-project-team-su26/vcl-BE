using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Thuế nhập khẩu của đơn kho mua hộ (PUR-xxx-n): thu MỘT lần trên hoá đơn chặng VN, tính trên
    /// tiền hàng theo giá mua thật của từng dòng. Phần hàng không tới tay khách (huỷ theo sự cố,
    /// thất lạc, NCC giao thiếu) thì không thu — ghi dòng <see cref="AdjustmentFeeType"/> âm.
    /// </summary>
    public static class PurchaseImportTax
    {
        /// <summary>Dòng hoá đơn điều chỉnh thuế NK (âm = giảm cho khách), cùng kiểu FREIGHT_ADJUSTMENT.</summary>
        public const string AdjustmentFeeType = "TAX_ADJUSTMENT";

        /// <summary>Tên dòng phí thuế NK trên báo giá đơn kho mua hộ.</summary>
        public const string FeeNamePrefix = "Thuế nhập khẩu";

        /// <summary>Kiện ở các trạng thái này không tới tay khách → không thu thuế NK phần hàng trong kiện.</summary>
        public static readonly HashSet<string> NotDeliveredParcelStatuses = new(StringComparer.OrdinalIgnoreCase)
        { VnParcelStatuses.Disposed, "LOST" };

        public static decimal RateOf(IEnumerable<ProductType> categories, string? productType)
        {
            var raw = productType?.Trim() ?? string.Empty;
            var category = Guid.TryParse(raw, out var typeId)
                ? categories.FirstOrDefault(c => c.Id == typeId)
                : categories.FirstOrDefault(c => string.Equals(c.Name?.Trim(), raw, StringComparison.OrdinalIgnoreCase));
            return category?.ImportTaxRate ?? 0m;
        }

        /// <summary>Dòng hàng của đơn kho ứng với một dòng đơn mua (đơn kho sinh từ đúng các dòng này, cùng tên).</summary>
        public static OrderItem? MatchOrderItem(IEnumerable<OrderItem> orderItems, PurchaseOrderItem poItem)
        {
            var sameName = orderItems
                .Where(o => string.Equals(o.ProductName?.Trim(), poItem.ProductName?.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            return sameName.FirstOrDefault(o => (o.Quantity ?? 0) == poItem.Quantity) ?? sameName.FirstOrDefault();
        }

        /// <summary>
        /// Thuế NK đã ghi trên báo giá / hoá đơn đơn kho cho từng dòng hàng. Lấy đúng số đã báo
        /// (dòng phí có OrderItemId, hoặc khớp tên sản phẩm ở ghi chú với báo giá cũ); không có dòng
        /// phí thì tính lại theo thuế suất danh mục × giá mua.
        /// </summary>
        public static async Task<Dictionary<Guid, decimal>> ByOrderItemAsync(IUnitOfWork unitOfWork, Order order, List<OrderItem> orderItems)
        {
            var quotationIds = (await unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == order.Id && !string.Equals(q.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                .Select(q => q.Id)
                .ToHashSet();
            var taxFees = (await unitOfWork.Repository<QuotationFee>().GetAllAsync())
                .Where(f => f.QuotationId.HasValue && quotationIds.Contains(f.QuotationId.Value)
                            && string.Equals(f.FeeType, "TAX", StringComparison.OrdinalIgnoreCase)
                            && (f.FeeName ?? string.Empty).StartsWith(FeeNamePrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var result = new Dictionary<Guid, decimal>();
            var used = new HashSet<Guid>();
            foreach (var item in orderItems)
            {
                var own = taxFees.Where(f => f.OrderItemId == item.Id).ToList();
                if (own.Count == 0)
                {
                    var byNote = taxFees.FirstOrDefault(f => !f.OrderItemId.HasValue && !used.Contains(f.Id)
                        && (f.Note ?? string.Empty).EndsWith($"({item.ProductName})", StringComparison.OrdinalIgnoreCase));
                    if (byNote != null) own.Add(byNote);
                }
                foreach (var f in own) used.Add(f.Id);
                if (own.Count > 0)
                {
                    result[item.Id] = own.Sum(f => f.Amount);
                }
            }

            var missing = orderItems.Where(i => !result.ContainsKey(i.Id)).ToList();
            if (missing.Count > 0)
            {
                // Không có dòng thuế riêng: có thể thuế suất 0 (không sinh dòng), hoặc báo giá cũ.
                var categories = (await unitOfWork.Repository<ProductType>().GetAllAsync()).ToList();
                var hasAnyTaxFee = taxFees.Count > 0;
                foreach (var item in missing)
                {
                    result[item.Id] = hasAnyTaxFee && taxFees.All(f => used.Contains(f.Id))
                        ? 0m
                        : Math.Round((item.DeclaredValue ?? 0m) * RateOf(categories, item.ProductType), 0);
                }
            }

            return result;
        }
    }
}
