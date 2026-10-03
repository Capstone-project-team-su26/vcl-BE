using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Đổi giá trị cột PRODUCT_TYPE của dòng hàng sang tên loại hàng để trả cho FE.
    /// </summary>
    /// <remarks>
    /// ORDER_ITEMS.PRODUCT_TYPE (và các bảng chép lại từ nó) lưu Id của PRODUCT_TYPES từ khi có
    /// bảng này; đơn cũ và đơn sinh từ mua hộ lưu thẳng tên. Trả nguyên cột ra thì màn hình in
    /// GUID thay vì tên, nên mọi DTO dòng hàng kèm thêm <c>ProductTypeName</c> tra từ đây.
    /// Tra theo lô: một truy vấn cho cả danh sách Id, không tra từng dòng.
    /// </remarks>
    public static class ProductTypeNames
    {
        /// <summary>Tên hiển thị khi dòng hàng chưa có loại hoặc Id loại không còn trong danh mục.</summary>
        public const string Unclassified = "Chưa phân loại";

        /// <summary>
        /// Tải tên các loại hàng được tham chiếu bởi <paramref name="rawValues"/> (chỉ giá trị dạng GUID),
        /// kể cả loại đã ngừng dùng — đơn cũ vẫn phải hiện đúng tên.
        /// </summary>
        public static async Task<IReadOnlyDictionary<Guid, string>> LoadAsync(
            IUnitOfWork unitOfWork, IEnumerable<string?> rawValues)
        {
            var ids = rawValues
                .Select(v => Guid.TryParse(v?.Trim(), out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                return new Dictionary<Guid, string>();
            }

            var types = await unitOfWork.Repository<ProductType>().FindAsync(t => ids.Contains(t.Id));
            return types
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .GroupBy(t => t.Id)
                .ToDictionary(g => g.Key, g => g.First().Name.Trim());
        }

        /// <summary>
        /// Tên loại hàng của một giá trị PRODUCT_TYPE. GUID tra được thì trả tên; GUID không tra được
        /// trả null (không bao giờ trả GUID cho FE in ra); chuỗi thường là tên lưu kiểu cũ thì giữ nguyên.
        /// </summary>
        public static string? Resolve(IReadOnlyDictionary<Guid, string> names, string? rawValue)
        {
            var raw = rawValue?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            if (Guid.TryParse(raw, out var id))
            {
                return names.TryGetValue(id, out var name) ? name : null;
            }

            return raw;
        }
    }
}
