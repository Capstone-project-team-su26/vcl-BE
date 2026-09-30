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
    /// Ảnh bằng chứng kho VN tiếp nhận kiện từ lô quốc tế (<see cref="AttachmentDocumentTypes.VnArrivalProof"/>).
    /// <para>
    /// Kiện đã về tới kho mà không có ảnh thì "kho đã nhận, hàng nguyên vẹn" chỉ là lời nói — lúc
    /// khách khiếu nại hay đòi hãng bồi thường không có gì để đưa ra. Cùng nguyên tắc với ảnh xếp
    /// kệ ở kho nguồn: không có ảnh thì không có nút cho kiện sang bước sau.
    /// </para>
    /// Không đổi schema: bảng ATTACHMENTS đã có liên kết theo loại (entity_type + entity_id).
    /// Ảnh từng kiện gắn vào PARCEL, ảnh toàn lô (mở container) gắn vào SHIPMENT.
    /// </summary>
    public static class VnArrivalPhotos
    {
        /// <summary>Ảnh tiếp nhận VN của các đối tượng (PARCEL hoặc SHIPMENT), theo id, cũ trước mới sau.</summary>
        public static async Task<Dictionary<Guid, List<AttachmentDto>>> LoadAsync(
            IUnitOfWork unitOfWork, string entityType, IEnumerable<Guid>? entityIds = null)
        {
            var type = entityType.Trim().ToUpperInvariant();
            var docType = AttachmentDocumentTypes.VnArrivalProof;
            var ids = entityIds?.Distinct().ToList();
            if (ids != null && ids.Count == 0) return new Dictionary<Guid, List<AttachmentDto>>();

            var rows = ids == null
                ? await unitOfWork.Repository<Attachment>().FindAsync(a => a.EntityType == type && a.DocumentType == docType)
                : await unitOfWork.Repository<Attachment>().FindAsync(
                    a => a.EntityType == type && a.DocumentType == docType && ids.Contains(a.EntityId));

            var list = rows.ToList();
            if (list.Count == 0) return new Dictionary<Guid, List<AttachmentDto>>();

            var uploaderIds = list.Where(a => a.UploadedById.HasValue).Select(a => a.UploadedById!.Value).Distinct().ToList();
            var names = uploaderIds.Count == 0
                ? new Dictionary<Guid, string>()
                : (await unitOfWork.Repository<User>().FindAsync(u => uploaderIds.Contains(u.Id)))
                    .ToDictionary(u => u.Id, u => u.FullName);

            return list
                .GroupBy(a => a.EntityId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(a => a.UploadedAt)
                        .Select(a => ToDto(a, a.UploadedById.HasValue && names.TryGetValue(a.UploadedById.Value, out var n) ? n : null))
                        .ToList());
        }

        /// <summary>Ảnh của một đối tượng lấy từ bảng đã nạp; không có thì trả danh sách rỗng (không null).</summary>
        public static List<AttachmentDto> Of(Dictionary<Guid, List<AttachmentDto>> photos, Guid entityId) =>
            photos.TryGetValue(entityId, out var list) ? list : new List<AttachmentDto>();

        /// <summary>
        /// Kho đích của lô quốc tế chở kiện này — nơi kiện được tiếp nhận ở VN. Null khi kiện chưa
        /// nằm trong lô quốc tế nào.
        /// </summary>
        public static async Task<Guid?> DestinationWarehouseOfParcelAsync(IUnitOfWork unitOfWork, Guid parcelId)
        {
            var shipmentIds = (await unitOfWork.Repository<ShipmentParcel>().FindAsync(sp => sp.ParcelId == parcelId))
                .Select(sp => sp.ShipmentId)
                .Distinct()
                .ToList();
            if (shipmentIds.Count == 0) return null;

            return (await unitOfWork.Repository<Shipment>().FindAsync(s => shipmentIds.Contains(s.Id)))
                .Where(s => s.DestinationWarehouseId.HasValue
                            && string.Equals(s.ShipmentStage, "INTERNATIONAL", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.ShippedAt ?? DateTime.MinValue)
                .Select(s => s.DestinationWarehouseId)
                .FirstOrDefault();
        }

        /// <summary>
        /// Gắn lại ảnh tiếp nhận của kiện vào sự cố vừa mở lúc tiếp nhận, làm ảnh hiện trạng
        /// (INCIDENT_PHOTO). Kho đã chụp đúng cái thùng hỏng rồi — bắt chụp/tải lại lần nữa cho
        /// sự cố chỉ tốn mạng và thời gian. Dùng chung file trên đĩa (giấy tờ không có API xoá).
        /// </summary>
        public static async Task CopyToIncidentAsync(
            IUnitOfWork unitOfWork, IEnumerable<AttachmentDto> photos, Guid incidentId, string packageCode, DateTime now)
        {
            var ids = photos.Select(p => p.Id).ToList();
            if (ids.Count == 0) return;

            var repo = unitOfWork.Repository<Attachment>();
            foreach (var source in await repo.FindAsync(a => ids.Contains(a.Id)))
            {
                await repo.AddAsync(new Attachment
                {
                    Id = Guid.NewGuid(),
                    EntityType = AttachmentEntityTypes.Incident,
                    EntityId = incidentId,
                    DocumentType = AttachmentDocumentTypes.IncidentPhoto,
                    FileName = source.FileName,
                    ContentType = source.ContentType,
                    Size = source.Size,
                    Sha256 = source.Sha256,
                    StorageKey = source.StorageKey,
                    UploadedById = source.UploadedById,
                    UploadedAt = now,
                    Note = $"Ảnh kho VN chụp lúc tiếp nhận kiện {packageCode}",
                });
            }
        }

        public static AttachmentDto ToDto(Attachment a, string? uploaderName) => new()
        {
            Id = a.Id,
            EntityType = a.EntityType,
            EntityId = a.EntityId,
            DocumentType = a.DocumentType,
            FileName = a.FileName,
            ContentType = a.ContentType,
            Size = a.Size,
            Note = a.Note,
            UploadedById = a.UploadedById,
            UploadedByName = uploaderName,
            UploadedAt = a.UploadedAt,
            DownloadUrl = $"/api/attachments/{a.Id}/download"
        };
    }
}
