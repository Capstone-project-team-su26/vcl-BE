using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Trạng thái đơn mua NCC của luồng mua hộ chuẩn (docs/migrate-source-cu/08-luong-mua-ho.md mục 6).
    /// </summary>
    public static class PurchaseOrderStatuses
    {
        public const string Draft = "DRAFT";

        /// <summary>Giá thực vượt giá báo quá ngưỡng — chờ khách xem phần chênh.</summary>
        public const string AwaitingCustomer = "AWAITING_CUSTOMER";

        /// <summary>Khách đồng ý phần chênh, chờ khách trả.</summary>
        public const string AwaitingCustomerPayment = "AWAITING_CUSTOMER_PAYMENT";

        public const string PendingApproval = "PENDING_APPROVAL";
        public const string Approved = "APPROVED";
        public const string Rejected = "REJECTED";
        public const string Ordered = "ORDERED";
        public const string SupplierConfirmed = "SUPPLIER_CONFIRMED";
        public const string SupplierShipped = "SUPPLIER_SHIPPED";
        public const string Cancelled = "CANCELLED";

        /// <summary>Sale còn sửa được nội dung đơn.</summary>
        public static readonly HashSet<string> Editable = new(StringComparer.OrdinalIgnoreCase) { Draft, Rejected };

        /// <summary>Đã đặt NCC — hàng hoá của đơn đã thành đơn kho.</summary>
        public static readonly HashSet<string> Placed = new(StringComparer.OrdinalIgnoreCase)
        { Ordered, SupplierConfirmed, SupplierShipped };

        public static bool Is(string? actual, string expected) =>
            string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

        public static bool IsActive(PurchaseOrder po) => !Is(po.Status, Cancelled);

        public static string Text(string? status) => status?.Trim().ToUpperInvariant() switch
        {
            Draft => "Nháp",
            AwaitingCustomer => "Chờ khách xem giá chênh",
            AwaitingCustomerPayment => "Chờ khách trả phần chênh",
            PendingApproval => "Chờ Admin duyệt",
            Approved => "Đã duyệt, chờ đặt NCC",
            Rejected => "Admin từ chối",
            Ordered => "Đã đặt NCC",
            SupplierConfirmed => "NCC đã xác nhận",
            SupplierShipped => "NCC đã phát hàng",
            Cancelled => "Đã huỷ",
            _ => status ?? string.Empty
        };
    }

    /// <summary>Loại khoản thu của mua hộ.</summary>
    public static class PurchasePaymentTypes
    {
        /// <summary>Luồng mới: 100% phần trả trước (tiền hàng + phí mua + ship nội địa + VAT phần phí).</summary>
        public const string Prepayment = "PREPAYMENT";

        /// <summary>Phần chênh khi giá thực vượt giá báo quá ngưỡng — gắn với một đơn mua NCC.</summary>
        public const string PriceDifference = "PRICE_DIFFERENCE";

        /// <summary>
        /// Hoàn tiền vì giá mua THỰC thấp hơn giá đã báo. Khách đã trả trước 100% theo
        /// giá báo, nên phần thừa là tiền của khách chứ không phải lãi của công ty.
        /// </summary>
        public const string RefundPriceDifference = "REFUND_PRICE_DIFF";

        /// <summary>Hoàn tiền vì huỷ đơn mua sau khi đã đặt NCC (đã trừ phí huỷ nếu có).</summary>
        public const string RefundCancellation = "REFUND_CANCEL";

        /// <summary>
        /// Hoàn phần KHÔNG mua được (NCC hết hàng, mua ít hơn khách đặt) hoặc NCC giao thiếu:
        /// hoàn đủ tiền hàng + phí mua hộ + VAT phần phí tương ứng.
        /// </summary>
        public const string RefundUnfulfilled = "REFUND_UNFULFILLED";

        public static string RefundText(string? type) => type?.Trim().ToUpperInvariant() switch
        {
            RefundPriceDifference => "Hoàn phần chênh giá mua",
            RefundCancellation => "Hoàn tiền do huỷ đơn mua",
            RefundUnfulfilled => "Hoàn phần không mua được / NCC giao thiếu",
            _ => type ?? string.Empty
        };

        /// <summary>Đời cũ: cọc theo tỷ lệ.</summary>
        public const string LegacyDeposit = "DEPOSIT";

        /// <summary>Đời cũ: trả một lần; cột để trống cũng coi là loại này.</summary>
        public const string LegacyFull = "FULL_PAYMENT";

        /// <summary>Đời cũ: đợt cuối thu ở kho nguồn.</summary>
        public const string LegacyFinal = "FINAL_PAYMENT";

        public static string Of(PurchasePayment p) =>
            string.IsNullOrWhiteSpace(p.PaymentType) ? LegacyFull : p.PaymentType.Trim().ToUpperInvariant();

        /// <summary>Khoản trả trước của cả yêu cầu (mới hoặc cũ) — điều kiện để lập / duyệt đơn mua.</summary>
        public static bool IsFirstInstalment(PurchasePayment p) =>
            Of(p) is Prepayment or LegacyDeposit or LegacyFull;

        public static bool IsPaid(PurchasePayment p) =>
            string.Equals(p.Status?.Trim(), "PAID", StringComparison.OrdinalIgnoreCase);

        /// <summary>Dòng tiền ĐI RA (trả lại khách), không phải tiền thu vào.</summary>
        public static bool IsRefund(PurchasePayment p) =>
            Of(p).StartsWith("REFUND", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Trạng thái của một khoản HOÀN TIỀN.
    ///
    /// Không dùng chung "PAID" với khoản thu: đọc "hoàn tiền: đã trả" rất dễ hiểu ngược
    /// thành khách đã trả tiền. Khoản hoàn nằm cùng bảng PURCHASE_PAYMENTS nhưng
    /// PaymentType bắt đầu bằng REFUND nên lọc ra được.
    /// </summary>
    public static class PurchaseRefundStatuses
    {
        /// <summary>Đã chốt số tiền phải trả lại, chờ kế toán chuyển.</summary>
        public const string Pending = "PENDING";

        /// <summary>Đã chuyển trả khách, có mã giao dịch.</summary>
        public const string Refunded = "REFUNDED";

        /// <summary>Không phải trả nữa (đơn quay lại chạy tiếp, hoặc gộp vào khoản khác).</summary>
        public const string Cancelled = "CANCELLED";

        public static string Text(string? status) => status?.Trim().ToUpperInvariant() switch
        {
            Pending => "Chờ hoàn tiền",
            Refunded => "Đã hoàn tiền",
            Cancelled => "Đã huỷ khoản hoàn",
            _ => status ?? string.Empty
        };
    }

    /// <summary>Lý do của từng dòng hoàn tiền (PURCHASE_REFUND_LINES.reason_code).</summary>
    public static class PurchaseRefundReasons
    {
        /// <summary>Giá mua thực thấp hơn giá báo — hoàn phần thừa.</summary>
        public const string PriceDiff = "PRICE_DIFF";

        /// <summary>Sản phẩm / số lượng không mua được — hoàn tiền hàng + phí mua hộ + VAT phí.</summary>
        public const string Unfulfilled = "UNFULFILLED";

        /// <summary>NCC giao thiếu trong đơn đã đặt — hoàn như không mua được, bỏ thuế NK phần thiếu.</summary>
        public const string SupplierShort = "SUPPLIER_SHORT";

        /// <summary>Khách huỷ sau khi đặt NCC — hoàn tiền hàng thực trả − phí huỷ, giữ phí mua hộ + VAT phí.</summary>
        public const string CancelCustomer = "CANCEL_CUSTOMER";

        /// <summary>NCC huỷ / hết hàng sau khi đặt — hoàn đủ tiền hàng + phí mua hộ + VAT phí.</summary>
        public const string CancelSupplier = "CANCEL_SUPPLIER";

        /// <summary>Huỷ đơn chưa đặt NCC mà khách đã trả phần chênh giá — trả lại phần chênh.</summary>
        public const string PriceDiffReturn = "PRICE_DIFF_RETURN";

        public static string Text(string? code) => code?.Trim().ToUpperInvariant() switch
        {
            PriceDiff => "Giá mua thực thấp hơn giá báo",
            Unfulfilled => "Không mua được",
            SupplierShort => "NCC giao thiếu",
            CancelCustomer => "Khách huỷ sau khi đã đặt NCC",
            CancelSupplier => "NCC huỷ / hết hàng sau khi đã đặt",
            PriceDiffReturn => "Trả lại phần chênh giá đã thu",
            _ => code ?? string.Empty
        };
    }

    /// <summary>
    /// Việc phải làm khi một khoản thu mua hộ được ghi nhận đã trả — dùng chung cho webhook,
    /// đồng bộ cổng thanh toán và Admin duyệt tay, để ba đường không lệch nhau.
    /// </summary>
    public static class PurchasePaymentEffects
    {
        /// <summary>Trạng thái yêu cầu đã đi quá bước trả trước — webhook tới muộn không được kéo lùi.</summary>
        private static readonly HashSet<string> BeyondPrepaid = new(StringComparer.OrdinalIgnoreCase)
        {
            "PURCHASING", "PURCHASED", "SELLER_SHIPPED", "ARRIVED_ORIGIN_WAREHOUSE", "WAITING_STORED",
            "STORED", "COMPLETED", "CANCELLED"
        };

        /// <summary>Trả về trạng thái mới của yêu cầu (hoặc trạng thái cũ nếu không đổi).</summary>
        public static async Task<string?> ApplyPaidAsync(IUnitOfWork unitOfWork, PurchasePayment payment)
        {
            var requestRepo = unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(payment.PurchaseRequestId);
            if (purchaseRequest == null) return null;

            var type = PurchasePaymentTypes.Of(payment);

            if (type == PurchasePaymentTypes.PriceDifference)
            {
                if (payment.PurchaseOrderId.HasValue)
                {
                    var poRepo = unitOfWork.Repository<PurchaseOrder>();
                    var po = await poRepo.GetByIdAsync(payment.PurchaseOrderId.Value);
                    if (po != null && PurchaseOrderStatuses.Is(po.Status, PurchaseOrderStatuses.AwaitingCustomerPayment))
                    {
                        po.Status = PurchaseOrderStatuses.PendingApproval;
                        po.SubmittedAt = DateTime.UtcNow;
                        po.UpdatedAt = DateTime.UtcNow;
                        poRepo.Update(po);
                    }
                }
                return purchaseRequest.Status;
            }

            if (type == PurchasePaymentTypes.LegacyFinal)
            {
                purchaseRequest.Status = "COMPLETED";
                requestRepo.Update(purchaseRequest);
                return purchaseRequest.Status;
            }

            if (!BeyondPrepaid.Contains(purchaseRequest.Status?.Trim() ?? string.Empty))
            {
                purchaseRequest.Status = "PAID";
                requestRepo.Update(purchaseRequest);
            }
            return purchaseRequest.Status;
        }
    }

    /// <summary>
    /// Yêu cầu mua hộ hoàn tất khi mọi sản phẩm đã nằm trong đơn mua đã đặt và mọi đơn kho sinh ra
    /// từ đó đã đóng. Gọi mỗi khi một đơn kho mua hộ chuyển sang COMPLETED (trước SaveChanges).
    /// </summary>
    public static class PurchaseRequestProgress
    {
        public static async Task SyncAfterOrderClosedAsync(IUnitOfWork unitOfWork, Order order)
        {
            if (!order.PurchaseOrderId.HasValue || !order.PurchaseRequestId.HasValue) return;

            var requestId = order.PurchaseRequestId.Value;
            var requestRepo = unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId);
            if (purchaseRequest == null || string.Equals(purchaseRequest.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                return;

            var placedPos = (await unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                .Where(po => po.PurchaseRequestId == requestId && PurchaseOrderStatuses.Placed.Contains(po.Status?.Trim() ?? string.Empty))
                .ToList();
            if (placedPos.Count == 0) return;

            // Còn đơn mua đang soạn / chờ duyệt thì chưa xong.
            var hasOpenPo = (await unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                .Any(po => po.PurchaseRequestId == requestId
                           && PurchaseOrderStatuses.IsActive(po)
                           && !PurchaseOrderStatuses.Placed.Contains(po.Status?.Trim() ?? string.Empty));
            if (hasOpenPo) return;

            var placedIds = placedPos.Select(po => po.Id).ToHashSet();
            var placedItemIds = (await unitOfWork.Repository<PurchaseOrderItem>().GetAllAsync())
                .Where(i => placedIds.Contains(i.PurchaseOrderId))
                .Select(i => i.PurchaseRequestItemId)
                .ToHashSet();

            // Sản phẩm đã đóng phần không mua được (có khoản hoàn UNFULFILLED còn hiệu lực) cũng coi là xong.
            var liveRefundIds = (await unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => p.PurchaseRequestId == requestId && PurchasePaymentTypes.IsRefund(p)
                            && !string.Equals(p.Status, PurchaseRefundStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .ToHashSet();
            foreach (var closed in (await unitOfWork.Repository<PurchaseRefundLine>().GetAllAsync())
                         .Where(l => liveRefundIds.Contains(l.PurchasePaymentId) && l.PurchaseRequestItemId.HasValue
                                     && string.Equals(l.ReasonCode, PurchaseRefundReasons.Unfulfilled, StringComparison.OrdinalIgnoreCase)))
                placedItemIds.Add(closed.PurchaseRequestItemId!.Value);
            var allItemsCovered = (await unitOfWork.Repository<PurchaseRequestItem>().GetAllAsync())
                .Where(i => i.PurchaseRequestId == requestId)
                .All(i => placedItemIds.Contains(i.Id));
            if (!allItemsCovered) return;

            var orders = (await unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => o.PurchaseOrderId.HasValue && placedIds.Contains(o.PurchaseOrderId.Value))
                .ToList();
            if (orders.Count == 0 || orders.Any(o => !string.Equals(o.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)))
                return;

            purchaseRequest.Status = "COMPLETED";
            requestRepo.Update(purchaseRequest);
        }
    }
}
