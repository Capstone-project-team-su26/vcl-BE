using System;
using Microsoft.Extensions.Configuration;
using VCL_BLL.Interface;
using VCL_DLL.Interface;

namespace VCL_BLL.Services
{
    public partial class OrderService : IOrderService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IQuotationService _quotationService;
        private readonly IConfiguration _configuration;
        private readonly IConsignmentReceiptService _consignmentReceiptService;

        // Các giá trị RestrictionType bị coi là hàng cấm (chặn tạo) → level BANNED.
        private static readonly string[] BannedRestrictionTypes = { "BANNED", "PROHIBITED", "CẤM", "CAM" };

        // Các giá trị RestrictionType được coi là hàng hạn chế → level RESTRICTED.
        private static readonly string[] RestrictedRestrictionTypes = { "RESTRICTED", "LIMITED", "HẠN CHẾ", "HAN CHE" };

        // Các trạng thái Order mà khách được phép tự hủy.
        private static readonly string[] CancellableStatuses = { "PENDING_REVIEW", "APPROVED" };

        // Các role nội bộ được xem mọi yêu cầu ký gửi (không cần là chủ đơn).
        private static readonly string[] StaffRoles = { "ADMIN", "ADMINISTRATOR", "SALE", "WAREHOUSESTAFF", "OPERATIONSMANAGER", "DELIVERY" };

        public OrderService(
            IUnitOfWork unitOfWork,
            IQuotationService quotationService,
            IConfiguration configuration,
            IConsignmentReceiptService consignmentReceiptService)
        {
            _unitOfWork = unitOfWork;
            _quotationService = quotationService;
            _configuration = configuration;
            _consignmentReceiptService = consignmentReceiptService;
        }
    }
}
