using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Một trạng thái của yêu cầu ký gửi để FE đổ dropdown/lọc (code + nhãn hiển thị).
    /// </summary>
    public class ConsignmentStatusDto
    {
        public string Code { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>
    /// Chi tiết đầy đủ một yêu cầu ký gửi: header + khách hàng + mặt hàng + báo giá mới nhất.
    /// </summary>
    public class ConsignmentDetailResponse
    {
        public Guid OrderId { get; set; }
        public string? ConsignmentCode { get; set; }
        public string ConsignmentType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? OrderType { get; set; }
        public decimal? TotalWeight { get; set; }
        /// <summary>Tổng thể tích (cm³)</summary>
        public decimal? TotalVolume { get; set; }
        public string? Note { get; set; }
        public string? Route { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }
        public bool RequiresInspection { get; set; }
        public DateTime? CreatedAt { get; set; }

        /// <summary>Danh sách ID các quy định giá / dịch vụ phụ áp dụng cho đơn.</summary>
        public List<Guid> PricingRuleIds { get; set; } = new();

        public ConsignmentCustomerInfo Customer { get; set; } = new();
        public List<ConsignmentItemDetail> Items { get; set; } = new();

        /// <summary>Báo giá mới nhất của đơn (null nếu chưa có).</summary>
        public ConsignmentQuotationSummary? Quotation { get; set; }
    }

    public class ConsignmentCustomerInfo
    {
        public Guid? CustomerId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }

    public class ConsignmentItemDetail
    {
        public Guid Id { get; set; }
        public string? ProductName { get; set; }
        public string? ProductType { get; set; }
        public int? Quantity { get; set; }
        public decimal? Weight { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? Length { get; set; }
        public decimal? DeclaredValue { get; set; }
        public string? ReferenceUrl { get; set; }
        public string? DomesticTrackingCode { get; set; }

        /// <summary>Cân nặng quy đổi của dòng sản phẩm = Length × Width × Height × Quantity / 5000</summary>
        public decimal? VolumetricWeight { get; set; }
    }

    public class ConsignmentQuotationSummary
    {
        public Guid QuotationId { get; set; }
        public string? QuoteType { get; set; }
        public string? Status { get; set; }
        public decimal? EstimatedFreightCharge { get; set; }
        public decimal? DomesticShippingFee { get; set; }
        public decimal? ServiceFee { get; set; }
        public decimal? TaxAndDuty { get; set; }
        /// <summary>Tổng thể tích (cm³)</summary>
        public decimal? TotalVolume { get; set; }
        public decimal TotalEstimatedCost { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? ExpiredAt { get; set; }
    }

    /// <summary>
    /// Yêu cầu tạo ký gửi (A1) — header + danh sách mặt hàng + phương án vận chuyển (A2).
    /// </summary>
    public class CreateConsignmentRequest
    {
        /// <summary>Tuyến hàng (VD: "China-Vietnam"). Khớp với PricingRule.Route đang active.</summary>
        [Required]
        public string Route { get; set; } = string.Empty;

        /// <summary>Phương án vận chuyển. Phải khớp một PricingRule active theo Route đã chọn (FE lấy danh sách từ API pricing-rules).</summary>
        [Required]
        public string ShippingOption { get; set; } = string.Empty;

        /// <summary>Tên người nhận hàng.</summary>
        public string? ReceiverName { get; set; }

        /// <summary>Số điện thoại người nhận.</summary>
        public string? ReceiverPhone { get; set; }

        /// <summary>Địa chỉ giao hàng (người nhận cuối tại VN).</summary>
        public string? ReceiverAddress { get; set; }

        /// <summary>Danh sách ID cấu hình bảng giá/dịch vụ phụ (ví dụ: đóng gỗ, kiểm hàng, bảo hiểm...).</summary>
        public List<Guid> PricingRuleIds { get; set; } = new();

        /// <summary>Ghi chú xử lý chung cho cả yêu cầu.</summary>
        public string? Note { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Yêu cầu ký gửi phải có ít nhất một mặt hàng.")]
        public List<ConsignmentItemRequest> Items { get; set; } = new();
    }

    /// <summary>
    /// Một mặt hàng trong yêu cầu ký gửi.
    /// </summary>
    public class ConsignmentItemRequest
    {
        [Required]
        public string ProductName { get; set; } = string.Empty;

        /// <summary>Loại hàng — dùng để đối chiếu hàng cấm/hạn chế.</summary>
        [Required]
        public string ProductType { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
        public int Quantity { get; set; }

        public decimal? Weight { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? Length { get; set; }

        /// <summary>Giá trị khai báo.</summary>
        public decimal? DeclaredValue { get; set; }

        /// <summary>Ảnh / link tham chiếu.</summary>
        public string? ReferenceUrl { get; set; }

        /// <summary>Mã tracking nội địa (nếu có) — chỉ để kho đối soát, không tính phí.</summary>
        public string? DomesticTrackingCode { get; set; }
    }



    /// <summary>
    /// Cảnh báo hàng cấm/hạn chế phát hiện khi tạo ký gửi.
    /// </summary>
    public class RestrictedItemWarning
    {
        public string ProductName { get; set; } = string.Empty;
        public string MatchedItem { get; set; } = string.Empty;
        public string RestrictionType { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    /// <summary>
    /// Yêu cầu kiểm tra hàng cấm/hạn chế (chỉ kiểm tra, không tạo Order).
    /// </summary>
    public class ValidateItemsRequest
    {
        [Required]
        [MinLength(1, ErrorMessage = "Phải có ít nhất một mặt hàng để kiểm tra.")]
        public List<ConsignmentItemRequest> Items { get; set; } = new();
    }

    /// <summary>
    /// Kết quả kiểm tra một mặt hàng so với danh mục hàng cấm/hạn chế.
    /// </summary>
    public class ItemValidationResult
    {
        public string ProductName { get; set; } = string.Empty;

        /// <summary>Mức độ: BANNED (cấm), RESTRICTED (hạn chế), WARNING (cảnh báo).</summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>Tên mục trong danh mục đã khớp.</summary>
        public string MatchedItem { get; set; } = string.Empty;

        /// <summary>Quốc gia áp dụng của rule (nếu có).</summary>
        public string? Country { get; set; }

        /// <summary>Lý do hạn chế (lấy từ Note của rule).</summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Kết quả kiểm tra cả danh sách mặt hàng.
    /// </summary>
    public class ValidateItemsResponse
    {
        /// <summary>True nếu KHÔNG có mặt hàng nào bị cấm (BANNED). Hàng hạn chế vẫn cho tạo.</summary>
        public bool CanCreate { get; set; }

        /// <summary>Chỉ chứa các mặt hàng có vấn đề (cấm/hạn chế/cảnh báo).</summary>
        public List<ItemValidationResult> Results { get; set; } = new();
    }

    /// <summary>
    /// Response sau khi tạo ký gửi: thông tin đơn + báo giá tạm tính + cảnh báo (nếu có).
    /// </summary>
    public class CreateConsignmentResponse
    {
        public Guid OrderId { get; set; }

        /// <summary>Mã gửi hàng thân thiện (mã nghiệp vụ chính hiển thị cho Customer thay UUID).</summary>
        public string ConsignmentCode { get; set; } = string.Empty;

        /// <summary>ID khách hàng (Customer) sở hữu đơn.</summary>
        public Guid? CustomerId { get; set; }

        /// <summary>ID user (tài khoản) của khách — FE cần để xử lý.</summary>
        public Guid? UserId { get; set; }

        public string Status { get; set; } = string.Empty;
        public string ConsignmentType { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public bool RequiresInspection { get; set; }
        public int ItemCount { get; set; }

        /// <summary>Danh sách ID các quy định giá / dịch vụ phụ áp dụng cho đơn.</summary>
        public List<Guid> PricingRuleIds { get; set; } = new();

        /// <summary>Báo giá tạm tính sinh tự động sau khi tạo.</summary>
        public EstimateQuotationResponse Quotation { get; set; } = new();

        /// <summary>Danh sách cảnh báo hàng hạn chế (Restricted). Hàng cấm (Banned) sẽ bị chặn tạo.</summary>
        public List<RestrictedItemWarning> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Yêu cầu tạo ký gửi bởi nhân viên (Sales) thay cho khách hàng.
    /// </summary>
    public class CreateConsignmentByStaffRequest
    {
        /// <summary>ID khách hàng (nếu chọn từ danh sách có sẵn)</summary>
        public Guid? CustomerId { get; set; }

        /// <summary>Số điện thoại khách hàng (để tìm kiếm khách hàng)</summary>
        public string? CustomerPhone { get; set; }

        /// <summary>Email khách hàng (để tìm kiếm khách hàng)</summary>
        public string? CustomerEmail { get; set; }

        /// <summary>Tuyến hàng (VD: "China-Vietnam" hoặc "TQ-VN"). Khớp với PricingRule.Route đang active.</summary>
        [Required(ErrorMessage = "Vui lòng chọn tuyến hàng.")]
        public string Route { get; set; } = string.Empty;

        /// <summary>Phương án vận chuyển. Phải khớp một PricingRule active theo Route đã chọn.</summary>
        [Required(ErrorMessage = "Vui lòng chọn phương thức vận chuyển.")]
        public string ShippingOption { get; set; } = string.Empty;

        /// <summary>Tên người nhận hàng.</summary>
        public string? ReceiverName { get; set; }

        /// <summary>Số điện thoại người nhận.</summary>
        public string? ReceiverPhone { get; set; }

        /// <summary>Địa chỉ giao hàng (người nhận cuối tại VN).</summary>
        public string? ReceiverAddress { get; set; }

        /// <summary>Danh sách ID cấu hình bảng giá/dịch vụ phụ (ví dụ: đóng gỗ, kiểm hàng, bảo hiểm...).</summary>
        public List<Guid> PricingRuleIds { get; set; } = new();

        /// <summary>Ghi chú từ Sales/Khách hàng.</summary>
        public string? Note { get; set; }

        [Required(ErrorMessage = "Yêu cầu ký gửi phải có ít nhất một mặt hàng.")]
        [MinLength(1, ErrorMessage = "Yêu cầu ký gửi phải có ít nhất một mặt hàng.")]
        public List<ConsignmentItemRequest> Items { get; set; } = new();
    }

    /// <summary>
    /// Thông tin tra cứu công khai một đơn ký gửi theo mã vận đơn (không yêu cầu đăng nhập).
    /// Chỉ trả về dữ liệu trạng thái, không lộ thông tin nhạy cảm của khách hàng.
    /// </summary>
    public class PublicTrackingResponse
    {
        public string ConsignmentCode { get; set; } = string.Empty;
        public string? ConsignmentType { get; set; }
        public string? Route { get; set; }
        public string Status { get; set; } = string.Empty;
        public string StatusLabel { get; set; } = string.Empty;
        public string? ReceiverName { get; set; }
        public int ItemCount { get; set; }
        public DateTime? CreatedAt { get; set; }
        public List<PublicTrackingParcelDto> Parcels { get; set; } = new();
    }

    /// <summary>Trạng thái một kiện hàng thuộc đơn ký gửi trong kết quả tra cứu công khai.</summary>
    public class PublicTrackingParcelDto
    {
        public string PackageCode { get; set; } = string.Empty;
        public string PackageStatus { get; set; } = string.Empty;
        public DateTime? CheckInTime { get; set; }
        public DateTime? PutAwayTime { get; set; }
    }

}
