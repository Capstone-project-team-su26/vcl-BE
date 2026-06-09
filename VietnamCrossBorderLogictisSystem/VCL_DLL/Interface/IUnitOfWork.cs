using System;
using System.Threading.Tasks;
using VCL_DLL.Models;

namespace VCL_DLL.Interface
{
    public interface IUnitOfWork : IDisposable
    {
        IGenericRepository<T> Repository<T>() where T : class;
        Task<int> SaveChangesAsync();
    }
}
