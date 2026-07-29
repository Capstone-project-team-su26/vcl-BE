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
    public class DeliveryAddressService : IDeliveryAddressService
    {
        private readonly IUnitOfWork _unitOfWork;

        public DeliveryAddressService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IEnumerable<DeliveryAddressResponse>> GetMyAddressesAsync(Guid userId)
        {
            var customers = await _unitOfWork.Repository<Customer>().GetAllAsync();
            var customer = customers.FirstOrDefault(c => c.UserId == userId);
            if (customer == null)
            {
                return Enumerable.Empty<DeliveryAddressResponse>();
            }

            var all = await _unitOfWork.Repository<DeliveryAddress>().GetAllAsync();
            return all
                .Where(a => a.CustomerId == customer.Id)
                .OrderByDescending(a => a.CreatedAt)
                .Select(MapToResponse)
                .ToList();
        }

        /// <summary>
        /// Nhân viên (Sale/Admin) xem sổ địa chỉ của một khách hàng cụ thể — dùng khi cần
        /// lấy địa chỉ bàn giao cho đơn vị vận chuyển thứ 3 (GHN/GHTK...).
        /// </summary>
        public async Task<IEnumerable<DeliveryAddressResponse>> GetAddressesByCustomerIdAsync(Guid customerId)
        {
            var customers = await _unitOfWork.Repository<Customer>().GetAllAsync();
            var customer = customers.FirstOrDefault(c => c.Id == customerId);
            if (customer == null)
                throw new NotFoundException($"Không tìm thấy khách hàng với ID {customerId}");

            var all = await _unitOfWork.Repository<DeliveryAddress>().GetAllAsync();
            return all
                .Where(a => a.CustomerId == customer.Id)
                .OrderByDescending(a => a.CreatedAt)
                .Select(MapToResponse)
                .ToList();
        }

        public async Task<DeliveryAddressResponse> CreateAsync(Guid userId, CreateDeliveryAddressRequest request)
        {
            var customer = await GetCustomerOrThrowAsync(userId);
            var repo = _unitOfWork.Repository<DeliveryAddress>();

            var address = new DeliveryAddress
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                Address = request.Address,
                // Các cột dưới DB vẫn giữ nguyên nhưng không còn nhận từ FE (theo feedback FE Phong)
                ReceiverName = string.Empty,
                ReceiverPhone = string.Empty,
                IsDefault = false,
                CreatedAt = DateTime.UtcNow
            };

            await repo.AddAsync(address);
            await _unitOfWork.SaveChangesAsync();

            return MapToResponse(address);
        }

        public async Task UpdateAsync(Guid userId, Guid addressId, UpdateDeliveryAddressRequest request)
        {
            var customer = await GetCustomerOrThrowAsync(userId);
            var repo = _unitOfWork.Repository<DeliveryAddress>();
            var address = await repo.GetByIdAsync(addressId);

            if (address == null || address.CustomerId != customer.Id)
                throw new NotFoundException("Không tìm thấy địa chỉ nhận hàng.");

            address.Address = request.Address;

            repo.Update(address);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid userId, Guid addressId)
        {
            var customer = await GetCustomerOrThrowAsync(userId);
            var repo = _unitOfWork.Repository<DeliveryAddress>();
            var address = await repo.GetByIdAsync(addressId);

            if (address == null || address.CustomerId != customer.Id)
                throw new NotFoundException("Không tìm thấy địa chỉ nhận hàng.");

            repo.Delete(address);
            await _unitOfWork.SaveChangesAsync();
        }

        private async Task<Customer> GetCustomerOrThrowAsync(Guid userId)
        {
            var customers = await _unitOfWork.Repository<Customer>().GetAllAsync();
            var customer = customers.FirstOrDefault(c => c.UserId == userId);
            if (customer == null)
                throw new UnauthorizedAccessException("Không tìm thấy thông tin khách hàng tương ứng với tài khoản.");
            return customer;
        }

        private static DeliveryAddressResponse MapToResponse(DeliveryAddress a) => new DeliveryAddressResponse
        {
            Id = a.Id,
            CustomerId = a.CustomerId,
            Address = a.Address ?? string.Empty,
            CreatedAt = a.CreatedAt
        };
    }
}
