using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class WarehouseReceivingNoteService : IWarehouseReceivingNoteService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notificationService;
        private readonly ILogger<WarehouseReceivingNoteService> _logger;
        private readonly IConsignmentReceiptService _receiptService;

        /// <summary>
        /// Tạo kiện khi kho chốt số. Dùng lại nguyên bản check-in thay vì tự sinh mã kiện và
        /// tự đẩy trạng thái đơn — hai đường tạo kiện mà khác luật là sớm muộn cũng lệch.
        /// </summary>
        private readonly IParcelService _parcelService;

        /*
         * Luồng ký gửi:
         *   Sale báo giá → khách xác nhận + thanh toán cọc → hệ thống chuyển đơn sang DEPOSIT_PAID
         *   → Sale lập phiếu nhập kho → quản lý kho duyệt → khách mang phiếu và hàng tới
         *   → kho kiểm, khớp thì nhập, không lập phiếu nào nữa.
         *
         * DEPOSIT_PAID là mốc duy nhất Sale lập được phiếu; hàng đợi việc của Sale dùng lại chính nó.
         */
        public static readonly string[] ReceivingNoteOpenableStatuses = { "DEPOSIT_PAID" };

        /// <summary>Phiếu còn hiệu lực — đã có một phiếu như vậy thì không lập phiếu mới.</summary>
        public static readonly string[] ActiveNoteStatuses =
            { StatusPendingApproval, StatusActive, StatusPartiallyReceived };

        /*
         * Vòng đời phiếu tiếp nhận:
         *
         *   PENDING_APPROVAL → ACTIVE → PARTIALLY_RECEIVED → RECEIVED ─(khớp)→ APPROVED
         *          ↘ REJECTED                                         ↘(chênh lệch) quản lý kho xem → APPROVED / REJECTED
         *
         * Quản lý của kho duyệt TRƯỚC khi kho nhận hàng. Phần kiểm đếm khớp phiếu đã duyệt thì
         * xếp kệ luôn, không duyệt lần hai; chỉ khi lệch số lượng hoặc có hàng ngoài khai báo thì
         * quản lý kho mới phải xem lại biên bản.
         *
         * Lệch CÂN mà số lượng khớp: phiếu vẫn tự chốt APPROVED (kiện xếp kệ được ngay), nhưng
         * nằm trong hàng đợi của quản lý kho ở bước DISCREPANCY_ACK cho tới khi có người chấp nhận
         * số thực tế (ghi vào ApprovedBy). Trước đây bước này không tồn tại: phiếu lệch cân hiện
         * "Đã duyệt · Có lệch" ở app kho mà màn OM không có phiếu nào để quyết.
         */
        public const string StatusPendingApproval = "PENDING_APPROVAL";
        public const string StatusActive = "ACTIVE";
        public const string StatusPartiallyReceived = "PARTIALLY_RECEIVED";
        public const string StatusReceived = "RECEIVED";
        public const string StatusApproved = "APPROVED";
        public const string StatusRejected = "REJECTED";

        /// <summary>Phiếu đã được quản lý kho cho phép nhận hàng và chưa bị huỷ.</summary>
        public static bool IsApprovedToReceive(WarehouseReceivingNote note) =>
            note.ReceiveApprovedAt.HasValue
            && !IsStatus(note.Status, StatusRejected)
            && !IsStatus(note.Status, StatusPendingApproval);

        /// <summary>Phiếu chờ duyệt cho nhận hàng.</summary>
        public const string StageReceive = "RECEIVE";

        /// <summary>Kiểm đếm lệch số lượng / hàng ngoài khai báo — chặn xếp kệ tới khi quyết.</summary>
        public const string StageDiscrepancy = "DISCREPANCY";

        /// <summary>Đã tự chốt (số lượng khớp) nhưng lệch cân — chờ quản lý kho chấp nhận số thực tế, không chặn xếp kệ.</summary>
        public const string StageDiscrepancyAck = "DISCREPANCY_ACK";

        /// <summary>Có dòng lệch số lượng hoặc lệch cân so với khai báo.</summary>
        public static bool HasDiscrepancy(IEnumerable<WarehouseReceivingNoteItem> items) =>
            items.Any(i => i.QuantityDifference != 0
                           || (i.WeightDifference.HasValue && i.WeightDifference.Value != 0));

        /// <summary>
        /// Phiếu tự chốt (không ai duyệt biên bản) mà biên bản vẫn lệch — chờ quản lý kho chấp nhận
        /// số thực tế. Hết chờ khi đã có người quyết (ApprovedBy).
        /// </summary>
        public static bool AwaitsDiscrepancyAcceptance(WarehouseReceivingNote note, IEnumerable<WarehouseReceivingNoteItem> items) =>
            IsStatus(note.Status, StatusApproved) && !note.ApprovedBy.HasValue && HasDiscrepancy(items);

        /// <summary>Bước đang chờ quyết định của phiếu, null khi không chờ ai.</summary>
        public static string? ResolveApprovalStage(WarehouseReceivingNote note, IReadOnlyCollection<WarehouseReceivingNoteItem> items)
        {
            if (IsStatus(note.Status, StatusPendingApproval)) return StageReceive;
            if ((IsStatus(note.Status, StatusReceived) || IsStatus(note.Status, StatusPartiallyReceived)) && RequiresReview(items))
                return StageDiscrepancy;
            if (AwaitsDiscrepancyAcceptance(note, items)) return StageDiscrepancyAck;
            return null;
        }

        /// <summary>Nhãn trạng thái có tính cả bước chờ quyết lệch — "Đã duyệt" trơn dễ hiểu nhầm là xong.</summary>
        public static string DescribeNoteStatus(WarehouseReceivingNote note, IReadOnlyCollection<WarehouseReceivingNoteItem> items) =>
            ResolveApprovalStage(note, items) == StageDiscrepancyAck
                ? "Đã chốt kiểm đếm · chờ quản lý kho quyết định lệch"
                : DescribeStatus(note.Status);

        /// <summary>
        /// Biên bản có dòng lệch số lượng hoặc hàng ngoài khai báo — hoặc kiểm đếm ra 0 món (NCC/khách
        /// không giao gì): phiếu đó luôn chờ quản lý kho quyết, không tự duyệt.
        /// </summary>
        public static bool RequiresReview(IEnumerable<WarehouseReceivingNoteItem> items)
        {
            var list = items as IReadOnlyCollection<WarehouseReceivingNoteItem> ?? items.ToList();
            return list.Any(i => i.QuantityDifference != 0 || !i.OrderItemId.HasValue)
                   || (list.Count > 0 && list.Sum(i => i.ActualQuantity) == 0);
        }

        /// <summary>Kiện của phiếu này được xếp kệ chưa.</summary>
        public static bool AllowsPutAway(WarehouseReceivingNote note, IEnumerable<WarehouseReceivingNoteItem> items) =>
            IsStatus(note.Status, StatusApproved)
            || (IsApprovedToReceive(note)
                && (IsStatus(note.Status, StatusReceived) || IsStatus(note.Status, StatusPartiallyReceived))
                && !RequiresReview(items));

        private static bool IsStatus(string? value, string expected) =>
            string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

        private static bool CanOpenReceivingNote(string? orderStatus) =>
            !string.IsNullOrWhiteSpace(orderStatus)
            && ReceivingNoteOpenableStatuses.Contains(orderStatus.Trim(), StringComparer.OrdinalIgnoreCase);

        public WarehouseReceivingNoteService(
            IUnitOfWork unitOfWork,
            INotificationService notificationService,
            ILogger<WarehouseReceivingNoteService> logger,
            IParcelService parcelService,
            IConsignmentReceiptService receiptService)
        {
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
            _logger = logger;
            _parcelService = parcelService;
            _receiptService = receiptService;
        }

        public async Task<WarehouseReceivingNoteResponseDto> GetReceivingNoteByConsignmentIdAsync(Guid consignmentOrderId)
        {
            var wrnRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();

            var allNotes = await wrnRepo.GetAllAsync();
            var matchedNote = allNotes
                .Where(n => n.ConsignmentOrderId == consignmentOrderId)
                .Where(n => ActiveNoteStatuses.Contains(n.Status?.Trim(), StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(n => n.CreatedAt)
                .FirstOrDefault();

            if (matchedNote == null)
                throw new NotFoundException("Chưa có phiếu tiếp nhận kho nào được kích hoạt cho yêu cầu ký gửi này.");

            // Trả bản đầy đủ: khách, tuyến, hàng khai báo. Trước đây chỉ trả 8 trường của bản thân
            // phiếu nên màn hình mở ra gần như trống, không đủ để kho cầm đi đối chiếu hàng.
            return await BuildDetailAsync(matchedNote);
        }

        public async Task<WarehouseReceivingNoteScanResponseDto> ScanReceivingNoteAsync(string code, Guid? actorId = null, string? actorRole = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Vui lòng cung cấp mã phiếu tiếp nhận kho.");

            // 1. Tìm phiếu theo mã (404 nếu không có)
            var wrnRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
            var allNotes = await wrnRepo.GetAllAsync();
            var note = allNotes.FirstOrDefault(n =>
                string.Equals(n.ReceivingNoteCode, code.Trim(), StringComparison.OrdinalIgnoreCase));
            if (note == null)
                throw new NotFoundException($"Không tìm thấy phiếu tiếp nhận kho với mã: {code}");

            // Nhân viên đã được gán kho chỉ quét được phiếu của kho mình (xác nhận nhận hàng và
            // check-in đã chặn như vậy; trước đây riêng bước quét lại lọt).
            await WarehouseAccessPolicy.EnsureAssignedWarehouseAsync(_unitOfWork, actorId, actorRole, note.WarehouseId);

            // 2. Yêu cầu ký gửi + khách hàng
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(note.ConsignmentOrderId);
            if (order == null)
                throw new NotFoundException("Không tìm thấy yêu cầu ký gửi liên kết với phiếu tiếp nhận.");

            Customer? customer = null;
            if (order.CustomerId.HasValue)
            {
                var customers = await _unitOfWork.Repository<Customer>().GetAllAsync();
                customer = customers.FirstOrDefault(c => c.Id == order.CustomerId.Value);
            }

            // 3. Danh sách hàng dự kiến nhận (order items)
            var expectedItems = await BuildExpectedItemsAsync(order.Id);

            // 4. Kho tiếp nhận
            var warehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(note.WarehouseId);

            var purchase = (await LoadPurchaseInfoAsync(new[] { order })).GetValueOrDefault(order.Id) ?? PurchaseInfo.Consignment;
            var scanLines = (await _unitOfWork.Repository<WarehouseReceivingNoteItem>().FindAsync(i => i.ReceivingNoteId == note.Id)).ToList();

            var scan = new WarehouseReceivingNoteScanResponseDto
            {
                ReceivingNoteId = note.Id,
                ReceivingNoteCode = note.ReceivingNoteCode ?? string.Empty,
                Status = note.Status ?? string.Empty,
                ConsignmentOrderId = order.Id,
                ConsignmentCode = order.ConsignmentCode ?? string.Empty,
                CustomerId = order.CustomerId,
                CustomerName = customer?.FullName ?? string.Empty,
                CustomerCode = customer?.CustomerCode ?? string.Empty,
                WarehouseId = note.WarehouseId,
                WarehouseName = warehouse?.WarehouseName ?? "Kho không xác định",
                WarehouseNote = note.WarehouseNote ?? string.Empty,
                StatusText = DescribeNoteStatus(note, scanLines),
                ExpectedItems = expectedItems
            };
            purchase.ApplyTo(scan);
            return scan;
        }

        public async Task<WarehouseReceivingNoteDetailDto> CreateReceivingNoteAsync(CreateWarehouseReceivingNoteRequestDto request, Guid actorId)
        {
            // 1. Kho nhận hàng ký gửi phải là kho quốc tế (ORIGIN) đang hoạt động. Hàng về VN đi phiếu
            // nhập kho VN riêng, không dùng phiếu tiếp nhận.
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var warehouse = await warehouseRepo.GetByIdAsync(request.WarehouseId);
            if (warehouse == null)
                throw new NotFoundException($"Không tìm thấy kho với ID: {request.WarehouseId}");

            if (warehouse.IsActive == false)
                throw new ArgumentException("Kho tiếp nhận được chọn đang tạm ngưng hoạt động.");

            if (!string.Equals(warehouse.WarehouseType?.Trim(), "ORIGIN", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Phiếu tiếp nhận ký gửi chỉ lập cho kho nhận hàng quốc tế (ORIGIN).");

            // 2. Đơn phải tồn tại, là ký gửi và đã có cọc được xác nhận.
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(request.ConsignmentOrderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID: {request.ConsignmentOrderId}");

            if (string.Equals(order.OrderType, "PURCHASE", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Đơn mua hộ vào kho qua bước duyệt nhập kho mua hộ, không lập phiếu tiếp nhận ký gửi.");

            if (!CanOpenReceivingNote(order.Status))
                throw new ArgumentException(
                    $"Không lập được phiếu tiếp nhận khi đơn đang ở trạng thái {StatusNames.Quote(order.Status)}. "
                    + "Chỉ lập được sau khi khách đã thanh toán cọc.");

            var wrnRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
            var allNotes = await wrnRepo.GetAllAsync();

            // Rule: Only 1 active receiving note per consignment
            var hasActiveNote = allNotes.Any(n =>
                n.ConsignmentOrderId == request.ConsignmentOrderId
                && ActiveNoteStatuses.Contains(n.Status?.Trim(), StringComparer.OrdinalIgnoreCase));

            if (hasActiveNote)
                throw new ConflictException("Yêu cầu ký gửi này đã có một phiếu tiếp nhận đang hoạt động.");

            var newNote = await AddNoteAsync(
                request.ConsignmentOrderId, request.WarehouseId, request.WarehouseNote, actorId, allNotes);

            await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, order.Status, order.Status,
                "RECEIVING_NOTE_CREATED", $"Lập phiếu tiếp nhận {newNote.ReceivingNoteCode} tại kho {warehouse.WarehouseName}.", actorId);

            await _unitOfWork.SaveChangesAsync();

            await NotifyReceiveApproversAsync(newNote, warehouse, order, actorId);

            // Trả luôn bản đầy đủ để màn hình vừa lập xong đã hiện được nội dung phiếu.
            return await BuildDetailAsync(newNote);
        }

        /// <summary>
        /// Dựng bản ghi phiếu với mã <c>WRN-XXXXXXXX</c> không trùng. Chưa lưu — người gọi tự
        /// quyết thời điểm <c>SaveChangesAsync</c>.
        /// </summary>
        private async Task<WarehouseReceivingNote> AddNoteAsync(
            Guid orderId, Guid warehouseId, string? warehouseNote, Guid createdById,
            IEnumerable<WarehouseReceivingNote>? knownNotes = null)
        {
            var existing = (knownNotes ?? await _unitOfWork.Repository<WarehouseReceivingNote>().GetAllAsync())
                .ToList();

            var random = new Random();
            string code;
            do
            {
                var randomDigits = random.Next(10000000, 99999999).ToString();
                code = $"WRN-{randomDigits}";
            } while (existing.Any(n => string.Equals(n.ReceivingNoteCode, code, StringComparison.OrdinalIgnoreCase)));

            var note = new WarehouseReceivingNote
            {
                Id = Guid.NewGuid(),
                ReceivingNoteCode = code,
                ConsignmentOrderId = orderId,
                WarehouseId = warehouseId,
                WarehouseNote = warehouseNote ?? string.Empty,
                Status = StatusPendingApproval,
                CreatedById = createdById,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Repository<WarehouseReceivingNote>().AddAsync(note);
            return note;
        }

        /// <summary>
        /// Phiếu tiếp nhận của MỘT đơn, cho khách tự tra.
        /// </summary>
        /// <remarks>
        /// Khách hàng thanh toán xong là muốn biết "kho nhận hàng chưa, có thiếu gì không" —
        /// trước đây mọi đường đọc phiếu đều chặn role Customer nên họ phải nhắn hỏi sale.
        /// <para>
        /// Bắt buộc kiểm đơn có phải của chính người gọi không: chỉ cần biết orderId của người
        /// khác là xem được toàn bộ hàng hoá và số liệu kiểm đếm của họ.
        /// </para>
        /// </remarks>
        public async Task<WarehouseReceivingNoteDetailDto?> GetMyReceivingNoteAsync(
            Guid consignmentOrderId, Guid userId)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(consignmentOrderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {consignmentOrderId}");

            var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .FirstOrDefault(c => c.UserId == userId);

            if (customer == null || order.CustomerId != customer.Id)
            {
                throw new UnauthorizedAccessException("Bạn chỉ xem được phiếu tiếp nhận của đơn hàng mình.");
            }

            // Phiếu bị từ chối là chuyện xử lý nội bộ giữa kho và OM, khách không cần thấy.
            var note = (await _unitOfWork.Repository<WarehouseReceivingNote>().GetAllAsync())
                .Where(n => n.ConsignmentOrderId == consignmentOrderId
                            && !string.Equals(n.Status, StatusRejected, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.CreatedAt)
                .FirstOrDefault();

            if (note == null) return null;

            var detail = await BuildDetailAsync(note);

            // Ghi chú nội bộ của kho và lý do từ chối không phải thứ đưa cho khách đọc.
            detail.WarehouseNote = string.Empty;
            detail.RejectionReason = null;

            // Như PurchaseOrderService (forCustomer): NCC và mã đơn bên NCC là thông tin nội bộ.
            detail.SupplierName = null;
            detail.SupplierOrderCode = null;
            return detail;
        }

        public async Task<WarehouseReceivingNoteDetailDto> GetReceivingNoteDetailAsync(Guid receivingNoteId)
        {
            var note = await _unitOfWork.Repository<WarehouseReceivingNote>().GetByIdAsync(receivingNoteId);
            if (note == null)
            {
                throw new NotFoundException($"Không tìm thấy phiếu tiếp nhận kho với ID: {receivingNoteId}");
            }

            return await BuildDetailAsync(note);
        }

        public async Task<WarehouseReceivingNoteDetailDto> ConfirmReceiptAsync(Guid receivingNoteId, ConfirmReceiptRequestDto request, Guid actorId)
        {
            if (request == null || request.Items == null || request.Items.Count == 0)
            {
                throw new ArgumentException("Vui lòng nhập số liệu thực tế của ít nhất một dòng hàng.");
            }

            if (request.Items.Any(i => i.ActualQuantity < 0))
            {
                throw new ArgumentException("Số lượng thực tế không được nhỏ hơn 0.");
            }

            // 1. Phiếu phải đã được quản lý kho cho phép nhận hàng và chưa chốt xong.
            var noteRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
            var note = await noteRepo.GetByIdAsync(receivingNoteId);
            if (note == null)
            {
                throw new NotFoundException($"Không tìm thấy phiếu tiếp nhận kho với ID: {receivingNoteId}");
            }

            if (IsStatus(note.Status, StatusPendingApproval))
            {
                throw new ArgumentException($"Phiếu {note.ReceivingNoteCode} đang chờ quản lý kho duyệt, chưa nhận hàng được.");
            }

            if (!IsStatus(note.Status, StatusActive) && !IsStatus(note.Status, StatusPartiallyReceived))
            {
                // Trước đây chỉ chặn RECEIVED nên phiếu đã duyệt/từ chối vẫn bị xác nhận lại và thêm dòng trùng.
                throw new ConflictException(
                    $"Phiếu tiếp nhận {note.ReceivingNoteCode} đã chốt kiểm đếm (hiện tại: {StatusNames.Quote(note.Status)}), không xác nhận lại được.");
            }

            if (!IsApprovedToReceive(note))
            {
                throw new ArgumentException($"Phiếu {note.ReceivingNoteCode} chưa được quản lý kho cho phép nhận hàng.");
            }

            // 2. Lấy các dòng hàng khách hàng khai báo để đối chiếu
            var orderItems = (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                .Where(i => i.OrderId == note.ConsignmentOrderId)
                .ToList();

            var orderItemById = orderItems.ToDictionary(i => i.Id);

            var itemRepo = _unitOfWork.Repository<WarehouseReceivingNoteItem>();
            var existingItems = (await itemRepo.GetAllAsync()).Where(i => i.ReceivingNoteId == note.Id).ToList();
            var newItems = new List<WarehouseReceivingNoteItem>();

            // Biên bản đếm được hàng thì phải có kiện thật để xếp kệ / xuất — chốt "Đã kiểm N dòng · 0 kiện"
            // làm phiếu thành "Đã chốt, được xếp kệ" mà trong kho không có gì. Kiện cân đo gửi kèm biên
            // bản (request.Parcels) đi qua check-in ngay trong lần chốt nên được tính. Đếm ra 0 món thì
            // vẫn cho chốt, nhưng phiếu vào luồng lệch để quản lý kho quyết (xem RequiresReview).
            var totalActual = existingItems.Sum(i => i.ActualQuantity) + request.Items.Sum(i => i.ActualQuantity);
            var sendsParcels = request.Parcels != null && request.Parcels.Any(p => p.ActualWeight > 0m);
            if (totalActual > 0 && !sendsParcels)
            {
                var hasCheckedInParcel = (await _unitOfWork.Repository<Parcel>()
                        .FindAsync(p => p.ConsignmentId == note.ConsignmentOrderId))
                    .Any();
                if (!hasCheckedInParcel)
                {
                    throw new ArgumentException(
                        "Chưa có kiện nào được check-in theo phiếu này — cân đo và check-in kiện trước khi chốt biên bản.");
                }
            }

            foreach (var requestItem in request.Items)
            {
                OrderItem? declaredItem = null;
                if (requestItem.OrderItemId is Guid orderItemId)
                {
                    if (!orderItemById.TryGetValue(orderItemId, out declaredItem))
                    {
                        throw new ArgumentException("Dòng hàng khai báo được chọn không thuộc yêu cầu ký gửi của phiếu tiếp nhận này.");
                    }

                    // Đợt nhận sau chỉ ghi dòng chưa kiểm, không thêm lại dòng đã chốt ở đợt trước.
                    if (existingItems.Any(i => i.OrderItemId == orderItemId))
                    {
                        throw new ConflictException($"Dòng hàng \"{declaredItem.ProductName}\" đã được kiểm đếm ở đợt nhận trước.");
                    }
                }

                var productName = !string.IsNullOrWhiteSpace(requestItem.ProductName)
                    ? requestItem.ProductName.Trim()
                    : declaredItem?.ProductName ?? string.Empty;

                if (string.IsNullOrWhiteSpace(productName))
                {
                    throw new ArgumentException("Dòng hàng phát sinh ngoài khai báo phải có tên hàng.");
                }

                var declaredQuantity = declaredItem?.Quantity ?? 0;
                var declaredWeight = declaredItem?.Weight;

                var line = new WarehouseReceivingNoteItem
                {
                    Id = Guid.NewGuid(),
                    ReceivingNoteId = note.Id,
                    OrderItemId = requestItem.OrderItemId,
                    ProductName = productName,
                    ProductType = !string.IsNullOrWhiteSpace(requestItem.ProductType)
                        ? requestItem.ProductType.Trim()
                        : declaredItem?.ProductType ?? string.Empty,
                    DeclaredQuantity = declaredQuantity,
                    ActualQuantity = requestItem.ActualQuantity,
                    QuantityDifference = requestItem.ActualQuantity - declaredQuantity,
                    DeclaredWeight = declaredWeight,
                    ActualWeight = requestItem.ActualWeight,
                    WeightDifference = requestItem.ActualWeight.HasValue && declaredWeight.HasValue
                        ? requestItem.ActualWeight.Value - declaredWeight.Value
                        : null,
                    Note = requestItem.Note?.Trim() ?? string.Empty
                };

                await itemRepo.AddAsync(line);
                newItems.Add(line);
            }

            note.ReceivedById = actorId;
            note.ReceivedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.WarehouseNote))
            {
                note.WarehouseNote = request.WarehouseNote.Trim();
            }

            noteRepo.Update(note);
            await _unitOfWork.SaveChangesAsync();

            // 3. Kiện cân đo ngay trên phiếu đi qua đúng luồng check-in. Lỗi ở đây làm hỏng cả lần
            // xác nhận (toàn bộ request nằm trong một transaction) — không còn cảnh biên bản đã lưu
            // mà kiện không có.
            await CreateParcelsFromReceiptAsync(note, request.Parcels, actorId);

            // 4. Chốt trạng thái phiếu sau kiểm đếm.
            var allLines = existingItems.Concat(newItems).ToList();
            var countedAllDeclared = orderItems.All(oi => allLines.Any(l => l.OrderItemId == oi.Id));
            var needsReview = RequiresReview(allLines);

            var before = note.Status;
            var awaitsAcceptance = false;
            if (needsReview)
            {
                note.Status = countedAllDeclared ? StatusReceived : StatusPartiallyReceived;
            }
            else if (countedAllDeclared)
            {
                note.Status = StatusApproved;
                note.ApprovedAt = DateTime.UtcNow;
                note.ApprovalNote = AppendNote(note.ApprovalNote, HasDiscrepancy(allLines)
                    ? "Số lượng khớp phiếu đã duyệt nên xếp kệ được; lệch cân chờ quản lý kho quyết định."
                    : "Kiểm đếm khớp phiếu đã duyệt, không cần duyệt lại.");
            }
            else
            {
                note.Status = StatusPartiallyReceived;
            }

            noteRepo.Update(note);
            awaitsAcceptance = AwaitsDiscrepancyAcceptance(note, allLines);

            await OrderHistoryWriter.RecordAsync(_unitOfWork, note.ConsignmentOrderId, null, null,
                "RECEIVING_NOTE_COUNTED",
                $"Phiếu {note.ReceivingNoteCode}: {StatusNames.Quote(before)} → {StatusNames.Quote(note.Status)}"
                + (needsReview ? ", có chênh lệch cần quản lý kho xem."
                    : awaitsAcceptance ? ", lệch cân — chờ quản lý kho quyết định (không chặn xếp kệ)." : "."),
                actorId);

            await _unitOfWork.SaveChangesAsync();

            if (needsReview || awaitsAcceptance)
            {
                await NotifyApproversAsync(note);
            }

            return await BuildDetailAsync(note);
        }

        /// <summary>
        /// Dựng kiện thật từ số kho vừa cân đo, đi qua đúng luồng check-in sẵn có.
        /// </summary>
        /// <remarks>
        /// Lỗi tạo kiện không được làm hỏng kết quả kiểm đếm vừa lưu — kho đã cân đếm xong rồi,
        /// bắt làm lại từ đầu vì một kiện gõ sai số là mất công vô ích. Ghi log và để kho thêm
        /// lại kiện đó sau.
        /// </remarks>
        private async Task CreateParcelsFromReceiptAsync(
            WarehouseReceivingNote note, List<ConfirmReceiptParcelDto>? parcels, Guid actorId)
        {
            if (parcels == null || parcels.Count == 0) return;

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(note.ConsignmentOrderId);
            var shippingCode = order?.ConsignmentCode;
            if (string.IsNullOrWhiteSpace(shippingCode))
            {
                throw new ArgumentException($"Phiếu {note.ReceivingNoteCode} không tra được mã đơn nên không tạo được kiện.");
            }

            var packages = parcels
                .Where(p => p.ActualWeight > 0m)
                .Select(p => new ParcelCheckInPackage
                {
                    OrderItemId = p.OrderItemId,
                    ActualWeight = p.ActualWeight,
                    Length = p.Length,
                    Width = p.Width,
                    Height = p.Height,
                })
                .ToList();

            if (packages.Count == 0) return;

            await _parcelService.CheckInParcelAsync(new ParcelCheckInRequest
            {
                ShippingCode = shippingCode,
                Packages = packages,
            }, actorId);
        }

        /// <summary>Nhãn tiếng Việt của trạng thái kiện, cho cả màn kho lẫn màn khách.</summary>
        public static string DescribeParcelStatus(string? status) => status?.Trim().ToUpperInvariant() switch
        {
            "RECEIVED" => "Đã nhận tại kho",
            "STORED" => "Đã xếp lên kệ",
            "IN_TRANSIT" => "Đang chuyển về VN",
            "ARRIVED_DESTINATION" => "Đã về kho VN",
            "RECEIVED_AT_DESTINATION" => "Kho VN đã kiểm đếm",
            "AWAITING_PICKUP" => "Chờ đơn vị giao lấy hàng",
            "OUT_FOR_DELIVERY" => "Đang giao",
            "DELIVERED" => "Đã giao",
            _ => status ?? string.Empty,
        };

        /// <summary>Nhãn tiếng Việt của trạng thái phiếu.</summary>
        public static string DescribeStatus(string? status) => status?.Trim().ToUpperInvariant() switch
        {
            StatusPendingApproval => "Chờ quản lý kho duyệt",
            StatusActive => "Chờ kho nhận hàng",
            StatusPartiallyReceived => "Nhận một phần",
            StatusReceived => "Chờ duyệt chênh lệch",
            StatusApproved => "Đã duyệt",
            StatusRejected => "Bị từ chối",
            _ => status ?? string.Empty,
        };

        public async Task<PaginatedResponse<WarehouseReceivingNoteListItemDto>> GetReceivingNotesAsync(
            string? status, Guid? warehouseId, string? search, int pageNumber, int pageSize,
            IReadOnlyCollection<Guid>? warehouseScope = null, string? orderType = null)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 200) pageSize = 200;

            var notes = (await _unitOfWork.Repository<WarehouseReceivingNote>().GetAllAsync()).ToList();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var wanted = status.Trim();
                notes = notes
                    .Where(n => string.Equals(n.Status, wanted, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (warehouseId.HasValue && warehouseId != Guid.Empty)
            {
                notes = notes.Where(n => n.WarehouseId == warehouseId.Value).ToList();
            }

            // Tài khoản đã được gán kho chỉ thấy phiếu của các kho được gán.
            if (warehouseScope != null)
            {
                notes = notes.Where(n => warehouseScope.Contains(n.WarehouseId)).ToList();
            }

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id);

            // Thông tin mua hộ (mã yêu cầu, đơn mua NCC, NCC, mã đơn NCC, vận đơn nội địa) nạp một lần
            // cho cả trang — không truy vấn từng phiếu.
            var purchaseByOrder = await LoadPurchaseInfoAsync(
                notes.Select(n => orders.TryGetValue(n.ConsignmentOrderId, out var o) ? o : null)
                     .Where(o => o != null)!);

            if (!string.IsNullOrWhiteSpace(orderType))
            {
                var wantPurchase = string.Equals(orderType.Trim(), "PURCHASE", StringComparison.OrdinalIgnoreCase);
                notes = notes
                    .Where(n => (purchaseByOrder.TryGetValue(n.ConsignmentOrderId, out var p) && p.IsPurchase) == wantPurchase)
                    .ToList();
            }
            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync()).ToDictionary(c => c.Id);
            var warehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync()).ToDictionary(w => w.Id);
            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id);

            var itemsByNote = (await _unitOfWork.Repository<WarehouseReceivingNoteItem>().GetAllAsync())
                .GroupBy(i => i.ReceivingNoteId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var orderItemsByOrder = (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                .GroupBy(i => i.OrderId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var orderItemCount = orderItemsByOrder.ToDictionary(kv => kv.Key, kv => kv.Value.Count);

            var parcelCount = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId.HasValue)
                .GroupBy(p => p.ConsignmentId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var rows = new List<WarehouseReceivingNoteListItemDto>();

            // Chuỗi để tìm theo mã (đã bỏ khoảng trắng, viết hoa) — kiện NCC giao thường chỉ in mã
            // đơn NCC hoặc mã vận đơn nội địa, không có mã WRN.
            var searchKeys = new Dictionary<Guid, List<string>>();

            foreach (var note in notes)
            {
                orders.TryGetValue(note.ConsignmentOrderId, out var order);

                Customer? customer = null;
                if (order?.CustomerId is Guid cid) customers.TryGetValue(cid, out customer);

                warehouses.TryGetValue(note.WarehouseId, out var warehouse);

                var lines = itemsByNote.TryGetValue(note.Id, out var found) ? found : new List<WarehouseReceivingNoteItem>();
                var stage = ResolveApprovalStage(note, lines);
                var purchase = purchaseByOrder.GetValueOrDefault(note.ConsignmentOrderId) ?? PurchaseInfo.Consignment;

                var keys = new List<string?>
                {
                    note.ReceivingNoteCode, order?.ConsignmentCode, purchase.PurchaseCode, purchase.PurchaseOrderCode,
                    purchase.SupplierOrderCode, purchase.DomesticTrackingCode,
                };
                if (orderItemsByOrder.TryGetValue(note.ConsignmentOrderId, out var declared))
                    keys.AddRange(declared.Select(i => i.DomesticTrackingCode));
                searchKeys[note.Id] = keys.Select(NormalizeCode).Where(k => k.Length > 0).Distinct().ToList();

                var row = new WarehouseReceivingNoteListItemDto
                {
                    Id = note.Id,
                    ReceivingNoteCode = note.ReceivingNoteCode ?? string.Empty,
                    ConsignmentOrderId = note.ConsignmentOrderId,
                    ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                    WarehouseId = note.WarehouseId,
                    WarehouseName = warehouse?.WarehouseName ?? string.Empty,
                    CustomerName = customer?.FullName ?? string.Empty,
                    CustomerPhone = customer?.Phone ?? string.Empty,
                    Status = note.Status ?? string.Empty,
                    StatusText = DescribeNoteStatus(note, lines),
                    CreatedAt = note.CreatedAt,
                    ApprovedByName = note.ApprovedBy.HasValue && users.TryGetValue(note.ApprovedBy.Value, out var approver)
                        ? approver.FullName ?? string.Empty
                        : string.Empty,
                    ApprovedAt = note.ApprovedAt,
                    RejectionReason = note.RejectionReason,
                    DeclaredItemCount = orderItemCount.TryGetValue(note.ConsignmentOrderId, out var dc) ? dc : 0,
                    CheckedItemCount = lines.Count,
                    HasDiscrepancy = HasDiscrepancy(lines),
                    ParcelCount = parcelCount.TryGetValue(note.ConsignmentOrderId, out var pc) ? pc : 0,
                    AwaitingApproval = stage != null,
                    ApprovalStage = stage,
                };
                purchase.ApplyTo(row);
                rows.Add(row);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var needle = search.Trim();
                var codeNeedle = NormalizeCode(needle);
                rows = rows.Where(r =>
                        r.CustomerName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || r.CustomerPhone.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || (r.SupplierName ?? string.Empty).Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || (codeNeedle.Length > 0
                            && searchKeys.TryGetValue(r.Id, out var ks)
                            && ks.Any(k => k.Contains(codeNeedle, StringComparison.Ordinal))))
                    .ToList();
            }

            // Phiếu chờ duyệt lên đầu — đó là việc cần làm; còn lại xếp theo mới nhất.
            var ordered = rows
                .OrderByDescending(r => r.AwaitingApproval)
                .ThenByDescending(r => r.CreatedAt)
                .ToList();

            return new PaginatedResponse<WarehouseReceivingNoteListItemDto>
            {
                Items = ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
                TotalCount = ordered.Count,
                PageNumber = pageNumber,
                PageSize = pageSize,
            };
        }

        public async Task<WarehouseReceivingNoteDetailDto> UpdateStatusAsync(
            Guid receivingNoteId, Guid userId, string userRole, UpdateReceivingNoteStatusDto request)
        {
            var target = request?.Status?.Trim().ToUpperInvariant();
            if (target != StatusApproved && target != StatusRejected)
            {
                throw new ArgumentException(
                    "Trạng thái không hợp lệ. Chỉ được duyệt hoặc từ chối.");
            }

            // Từ chối mà không nói lý do thì người lập/kho không biết phải sửa gì.
            if (target == StatusRejected && string.IsNullOrWhiteSpace(request?.RejectionReason))
            {
                throw new ArgumentException("Từ chối phiếu tiếp nhận thì bắt buộc ghi lý do.");
            }

            var noteRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
            var note = await noteRepo.GetByIdAsync(receivingNoteId)
                ?? throw new NotFoundException($"Không tìm thấy phiếu tiếp nhận kho với ID: {receivingNoteId}");

            var current = note.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            var items = (await _unitOfWork.Repository<WarehouseReceivingNoteItem>().GetAllAsync())
                .Where(i => i.ReceivingNoteId == note.Id)
                .ToList();

            var reason = string.IsNullOrWhiteSpace(request!.Reason) ? request.RejectionReason : request.Reason;
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(note.ConsignmentOrderId);
            var now = DateTime.UtcNow;

            if (current == StatusPendingApproval)
            {
                // Duyệt cho nhận hàng: đúng quản lý của kho, khác người lập phiếu.
                var warehouse = await WarehouseApprovalPolicy.EnsureCanDecideAsync(
                    _unitOfWork, note.WarehouseId, userId, userRole,
                    note.CreatedById, reason, "phiếu tiếp nhận");

                if (target == StatusApproved)
                {
                    // Kiểm lại cọc ngay lúc duyệt: đơn có thể đã bị huỷ hoặc khoản cọc bị đảo sau khi lập phiếu.
                    if (order == null || !await DepositQuery.HasConfirmedDepositAsync(_unitOfWork, order.Id))
                    {
                        throw new ArgumentException(
                            $"Đơn {order?.ConsignmentCode} chưa có khoản cọc được xác nhận nên chưa duyệt nhận hàng được.");
                    }

                    note.Status = StatusActive;
                    note.ReceiveApprovedById = userId;
                    note.ReceiveApprovedAt = now;
                    note.ApprovalNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
                }
                else
                {
                    note.Status = StatusRejected;
                    note.RejectionReason = request.RejectionReason!.Trim();
                    note.ApprovedBy = userId;
                    note.ApprovedAt = now;
                }

                noteRepo.Update(note);

                if (order != null)
                {
                    await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, order.Status, order.Status,
                        target == StatusApproved ? "RECEIVING_NOTE_APPROVED_TO_RECEIVE" : "RECEIVING_NOTE_REJECTED",
                        $"Phiếu {note.ReceivingNoteCode} tại kho {warehouse.WarehouseName}."
                        + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" {reason.Trim()}"),
                        userId);
                }

                await _unitOfWork.SaveChangesAsync();

                if (target == StatusApproved)
                {
                    var receiptUrl = await IssueReceiptAsync(note, userId);
                    await NotifyApprovedToReceiveAsync(note, warehouse, order, userId, receiptUrl);
                }
                else if (note.CreatedById.HasValue)
                {
                    await _notificationService.NotifyUserAsync(note.CreatedById.Value,
                        "Phiếu tiếp nhận kho bị từ chối",
                        $"Phiếu {note.ReceivingNoteCode} của đơn {order?.ConsignmentCode} bị từ chối: {note.RejectionReason}");
                }

                return await BuildDetailAsync(note);
            }

            if (current == StatusReceived || current == StatusPartiallyReceived)
            {
                // Xem chênh lệch sau kiểm đếm: người kiểm đếm không tự duyệt biên bản của mình.
                await WarehouseApprovalPolicy.EnsureCanDecideAsync(
                    _unitOfWork, note.WarehouseId, userId, userRole,
                    note.ReceivedById, reason, "biên bản kiểm đếm");

                note.Status = target;
                note.ApprovedBy = userId;
                note.ApprovedAt = now;
                note.RejectionReason = target == StatusRejected ? request.RejectionReason!.Trim() : null;
                if (target == StatusApproved && !string.IsNullOrWhiteSpace(reason))
                {
                    note.ApprovalNote = AppendNote(note.ApprovalNote, reason.Trim());
                }
                noteRepo.Update(note);

                if (order != null)
                {
                    await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, order.Status, order.Status,
                        target == StatusApproved ? "RECEIVING_COUNT_APPROVED" : "RECEIVING_COUNT_REJECTED",
                        $"Biên bản phiếu {note.ReceivingNoteCode}" + (RequiresReview(items) ? " có chênh lệch." : ".")
                        + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" {reason.Trim()}"),
                        userId);
                }

                await _unitOfWork.SaveChangesAsync();

                if (target == StatusApproved && HasDiscrepancy(items))
                {
                    await NotifyDiscrepancyAcceptedAsync(note, order, items, userId);
                }

                if (target == StatusRejected)
                {
                    await _notificationService.NotifyRolesAsync(
                        new[] { "WarehouseStaff", "Warehouse", "Staff", "OperationsManager", "Operations", "Admin" },
                        "Biên bản kiểm đếm bị từ chối",
                        $"Phiếu {note.ReceivingNoteCode} của đơn {order?.ConsignmentCode} bị từ chối: {note.RejectionReason}",
                        excludeUserId: userId);
                }

                return await BuildDetailAsync(note);
            }

            if (current == StatusApproved)
            {
                if (!AwaitsDiscrepancyAcceptance(note, items))
                {
                    // Đã có người quyết (hoặc biên bản khớp) — không cho quyết lần hai.
                    var decider = note.ApprovedBy.HasValue
                        ? await _unitOfWork.Repository<User>().GetByIdAsync(note.ApprovedBy.Value)
                        : null;
                    throw new ConflictException(decider != null
                        ? $"Phiếu {note.ReceivingNoteCode} đã được {decider.FullName} chốt lúc {note.ApprovedAt:dd/MM/yyyy HH:mm} (UTC), không quyết định lại."
                        : $"Phiếu {note.ReceivingNoteCode} đã chốt và biên bản khớp khai báo, không có gì cần quyết định.");
                }

                if (target == StatusRejected)
                {
                    throw new ArgumentException(
                        $"Phiếu {note.ReceivingNoteCode} đã tự chốt vì số lượng khớp, kiện có thể đã lên kệ nên không từ chối được. "
                        + "Chỉ chấp nhận số thực tế; hàng hư hỏng hay sai lệch cần xử lý thì mở sự cố cho kiện.");
                }

                // Người kiểm đếm không tự chấp nhận biên bản của mình; Admin quyết thay phải ghi lý do.
                await WarehouseApprovalPolicy.EnsureCanDecideAsync(
                    _unitOfWork, note.WarehouseId, userId, userRole,
                    note.ReceivedById, reason, "biên bản lệch cân");

                note.ApprovedBy = userId;
                note.ApprovedAt = now;
                note.ApprovalNote = AppendNote(note.ApprovalNote,
                    "Quản lý kho chấp nhận số thực tế" + (string.IsNullOrWhiteSpace(reason) ? "." : $": {reason.Trim()}"));
                noteRepo.Update(note);

                if (order != null)
                {
                    await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, order.Status, order.Status,
                        "RECEIVING_DISCREPANCY_ACCEPTED",
                        $"Biên bản phiếu {note.ReceivingNoteCode} lệch khai báo — chấp nhận số thực tế. {DescribeDifferences(items)}"
                        + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Ghi chú: {reason.Trim()}"),
                        userId);
                }

                await _unitOfWork.SaveChangesAsync();
                await NotifyDiscrepancyAcceptedAsync(note, order, items, userId);

                return await BuildDetailAsync(note);
            }

            throw new ArgumentException(
                $"Phiếu {note.ReceivingNoteCode} không ở bước chờ duyệt (hiện tại: {StatusNames.Quote(note.Status)}).");
        }

        /// <summary>Tóm tắt các dòng lệch: "Áo: SL 2→3; cân 2→3 kg". Tối đa 5 dòng cho vừa thông báo.</summary>
        private static string DescribeDifferences(IEnumerable<WarehouseReceivingNoteItem> items)
        {
            var parts = items
                .Where(i => i.QuantityDifference != 0 || (i.WeightDifference.HasValue && i.WeightDifference.Value != 0))
                .Select(i =>
                {
                    var bits = new List<string>();
                    if (i.QuantityDifference != 0)
                        bits.Add($"SL khai {i.DeclaredQuantity} → thực {i.ActualQuantity}");
                    if (i.WeightDifference.HasValue && i.WeightDifference.Value != 0)
                        bits.Add($"cân khai {i.DeclaredWeight:0.##} kg → thực {i.ActualWeight:0.##} kg");
                    return $"{i.ProductName}: {string.Join(", ", bits)}";
                })
                .ToList();

            if (parts.Count == 0) return string.Empty;
            var shown = string.Join("; ", parts.Take(5));
            return parts.Count > 5 ? $"{shown}; và {parts.Count - 5} dòng khác." : $"{shown}.";
        }

        /// <summary>
        /// Chấp nhận số thực tế của biên bản lệch: Sale lập phiếu và khách biết phí sẽ tính theo
        /// số kho đã cân đếm.
        /// </summary>
        private async Task NotifyDiscrepancyAcceptedAsync(
            WarehouseReceivingNote note, Order? order, IEnumerable<WarehouseReceivingNoteItem> items, Guid actorId)
        {
            var differences = DescribeDifferences(items);

            if (note.CreatedById.HasValue && note.CreatedById.Value != actorId)
            {
                await _notificationService.NotifyUserAsync(note.CreatedById.Value,
                    "Biên bản lệch đã được chấp nhận",
                    $"Phiếu {note.ReceivingNoteCode} của đơn {order?.ConsignmentCode}: quản lý kho chấp nhận số thực tế. {differences}");
            }

            if (order?.CustomerId is Guid customerId)
            {
                var customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(customerId);
                if (customer?.UserId is Guid customerUserId)
                {
                    await _notificationService.NotifyUserAsync(customerUserId,
                        "Kho đã chốt số thực nhận",
                        $"Đơn {order.ConsignmentCode}: kho đã cân đếm và ghi nhận khác so với khai báo. {differences} "
                        + "Phí sẽ tính theo số thực tế kho đã chốt.");
                }
            }
        }

        private static string AppendNote(string? existing, string addition)
        {
            var merged = string.IsNullOrWhiteSpace(existing) ? addition : $"{existing} · {addition}";
            // Cột approval_note dài tối đa 500 ký tự.
            return merged.Length <= 500 ? merged : merged[..500];
        }

        /// <summary>Báo quản lý kho (hoặc OM khi kho chưa có quản lý) có phiếu chờ duyệt cho nhận hàng.</summary>
        private async Task NotifyReceiveApproversAsync(WarehouseReceivingNote note, Warehouse? warehouse, Order? order, Guid? excludeUserId)
        {
            var title = "Phiếu tiếp nhận chờ duyệt nhận hàng";
            var content = $"Đơn {order?.ConsignmentCode} đã có cọc. Phiếu {note.ReceivingNoteCode} tại kho "
                          + $"{warehouse?.WarehouseName} chờ quản lý kho duyệt cho nhận hàng.";

            if (warehouse?.ManagerId is Guid managerId && managerId != excludeUserId)
            {
                await _notificationService.NotifyUserAsync(managerId, title, content);
                return;
            }

            await _notificationService.NotifyRolesAsync(
                new[] { "OperationsManager", "Operations", "Manager", "Admin" }, title, content, excludeUserId);
        }

        /// <summary>Duyệt xong: kho chuẩn bị nhận, khách biết mang hàng tới kho nào theo mã phiếu nào.</summary>
        /// <summary>
        /// Phát hành phiếu PDF khách mang theo khi gửi hàng tới kho. Phiếu phục vụ động từ dữ liệu
        /// đơn nên lỗi sinh file lúc này không chặn việc duyệt — kho vẫn mở lại được bằng link đơn.
        /// </summary>
        private async Task<string?> IssueReceiptAsync(WarehouseReceivingNote note, Guid actorId)
        {
            try
            {
                return await _receiptService.GenerateAndStoreReceiptAsync(note.ConsignmentOrderId, note.CreatedById ?? actorId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không phát hành được phiếu PDF cho phiếu nhập kho {Code}.", note.ReceivingNoteCode);
                return null;
            }
        }

        private async Task NotifyApprovedToReceiveAsync(WarehouseReceivingNote note, Warehouse warehouse, Order? order, Guid actorId, string? receiptUrl)
        {
            await _notificationService.NotifyRolesAsync(
                new[] { "WarehouseStaff", "Warehouse", "Staff" },
                "Có đơn chờ nhận tại kho",
                $"Phiếu {note.ReceivingNoteCode} của đơn {order?.ConsignmentCode} đã được duyệt. Kho {warehouse.WarehouseName} chuẩn bị nhận hàng.",
                excludeUserId: actorId);

            if (order?.CustomerId is Guid customerId)
            {
                var customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(customerId);
                if (customer?.UserId is Guid customerUserId)
                {
                    await _notificationService.NotifyUserAsync(customerUserId,
                        "Kho đã sẵn sàng nhận hàng",
                        $"Đơn {order.ConsignmentCode}: vui lòng gửi hàng tới kho {warehouse.WarehouseName}"
                        + (string.IsNullOrWhiteSpace(warehouse.Address) ? string.Empty : $" ({warehouse.Address})")
                        + $", mang theo phiếu nhập kho {note.ReceivingNoteCode}."
                        + (string.IsNullOrWhiteSpace(receiptUrl) ? string.Empty : $" Tải phiếu: {receiptUrl}"));
                }
            }

            if (note.CreatedById.HasValue && note.CreatedById.Value != actorId)
            {
                await _notificationService.NotifyUserAsync(note.CreatedById.Value,
                    "Phiếu tiếp nhận đã được duyệt",
                    $"Phiếu {note.ReceivingNoteCode} của đơn {order?.ConsignmentCode} đã được duyệt cho nhận hàng.");
            }
        }

        /// <summary>
        /// Báo cho người duyệt khi kho vừa chốt số thực tế, kèm cảnh báo nếu có dòng lệch khai báo.
        /// </summary>
        private async Task NotifyApproversAsync(WarehouseReceivingNote note)
        {
            var lines = (await _unitOfWork.Repository<WarehouseReceivingNoteItem>().GetAllAsync())
                .Where(i => i.ReceivingNoteId == note.Id)
                .ToList();
            var hasDiscrepancy = HasDiscrepancy(lines);
            var acceptOnly = AwaitsDiscrepancyAcceptance(note, lines);

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(note.ConsignmentOrderId);

            await _notificationService.NotifyRolesAsync(
                new[] { "OperationsManager", "Operations", "Admin", "Manager" },
                acceptOnly ? "Phiếu tiếp nhận lệch cân chờ quyết định" : "Phiếu tiếp nhận kho chờ duyệt",
                $"Kho đã kiểm đếm xong phiếu {note.ReceivingNoteCode} của đơn {order?.ConsignmentCode}."
                + (acceptOnly
                    ? $" Số lượng khớp nên kiện đã được xếp kệ, nhưng cân lệch khai báo ({DescribeDifferences(lines)}) — vào mục Cần quyết định để chấp nhận số thực tế."
                    : hasDiscrepancy
                        ? " Có dòng hàng lệch so với khai báo — xem biên bản trước khi duyệt."
                        : " Số thực tế khớp khai báo."));
        }

        /// <summary>
        /// Hàng khách khai báo trên đơn, dùng chung cho cả phiếu lẫn màn quét của kho.
        /// </summary>
        /// <remarks>
        /// Cột ORDER_ITEMS.PRODUCT_TYPE lưu ID của loại hàng chứ không lưu tên, nên đổ thẳng ra là
        /// phiếu hiện một chuỗi GUID. Tra sang bảng PRODUCT_TYPES lấy tên; ID nào không tra được
        /// thì giữ nguyên giá trị gốc còn hơn để trống.
        /// </remarks>
        private async Task<List<WarehouseReceivingNoteExpectedItemDto>> BuildExpectedItemsAsync(Guid orderId)
        {
            var productTypeNames = (await _unitOfWork.Repository<ProductType>().GetAllAsync())
                .ToDictionary(t => t.Id.ToString(), t => t.Name ?? string.Empty);

            var orderItems = (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                .Where(i => i.OrderId == orderId)
                .ToList();
            var configs = await ItemServiceRules.LoadPackageConfigurationsAsync(_unitOfWork);
            var servicesByItem = await ItemServiceRules.LoadDtosAsync(_unitOfWork, orderItems.Select(i => i.Id));

            return orderItems
                .Select(i =>
                {
                    var rawType = i.ProductType ?? string.Empty;
                    var typeName = productTypeNames.TryGetValue(rawType, out var name) && !string.IsNullOrWhiteSpace(name)
                        ? name
                        : rawType;

                    return new WarehouseReceivingNoteExpectedItemDto
                    {
                        OrderItemId = i.Id,
                        ProductName = i.ProductName ?? string.Empty,
                        ProductType = typeName,
                        Quantity = i.Quantity ?? 0,
                        Weight = i.Weight,
                        DeclaredValue = i.DeclaredValue,
                        PackageConfiguration = ItemServiceRules.MapPackageConfiguration(i.PackageConfigurationId, configs),
                        Services = ItemServiceRules.For(servicesByItem, i.Id)
                    };
                })
                .OrderBy(i => i.ProductName)
                .ToList();
        }

        /// <summary>
        /// Ghép phần đầu phiếu với các dòng đối chiếu và thông tin khách hàng.
        /// </summary>
        private async Task<WarehouseReceivingNoteDetailDto> BuildDetailAsync(WarehouseReceivingNote note)
        {
            var warehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(note.WarehouseId);
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(note.ConsignmentOrderId);

            Customer? customer = null;
            if (order?.CustomerId is Guid customerId)
            {
                customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(customerId);
            }

            var userRepo = _unitOfWork.Repository<User>();
            var approver = note.ApprovedBy.HasValue ? await userRepo.GetByIdAsync(note.ApprovedBy.Value) : null;
            var creator = note.CreatedById.HasValue ? await userRepo.GetByIdAsync(note.CreatedById.Value) : null;
            var receiveApprover = note.ReceiveApprovedById.HasValue ? await userRepo.GetByIdAsync(note.ReceiveApprovedById.Value) : null;
            var receiver = note.ReceivedById.HasValue ? await userRepo.GetByIdAsync(note.ReceivedById.Value) : null;

            // Kiện thật của đơn: kho cần để biết đã nhận đủ chưa, khách cần để biết hàng mình
            // đang nằm ở ô nào.
            var bins = (await _unitOfWork.Repository<Bin>().GetAllAsync()).ToDictionary(b => b.Id);
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId == note.ConsignmentOrderId)
                .OrderBy(p => p.CheckInTime ?? DateTime.MaxValue)
                .Select(p => new ReceivingNoteParcelDto
                {
                    ParcelId = p.Id,
                    PackageCode = p.PackageCode ?? string.Empty,
                    PackageStatus = p.PackageStatus ?? string.Empty,
                    PackageStatusText = DescribeParcelStatus(p.PackageStatus),
                    ActualWeight = p.ActualWeight,
                    Length = p.Length,
                    Width = p.Width,
                    Height = p.Height,
                    Volume = (p.Length ?? 0m) * (p.Width ?? 0m) * (p.Height ?? 0m) is var v && v > 0m ? v : null,
                    BinCode = p.BinId.HasValue && bins.TryGetValue(p.BinId.Value, out var bin)
                        ? bin.BinCode ?? string.Empty
                        : string.Empty,
                    CheckInTime = p.CheckInTime,
                })
                .ToList();

            var noteLines = (await _unitOfWork.Repository<WarehouseReceivingNoteItem>().GetAllAsync())
                .Where(i => i.ReceivingNoteId == note.Id)
                .ToList();

            var items = noteLines
                .Select(i => new WarehouseReceivingNoteItemDto
                {
                    Id = i.Id,
                    OrderItemId = i.OrderItemId,
                    ProductName = i.ProductName ?? string.Empty,
                    ProductType = i.ProductType ?? string.Empty,
                    DeclaredQuantity = i.DeclaredQuantity,
                    ActualQuantity = i.ActualQuantity,
                    QuantityDifference = i.QuantityDifference,
                    DeclaredWeight = i.DeclaredWeight,
                    ActualWeight = i.ActualWeight,
                    WeightDifference = i.WeightDifference,
                    Note = i.Note ?? string.Empty
                })
                .OrderBy(i => i.ProductName)
                .ToList();

            // Hàng khách khai trên đơn — phần ruột của phiếu lúc mới lập, khi chưa có dòng đối chiếu.
            // Với mua hộ đây chính là sản phẩm + số lượng đã đặt NCC.
            var expectedItems = await BuildExpectedItemsAsync(note.ConsignmentOrderId);

            var purchase = (order != null
                    ? (await LoadPurchaseInfoAsync(new[] { order })).GetValueOrDefault(order.Id)
                    : null)
                ?? PurchaseInfo.Consignment;

            var detail = new WarehouseReceivingNoteDetailDto
            {
                Id = note.Id,
                ReceivingNoteCode = note.ReceivingNoteCode ?? string.Empty,
                ConsignmentOrderId = note.ConsignmentOrderId,
                WarehouseId = note.WarehouseId,
                WarehouseName = warehouse?.WarehouseName ?? string.Empty,
                WarehouseNote = note.WarehouseNote ?? string.Empty,
                Status = note.Status ?? string.Empty,
                CreatedAt = note.CreatedAt,
                ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                CustomerName = customer?.FullName ?? string.Empty,
                CustomerCode = customer?.CustomerCode ?? string.Empty,
                CustomerPhone = customer?.Phone ?? string.Empty,
                Route = order?.Route ?? string.Empty,
                ExpectedItems = expectedItems,
                HasDiscrepancy = HasDiscrepancy(noteLines),
                StatusText = DescribeNoteStatus(note, noteLines),
                AwaitingApproval = ResolveApprovalStage(note, noteLines) != null,
                ApprovalStage = ResolveApprovalStage(note, noteLines),
                Items = items,
                ApprovedByName = approver?.FullName ?? string.Empty,
                ApprovedAt = note.ApprovedAt,
                RejectionReason = note.RejectionReason,
                Parcels = parcels,
                CanPutAway = AllowsPutAway(note, noteLines),
                CreatedByName = creator?.FullName ?? string.Empty,
                ReceiveApprovedByName = receiveApprover?.FullName ?? string.Empty,
                ReceiveApprovedAt = note.ReceiveApprovedAt,
                ApprovalNote = note.ApprovalNote,
                ReceivedByName = receiver?.FullName ?? string.Empty,
                ReceivedAt = note.ReceivedAt,
                RequiresReview = RequiresReview(noteLines),
                ReceiptPdfUrl = order?.ReceiptPdfUrl
            };
            purchase.ApplyTo(detail);
            return detail;
        }

        /// <summary>Bỏ khoảng trắng, viết hoa — để so mã quét/nhập tay với mã lưu trong DB.</summary>
        private static string NormalizeCode(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

        /// <summary>Thông tin mua hộ gắn với đơn kho của phiếu.</summary>
        private sealed class PurchaseInfo
        {
            public static readonly PurchaseInfo Consignment = new();

            public bool IsPurchase { get; init; }
            public string? PurchaseCode { get; init; }
            public string? PurchaseOrderCode { get; init; }
            public string? SupplierName { get; init; }
            public string? SupplierOrderCode { get; init; }
            public string? DomesticCarrier { get; init; }
            public string? DomesticTrackingCode { get; init; }

            public void ApplyTo(WarehouseReceivingNoteListItemDto d)
            {
                d.OrderType = IsPurchase ? "PURCHASE" : "CONSIGNMENT"; d.IsPurchase = IsPurchase;
                d.PurchaseCode = PurchaseCode; d.PurchaseOrderCode = PurchaseOrderCode;
                d.SupplierName = SupplierName; d.SupplierOrderCode = SupplierOrderCode;
                d.DomesticCarrier = DomesticCarrier; d.DomesticTrackingCode = DomesticTrackingCode;
            }

            public void ApplyTo(WarehouseReceivingNoteDetailDto d)
            {
                d.OrderType = IsPurchase ? "PURCHASE" : "CONSIGNMENT"; d.IsPurchase = IsPurchase;
                d.PurchaseCode = PurchaseCode; d.PurchaseOrderCode = PurchaseOrderCode;
                d.SupplierName = SupplierName; d.SupplierOrderCode = SupplierOrderCode;
                d.DomesticCarrier = DomesticCarrier; d.DomesticTrackingCode = DomesticTrackingCode;
            }

            public void ApplyTo(WarehouseReceivingNoteScanResponseDto d)
            {
                d.OrderType = IsPurchase ? "PURCHASE" : "CONSIGNMENT"; d.IsPurchase = IsPurchase;
                d.PurchaseCode = PurchaseCode; d.PurchaseOrderCode = PurchaseOrderCode;
                d.SupplierName = SupplierName; d.SupplierOrderCode = SupplierOrderCode;
                d.DomesticCarrier = DomesticCarrier; d.DomesticTrackingCode = DomesticTrackingCode;
            }
        }

        /// <summary>
        /// Nạp thông tin mua hộ cho một lô đơn kho: 2 truy vấn (đơn mua NCC + yêu cầu mua hộ) cho cả
        /// lô, không truy vấn từng phiếu. Đơn ký gửi không có mặt trong kết quả.
        /// </summary>
        private async Task<Dictionary<Guid, PurchaseInfo>> LoadPurchaseInfoAsync(IEnumerable<Order> orders)
        {
            var purchaseOrders = orders
                .Where(o => o != null
                            && (string.Equals(o.OrderType?.Trim(), "PURCHASE", StringComparison.OrdinalIgnoreCase)
                                || o.PurchaseOrderId.HasValue || o.PurchaseRequestId.HasValue))
                .GroupBy(o => o.Id)
                .Select(g => g.First())
                .ToList();
            var result = new Dictionary<Guid, PurchaseInfo>();
            if (purchaseOrders.Count == 0) return result;

            var poIds = purchaseOrders.Where(o => o.PurchaseOrderId.HasValue).Select(o => o.PurchaseOrderId!.Value).Distinct().ToList();
            var pos = poIds.Count == 0
                ? new Dictionary<Guid, PurchaseOrder>()
                : (await _unitOfWork.Repository<PurchaseOrder>().FindAsync(p => poIds.Contains(p.Id))).ToDictionary(p => p.Id);

            var requestIds = purchaseOrders
                .Select(o => o.PurchaseRequestId ?? (o.PurchaseOrderId.HasValue && pos.TryGetValue(o.PurchaseOrderId.Value, out var p) ? p.PurchaseRequestId : (Guid?)null))
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            var requests = requestIds.Count == 0
                ? new Dictionary<Guid, PurchaseRequest>()
                : (await _unitOfWork.Repository<PurchaseRequest>().FindAsync(r => requestIds.Contains(r.Id))).ToDictionary(r => r.Id);

            foreach (var o in purchaseOrders)
            {
                PurchaseOrder? po = o.PurchaseOrderId.HasValue && pos.TryGetValue(o.PurchaseOrderId.Value, out var found) ? found : null;
                var requestId = o.PurchaseRequestId ?? po?.PurchaseRequestId;
                PurchaseRequest? pr = requestId.HasValue && requests.TryGetValue(requestId.Value, out var r) ? r : null;

                result[o.Id] = new PurchaseInfo
                {
                    IsPurchase = true,
                    PurchaseCode = NullIfBlank(pr?.PurchaseCode),
                    PurchaseOrderCode = NullIfBlank(po?.PurchaseOrderCode),
                    SupplierName = NullIfBlank(po?.SupplierName),
                    SupplierOrderCode = NullIfBlank(po?.SupplierOrderCode),
                    DomesticCarrier = NullIfBlank(po?.DomesticCarrier),
                    DomesticTrackingCode = NullIfBlank(po?.DomesticTrackingCode),
                };
            }

            return result;
        }

        private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
