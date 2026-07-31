using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class ProductTypeService : IProductTypeService
    {
        private readonly IUnitOfWork _unitOfWork;

        public ProductTypeService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<List<ProductTypeResponse>> GetActiveProductTypesAsync()
        {
            var all = await _unitOfWork.Repository<ProductType>().GetAllAsync();
            return all
                .Where(t => t.IsActive)
                .OrderBy(t => t.Name)
                .Select(t => new ProductTypeResponse { Id = t.Id, Name = t.Name ?? string.Empty })
                .ToList();
        }

        public async Task<List<ProductTypeDetailResponse>> GetAllProductTypesAsync()
        {
            var all = await _unitOfWork.Repository<ProductType>().GetAllAsync();
            return all
                .OrderBy(t => t.Name)
                .Select(ToDetail)
                .ToList();
        }

        public async Task<ProductTypeDetailResponse> GetByIdAsync(Guid id)
        {
            var productType = await _unitOfWork.Repository<ProductType>().GetByIdAsync(id);
            if (productType == null)
            {
                throw new NotFoundException($"Không tìm thấy loại hàng với ID: {id}");
            }

            return ToDetail(productType);
        }

        public async Task<ProductTypeDetailResponse> CreateAsync(CreateProductTypeRequest request)
        {
            var repo = _unitOfWork.Repository<ProductType>();
            var all = await repo.GetAllAsync();

            // Tên loại hàng là khóa nghiệp vụ vì đơn hàng đang lưu loại hàng dưới dạng chuỗi
            var isDuplicate = all.Any(t => string.Equals(t.Name?.Trim(), request.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (isDuplicate)
            {
                throw new ArgumentException($"Loại hàng \"{request.Name.Trim()}\" đã tồn tại.");
            }

            var productType = new ProductType
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                ImportTaxRate = request.ImportTaxRate,
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            await repo.AddAsync(productType);
            await _unitOfWork.SaveChangesAsync();

            return ToDetail(productType);
        }

        public async Task UpdateAsync(Guid id, UpdateProductTypeRequest request)
        {
            var repo = _unitOfWork.Repository<ProductType>();
            var productType = await repo.GetByIdAsync(id);
            if (productType == null)
            {
                throw new NotFoundException($"Không tìm thấy loại hàng với ID: {id}");
            }

            var all = await repo.GetAllAsync();
            var isDuplicate = all.Any(t => t.Id != id
                && string.Equals(t.Name?.Trim(), request.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (isDuplicate)
            {
                throw new ArgumentException($"Loại hàng \"{request.Name.Trim()}\" đã tồn tại.");
            }

            productType.Name = request.Name.Trim();
            productType.ImportTaxRate = request.ImportTaxRate;
            productType.IsActive = request.IsActive;

            repo.Update(productType);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var repo = _unitOfWork.Repository<ProductType>();
            var productType = await repo.GetByIdAsync(id);
            if (productType == null)
            {
                throw new NotFoundException($"Không tìm thấy loại hàng với ID: {id}");
            }

            // Đơn hàng lưu loại hàng dưới dạng chuỗi tên, nên loại hàng đã phát sinh dữ liệu
            // chỉ được ngừng sử dụng để không làm hỏng dữ liệu lịch sử.
            var name = productType.Name?.Trim() ?? string.Empty;

            var orderItems = await _unitOfWork.Repository<OrderItem>().GetAllAsync();
            var usedInOrders = orderItems.Any(i => string.Equals(i.ProductType?.Trim(), name, StringComparison.OrdinalIgnoreCase));

            var purchaseItems = await _unitOfWork.Repository<PurchaseRequestItem>().GetAllAsync();
            var usedInPurchases = purchaseItems.Any(i => string.Equals(i.ProductType?.Trim(), name, StringComparison.OrdinalIgnoreCase));

            if (usedInOrders || usedInPurchases)
            {
                if (!productType.IsActive)
                {
                    throw new ArgumentException("Loại hàng đã được ngừng sử dụng trước đó.");
                }

                productType.IsActive = false;
                repo.Update(productType);
            }
            else
            {
                repo.Delete(productType);
            }

            await _unitOfWork.SaveChangesAsync();
        }

        private static ProductTypeDetailResponse ToDetail(ProductType productType) => new ProductTypeDetailResponse
        {
            Id = productType.Id,
            Name = productType.Name ?? string.Empty,
            ImportTaxRate = productType.ImportTaxRate,
            IsActive = productType.IsActive,
            CreatedAt = productType.CreatedAt
        };
    }
}
