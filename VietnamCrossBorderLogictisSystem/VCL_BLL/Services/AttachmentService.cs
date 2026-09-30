using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Lưu giấy tờ của luồng xuất kho vào thư mục riêng của server thay vì cloud công khai:
    /// tờ khai, biên bản bàn giao, giấy phép hàng hạn chế là dữ liệu của khách, không được để ai
    /// có link cũng tải được.
    /// </summary>
    public class AttachmentService : IAttachmentService
    {
        private const long MaxFileSize = 10 * 1024 * 1024;

        private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = ".pdf",
            ["image/jpeg"] = ".jpg",
            ["image/jpg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp"
        };

        private readonly IUnitOfWork _unitOfWork;
        private readonly string _root;

        public AttachmentService(IUnitOfWork unitOfWork, IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _root = configuration["App:AttachmentRoot"];
            if (string.IsNullOrWhiteSpace(_root)) _root = "/app/attachments";
        }

        public async Task<AttachmentDto> UploadAsync(Guid userId, string role, UploadAttachmentRequest request)
        {
            if (request?.Content == null)
                throw new ArgumentException("Chưa chọn file để tải lên.");

            var entityType = Normalize(request.EntityType);
            if (!AttachmentEntityTypes.All.Contains(entityType))
                throw new ArgumentException($"entityType không hợp lệ. Hợp lệ: {string.Join(", ", AttachmentEntityTypes.All)}.");

            var documentType = Normalize(request.DocumentType);
            if (!AttachmentDocumentTypes.All.Contains(documentType))
                throw new ArgumentException($"documentType không hợp lệ. Hợp lệ: {string.Join(", ", AttachmentDocumentTypes.All)}.");

            if (request.Length <= 0)
                throw new ArgumentException("File rỗng.");
            if (request.Length > MaxFileSize)
                throw new ArgumentException("File vượt quá 10 MB.");

            var contentType = request.ContentType?.Trim() ?? string.Empty;
            if (!AllowedContentTypes.TryGetValue(contentType, out var extension))
                throw new ArgumentException("Chỉ nhận file PDF, JPG, PNG hoặc WEBP.");

            await EnsureCanAccessAsync(userId, role, entityType, request.EntityId, documentType, forUpload: true);

            if (string.Equals(documentType, AttachmentDocumentTypes.VnArrivalProof, StringComparison.OrdinalIgnoreCase))
            {
                // Ảnh tiếp nhận kho VN là ẢNH chụp kiện — PDF không chứng minh được kiện trông ra sao.
                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Ảnh tiếp nhận kho VN phải là file ảnh JPG, PNG hoặc WEBP.");

                // Chỉ nhân viên kho VN được gán (hoặc chưa gán — giữ hành vi cũ) mới chụp cho kiện về kho mình.
                await WarehouseAccessPolicy.EnsureAssignedWarehouseAsync(
                    _unitOfWork, userId, role, await ArrivalWarehouseOfAsync(entityType, request.EntityId));
            }

            // Đọc hết vào bộ nhớ trước: cần băm SHA-256 để đối chiếu file trùng, mà stream của
            // multipart chỉ đọc được một lần. 10 MB là trần nên không có rủi ro phình bộ nhớ.
            using var buffer = new MemoryStream();
            await request.Content.CopyToAsync(buffer);
            var bytes = buffer.ToArray();
            if (bytes.LongLength > MaxFileSize)
                throw new ArgumentException("File vượt quá 10 MB.");

            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            var id = Guid.NewGuid();
            var storageKey = Path.Combine(entityType.ToLowerInvariant(), DateTime.UtcNow.ToString("yyyyMM"), id + extension);
            var fullPath = Path.Combine(_root, storageKey);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, bytes);

            var attachment = new Attachment
            {
                Id = id,
                EntityType = entityType,
                EntityId = request.EntityId,
                DocumentType = documentType,
                FileName = SafeFileName(request.FileName, extension),
                ContentType = contentType,
                Size = bytes.LongLength,
                Sha256 = sha256,
                StorageKey = storageKey.Replace('\\', '/'),
                UploadedById = userId,
                UploadedAt = DateTime.UtcNow,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
            };

            await _unitOfWork.Repository<Attachment>().AddAsync(attachment);
            await _unitOfWork.SaveChangesAsync();

            var uploader = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            return ToDto(attachment, uploader?.FullName);
        }

        public async Task<List<AttachmentDto>> ListAsync(Guid userId, string role, string entityType, Guid entityId)
        {
            var type = Normalize(entityType);
            if (!AttachmentEntityTypes.All.Contains(type))
                throw new ArgumentException($"entityType không hợp lệ. Hợp lệ: {string.Join(", ", AttachmentEntityTypes.All)}.");

            await EnsureCanAccessAsync(userId, role, type, entityId, documentType: null, forUpload: false);

            var items = (await _unitOfWork.Repository<Attachment>().GetAllAsync())
                .Where(a => string.Equals(a.EntityType, type, StringComparison.OrdinalIgnoreCase) && a.EntityId == entityId)
                .OrderBy(a => a.UploadedAt)
                .ToList();

            // Khách xem ảnh kiện của mình: chỉ ảnh tiếp nhận kho VN, không thấy ảnh nội bộ khác (ô kệ...).
            if (!StaffRoleHelper.IsInternalStaff(role) && string.Equals(type, AttachmentEntityTypes.Parcel, StringComparison.OrdinalIgnoreCase))
                items = items.Where(IsVnArrivalProof).ToList();

            // Ảnh tiếp nhận VN theo kho được gán: nhân viên kho khác không thấy (lọc bớt, không chặn cả
            // danh sách — màn xếp kệ kho nguồn vẫn liệt kê ảnh ô kệ của kiện như cũ).
            if (StaffRoleHelper.IsInternalStaff(role) && items.Any(IsVnArrivalProof) && !await CanSeeVnArrivalAsync(userId, role, type, entityId))
                items = items.Where(a => !IsVnArrivalProof(a)).ToList();

            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id, u => u.FullName);
            return items
                .Select(a => ToDto(a, a.UploadedById.HasValue && users.TryGetValue(a.UploadedById.Value, out var n) ? n : null))
                .ToList();
        }

        public async Task<(Stream Content, string ContentType, string FileName)> DownloadAsync(Guid userId, string role, Guid attachmentId)
        {
            var attachment = await _unitOfWork.Repository<Attachment>().GetByIdAsync(attachmentId)
                ?? throw new NotFoundException("Không tìm thấy giấy tờ đính kèm.");

            await EnsureCanAccessAsync(userId, role, attachment.EntityType, attachment.EntityId, attachment.DocumentType, forUpload: false);

            if (StaffRoleHelper.IsInternalStaff(role) && IsVnArrivalProof(attachment))
            {
                await WarehouseAccessPolicy.EnsureAssignedWarehouseAsync(
                    _unitOfWork, userId, role, await ArrivalWarehouseOfAsync(attachment.EntityType, attachment.EntityId));
            }

            var fullPath = Path.Combine(_root, attachment.StorageKey ?? string.Empty);
            if (!File.Exists(fullPath))
                throw new NotFoundException("File đính kèm không còn trên máy chủ.");

            return (File.OpenRead(fullPath), attachment.ContentType ?? "application/octet-stream", attachment.FileName ?? "attachment");
        }

        public async Task<HashSet<string>> DocumentTypesOfAsync(string entityType, Guid entityId)
        {
            var type = Normalize(entityType);
            return (await _unitOfWork.Repository<Attachment>().GetAllAsync())
                .Where(a => string.Equals(a.EntityType, type, StringComparison.OrdinalIgnoreCase) && a.EntityId == entityId)
                .Select(a => a.DocumentType?.Trim().ToUpperInvariant() ?? string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Nhân viên nội bộ xem/đính được mọi giấy tờ. Khách chỉ đụng tới giấy phép trên đơn của
        /// chính mình — chứng từ lô và phiếu kho là việc nội bộ.
        /// </summary>
        private async Task EnsureCanAccessAsync(Guid userId, string role, string entityType, Guid entityId, string documentType, bool forUpload)
        {
            if (StaffRoleHelper.IsInternalStaff(role)) return;

            Guid orderId;
            if (string.Equals(entityType, AttachmentEntityTypes.Order, StringComparison.OrdinalIgnoreCase))
            {
                if (forUpload && !string.Equals(documentType, AttachmentDocumentTypes.Permit, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Khách hàng chỉ đính kèm được giấy phép (PERMIT) cho đơn của mình.");
                orderId = entityId;
            }
            else if (string.Equals(entityType, AttachmentEntityTypes.Incident, StringComparison.OrdinalIgnoreCase))
            {
                // Khách xem được ảnh hiện trạng kiện của mình và gửi thêm ảnh khi khiếu nại.
                if (forUpload && !string.Equals(documentType, AttachmentDocumentTypes.IncidentPhoto, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Khách hàng chỉ gửi được ảnh hiện trạng sự cố cho sự cố của mình.");
                var incident = await _unitOfWork.Repository<ParcelIncident>().GetByIdAsync(entityId)
                    ?? throw new NotFoundException("Không tìm thấy sự cố.");
                orderId = incident.OrderId ?? Guid.Empty;
            }
            else if (string.Equals(entityType, AttachmentEntityTypes.Parcel, StringComparison.OrdinalIgnoreCase))
            {
                // Khách chỉ XEM ảnh kho VN chụp lúc tiếp nhận kiện của đơn mình; không tải lên,
                // không xem ảnh nội bộ khác của kiện.
                if (forUpload)
                    throw new UnauthorizedAccessException("Khách hàng không tải ảnh kiện lên được.");
                if (documentType != null && !string.Equals(documentType, AttachmentDocumentTypes.VnArrivalProof, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Khách hàng chỉ xem được ảnh tiếp nhận kiện tại kho Việt Nam.");
                var parcel = await _unitOfWork.Repository<Parcel>().GetByIdAsync(entityId)
                    ?? throw new NotFoundException("Không tìm thấy kiện hàng.");
                orderId = parcel.ConsignmentId ?? Guid.Empty;
            }
            else
            {
                throw new UnauthorizedAccessException("Chỉ nhân viên mới xem được giấy tờ của phiếu xuất và lô vận chuyển.");
            }

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

            var customer = order.CustomerId.HasValue
                ? await _unitOfWork.Repository<Customer>().GetByIdAsync(order.CustomerId.Value)
                : null;

            if (customer?.UserId != userId)
                throw new UnauthorizedAccessException("Bạn chỉ xem được giấy tờ của đơn mình.");
        }

        private static bool IsVnArrivalProof(Attachment a) =>
            string.Equals(a.DocumentType, AttachmentDocumentTypes.VnArrivalProof, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Kho VN nơi ảnh tiếp nhận được chụp: kho đích của lô (SHIPMENT) hoặc của lô chở kiện (PARCEL).
        /// Ảnh tiếp nhận chỉ gắn được vào hai loại đối tượng này.
        /// </summary>
        private async Task<Guid?> ArrivalWarehouseOfAsync(string entityType, Guid entityId)
        {
            if (string.Equals(entityType, AttachmentEntityTypes.Parcel, StringComparison.OrdinalIgnoreCase))
            {
                _ = await _unitOfWork.Repository<Parcel>().GetByIdAsync(entityId)
                    ?? throw new NotFoundException("Không tìm thấy kiện hàng.");
                return await VnArrivalPhotos.DestinationWarehouseOfParcelAsync(_unitOfWork, entityId)
                    ?? throw new ArgumentException("Kiện chưa nằm trong lô vận chuyển quốc tế nào nên chưa có bước tiếp nhận tại kho Việt Nam.");
            }

            if (string.Equals(entityType, AttachmentEntityTypes.Shipment, StringComparison.OrdinalIgnoreCase))
            {
                var shipment = await _unitOfWork.Repository<Shipment>().GetByIdAsync(entityId)
                    ?? throw new NotFoundException("Không tìm thấy lô vận chuyển.");
                return shipment.DestinationWarehouseId;
            }

            throw new ArgumentException(
                $"Ảnh tiếp nhận kho VN ({AttachmentDocumentTypes.VnArrivalProof}) chỉ gắn vào kiện ({AttachmentEntityTypes.Parcel}) hoặc lô ({AttachmentEntityTypes.Shipment}).");
        }

        private async Task<bool> CanSeeVnArrivalAsync(Guid userId, string role, string entityType, Guid entityId)
        {
            try
            {
                await WarehouseAccessPolicy.EnsureAssignedWarehouseAsync(
                    _unitOfWork, userId, role, await ArrivalWarehouseOfAsync(entityType, entityId));
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or NotFoundException)
            {
                return false;
            }
        }

        private static string Normalize(string value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

        /// <summary>Giữ tên gốc cho người dùng dễ nhận ra, nhưng bỏ đường dẫn để không ghi đè file khác.</summary>
        private static string SafeFileName(string fileName, string extension)
        {
            var name = Path.GetFileName(fileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) return "file" + extension;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length > 200 ? name[^200..] : name;
        }

        private static AttachmentDto ToDto(Attachment a, string uploaderName) => new()
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
