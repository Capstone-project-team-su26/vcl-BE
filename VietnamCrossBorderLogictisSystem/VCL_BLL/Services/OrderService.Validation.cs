using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public partial class OrderService
    {
        public async Task<ValidateItemsResponse> ValidateConsignmentItemsAsync(ValidateItemsRequest request)
        {
            if (request.Items == null || request.Items.Count == 0)
                throw new ArgumentException("Phải có ít nhất một mặt hàng để kiểm tra.");

            var results = await ValidateItemsAgainstRestrictedAsync(request.Items);

            return new ValidateItemsResponse
            {
                // Có hàng BANNED → không cho tạo; hàng hạn chế/cảnh báo vẫn cho tạo.
                CanCreate = !results.Any(r => r.Level == "BANNED"),
                Results = results
            };
        }

        /// <summary>
        /// Đối chiếu từng mặt hàng với bảng RestrictedItem, phân loại 3 mức BANNED/RESTRICTED/WARNING.
        /// Chỉ trả về các mặt hàng có khớp (có vấn đề). Logic dùng chung cho cả create lẫn validate.
        /// </summary>
        private async Task<List<ItemValidationResult>> ValidateItemsAgainstRestrictedAsync(List<ConsignmentItemRequest> items)
        {
            var restrictedRepo = _unitOfWork.Repository<RestrictedItem>();
            var allRestricted = (await restrictedRepo.GetAllAsync()).ToList();

            var results = new List<ItemValidationResult>();

            foreach (var item in items)
            {
                foreach (var r in allRestricted)
                {
                    if (!r.IsActive) continue;
                    if (string.IsNullOrWhiteSpace(r.ItemName)) continue;

                    var matchesName = !string.IsNullOrWhiteSpace(item.ProductName)
                        && item.ProductName.Contains(r.ItemName, StringComparison.OrdinalIgnoreCase);
                    var matchesType = !string.IsNullOrWhiteSpace(item.ProductType)
                        && item.ProductType.Contains(r.ItemName, StringComparison.OrdinalIgnoreCase);

                    if (!matchesName && !matchesType) continue;

                    results.Add(new ItemValidationResult
                    {
                        ProductName = item.ProductName,
                        Level = MapRestrictionLevel(r.RestrictionType),
                        MatchedItem = r.ItemName,
                        Country = r.Country,
                        Reason = r.Note
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Quy đổi RestrictionType (free text trong DB) về 1 trong 3 mức chuẩn: BANNED / RESTRICTED / WARNING.
        /// </summary>
        private static string MapRestrictionLevel(string? restrictionType)
        {
            if (string.IsNullOrWhiteSpace(restrictionType))
                return "WARNING";

            var normalized = restrictionType.Trim().ToUpperInvariant();

            if (BannedRestrictionTypes.Contains(normalized))
                return "BANNED";
            if (RestrictedRestrictionTypes.Contains(normalized))
                return "RESTRICTED";

            return "WARNING";
        }
    }
}
