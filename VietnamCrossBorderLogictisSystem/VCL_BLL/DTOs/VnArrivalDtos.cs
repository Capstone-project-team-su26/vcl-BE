using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    // ── Tất toán theo cân đo tại kho VN ─────────────────────────────────────────────────────

    public class SettlementBlockerDto
    {
        /// <summary>PARCEL_NOT_INSPECTED · OPEN_INCIDENT · NO_INVOICE · NO_DEPOSIT · ALREADY_SETTLED.</summary>
        public string? Code { get; set; }
        public string? Message { get; set; }
    }

    public class SettlementParcelDto
    {
        public Guid ParcelId { get; set; }
        public string? PackageCode { get; set; }
        public string? PackageStatus { get; set; }
        public bool IsDisposed { get; set; }

        /// <summary>Cân lúc nhận ở kho nguồn (kg).</summary>
        public decimal? OriginWeight { get; set; }

        /// <summary>Cân đo kho VN — số dùng để tính cước.</summary>
        public decimal? VnWeight { get; set; }
        public decimal? VnLength { get; set; }
        public decimal? VnWidth { get; set; }
        public decimal? VnHeight { get; set; }
        public decimal VolumetricWeight { get; set; }
        public decimal ChargeableWeight { get; set; }

        /// <summary>Ảnh kho VN chụp lúc tiếp nhận kiện (VN_ARRIVAL_PROOF) — căn cứ cho số cân đo VN.</summary>
        public List<AttachmentDto> ArrivalPhotos { get; set; } = new();
    }

    public class SettlementPreviewDto
    {
        public Guid OrderId { get; set; }
        public string? ConsignmentCode { get; set; }

        /// <summary>Phát hành được đợt tất toán ngay bây giờ hay chưa.</summary>
        public bool CanIssue { get; set; }
        public List<SettlementBlockerDto> Blockers { get; set; } = new();

        public decimal FreightRate { get; set; }
        public decimal QuotedFreight { get; set; }
        public decimal MinimumWeight { get; set; }
        public decimal VolumetricDivisor { get; set; }
        public decimal VnChargeableWeight { get; set; }
        public decimal VnFreight { get; set; }

        /// <summary>Cước theo cân đo VN − cước đã báo. Âm là giảm tiền cho khách.</summary>
        public decimal FreightAdjustment { get; set; }
        public decimal VatRatePercent { get; set; }
        public decimal VatAdjustment { get; set; }

        /// <summary>
        /// Mua hộ: thuế nhập khẩu KHÔNG thu của phần hàng không tới tay khách (kiện bị huỷ / thất lạc,
        /// NCC giao thiếu). Luôn ≤ 0; ghi thành dòng TAX_ADJUSTMENT trên hoá đơn khi phát hành đợt cuối.
        /// </summary>
        public decimal ImportTaxAdjustment { get; set; }
        public string? ImportTaxAdjustmentNote { get; set; }

        /// <summary>Lý do không tính lại cước (đơn cũ chưa cân đo VN, báo giá không theo kg). Null là đã tính lại.</summary>
        public string? AdjustmentNote { get; set; }

        public decimal StorageFee { get; set; }

        /// <summary>Tổng hoá đơn hiện tại (trước điều chỉnh của lần phát hành này).</summary>
        public decimal InvoiceTotalBefore { get; set; }
        public decimal DepositPaid { get; set; }

        /// <summary>Số tiền đợt cuối dự kiến, chưa gồm phụ phí Sale nhập thêm lúc phát hành.</summary>
        public decimal EstimatedFinalAmount { get; set; }
        public List<SettlementParcelDto> Parcels { get; set; } = new();
    }

    // ── Hướng xử lý từng kiện ───────────────────────────────────────────────────────────────

    public class SetDestinationHandlingRequest
    {
        public List<ParcelHandlingItem> Items { get; set; } = new();
    }

    public class ParcelHandlingItem
    {
        public Guid ParcelId { get; set; }

        /// <summary>DIRECT_DELIVERY · STORE_AT_VN.</summary>
        public string? Handling { get; set; }
    }

    public class ParcelHandlingDto
    {
        public Guid ParcelId { get; set; }
        public string? PackageCode { get; set; }
        public string? PackageStatus { get; set; }
        public string? Handling { get; set; }

        /// <summary>Còn đổi được không — kiện đã vào phiếu nhập / yêu cầu giao thì khoá.</summary>
        public bool CanChange { get; set; }
        public string? LockedReason { get; set; }
    }

    // ── Sự cố ──────────────────────────────────────────────────────────────────────────────

    public class CreateParcelIncidentRequest
    {
        public Guid ParcelId { get; set; }
        public string? IncidentType { get; set; }
        public string? Description { get; set; }
    }

    public class CreateComplaintRequest
    {
        public List<Guid>? ParcelIds { get; set; }
        public string? Description { get; set; }
    }

    public class IncidentCustomerResponseRequest
    {
        public string? Choice { get; set; }
        public string? Note { get; set; }
    }

    public class ResolveIncidentRequest
    {
        public string? Resolution { get; set; }
        public decimal? CompensationAmount { get; set; }
        public string? Note { get; set; }
    }

    public class CompensationPaidRequest
    {
        public string? Reference { get; set; }
        public string? Note { get; set; }
    }

    public class ParcelIncidentDto
    {
        public Guid Id { get; set; }
        public string? IncidentCode { get; set; }
        public Guid ParcelId { get; set; }
        public string? PackageCode { get; set; }
        public Guid? OrderId { get; set; }
        public string? ConsignmentCode { get; set; }
        public string? CustomerName { get; set; }
        public string? IncidentType { get; set; }
        public string? IncidentTypeText { get; set; }
        public string? Status { get; set; }
        public string? Stage { get; set; }
        public string? Description { get; set; }
        public string? ReportedByName { get; set; }
        public DateTime ReportedAt { get; set; }
        public string? CustomerChoice { get; set; }
        public string? CustomerNote { get; set; }
        public DateTime? CustomerRespondedAt { get; set; }
        public string? Resolution { get; set; }
        public string? ResolutionNote { get; set; }
        public decimal? CompensationAmount { get; set; }
        public string? ResolvedByName { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public DateTime? CompensationPaidAt { get; set; }
        public string? CompensationReference { get; set; }

        /// <summary>Khách được chọn cách xử lý cho loại sự cố này không.</summary>
        public bool AwaitsCustomerChoice { get; set; }
        public List<AttachmentDto> Attachments { get; set; } = new();
    }

    public class ParcelIncidentQuery
    {
        public string? Status { get; set; }
        public Guid? OrderId { get; set; }
        public string? IncidentType { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class PagedParcelIncidentsDto
    {
        public List<ParcelIncidentDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    // ── Giao hàng: bằng chứng, nhận hàng hoàn ───────────────────────────────────────────────

    public class DeliveryProofRequest
    {
        public string? ReceivedBy { get; set; }
        public string? Note { get; set; }
    }

    public class ReceiveReturnedDeliveryRequest
    {
        /// <summary>Kiện hãng trả về. Bỏ trống = mọi kiện đang hoàn của yêu cầu này.</summary>
        public List<Guid>? ParcelIds { get; set; }
        public string? Note { get; set; }
    }
}
