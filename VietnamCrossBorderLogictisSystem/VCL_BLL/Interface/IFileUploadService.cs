using System.IO;
using System.Threading.Tasks;

namespace VCL_BLL.Interface
{
    public interface IFileUploadService
    {
        /// <summary>
        /// Upload một ảnh lên cloud (Cloudinary) và trả về URL công khai (https).
        /// </summary>
        Task<string> UploadImageAsync(Stream fileStream, string fileName);

        /// <summary>
        /// Upload một file không phải ảnh (PDF chứng từ) lên cloud và trả về URL công khai (https).
        /// </summary>
        Task<string> UploadDocumentAsync(Stream fileStream, string fileName);
    }
}
