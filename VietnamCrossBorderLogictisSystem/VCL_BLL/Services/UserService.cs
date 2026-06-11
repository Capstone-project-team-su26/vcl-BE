using System;
using System.Threading.Tasks;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class UserService : IUserService
    {
        private readonly IUnitOfWork _unitOfWork;

        public UserService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task LockUserAsync(Guid userId)
        {
            var userRepo = _unitOfWork.Repository<User>();
            var user = await userRepo.GetByIdAsync(userId);

            if (user == null)
            {
                throw new Exception("Người dùng không tồn tại.");
            }

            user.Status = "Locked";
            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task UnlockUserAsync(Guid userId)
        {
            var userRepo = _unitOfWork.Repository<User>();
            var user = await userRepo.GetByIdAsync(userId);

            if (user == null)
            {
                throw new Exception("Người dùng không tồn tại.");
            }

            user.Status = "Active";
            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
