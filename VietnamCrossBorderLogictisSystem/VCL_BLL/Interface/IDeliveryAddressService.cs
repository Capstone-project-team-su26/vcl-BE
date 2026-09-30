using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IDeliveryAddressService
    {
        Task<IEnumerable<DeliveryAddressResponse>> GetMyAddressesAsync(Guid userId);
        Task<IEnumerable<DeliveryAddressResponse>> GetAddressesByCustomerIdAsync(Guid customerId);
        Task<DeliveryAddressResponse> CreateAsync(Guid userId, CreateDeliveryAddressRequest request);

        /// <summary>
        /// Nhân viên (Sale/Admin) thêm địa chỉ nhận hàng HỘ khách.
        /// Khác CreateAsync ở chỗ định danh khách bằng customerId lấy từ màn hình,
        /// không bằng tài khoản đang đăng nhập — vì người bấm là nhân viên.
        /// </summary>
        Task<DeliveryAddressResponse> CreateForCustomerAsync(Guid customerId, CreateDeliveryAddressRequest request);
        Task UpdateAsync(Guid userId, Guid addressId, UpdateDeliveryAddressRequest request);
        Task DeleteAsync(Guid userId, Guid addressId);
    }
}
