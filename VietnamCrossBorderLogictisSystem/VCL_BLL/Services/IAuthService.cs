using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Services
{
    public interface IAuthService
    {
        Task<AuthResponse> LoginAsync(LoginRequest request);
        Task<Guid> RegisterAsync(RegisterRequest request);
    }
}
