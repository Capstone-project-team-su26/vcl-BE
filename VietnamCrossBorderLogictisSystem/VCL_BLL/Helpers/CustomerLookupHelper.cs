using System;
using System.Collections.Generic;
using System.Linq;
using VCL_BLL.Exceptions;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Tra cứu khách hàng khi nhân viên lên đơn hộ. Dùng chung cho luồng ký gửi
    /// (POST /api/staff/consignments) và mua hộ (POST /api/staff/purchase-requests)
    /// để hai bên luôn cùng thứ tự ưu tiên và cùng thông báo lỗi.
    /// </summary>
    public static class CustomerLookupHelper
    {
        /// <summary>
        /// Tìm khách theo ID được chọn, nếu không có thì tra theo số điện thoại rồi tới email.
        /// </summary>
        /// <exception cref="NotFoundException">Có thông tin tra cứu nhưng không khớp khách nào.</exception>
        /// <exception cref="ArgumentException">Không truyền thông tin tra cứu nào.</exception>
        public static Customer ResolveForStaff(
            IEnumerable<Customer> customers, Guid? customerId, string? customerPhone, string? customerEmail)
        {
            var all = customers as IList<Customer> ?? customers.ToList();

            if (customerId.HasValue && customerId.Value != Guid.Empty)
            {
                var byId = all.FirstOrDefault(c => c.Id == customerId.Value);
                if (byId == null)
                    throw new NotFoundException($"Không tìm thấy khách hàng với ID {customerId.Value}");
                return byId;
            }

            if (!string.IsNullOrWhiteSpace(customerPhone))
            {
                var byPhone = all.FirstOrDefault(c => string.Equals(c.Phone, customerPhone.Trim(), StringComparison.OrdinalIgnoreCase));
                if (byPhone == null)
                    throw new NotFoundException($"Không tìm thấy khách hàng với số điện thoại {customerPhone}");
                return byPhone;
            }

            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                var byEmail = all.FirstOrDefault(c => string.Equals(c.Email, customerEmail.Trim(), StringComparison.OrdinalIgnoreCase));
                if (byEmail == null)
                    throw new NotFoundException($"Không tìm thấy khách hàng với email {customerEmail}");
                return byEmail;
            }

            throw new ArgumentException("Vui lòng chọn khách hàng có sẵn hoặc nhập email/số điện thoại để tìm kiếm.");
        }
    }
}
