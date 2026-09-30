using System;
using System.Collections.Generic;
using System.Linq;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>Trạng thái báo giá và điều kiện khách được xác nhận.</summary>
    public static class QuotationAcceptanceRules
    {
        public const string StatusDraft = "DRAFT";
        public const string StatusPending = "PENDING";
        public const string StatusPendingPriceApproval = "PENDING_PRICE_APPROVAL";
        public const string StatusPriceRejected = "PRICE_REJECTED";
        public const string StatusAccepted = "ACCEPTED";
        public const string StatusRejected = "REJECTED";
        public const string StatusSuperseded = "SUPERSEDED";

        public const string PriceApprovalNotRequired = "NOT_REQUIRED";
        public const string PriceApprovalPending = "PENDING";
        public const string PriceApprovalApproved = "APPROVED";
        public const string PriceApprovalRejected = "REJECTED";

        /// <summary>Trạng thái khách không được nhìn thấy (chưa gửi, hoặc đã bị thay thế).</summary>
        public static bool IsHiddenFromCustomer(Quotation quotation) =>
            Is(quotation.Status, StatusPendingPriceApproval)
            || Is(quotation.Status, StatusPriceRejected)
            || Is(quotation.Status, StatusSuperseded);

        public static void EnsureCustomerCanAccept(Quotation quotation)
        {
            var status = quotation.Status?.Trim().ToUpperInvariant() ?? string.Empty;

            switch (status)
            {
                case StatusAccepted:
                    throw new ArgumentException("Báo giá này đã được xác nhận trước đó.");
                case StatusPendingPriceApproval:
                    throw new ArgumentException("Báo giá có giá ngoài bảng giá đang chờ Admin duyệt, chưa xác nhận được.");
                case StatusSuperseded:
                    throw new ArgumentException("Báo giá này đã được thay bằng báo giá mới. Vui lòng xem báo giá mới nhất.");
                case StatusRejected or "CANCELLED" or "EXPIRED" or StatusPriceRejected:
                    throw new ArgumentException($"Không thể xác nhận báo giá ở trạng thái: {StatusNames.Quote(quotation.Status)}.");
            }

            if (status == StatusDraft)
            {
                // Báo giá tạm tính hệ thống tự sinh lúc tạo đơn chỉ để khách tham khảo; khách xác
                // nhận báo giá Sale đã lập và gửi.
                throw new ArgumentException(
                    "Báo giá tạm tính chỉ để tham khảo. Vui lòng chờ Sale gửi báo giá chính thức.");
            }

            if (status != StatusPending)
            {
                throw new ArgumentException($"Không thể xác nhận báo giá ở trạng thái: {StatusNames.Quote(quotation.Status)}.");
            }

            if (Is(quotation.PriceApprovalStatus, PriceApprovalPending) || Is(quotation.PriceApprovalStatus, PriceApprovalRejected))
            {
                throw new ArgumentException("Giá ngoài bảng giá của báo giá này chưa được Admin duyệt.");
            }

            if (quotation.ExpiredAt.HasValue && quotation.ExpiredAt.Value < DateTime.UtcNow)
            {
                throw new ArgumentException("Báo giá đã hết hạn. Vui lòng liên hệ Sales để nhận báo giá mới.");
            }
        }

        /// <summary>
        /// Mốc đơn được lập/gửi báo giá: đơn mới (gửi báo giá coi như Sale đã tiếp nhận), đơn đã bổ sung
        /// thông tin, đã tiếp nhận, đang có báo giá (hết hạn thì lập lại), hoặc khách vừa từ chối.
        /// </summary>
        public static readonly string[] QuotableOrderStatuses =
            { "PENDING_REVIEW", "NEED_MORE_INFO", "APPROVED", "QUOTATION_SENT", "QUOTATION_REJECTED" };

        // Kết quả xét "Sale còn lập/gửi báo giá mới được không" (RequoteState trả cho FE).
        public const string RequoteOpen = "OPEN";
        public const string RequoteReplacePendingApproval = "REPLACE_PENDING_PRICE_APPROVAL";
        public const string RequoteAfterCustomerRejected = "CUSTOMER_REJECTED";
        public const string RequoteAfterExpired = "EXPIRED";
        public const string RequoteAfterPriceRejected = "PRICE_REJECTED";
        public const string RequoteBlockedSentToCustomer = "SENT_TO_CUSTOMER";
        public const string RequoteBlockedApprovedSent = "APPROVED_SENT_TO_CUSTOMER";
        public const string RequoteBlockedAccepted = "ACCEPTED";
        public const string RequoteBlockedOrderStatus = "ORDER_NOT_QUOTABLE";

        public sealed record RequoteDecision(bool Allowed, string State, string? Reason);

        /// <summary>Báo giá khách đang xem và còn xác nhận được: đã gửi (PENDING) và chưa hết hạn.</summary>
        public static bool IsLiveForCustomer(Quotation quotation, DateTime nowUtc) =>
            Is(quotation.Status, StatusPending)
            && !(quotation.ExpiredAt.HasValue && quotation.ExpiredAt.Value < nowUtc);

        /// <summary>
        /// Sale còn lập/gửi báo giá mới cho đơn được không.
        /// Chặn khi đã có báo giá tới tay khách mà khách chưa từ chối và chưa hết hạn (gửi thẳng theo bảng giá,
        /// hoặc Admin đã duyệt giá ngoại lệ), hoặc khách đã chấp nhận. Lập lại được khi khách từ chối, báo giá
        /// hết hạn, Admin từ chối giá ngoại lệ; bản còn chờ Admin duyệt giá thì Sale thay bằng bản mới.
        /// </summary>
        public static RequoteDecision EvaluateRequote(Order order, IEnumerable<Quotation> orderQuotations, DateTime nowUtc)
        {
            var quotations = orderQuotations.ToList();

            if (!QuotableOrderStatuses.Contains(order.Status?.Trim().ToUpperInvariant()))
            {
                return new RequoteDecision(false, RequoteBlockedOrderStatus,
                    $"Không lập được báo giá khi đơn đang ở trạng thái {StatusNames.Quote(order.Status)}.");
            }

            if (quotations.Any(q => Is(q.Status, StatusAccepted)))
            {
                return new RequoteDecision(false, RequoteBlockedAccepted,
                    "Đơn đã có báo giá được khách chấp nhận. Không sửa đè báo giá đã chấp nhận; phí phát sinh đi qua đợt thanh toán cuối.");
            }

            var live = quotations
                .Where(q => IsLiveForCustomer(q, nowUtc))
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();
            if (live != null)
            {
                var deadline = live.ExpiredAt.HasValue
                    ? $" (hạn {live.ExpiredAt.Value.AddHours(7):dd/MM/yyyy HH:mm} giờ VN)"
                    : string.Empty;
                const string when = "Chỉ lập báo giá mới khi khách từ chối báo giá, báo giá hết hạn{0} hoặc Admin từ chối giá ngoại lệ.";

                return Is(live.PriceApprovalStatus, PriceApprovalApproved)
                    ? new RequoteDecision(false, RequoteBlockedApprovedSent,
                        "Báo giá đã được Admin duyệt và gửi khách — không lập lại được. " + string.Format(when, deadline))
                    : new RequoteDecision(false, RequoteBlockedSentToCustomer,
                        "Báo giá đã gửi khách và đang chờ khách xác nhận — không lập lại được. " + string.Format(when, deadline));
            }

            var latest = quotations
                .Where(q => !Is(q.Status, StatusDraft) && !Is(q.Status, StatusSuperseded))
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            if (latest == null) return new RequoteDecision(true, RequoteOpen, null);
            if (Is(latest.Status, StatusPendingPriceApproval)) return new RequoteDecision(true, RequoteReplacePendingApproval, null);
            if (Is(latest.Status, StatusRejected)) return new RequoteDecision(true, RequoteAfterCustomerRejected, null);
            if (Is(latest.Status, StatusPriceRejected)) return new RequoteDecision(true, RequoteAfterPriceRejected, null);
            if (Is(latest.Status, StatusPending)) return new RequoteDecision(true, RequoteAfterExpired, null);
            return new RequoteDecision(true, RequoteOpen, null);
        }

        private static bool Is(string? value, string expected) =>
            string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
