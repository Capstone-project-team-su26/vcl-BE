using System;
using System.IO;
using System.Threading.Tasks;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Configuration;
using VCL_BLL.Interface;

namespace VCL_BLL.Services
{
    public class CloudinaryFileUploadService : IFileUploadService
    {
        private readonly Cloudinary _cloudinary;

        public CloudinaryFileUploadService(IConfiguration configuration)
        {
            var cloudName = configuration["Cloudinary:CloudName"];
            var apiKey = configuration["Cloudinary:ApiKey"];
            var apiSecret = configuration["Cloudinary:ApiSecret"];

            if (string.IsNullOrWhiteSpace(cloudName) ||
                string.IsNullOrWhiteSpace(apiKey) ||
                string.IsNullOrWhiteSpace(apiSecret))
            {
                throw new InvalidOperationException(
                    "Chưa cấu hình Cloudinary. Vui lòng điền Cloudinary:CloudName/ApiKey/ApiSecret trong appsettings.");
            }

            var account = new Account(cloudName, apiKey, apiSecret);
            _cloudinary = new Cloudinary(account) { Api = { Secure = true } };
        }

        public async Task<string> UploadImageAsync(Stream fileStream, string fileName)
        {
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, fileStream),
                Folder = "vcl/consignment-items"
            };

            var result = await _cloudinary.UploadAsync(uploadParams);

            if (result.Error != null)
                throw new Exception($"Upload ảnh thất bại: {result.Error.Message}");

            return result.SecureUrl?.ToString() ?? string.Empty;
        }

        public async Task<string> UploadDocumentAsync(Stream fileStream, string fileName)
        {
            // PDF phải upload dưới dạng raw, Cloudinary không xử lý được như ảnh.
            var uploadParams = new RawUploadParams
            {
                File = new FileDescription(fileName, fileStream),
                Folder = "vcl/consignment-receipts"
            };

            var result = await _cloudinary.UploadAsync(uploadParams);

            if (result.Error != null)
                throw new Exception($"Upload phiếu PDF thất bại: {result.Error.Message}");

            return result.SecureUrl?.ToString() ?? string.Empty;
        }
    }
}
