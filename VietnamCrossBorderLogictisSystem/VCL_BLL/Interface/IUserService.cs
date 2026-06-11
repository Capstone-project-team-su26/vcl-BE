using System;
using System.Threading.Tasks;

namespace VCL_BLL.Interface
{
    public interface IUserService
    {
        Task LockUserAsync(Guid userId);
        Task UnlockUserAsync(Guid userId);
    }
}
