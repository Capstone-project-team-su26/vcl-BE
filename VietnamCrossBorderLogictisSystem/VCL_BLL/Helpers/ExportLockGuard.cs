using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Kiện đang nằm trong một phiếu xuất còn hiệu lực thì không tách, không gộp, không chuyển ô
    /// bằng API điều chuyển: phiếu đã chốt danh sách kiện (và có thể đã được duyệt), nên đổi kiện
    /// sau lưng phiếu là làm sổ sách lệch với tờ giấy kho đang cầm.
    /// </summary>
    public static class ExportLockGuard
    {
        public static async Task EnsureNotInActiveReleaseAsync(
            IUnitOfWork unitOfWork, IEnumerable<Guid> parcelIds, string action)
        {
            var ids = parcelIds?.Where(id => id != Guid.Empty).ToHashSet();
            if (ids == null || ids.Count == 0) return;

            var activeRequests = (await unitOfWork.Repository<WroRequest>().GetAllAsync())
                .Where(w => ExportEligibilityContext.ActiveWroStatuses.Contains(w.Status?.Trim() ?? string.Empty))
                .ToDictionary(w => w.Id, w => w.WroCode);
            if (activeRequests.Count == 0) return;

            var blocked = (await unitOfWork.Repository<WroParcel>().GetAllAsync())
                .Where(r => ids.Contains(r.ParcelId)
                            && activeRequests.ContainsKey(r.WroRequestId)
                            && !string.Equals(r.Status?.Trim(), WroParcelStatuses.Removed, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (blocked.Count == 0) return;

            var parcels = (await unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => ids.Contains(p.Id))
                .ToDictionary(p => p.Id, p => p.PackageCode);

            var detail = string.Join(", ", blocked.Select(r =>
                $"{(parcels.TryGetValue(r.ParcelId, out var code) ? code : r.ParcelId.ToString())} (phiếu {activeRequests[r.WroRequestId]})"));

            throw new ArgumentException(
                $"Không {action} được: kiện đang nằm trong phiếu xuất kho — {detail}. "
                + "Muốn đổi thì bỏ kiện khỏi phiếu trước (POST /api/warehouse-release-requests/{id}/parcels/{parcelId}/remove).");
        }
    }
}
