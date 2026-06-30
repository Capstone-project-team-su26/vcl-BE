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
            var customer = await GetCustomerOrThrowAsync(userId);
            var all = await _unitOfWork.Repository<DeliveryAddress>().GetAllAsync();
            return all
                .Where(a => a.CustomerId == customer.Id)
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.CreatedAt)
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
                ReceiverName = request.ReceiverName,
                ReceiverPhone = request.ReceiverPhone,
                Address = request.Address,
                IsDefault = request.IsDefault,
                CreatedAt = DateTime.UtcNow
            };

            // Nếu đặt làm mặc định → bỏ mặc định của các địa chỉ khác
            if (request.IsDefault)
                await UnsetOtherDefaultsAsync(customer.Id, null);

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

            address.ReceiverName = request.ReceiverName;
            address.ReceiverPhone = request.ReceiverPhone;
            address.Address = request.Address;
            address.IsDefault = request.IsDefault;

            if (request.IsDefault)
                await UnsetOtherDefaultsAsync(customer.Id, address.Id);

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

        private async Task UnsetOtherDefaultsAsync(Guid customerId, Guid? exceptId)
        {
            var repo = _unitOfWork.Repository<DeliveryAddress>();
            var all = await repo.GetAllAsync();
            var others = all.Where(a => a.CustomerId == customerId && a.IsDefault && a.Id != exceptId).ToList();
            foreach (var a in others)
            {
                a.IsDefault = false;
                repo.Update(a);
            }
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
            ReceiverName = a.ReceiverName ?? string.Empty,
            ReceiverPhone = a.ReceiverPhone ?? string.Empty,
            Address = a.Address ?? string.Empty,
            IsDefault = a.IsDefault,
            CreatedAt = a.CreatedAt
        };
    }
}
