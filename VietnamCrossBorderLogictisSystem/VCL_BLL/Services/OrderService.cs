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
        private readonly INotificationService _notificationService;

        /// <summary>Một chỗ duy nhất quyết định "đơn này đã tất toán chưa".</summary>
        private readonly ISettlementQueryService _settlementQueryService;

        // Các giá trị RestrictionType bị coi là hàng cấm (chặn tạo) → level BANNED.
        private static readonly string[] BannedRestrictionTypes = { "BANNED", "PROHIBITED", "CẤM", "CAM" };

        // Các giá trị RestrictionType được coi là hàng hạn chế → level RESTRICTED.
        private static readonly string[] RestrictedRestrictionTypes = { "RESTRICTED", "LIMITED", "HẠN CHẾ", "HAN CHE" };

        // Các trạng thái Order mà khách được phép tự hủy.
        // Khách tự huỷ được khi đơn chưa có tiền thực nhận và chưa có hàng; bước huỷ kiểm thêm không có
        // khoản thu nào đã PAID.
        private static readonly string[] CancellableStatuses =
            { "PENDING_REVIEW", "NEED_MORE_INFO", "APPROVED", "QUOTATION_SENT", "QUOTATION_REJECTED", "QUOTATION_CONFIRMED", "WAITING_DEPOSIT" };

        // Các role nội bộ được xem mọi yêu cầu ký gửi (không cần là chủ đơn).
        // Vai trò khai báo tập trung ở StaffRoleHelper

        public OrderService(
            IUnitOfWork unitOfWork,
            IQuotationService quotationService,
            IConfiguration configuration,
            IConsignmentReceiptService consignmentReceiptService,
            INotificationService notificationService,
            ISettlementQueryService settlementQueryService)
        {
            _unitOfWork = unitOfWork;
            _quotationService = quotationService;
            _configuration = configuration;
            _consignmentReceiptService = consignmentReceiptService;
            _notificationService = notificationService;
            _settlementQueryService = settlementQueryService;
        }
    }
}
