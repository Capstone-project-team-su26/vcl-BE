using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IParcelService
    {
        Task<ParcelCheckInResponse> CheckInParcelAsync(ParcelCheckInRequest request);
    }
}
