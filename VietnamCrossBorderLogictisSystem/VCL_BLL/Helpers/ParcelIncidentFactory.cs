using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Mở sự cố cho một kiện. Dùng chung cho kho tiếp nhận tự mở, NV kho mở tay và khách khiếu
    /// nại — cùng một cách sinh mã và cùng trạng thái đầu.
    /// </summary>
    public static class ParcelIncidentFactory
    {
        /// <param name="reservedCodes">
        /// Mã đã cấp trong cùng thao tác nhưng chưa lưu xuống DB (vd. tiếp nhận nhiều kiện hỏng một
        /// lúc). Truyền cùng một tập cho mọi lần gọi trong thao tác đó để mã không trùng nhau.
        /// </param>
        public static async Task<ParcelIncident> OpenAsync(
            IUnitOfWork unitOfWork, Parcel parcel, string incidentType, string stage, string? description, Guid? reportedById,
            System.Collections.Generic.HashSet<string>? reservedCodes = null)
        {
            var now = DateTime.UtcNow;
            var incident = new ParcelIncident
            {
                Id = Guid.NewGuid(),
                IncidentCode = await NextCodeAsync(unitOfWork, now, reservedCodes ?? new System.Collections.Generic.HashSet<string>()),
                ParcelId = parcel.Id,
                OrderId = parcel.ConsignmentId,
                IncidentType = incidentType,
                Stage = stage,
                Status = IncidentStatuses.Open,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                ReportedById = reportedById,
                ReportedAt = now
            };

            await unitOfWork.Repository<ParcelIncident>().AddAsync(incident);
            return incident;
        }

        /// <summary>Kiện đang có sự cố chưa xử lý xong.</summary>
        public static async Task<bool> HasOpenIncidentAsync(IUnitOfWork unitOfWork, Guid parcelId) =>
            (await unitOfWork.Repository<ParcelIncident>().GetAllAsync())
            .Any(i => i.ParcelId == parcelId
                      && !string.Equals(i.Status, IncidentStatuses.Resolved, StringComparison.OrdinalIgnoreCase));

        private static async Task<string> NextCodeAsync(
            IUnitOfWork unitOfWork, DateTime now, System.Collections.Generic.HashSet<string> reserved)
        {
            var prefix = $"INC-{now:yyyyMMdd}-";
            var existing = (await unitOfWork.Repository<ParcelIncident>().GetAllAsync())
                .Select(i => i.IncidentCode)
                .Where(c => c != null && c.StartsWith(prefix))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var i = 1; i <= 9999; i++)
            {
                var code = prefix + i.ToString("D4");
                if (existing.Contains(code) || reserved.Contains(code)) continue;
                reserved.Add(code);
                return code;
            }

            return prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        }
    }
}
