using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Giấy tờ đính kèm của luồng xuất kho: đơn (giấy phép), phiếu xuất, kiện trong phiếu, lô.
    /// File lưu riêng tư trên volume của container, tải về phải qua kiểm quyền.
    /// </summary>
    public interface IAttachmentService
    {
        Task<AttachmentDto> UploadAsync(Guid userId, string role, UploadAttachmentRequest request);

        Task<List<AttachmentDto>> ListAsync(Guid userId, string role, string entityType, Guid entityId);

        Task<(Stream Content, string ContentType, string FileName)> DownloadAsync(Guid userId, string role, Guid attachmentId);

        /// <summary>Các loại giấy tờ đã có của một đối tượng — dùng để chặn ở các bước bắt buộc.</summary>
        Task<HashSet<string>> DocumentTypesOfAsync(string entityType, Guid entityId);
    }
}
