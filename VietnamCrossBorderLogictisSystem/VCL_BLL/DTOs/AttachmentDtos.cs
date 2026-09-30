using System;
using System.IO;

namespace VCL_BLL.DTOs
{
    public class UploadAttachmentRequest
    {
        public string? EntityType { get; set; }
        public Guid EntityId { get; set; }
        public string? DocumentType { get; set; }
        public string? Note { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public Stream Content { get; set; }
        public long Length { get; set; }
    }

    public class AttachmentDto
    {
        public Guid Id { get; set; }
        public string? EntityType { get; set; }
        public Guid EntityId { get; set; }
        public string? DocumentType { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public long Size { get; set; }
        public string? Note { get; set; }
        public Guid? UploadedById { get; set; }
        public string? UploadedByName { get; set; }
        public DateTime UploadedAt { get; set; }
        public string? DownloadUrl { get; set; }
    }
}
