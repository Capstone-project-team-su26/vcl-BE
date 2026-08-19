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

        /// <summary>
        /// Tạo kiện khi kho chốt số. Dùng lại nguyên bản check-in thay vì tự sinh mã kiện và
        /// tự đẩy trạng thái đơn — hai đường tạo kiện mà khác luật là sớm muộn cũng lệch.
        /// </summary>
        private readonly IParcelService _parcelService;

        /*
         * Mốc đơn được phép mở phiếu tiếp nhận kho.
         *
         * APPROVED: đơn nhân viên tạo, hàng khách mang tới kho ngay, chưa qua báo giá.
         *
         * DEPOSIT_PAID: khách vừa trả cọc xong — đây là mốc "đủ điều kiện" của nghiệp vụ, và cũng
         * là mốc hàng đợi của Sale đẩy đơn ra (xem OrderService.BuildReceivingNoteRowsAsync).
         *
         * PAYMENT_CONFIRMED: Sale đã đối chiếu và xác nhận khoản tiền.
         *
         * Trước đây chỉ chấp nhận APPROVED nên mọi đơn đã qua báo giá đều tắc: đơn rời APPROVED
         * từ lúc gửi báo giá và không bao giờ quay lại, kho vì thế không nhận được hàng của đơn đó
         * nữa. Danh sách này là nguồn duy nhất — hàng đợi của Sale dùng lại chính nó để nút bày ra
         * và luật của API không bao giờ lệch nhau.
         */
        public static readonly string[] ReceivingNoteOpenableStatuses =
            { "APPROVED", "DEPOSIT_PAID", "PAYMENT_CONFIRMED" };

        /// <summary>Trạng thái phiếu còn hiệu lực — đã có một phiếu như vậy thì không mở phiếu mới.</summary>
        public static readonly string[] ActiveNoteStatuses =
            { "ACTIVE", "PENDING", "OPEN", "IN_EFFECT" };

        /*
         * Vòng đời phiếu tiếp nhận:
         *
         *   ACTIVE → PARTIALLY_RECEIVED → RECEIVED → APPROVED
         *                                         ↘ REJECTED
         *
         * Kho cân đếm xong đặt phiếu về RECEIVED; OM nhìn số chênh lệch rồi mới gật. Hàng chỉ
         * được xếp lên kệ sau khi phiếu APPROVED (chốt nằm ở ParcelService.PutAwayParcelAsync),
         * đúng như phía kho Việt Nam đang làm với phiếu nhập kho.
         */
        public const string StatusActive = "ACTIVE";
        public const string StatusPartiallyReceived = "PARTIALLY_RECEIVED";
        public const string StatusReceived = "RECEIVED";
        public const string StatusApproved = "APPROVED";
        public const string StatusRejected = "REJECTED";

        private static bool CanOpenReceivingNote(string? orderStatus) =>
            !string.IsNullOrWhiteSpace(orderStatus)
            && ReceivingNoteOpenableStatuses.Contains(orderStatus.Trim(), StringComparer.OrdinalIgnoreCase);

        public WarehouseReceivingNoteService(
            IUnitOfWork unitOfWork,
            INotificationService notificationService,
            ILogger<WarehouseReceivingNoteService> logger,
            IParcelService parcelService)
        {
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
            _logger = logger;
            _parcelService = parcelService;
        }

        public async Task<WarehouseReceivingNoteResponseDto> GetReceivingNoteByConsignmentIdAsync(Guid consignmentOrderId)
        {
            var wrnRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();

            var allNotes = await wrnRepo.GetAllAsync();
            var matchedNote = allNotes
                .Where(n => n.ConsignmentOrderId == consignmentOrderId)
                .Where(n => string.Equals(n.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(n.Status, "PENDING", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(n.Status, "OPEN", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(n.Status, "IN_EFFECT", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.CreatedAt)
                .FirstOrDefault();

            if (matchedNote == null)
                throw new NotFoundException("Chưa có phiếu tiếp nhận kho nào được kích hoạt cho yêu cầu ký gửi này.");

            // Trả bản đầy đủ: khách, tuyến, hàng khai báo. Trước đây chỉ trả 8 trường của bản thân
            // phiếu nên màn hình mở ra gần như trống, không đủ để kho cầm đi đối chiếu hàng.
            return await BuildDetailAsync(matchedNote);
        }

        public async Task<WarehouseReceivingNoteScanResponseDto> ScanReceivingNoteAsync(string code)
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

            return new WarehouseReceivingNoteScanResponseDto
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
                ExpectedItems = expectedItems
            };
        }

        public async Task<WarehouseReceivingNoteResponseDto> CreateReceivingNoteAsync(CreateWarehouseReceivingNoteRequestDto request)
        {
            // 1. Rule: Warehouse must exist, be active and be of type DESTINATION
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var warehouse = await warehouseRepo.GetByIdAsync(request.WarehouseId);
            if (warehouse == null)
                throw new NotFoundException($"Không tìm thấy kho với ID: {request.WarehouseId}");

            if (warehouse.IsActive == false)
                throw new ArgumentException("Kho tiếp nhận được chọn đang tạm ngưng hoạt động.");

            // Phiếu tiếp nhận áp dụng cho cả kho gốc (nhập kho quốc tế - M05)
            // và kho đích Việt Nam (nhận lô hàng về - M11).
            var receivingWarehouseTypes = new[] { "ORIGIN", "DESTINATION" };
            if (!receivingWarehouseTypes.Any(t => string.Equals(warehouse.WarehouseType, t, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Phiếu tiếp nhận kho chỉ được áp dụng cho kho gốc hoặc kho đích.");

            // 2. Rule: đơn phải tồn tại và đang ở một mốc cho phép lập phiếu tiếp nhận.
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(request.ConsignmentOrderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID: {request.ConsignmentOrderId}");

            if (!CanOpenReceivingNote(order.Status))
                throw new ArgumentException(
                    $"Không lập được phiếu tiếp nhận khi đơn đang ở trạng thái {order.Status}. "
                    + $"Chỉ lập được khi đơn ở: {string.Join(", ", ReceivingNoteOpenableStatuses)}.");

            var wrnRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
            var allNotes = await wrnRepo.GetAllAsync();

            // Rule: Only 1 active receiving note per consignment
            var hasActiveNote = allNotes.Any(n => 
                n.ConsignmentOrderId == request.ConsignmentOrderId &&
                (string.Equals(n.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(n.Status, "PENDING", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(n.Status, "OPEN", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(n.Status, "IN_EFFECT", StringComparison.OrdinalIgnoreCase))
            );

            if (hasActiveNote)
                throw new ConflictException("Yêu cầu ký gửi này đã có một phiếu tiếp nhận đang hoạt động.");

            var newNote = await AddNoteAsync(
                request.ConsignmentOrderId, request.WarehouseId, request.WarehouseNote, allNotes);
            await _unitOfWork.SaveChangesAsync();

            // Trả luôn bản đầy đủ để màn hình vừa lập xong đã hiện được nội dung phiếu.
            return await BuildDetailAsync(newNote);
        }

        /// <summary>
        /// Dựng bản ghi phiếu với mã <c>WRN-XXXXXXXX</c> không trùng. Chưa lưu — người gọi tự
        /// quyết thời điểm <c>SaveChangesAsync</c>.
        /// </summary>
        /// <remarks>
        /// Tách ra để đường lập tay và đường hệ thống tự sinh dùng chung một luật đặt mã; hai
        /// nơi tự sinh mã riêng là sớm muộn cũng lệch định dạng.
        /// </remarks>
        private async Task<WarehouseReceivingNote> AddNoteAsync(
            Guid orderId, Guid warehouseId, string? warehouseNote,
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
                Status = StatusActive,
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

        public async Task<WarehouseReceivingNoteDetailDto> ConfirmReceiptAsync(Guid receivingNoteId, ConfirmReceiptRequestDto request)
        {
            if (request == null || request.Items == null || request.Items.Count == 0)
            {
                throw new ArgumentException("Vui lòng nhập số liệu thực tế của ít nhất một dòng hàng.");
            }

            if (request.Items.Any(i => i.ActualQuantity < 0))
            {
                throw new ArgumentException("Số lượng thực tế không được nhỏ hơn 0.");
            }

            // 1. Phiếu tiếp nhận phải tồn tại và chưa được xác nhận trước đó
            var noteRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
            var note = await noteRepo.GetByIdAsync(receivingNoteId);
            if (note == null)
            {
                throw new NotFoundException($"Không tìm thấy phiếu tiếp nhận kho với ID: {receivingNoteId}");
            }

            if (string.Equals(note.Status, "RECEIVED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException("Phiếu tiếp nhận kho này đã được xác nhận nhập kho trước đó.");
            }

            // 2. Lấy các dòng hàng khách hàng khai báo để đối chiếu
            var orderItems = (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                .Where(i => i.OrderId == note.ConsignmentOrderId)
                .ToList();

            var orderItemById = orderItems.ToDictionary(i => i.Id);

            var itemRepo = _unitOfWork.Repository<WarehouseReceivingNoteItem>();

            foreach (var requestItem in request.Items)
            {
                OrderItem? declaredItem = null;
                if (requestItem.OrderItemId is Guid orderItemId)
                {
                    if (!orderItemById.TryGetValue(orderItemId, out declaredItem))
                    {
                        throw new ArgumentException("Dòng hàng khai báo được chọn không thuộc yêu cầu ký gửi của phiếu tiếp nhận này.");
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

                await itemRepo.AddAsync(new WarehouseReceivingNoteItem
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
                });
            }

            // 3. Chuyển phiếu sang trạng thái đã nhập kho
            note.Status = StatusReceived;
            if (!string.IsNullOrWhiteSpace(request.WarehouseNote))
            {
                note.WarehouseNote = request.WarehouseNote.Trim();
            }

            noteRepo.Update(note);
            await _unitOfWork.SaveChangesAsync();

            // Kho cân đo kiện ngay trên phiếu này luôn. Tạo kiện tại đây để sau khi OM duyệt là
            // hàng vào thẳng hàng đợi xếp kệ — không bắt kho mở thêm màn check-in riêng rồi gõ
            // lại số vừa cân xong.
            await CreateParcelsFromReceiptAsync(note, request.Parcels);

            await NotifyApproversAsync(note);

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
            WarehouseReceivingNote note, List<ConfirmReceiptParcelDto>? parcels)
        {
            if (parcels == null || parcels.Count == 0) return;

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(note.ConsignmentOrderId);
            var shippingCode = order?.ConsignmentCode;
            if (string.IsNullOrWhiteSpace(shippingCode))
            {
                _logger.LogWarning(
                    "Phiếu {Code} không tra được mã đơn nên chưa tạo được kiện.", note.ReceivingNoteCode);
                return;
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

            try
            {
                await _parcelService.CheckInParcelAsync(new ParcelCheckInRequest
                {
                    ShippingCode = shippingCode,
                    Packages = packages,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Không tạo được kiện cho phiếu {Code} (đơn {OrderCode}). Kết quả kiểm đếm vẫn được lưu.",
                    note.ReceivingNoteCode, shippingCode);
            }
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
            StatusActive => "Chờ kho nhận hàng",
            StatusPartiallyReceived => "Nhận một phần",
            StatusReceived => "Chờ duyệt",
            StatusApproved => "Đã duyệt",
            StatusRejected => "Bị từ chối",
            _ => status ?? string.Empty,
        };

        public async Task<PaginatedResponse<WarehouseReceivingNoteListItemDto>> GetReceivingNotesAsync(
            string? status, Guid? warehouseId, string? search, int pageNumber, int pageSize)
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

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id);
            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync()).ToDictionary(c => c.Id);
            var warehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync()).ToDictionary(w => w.Id);
            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id);

            var itemsByNote = (await _unitOfWork.Repository<WarehouseReceivingNoteItem>().GetAllAsync())
                .GroupBy(i => i.ReceivingNoteId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var orderItemCount = (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                .GroupBy(i => i.OrderId)
                .ToDictionary(g => g.Key, g => g.Count());

            var parcelCount = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId.HasValue)
                .GroupBy(p => p.ConsignmentId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var rows = new List<WarehouseReceivingNoteListItemDto>();

            foreach (var note in notes)
            {
                orders.TryGetValue(note.ConsignmentOrderId, out var order);

                Customer? customer = null;
                if (order?.CustomerId is Guid cid) customers.TryGetValue(cid, out customer);

                warehouses.TryGetValue(note.WarehouseId, out var warehouse);

                var lines = itemsByNote.TryGetValue(note.Id, out var found) ? found : new List<WarehouseReceivingNoteItem>();
                var noteStatus = note.Status?.Trim().ToUpperInvariant() ?? string.Empty;

                rows.Add(new WarehouseReceivingNoteListItemDto
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
                    StatusText = DescribeStatus(note.Status),
                    CreatedAt = note.CreatedAt,
                    ApprovedByName = note.ApprovedBy.HasValue && users.TryGetValue(note.ApprovedBy.Value, out var approver)
                        ? approver.FullName ?? string.Empty
                        : string.Empty,
                    ApprovedAt = note.ApprovedAt,
                    RejectionReason = note.RejectionReason,
                    DeclaredItemCount = orderItemCount.TryGetValue(note.ConsignmentOrderId, out var dc) ? dc : 0,
                    CheckedItemCount = lines.Count,
                    HasDiscrepancy = lines.Any(i => i.QuantityDifference != 0
                        || (i.WeightDifference.HasValue && i.WeightDifference.Value != 0)),
                    ParcelCount = parcelCount.TryGetValue(note.ConsignmentOrderId, out var pc) ? pc : 0,
                    AwaitingApproval = noteStatus == StatusReceived || noteStatus == StatusPartiallyReceived,
                });
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var needle = search.Trim();
                rows = rows.Where(r =>
                        r.ReceivingNoteCode.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || r.ConsignmentCode.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || r.CustomerName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || r.CustomerPhone.Contains(needle, StringComparison.OrdinalIgnoreCase))
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
            if (!StaffRoleHelper.CanApproveWarehouseStore(userRole))
            {
                throw new UnauthorizedAccessException(
                    "Chỉ Operations Manager hoặc Admin được duyệt phiếu tiếp nhận kho.");
            }

            var target = request?.Status?.Trim().ToUpperInvariant();
            if (target != StatusApproved && target != StatusRejected)
            {
                throw new ArgumentException(
                    $"Trạng thái không hợp lệ. Chỉ nhận {StatusApproved} hoặc {StatusRejected}.");
            }

            // Từ chối mà không nói lý do thì kho không biết phải sửa gì.
            if (target == StatusRejected && string.IsNullOrWhiteSpace(request?.RejectionReason))
            {
                throw new ArgumentException("Từ chối phiếu tiếp nhận thì bắt buộc ghi lý do.");
            }

            var noteRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
            var note = await noteRepo.GetByIdAsync(receivingNoteId)
                ?? throw new NotFoundException($"Không tìm thấy phiếu tiếp nhận kho với ID: {receivingNoteId}");

            var current = note.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (current == StatusApproved || current == StatusRejected)
            {
                throw new ArgumentException(
                    $"Phiếu này đã được xử lý trước đó (hiện tại: {note.Status}), không duyệt lại được.");
            }

            // Duyệt là duyệt cái BIÊN BẢN ĐỐI CHIẾU, nên phải có số thực tế rồi mới gật được.
            // Cho duyệt lúc phiếu còn ACTIVE thì OM ký vào một tờ giấy trắng.
            //
            // PARTIALLY_RECEIVED cũng duyệt được: hàng về làm nhiều đợt là chuyện thường, chặn ở
            // đây thì phần đã nhận nằm chờ mãi không lên kệ được mà cũng không ai duyệt được.
            var approvable = new[] { StatusReceived, StatusPartiallyReceived };
            if (!approvable.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Chỉ duyệt được sau khi kho xác nhận nhập kho (phiếu phải ở {StatusReceived} "
                    + $"hoặc {StatusPartiallyReceived}, hiện tại: {note.Status}).");
            }

            note.Status = target;
            note.ApprovedBy = userId;
            note.ApprovedAt = DateTime.UtcNow;
            note.RejectionReason = target == StatusRejected ? request!.RejectionReason!.Trim() : null;
            noteRepo.Update(note);
            await _unitOfWork.SaveChangesAsync();

            if (target == StatusRejected)
            {
                var order = await _unitOfWork.Repository<Order>().GetByIdAsync(note.ConsignmentOrderId);
                await _notificationService.NotifyRolesAsync(
                    new[] { "WarehouseStaff", "Warehouse", "Staff", "OperationsManager", "Operations", "Admin" },
                    "Phiếu tiếp nhận kho bị từ chối",
                    $"Phiếu {note.ReceivingNoteCode} của đơn {order?.ConsignmentCode} bị từ chối: {note.RejectionReason} "
                    + "Kiểm đếm lại rồi cập nhật số thực tế.",
                    excludeUserId: userId);
            }

            return await BuildDetailAsync(note);
        }

        /// <summary>
        /// Báo cho người duyệt khi kho vừa chốt số thực tế, kèm cảnh báo nếu có dòng lệch khai báo.
        /// </summary>
        private async Task NotifyApproversAsync(WarehouseReceivingNote note)
        {
            var hasDiscrepancy = (await _unitOfWork.Repository<WarehouseReceivingNoteItem>().GetAllAsync())
                .Where(i => i.ReceivingNoteId == note.Id)
                .Any(i => i.QuantityDifference != 0
                          || (i.WeightDifference.HasValue && i.WeightDifference.Value != 0));

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(note.ConsignmentOrderId);

            await _notificationService.NotifyRolesAsync(
                new[] { "OperationsManager", "Operations", "Admin", "Manager" },
                "Phiếu tiếp nhận kho chờ duyệt",
                $"Kho đã kiểm đếm xong phiếu {note.ReceivingNoteCode} của đơn {order?.ConsignmentCode}."
                + (hasDiscrepancy
                    ? " Có dòng hàng lệch so với khai báo — xem biên bản trước khi duyệt."
                    : " Số thực tế khớp khai báo."));
        }

        /// <summary>
        /// Sinh phiếu tiếp nhận ngay khi đơn ký gửi thu được tiền, thay vì đợi Sale bấm tay.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Gọi từ mọi nhánh xác nhận thanh toán (webhook PayOS/SePay lẫn xác nhận thủ công) nên
        /// phải chịu được gọi lại nhiều lần: đã có phiếu còn hiệu lực thì trả về luôn, không đẻ
        /// thêm. Webhook của cổng thanh toán bắn trùng là chuyện bình thường.
        /// </para>
        /// <para>
        /// Không bao giờ ném lỗi ra ngoài: đây là việc phụ chạy ké giao dịch thanh toán, hỏng thì
        /// ghi log để người ta lập tay, chứ làm vỡ luôn bước xác nhận tiền thì mất mát lớn hơn
        /// nhiều so với việc thiếu một tờ phiếu.
        /// </para>
        /// </remarks>
        public async Task<Guid?> EnsureReceivingNoteForPaidOrderAsync(Guid orderId)
        {
            try
            {
                var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId);
                if (order == null) return null;

                // Mua hộ đi đường riêng: hàng vào kho qua bước OM duyệt nhập kho của
                // PurchaseRequestService, không dùng phiếu tiếp nhận.
                if (string.Equals(order.OrderType, "PURCHASE", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var allNotes = (await _unitOfWork.Repository<WarehouseReceivingNote>().GetAllAsync()).ToList();
                var existing = allNotes.FirstOrDefault(n =>
                    n.ConsignmentOrderId == orderId
                    && !string.Equals(n.Status, StatusRejected, StringComparison.OrdinalIgnoreCase));
                if (existing != null) return existing.Id;

                // Hàng đã vào kho rồi thì phiếu tiếp nhận không còn nghĩa gì. Chốt này quan trọng
                // vì hàm cũng được gọi ở nhánh trả nốt đợt cuối — lúc đó hàng đang trên đường về
                // VN, đẻ ra một phiếu ACTIVE ở đây là chặn luôn bước xếp kệ của kho.
                var hasParcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                    .Any(p => p.ConsignmentId == orderId);
                if (hasParcels) return null;

                // Kho gốc suy từ báo giá mới nhất — cùng nguồn với chi tiết đơn và hàng đợi của
                // Sale, để phiếu tự sinh rơi đúng cái kho mà mọi màn hình khác đang hiển thị.
                var warehouseId = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                    .Where(q => q.OrderId == orderId && q.WarehouseId.HasValue)
                    .OrderByDescending(q => q.CreatedAt)
                    .Select(q => q.WarehouseId)
                    .FirstOrDefault();

                if (!warehouseId.HasValue)
                {
                    _logger.LogWarning(
                        "Đơn {OrderId} đã thu tiền nhưng báo giá không có kho gốc nên chưa sinh được phiếu tiếp nhận.",
                        orderId);
                    return null;
                }

                var note = await AddNoteAsync(orderId, warehouseId.Value, null, allNotes);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation(
                    "Đã tự sinh phiếu tiếp nhận {Code} cho đơn {OrderCode} sau khi thu tiền.",
                    note.ReceivingNoteCode, order.ConsignmentCode);

                await _notificationService.NotifyRolesAsync(
                    new[] { "WarehouseStaff", "Warehouse", "Staff", "OperationsManager", "Operations", "Admin" },
                    "Có đơn chờ nhận tại kho",
                    $"Đơn {order.ConsignmentCode} đã thanh toán. Phiếu tiếp nhận {note.ReceivingNoteCode} "
                    + "đã sẵn sàng, kho chuẩn bị nhận hàng.");

                return note.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Không sinh được phiếu tiếp nhận tự động cho đơn {OrderId}. Lập tay bằng POST /api/warehouse-receiving-notes.",
                    orderId);
                return null;
            }
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

            return (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                .Where(i => i.OrderId == orderId)
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
                        DeclaredValue = i.DeclaredValue
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

            var approver = note.ApprovedBy.HasValue
                ? await _unitOfWork.Repository<User>().GetByIdAsync(note.ApprovedBy.Value)
                : null;

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

            var items = (await _unitOfWork.Repository<WarehouseReceivingNoteItem>().GetAllAsync())
                .Where(i => i.ReceivingNoteId == note.Id)
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
            var expectedItems = await BuildExpectedItemsAsync(note.ConsignmentOrderId);

            return new WarehouseReceivingNoteDetailDto
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
                HasDiscrepancy = items.Any(i => i.QuantityDifference != 0
                    || (i.WeightDifference.HasValue && i.WeightDifference.Value != 0)),
                Items = items,
                ApprovedByName = approver?.FullName ?? string.Empty,
                ApprovedAt = note.ApprovedAt,
                RejectionReason = note.RejectionReason,
                Parcels = parcels,
                CanPutAway = string.Equals(note.Status, StatusApproved, StringComparison.OrdinalIgnoreCase)
            };
        }
    }
}
