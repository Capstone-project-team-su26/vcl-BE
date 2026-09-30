using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class ParcelService : IParcelService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfiguration _configuration;
        private readonly IConsignmentReceiptService _consignmentReceiptService;

        // Khách khai số theo ước lượng nên lệch là bình thường; chỉ cảnh báo khi chênh đủ lớn
        // để đáng đi hỏi lại, tránh báo động giả làm kho bỏ qua mọi cảnh báo.
        private const decimal WeightDeviationWarningThreshold = 0.5m;
        private const decimal VolumeDeviationWarningThreshold = 0.8m;

        // Kiện nhỏ thì chênh vài trăm gram hoặc vài cm đã vọt phần trăm, nên bỏ qua
        // khi bản thân mức chênh tuyệt đối còn nhỏ.
        private const decimal MinWeightDifferenceForWarningKg = 2m;
        private const decimal MinVolumeDifferenceForWarningCm3 = 20000m;

        /// <summary>Ngưỡng vật lý để chặn số nhập sai rõ ràng.</summary>
        private const decimal MaxParcelWeightKg = 1000m;
        private const decimal MaxParcelDimensionCm = 500m;


        public ParcelService(
            IUnitOfWork unitOfWork,
            IConfiguration configuration,
            IConsignmentReceiptService consignmentReceiptService)
        {
            _unitOfWork = unitOfWork;
            _configuration = configuration;
            _consignmentReceiptService = consignmentReceiptService;
        }

        /// <summary>
        /// Hệ số quy đổi thể tích — cùng nguồn với báo giá và tất toán VN (<see cref="VolumetricDivisorResolver"/>).
        /// </summary>
        private Task<decimal> GetVolumetricDivisorAsync() =>
            VolumetricDivisorResolver.ResolveAsync(_unitOfWork, _configuration);

        public async Task<ParcelCheckInResponse> CheckInParcelAsync(ParcelCheckInRequest request, Guid? actorUserId = null)
        {
            if (request == null)
                throw new ArgumentException("Yêu cầu check-in không được để trống.");

            // 1. Kiểm tra mã gửi hàng: VCL- là đơn kho (ký gửi và mua hộ sau khi duyệt),
            // PUR- là mã yêu cầu mua hộ mà nhân viên kho quen cầm.
            if (string.IsNullOrWhiteSpace(request.ShippingCode) ||
                (!request.ShippingCode.StartsWith("VCL-", StringComparison.OrdinalIgnoreCase)
                 && !request.ShippingCode.StartsWith("PUR-", StringComparison.OrdinalIgnoreCase)
                 && !request.ShippingCode.StartsWith("WRN-", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Mã gửi hàng không hợp lệ. Mã hợp lệ bắt đầu bằng VCL-, PUR- hoặc mã phiếu nhập kho WRN-.");
            }

            // Khách mang phiếu nhập kho tới: kho quét mã phiếu WRN-, hàng nhận theo đúng phiếu đó.
            WarehouseReceivingNote? scannedNote = null;
            if (request.ShippingCode.StartsWith("WRN-", StringComparison.OrdinalIgnoreCase))
            {
                scannedNote = (await _unitOfWork.Repository<WarehouseReceivingNote>().GetAllAsync())
                    .FirstOrDefault(n => string.Equals(n.ReceivingNoteCode, request.ShippingCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new NotFoundException($"Không tìm thấy phiếu nhập kho {request.ShippingCode}.");
            }

            // 2. Tìm đơn theo mã. Ký gửi dùng mã VCL-…; hàng mua hộ sau khi OM duyệt nhập kho
            // cũng có mã VCL-… riêng, nhưng kho quen cầm mã mua hộ PUR-… nên nhận luôn cả hai.
            var orderRepo = _unitOfWork.Repository<Order>();
            var allOrders = await orderRepo.GetAllAsync();
            var order = scannedNote != null
                ? allOrders.FirstOrDefault(o => o.Id == scannedNote.ConsignmentOrderId)
                : allOrders.FirstOrDefault(o =>
                    string.Equals(o.ConsignmentCode, request.ShippingCode, StringComparison.OrdinalIgnoreCase));

            if (order == null)
            {
                var purchaseRequest = (await _unitOfWork.Repository<PurchaseRequest>().GetAllAsync())
                    .FirstOrDefault(p => string.Equals(p.PurchaseCode, request.ShippingCode, StringComparison.OrdinalIgnoreCase));

                if (purchaseRequest != null)
                {
                    var requestOrders = allOrders.Where(o => o.PurchaseRequestId == purchaseRequest.Id).ToList();
                    if (requestOrders.Count > 1)
                        throw new ArgumentException(
                            $"Yêu cầu {purchaseRequest.PurchaseCode} có {requestOrders.Count} đơn mua NCC — vui lòng quét mã phiếu tiếp nhận WRN- hoặc mã đơn kho {purchaseRequest.PurchaseCode}-n.");
                    order = requestOrders.FirstOrDefault()
                        ?? throw new ArgumentException(
                            $"Yêu cầu mua hộ {purchaseRequest.PurchaseCode} chưa đặt NCC nên chưa có đơn kho để check-in.");
                }
            }

            if (order == null)
            {
                throw new NotFoundException("Không tìm thấy đơn hàng theo mã đã nhập.");
            }

            // 3. Điều kiện nhận hàng.
            // Ký gửi: khách đã có cọc được xác nhận VÀ phiếu tiếp nhận đã được quản lý kho duyệt cho
            // nhận hàng. Trước đây kho nhận cả đơn đang chờ cọc (WAITING_DEPOSIT, QUOTATION_CONFIRMED)
            // hoặc đơn mới được Sales tiếp nhận (APPROVED) mà không cần phiếu nào.
            // Mua hộ: đơn kho sinh ra sau bước OM duyệt nhập kho mua hộ (APPROVED), giữ như cũ.
            // Đơn kho mua hộ luồng mới (sinh khi Sale đặt NCC) nhận hàng y hệt ký gửi: có phiếu tiếp
            // nhận ACTIVE và khoản "cọc" 0đ đánh dấu đã trả trước ở luồng mua. Chỉ đơn mua hộ đời cũ
            // (sinh từ approve-store, không có phiếu) mới đi nhánh riêng.
            var isPurchaseOrder = string.Equals(order.OrderType, "PURCHASE", StringComparison.OrdinalIgnoreCase)
                && !order.PurchaseOrderId.HasValue;
            WarehouseReceivingNote? receivingNote = null;

            if (isPurchaseOrder)
            {
                var purchaseEligibleStatuses = new[] { "APPROVED", "CHECKED_IN" };
                if (!purchaseEligibleStatuses.Any(s => string.Equals(s, order.Status, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException($"Đơn mua hộ không ở trạng thái cho phép check-in (Hiện tại: {StatusNames.Quote(order.Status)}).");
                }
            }
            else
            {
                // Kiện nhận nhiều đợt: đơn có thể đã sang WAREHOUSE_RECEIVED sau khi xếp kệ kiện đầu,
                // nên điều kiện tiền đọc từ khoản cọc thật chứ không từ trạng thái đơn.
                var closedStatuses = new[] { "CANCELLED", "CANCELLED_FORFEITED", "REJECTED", "COMPLETED" };
                if (closedStatuses.Any(s => string.Equals(s, order.Status, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException($"Đơn {order.ConsignmentCode} đã đóng ({StatusNames.Quote(order.Status)}), không nhận hàng.");
                }

                if (!await DepositQuery.HasConfirmedDepositAsync(_unitOfWork, order.Id))
                {
                    throw new ArgumentException(
                        $"Đơn {order.ConsignmentCode} chưa có cọc được xác nhận nên kho chưa nhận hàng được (Hiện tại: {StatusNames.Quote(order.Status)}).");
                }

                receivingNote = scannedNote ?? (await _unitOfWork.Repository<WarehouseReceivingNote>().GetAllAsync())
                    .Where(n => n.ConsignmentOrderId == order.Id && WarehouseReceivingNoteService.IsApprovedToReceive(n))
                    .OrderByDescending(n => n.CreatedAt)
                    .FirstOrDefault();

                if (receivingNote == null || !WarehouseReceivingNoteService.IsApprovedToReceive(receivingNote))
                {
                    throw new ArgumentException(
                        receivingNote == null
                            ? $"Đơn {order.ConsignmentCode} chưa có phiếu nhập kho được quản lý kho duyệt."
                            : $"Phiếu {receivingNote.ReceivingNoteCode} chưa được quản lý kho duyệt (hiện tại: {StatusNames.Quote(receivingNote.Status)}).");
                }

                // Nhân viên kho đã được gán kho chỉ nhận hàng ở đúng kho của phiếu.
                await WarehouseAccessPolicy.EnsureAssignedWarehouseAsync(_unitOfWork, actorUserId, null, receivingNote.WarehouseId);

                if (!string.Equals(receivingNote.Status, WarehouseReceivingNoteService.StatusActive, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(receivingNote.Status, WarehouseReceivingNoteService.StatusPartiallyReceived, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Phiếu tiếp nhận {receivingNote.ReceivingNoteCode} đã chốt kiểm đếm (hiện tại: {StatusNames.Quote(receivingNote.Status)}), không nhận thêm kiện theo phiếu này.");
                }
            }

            // 4. Chuẩn hoá về danh sách kiện cần nhận. Request cũ (không có Packages) coi như nhận một kiện.
            var incomingPackages = request.Packages is { Count: > 0 }
                ? request.Packages
                : new List<ParcelCheckInPackage>
                {
                    new()
                    {
                        OrderItemId = request.OrderItemId,
                        ActualWeight = request.ActualWeight,
                        Length = request.Length,
                        Width = request.Width,
                        Height = request.Height
                    }
                };

            foreach (var package in incomingPackages)
            {
                ValidateMeasurements(package);
            }

            // 5. Mỗi dòng OrderItem là một kiện hàng riêng của đơn.
            var orderItems = (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                .Where(i => i.OrderId == order.Id)
                .OrderBy(i => i.Id)
                .ToList();

            if (orderItems.Count == 0)
            {
                throw new ArgumentException("Đơn ký gửi không có kiện hàng nào để nhận.");
            }

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();

            // Kiện đã nhận trước đó của đơn. Kiện gốc đã tách (SPLIT) không tính vì kiện con đã thay thế.
            var existingParcels = allParcels
                .Where(p => p.ConsignmentId == order.Id
                            && !string.Equals(p.PackageStatus, "SPLIT", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var receivedItemIds = existingParcels
                .Where(p => p.OrderItemId.HasValue)
                .Select(p => p.OrderItemId!.Value)
                .ToHashSet();

            var divisor = await GetVolumetricDivisorAsync();
            var usedPackageCodes = allParcels
                .Select(p => p.PackageCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var createdParcels = new List<(Parcel Parcel, OrderItem? Item, List<string> Warnings, decimal? WeightDifference)>();

            foreach (var package in incomingPackages)
            {
                var matchedItem = ResolveOrderItem(package.OrderItemId, orderItems, receivedItemIds, order.ConsignmentCode);

                if (matchedItem != null)
                {
                    receivedItemIds.Add(matchedItem.Id);
                }

                var packageCode = GeneratePackageCode(usedPackageCodes);

                decimal? volumetricWeight = null;
                if (package.Length is > 0 && package.Width is > 0 && package.Height is > 0)
                {
                    volumetricWeight = Math.Round(
                        package.Length.Value * package.Width.Value * package.Height.Value / divisor, 4);
                }

                var chargeableWeight = Math.Round(
                    volumetricWeight.HasValue
                        ? Math.Max(package.ActualWeight, volumetricWeight.Value)
                        : package.ActualWeight, 4);

                var parcel = new Parcel
                {
                    Id = Guid.NewGuid(),
                    ConsignmentId = order.Id,
                    OrderItemId = matchedItem?.Id,
                    PackageCode = packageCode,
                    ActualWeight = package.ActualWeight,
                    Length = package.Length,
                    Width = package.Width,
                    Height = package.Height,
                    VolumetricWeight = volumetricWeight,
                    ChargeableWeight = chargeableWeight,
                    PackageConfigurationId = matchedItem?.PackageConfigurationId,
                    PackageStatus = "RECEIVED",
                    CheckInTime = DateTime.UtcNow,
                    CheckedInById = actorUserId
                };

                await parcelRepo.AddAsync(parcel);

                var (warnings, weightDifference) = CompareWithDeclaration(package, matchedItem);
                createdParcels.Add((parcel, matchedItem, warnings, weightDifference));
            }

            // 6. Đơn chỉ chuyển CHECKED_IN khi mọi kiện khai báo đều đã được nhận.
            var declaredPackageCount = orderItems.Count;
            var receivedPackageCount = existingParcels.Count + createdParcels.Count;
            var isFullyReceived = receivedItemIds.Count >= declaredPackageCount;

            var priorStatuses = isPurchaseOrder
                ? new[] { "APPROVED" }
                : new[] { "DEPOSIT_PAID" };
            if (isFullyReceived && priorStatuses.Any(s => string.Equals(s, order.Status, StringComparison.OrdinalIgnoreCase)))
            {
                order.Status = "CHECKED_IN";
                orderRepo.Update(order);
            }

            // 6b. Cập nhật phiếu tiếp nhận theo tiến độ nhận kiện. Nhận đủ và biên bản không có
            // chênh lệch thì phiếu tự chốt APPROVED: phần khớp phiếu đã duyệt không duyệt lần hai.
            if (receivingNote != null)
            {
                var noteLines = (await _unitOfWork.Repository<WarehouseReceivingNoteItem>().GetAllAsync())
                    .Where(i => i.ReceivingNoteId == receivingNote.Id)
                    .ToList();

                receivingNote.ReceivedById ??= actorUserId;
                receivingNote.ReceivedAt ??= DateTime.UtcNow;

                if (isFullyReceived && !WarehouseReceivingNoteService.RequiresReview(noteLines))
                {
                    receivingNote.Status = WarehouseReceivingNoteService.StatusApproved;
                    receivingNote.ApprovedAt = DateTime.UtcNow;
                    receivingNote.ApprovalNote = string.IsNullOrWhiteSpace(receivingNote.ApprovalNote)
                        ? "Nhận đủ kiện khớp phiếu đã duyệt, không cần duyệt lại."
                        : receivingNote.ApprovalNote;
                }
                else
                {
                    receivingNote.Status = isFullyReceived
                        ? WarehouseReceivingNoteService.StatusReceived
                        : WarehouseReceivingNoteService.StatusPartiallyReceived;
                }

                _unitOfWork.Repository<WarehouseReceivingNote>().Update(receivingNote);
            }

            var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .FirstOrDefault(c => c.Id == order.CustomerId);

            await _unitOfWork.SaveChangesAsync();

            // Nhận đủ kiện thì sinh lại phiếu để có tên nhân viên kho và số thực nhận.
            // Lỗi sinh phiếu không được làm hỏng việc check-in.
            if (isFullyReceived)
            {
                try
                {
                    await _consignmentReceiptService.GenerateAndStoreReceiptAsync(order.Id, order.ReceiptGeneratedById);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"REGENERATE CONSIGNMENT RECEIPT ERROR ({order.Id}): {ex.Message}");
                }
            }

            // 7. Tiến độ từng kiện để app kho biết kiện nào đã nhận, kiện nào còn thiếu.
            var parcelByItemId = existingParcels
                .Concat(createdParcels.Select(c => c.Parcel))
                .Where(p => p.OrderItemId.HasValue)
                .GroupBy(p => p.OrderItemId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var packageConfigs = await ItemServiceRules.LoadPackageConfigurationsAsync(_unitOfWork);
            var servicesByItem = await ItemServiceRules.LoadDtosAsync(_unitOfWork, orderItems.Select(i => i.Id));

            var packageProgress = orderItems.Select(item =>
            {
                parcelByItemId.TryGetValue(item.Id, out var matchedParcel);

                return new ConsignmentPackageProgress
                {
                    PackageConfiguration = ItemServiceRules.MapPackageConfiguration(item.PackageConfigurationId, packageConfigs),
                    Services = ItemServiceRules.For(servicesByItem, item.Id),
                    OrderItemId = item.Id,
                    ProductName = item.ProductName ?? string.Empty,
                    Quantity = item.Quantity ?? 0,
                    DeclaredWeight = item.Weight,
                    IsReceived = matchedParcel != null,
                    PackageCode = matchedParcel?.PackageCode,
                    ActualWeight = matchedParcel?.ActualWeight
                };
            }).ToList();

            var declaredItems = orderItems.Select(item => new DeclaredItemDto
            {
                ProductName = item.ProductName ?? string.Empty,
                ProductType = item.ProductType ?? string.Empty,
                Quantity = item.Quantity ?? 0,
                Weight = item.Weight,
                Length = item.Length,
                Width = item.Width,
                Height = item.Height,
                DeclaredValue = item.DeclaredValue
            }).ToList();

            // Đơn kho sinh từ mua hộ thì trả kèm mã PUR- để kho khỏi phải đối chiếu hai mã.
            var purchaseCodeOfOrder = order.PurchaseRequestId.HasValue
                ? (await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(order.PurchaseRequestId.Value))?.PurchaseCode
                : null;

            ParcelCheckInResponse BuildResponse((Parcel Parcel, OrderItem? Item, List<string> Warnings, decimal? WeightDifference) created) =>
                new()
                {
                    ParcelId = created.Parcel.Id,
                    ShippingCode = request.ShippingCode,
                    ConsignmentId = order.Id,
                    OrderCode = order.ConsignmentCode ?? string.Empty,
                    // Đơn mua hộ không có "mã ký gửi" — để null thay vì lặp lại mã ở field sai nghĩa.
                    ConsignmentCode = purchaseCodeOfOrder == null ? order.ConsignmentCode : null,
                    OrderType = order.OrderType,
                    PurchaseCode = purchaseCodeOfOrder,
                    CustomerName = customer?.FullName ?? string.Empty,
                    CustomerCode = customer?.CustomerCode ?? string.Empty,
                    ReceiverName = order.ReceiverName,
                    ReceiverPhone = order.ReceiverPhone,
                    ReceiverAddress = order.ReceiverAddress,
                    PackageCode = created.Parcel.PackageCode ?? string.Empty,
                    OrderItemId = created.Item?.Id,
                    ProductName = created.Item?.ProductName ?? string.Empty,
                    DeclaredWeight = created.Item?.Weight,
                    ActualWeight = created.Parcel.ActualWeight ?? 0m,
                    Length = created.Parcel.Length,
                    Width = created.Parcel.Width,
                    Height = created.Parcel.Height,
                    VolumetricWeight = created.Parcel.VolumetricWeight,
                    ChargeableWeight = created.Parcel.ChargeableWeight,
                    PackageStatus = created.Parcel.PackageStatus ?? string.Empty,
                    WeightDifference = created.WeightDifference,
                    Warnings = created.Warnings,
                    DeclaredPackageCount = declaredPackageCount,
                    ReceivedPackageCount = receivedPackageCount,
                    RemainingPackageCount = Math.Max(0, declaredPackageCount - receivedItemIds.Count),
                    IsFullyReceived = isFullyReceived,
                    OrderStatus = order.Status ?? string.Empty,
                    DeclaredItems = declaredItems,
                    PackageProgress = packageProgress
                };

            var response = BuildResponse(createdParcels[0]);

            if (createdParcels.Count > 1)
            {
                response.Parcels = createdParcels.Select(BuildResponse).ToList();
                response.Warnings = createdParcels.SelectMany(c => c.Warnings).ToList();
            }

            return response;
        }

        /// <summary>
        /// Xác định kiện khai báo ứng với lần nhận này. Có OrderItemId thì dùng đúng kiện đó,
        /// không có thì lấy kiện chưa nhận đầu tiên để giữ tương thích với client cũ.
        /// </summary>
        private static OrderItem ResolveOrderItem(
            Guid? orderItemId,
            List<OrderItem> orderItems,
            HashSet<Guid> receivedItemIds,
            string? consignmentCode)
        {
            if (orderItemId.HasValue)
            {
                var item = orderItems.FirstOrDefault(i => i.Id == orderItemId.Value)
                    ?? throw new ArgumentException($"Kiện hàng không thuộc đơn ký gửi {consignmentCode}.");

                if (receivedItemIds.Contains(item.Id))
                {
                    throw new ArgumentException($"Kiện \"{item.ProductName}\" đã được check-in trước đó.");
                }

                return item;
            }

            // Hết dòng hàng chưa nhận nghĩa là kho đang nhận nhiều kiện hơn số khai báo.
            // Trước đây trả null rồi vẫn tạo kiện kèm cảnh báo — kiện đó mồ côi, không gắn
            // được vào đơn, không tra ra khách và không bao giờ vào được tồn kho.
            return orderItems.FirstOrDefault(i => !receivedItemIds.Contains(i.Id))
                ?? throw new ArgumentException(
                    $"Đơn {consignmentCode} đã nhận đủ {orderItems.Count} kiện theo khai báo, không thể nhận thêm. " +
                    "Nếu hàng thực tế nhiều kiện hơn, cần bổ sung dòng hàng vào đơn trước khi check-in.");
        }

        /// <summary>Chặn số đo phi lý trước khi ghi nhận, tránh sai số nhân lên khi tính cước.</summary>
        private static void ValidateMeasurements(ParcelCheckInPackage package)
        {
            if (package.ActualWeight <= 0)
            {
                throw new ArgumentException("Cân nặng thực tế phải lớn hơn 0.");
            }

            if (package.ActualWeight > MaxParcelWeightKg)
            {
                throw new ArgumentException($"Cân nặng thực tế {package.ActualWeight} kg vượt ngưỡng cho phép ({MaxParcelWeightKg} kg). Kiểm tra lại số đã nhập.");
            }

            foreach (var (name, value) in new[]
                     {
                         ("Chiều dài", package.Length),
                         ("Chiều rộng", package.Width),
                         ("Chiều cao", package.Height)
                     })
            {
                if (value is null)
                {
                    continue;
                }

                if (value <= 0)
                {
                    throw new ArgumentException($"{name} phải lớn hơn 0.");
                }

                if (value > MaxParcelDimensionCm)
                {
                    throw new ArgumentException($"{name} {value} cm vượt ngưỡng cho phép ({MaxParcelDimensionCm} cm). Kiểm tra lại số đã nhập.");
                }
            }
        }

        /// <summary>Đối chiếu số thực đo với khai báo của khách. Chỉ cảnh báo, không chặn.</summary>
        private static (List<string> Warnings, decimal? WeightDifference) CompareWithDeclaration(
            ParcelCheckInPackage package,
            OrderItem? matchedItem)
        {
            var warnings = new List<string>();
            decimal? weightDifference = null;

            if (matchedItem == null)
            {
                warnings.Add("Kiện này không khớp dòng hàng nào trong đơn, hệ thống không đối chiếu được với khai báo.");
                return (warnings, null);
            }

            var productName = matchedItem.ProductName ?? "không tên";

            if (matchedItem.Weight is > 0)
            {
                weightDifference = Math.Round(package.ActualWeight - matchedItem.Weight.Value, 4);

                var deviationRatio = Math.Abs(weightDifference.Value) / matchedItem.Weight.Value;
                if (deviationRatio > WeightDeviationWarningThreshold &&
                    Math.Abs(weightDifference.Value) >= MinWeightDifferenceForWarningKg)
                {
                    warnings.Add($"Kiện \"{productName}\": cân nặng thực {package.ActualWeight} kg lệch {Math.Round(deviationRatio * 100, 1)}% so với khai báo {matchedItem.Weight.Value} kg.");
                }
            }

            if (package.Length is > 0 && package.Width is > 0 && package.Height is > 0 &&
                matchedItem.Length is > 0 && matchedItem.Width is > 0 && matchedItem.Height is > 0)
            {
                var actualVolume = package.Length.Value * package.Width.Value * package.Height.Value;
                var declaredVolume = matchedItem.Length.Value * matchedItem.Width.Value * matchedItem.Height.Value;

                var volumeDifference = Math.Abs(actualVolume - declaredVolume);
                var volumeRatio = volumeDifference / declaredVolume;
                if (volumeRatio > VolumeDeviationWarningThreshold &&
                    volumeDifference >= MinVolumeDifferenceForWarningCm3)
                {
                    warnings.Add($"Kiện \"{productName}\": thể tích thực lệch {Math.Round(volumeRatio * 100, 1)}% so với khai báo. Kiểm tra lại số đo hoặc thùng đóng gói.");
                }
            }

            return (warnings, weightDifference);
        }

        /// <summary>Ảnh sản phẩm lưu thành một chuỗi ngăn cách bởi dấu phẩy trong DB.</summary>
        private static List<string> SplitReferenceUrls(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw
                .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(url => url.Trim())
                .Where(url => url.Length > 0)
                .ToList();
        }

        private static string GeneratePackageCode(HashSet<string> usedCodes)
        {
            var random = new Random();
            string packageCode;

            do
            {
                packageCode = $"PCL-{DateTime.UtcNow:yyyyMMddHHmmss}-{random.Next(100000, 999999)}";
            } while (!usedCodes.Add(packageCode));

            return packageCode;
        }

        public async Task<ParcelDetailResponse> GetParcelDetailAsync(Guid parcelId)
        {
            // 1. Tìm Parcel
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcel = await parcelRepo.GetByIdAsync(parcelId);
            if (parcel == null)
                throw new NotFoundException("Không tìm thấy kiện hàng.");

            // 2. Yêu cầu ký gửi + khách hàng liên quan
            Order? order = null;
            if (parcel.ConsignmentId.HasValue)
                order = await _unitOfWork.Repository<Order>().GetByIdAsync(parcel.ConsignmentId.Value);

            Customer? customer = null;
            if (order?.CustomerId != null)
                customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(order.CustomerId.Value);

            OrderItem? parcelOrderItem = null;
            if (parcel.OrderItemId.HasValue)
                parcelOrderItem = await _unitOfWork.Repository<OrderItem>().GetByIdAsync(parcel.OrderItemId.Value);

            // 3. Vị trí kho (chỉ khi đã put-away — BinId != null)
            ParcelStorageLocation? location = null;
            if (parcel.BinId.HasValue)
            {
                var bin = await _unitOfWork.Repository<Bin>().GetByIdAsync(parcel.BinId.Value);
                if (bin != null)
                {
                    Shelf? shelf = bin.ShelfId.HasValue
                        ? await _unitOfWork.Repository<Shelf>().GetByIdAsync(bin.ShelfId.Value) : null;
                    Warehouse? warehouse = bin.WarehouseId.HasValue
                        ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(bin.WarehouseId.Value) : null;
                    WarehouseZone? zone = shelf?.ZoneId != null
                        ? await _unitOfWork.Repository<WarehouseZone>().GetByIdAsync(shelf.ZoneId.Value) : null;

                    location = new ParcelStorageLocation
                    {
                        WarehouseName = warehouse?.WarehouseName ?? string.Empty,
                        ZoneName = zone?.ZoneName ?? string.Empty,
                        ZoneType = ZoneTypes.Normalize(zone?.ZoneType) ?? string.Empty,
                        ShelfCode = shelf?.ShelfCode ?? string.Empty,
                        BinCode = bin.BinCode ?? string.Empty
                    };
                }
            }

            // 3.5. Kiểm tra sự tồn tại của bản ghi tồn kho
            var allInventories = await _unitOfWork.Repository<Inventory>().GetAllAsync();
            bool hasInventory = allInventories.Any(i => i.ParcelId == parcel.Id);

            var packageConfigs = await ItemServiceRules.LoadPackageConfigurationsAsync(_unitOfWork);
            var services = parcelOrderItem != null
                ? await ItemServiceRules.LoadDtosAsync(_unitOfWork, new[] { parcelOrderItem.Id })
                : new Dictionary<Guid, List<ItemServiceDto>>();

            // 4. Map response
            return new ParcelDetailResponse
            {
                PackageConfiguration = ItemServiceRules.MapPackageConfiguration(
                    parcel.PackageConfigurationId ?? parcelOrderItem?.PackageConfigurationId, packageConfigs),
                Services = ItemServiceRules.For(services, parcel.OrderItemId),
                ParcelId = parcel.Id,
                PackageCode = parcel.PackageCode ?? string.Empty,
                PackageStatus = parcel.PackageStatus ?? string.Empty,
                ConsignmentId = parcel.ConsignmentId,
                ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                OrderItemId = parcel.OrderItemId,
                ProductName = parcelOrderItem?.ProductName ?? string.Empty,
                ProductType = parcelOrderItem?.ProductType ?? string.Empty,
                Quantity = parcelOrderItem?.Quantity ?? 0,
                DeclaredWeight = parcelOrderItem?.Weight,
                ReferenceUrls = SplitReferenceUrls(parcelOrderItem?.ReferenceUrl),
                CustomerName = customer?.FullName ?? string.Empty,
                CustomerCode = customer?.CustomerCode ?? string.Empty,
                CustomerEmail = customer?.Email,
                CustomerPhone = customer?.Phone,
                ReceiverName = order?.ReceiverName,
                ReceiverPhone = order?.ReceiverPhone,
                ReceiverAddress = order?.ReceiverAddress,
                ActualWeight = parcel.ActualWeight,
                Length = parcel.Length,
                Width = parcel.Width,
                Height = parcel.Height,
                VolumetricWeight = parcel.VolumetricWeight.HasValue ? Math.Round(parcel.VolumetricWeight.Value, 4) : null,
                ChargeableWeight = parcel.ChargeableWeight.HasValue ? Math.Round(parcel.ChargeableWeight.Value, 4) : null,
                CheckInTime = parcel.CheckInTime,
                PutAwayTime = parcel.PutAwayTime,
                StorageLocation = location,
                HasInventory = hasInventory
            };
        }

        public async Task<PaginatedResponse<PendingPutawayParcelDto>> GetPendingPutawayParcelsAsync(
            int pageNumber, 
            int pageSize, 
            string? search, 
            string? status,
            string? region = null,
            IReadOnlyCollection<Guid>? warehouseIds = null)
        {
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var orderRepo = _unitOfWork.Repository<Order>();
            var customerRepo = _unitOfWork.Repository<Customer>();
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var userRepo = _unitOfWork.Repository<User>();

            // Get all data into memory - suitable for GenericRepository pattern
            var allParcels = await parcelRepo.GetAllAsync();
            var allOrders = await orderRepo.GetAllAsync();
            var allCustomers = await customerRepo.GetAllAsync();
            var allWarehouses = await warehouseRepo.GetAllAsync();
            var allUsers = await userRepo.GetAllAsync();

            var query = allParcels.Where(p => p.BinId == null).AsQueryable();

            /*
             * "Chờ xếp kệ" = kiện đang nằm ở MỘT kho cụ thể, chưa có ô, và kho đó xếp được nó ngay.
             *
             * Ô kệ bị gỡ khi kiện rời kho (bốc hàng xuất, bàn giao lô, giao khách…), nên chỉ nhìn
             * BinId == null là sai: kiện đã xuất khỏi kho nguồn, bay về VN, kho VN nhận
             * (RECEIVED_AT_DESTINATION) vẫn không có ô và trước đây còn bị gán về KHO NGUỒN qua
             * phiếu tiếp nhận của đơn → hiện "Chờ xếp kệ" ở kho Trung Quốc dù hàng đã ở Việt Nam.
             *
             * Nay kho của kiện được xác định theo chặng hiện tại:
             *   - RECEIVED (check-in ở kho nguồn): kho của phiếu tiếp nhận — như cũ.
             *   - Kiện đã đi lô quốc tế / đã vào phiếu nhập kho VN: kho của phiếu nhập kho đã duyệt,
             *     không có thì kho đích của lô; không bao giờ quy về kho nguồn nữa.
             * Và RECEIVED_AT_DESTINATION chỉ tính là chờ xếp kệ khi phiếu nhập kho VN đã được duyệt
             * (đúng chốt của put-away — chưa duyệt thì kho có muốn xếp cũng bị chặn; kiện giao thẳng
             * thì không bao giờ lên kệ).
             */
            var inboundRequestsById = (await _unitOfWork.Repository<WarehouseInboundRequest>().GetAllAsync())
                .ToDictionary(r => r.Id);
            var inboundItems = (await _unitOfWork.Repository<WarehouseInboundItem>().GetAllAsync()).ToList();
            var parcelsInInbound = inboundItems.Select(i => i.ParcelId).ToHashSet();
            var approvedInboundWarehouse = new Dictionary<Guid, Guid?>();
            foreach (var item in inboundItems)
            {
                if (!inboundRequestsById.TryGetValue(item.InboundRequestId, out var inbound)
                    || !string.Equals(inbound.Status, "INBOUND_APPROVED", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!approvedInboundWarehouse.TryGetValue(item.ParcelId, out var existingWh) || existingWh == null)
                    approvedInboundWarehouse[item.ParcelId] = inbound.WarehouseId;
            }

            var shipmentsById = (await _unitOfWork.Repository<Shipment>().GetAllAsync()).ToDictionary(s => s.Id);
            var shipmentDestinationByParcel = (await _unitOfWork.Repository<ShipmentParcel>().GetAllAsync())
                .Where(sp => shipmentsById.ContainsKey(sp.ShipmentId))
                .Select(sp => (sp.ParcelId, Shipment: shipmentsById[sp.ShipmentId]))
                .GroupBy(x => x.ParcelId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.Shipment.ShippedAt ?? DateTime.MinValue)
                          .Select(x => x.Shipment.DestinationWarehouseId)
                          .FirstOrDefault(id => id.HasValue));

            // Kiện đã qua chặng kho nguồn (đi lô quốc tế hoặc vào phiếu nhập kho VN) mà không còn ở
            // trạng thái check-in kho nguồn.
            bool IsPastOrigin(Parcel p) =>
                !string.Equals(p.PackageStatus, "RECEIVED", StringComparison.OrdinalIgnoreCase)
                && (shipmentDestinationByParcel.ContainsKey(p.Id) || parcelsInInbound.Contains(p.Id));

            // Filter by status if provided; nếu không truyền, chỉ lấy kiện thật sự đang chờ put-away
            // (RECEIVED = check-in trực tiếp ở kho nguồn; RECEIVED_AT_DESTINATION = kho VN đã nhận
            // từ lô quốc tế VÀ phiếu nhập kho VN đã duyệt).
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => string.Equals(p.PackageStatus, status, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                query = query.Where(p => p.PackageStatus != null
                    && (string.Equals(p.PackageStatus, "RECEIVED", StringComparison.OrdinalIgnoreCase)
                        || (string.Equals(p.PackageStatus, "RECEIVED_AT_DESTINATION", StringComparison.OrdinalIgnoreCase)
                            && approvedInboundWarehouse.ContainsKey(p.Id))));
            }

            // Search by package code or consignment code
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p => 
                    (p.PackageCode != null && p.PackageCode.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (p.ConsignmentId.HasValue && 
                     allOrders.Any(o => o.Id == p.ConsignmentId.Value && 
                                       o.ConsignmentCode != null && 
                                       o.ConsignmentCode.Contains(search, StringComparison.OrdinalIgnoreCase))));
            }

            // Kèm dòng hàng của kiện để nhân viên xếp kệ phân biệt được các kiện cùng một đơn.
            var orderItemMap = (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                .ToDictionary(i => i.Id, i => i);

            var allWrns = await _unitOfWork.Repository<WarehouseReceivingNote>().GetAllAsync();

            // Map all matching parcels first to allow region filtering
            var allMappedItems = query.ToList().Select(p =>
            {
                var order = allOrders.FirstOrDefault(o => o.Id == p.ConsignmentId);
                var customer = order != null ? allCustomers.FirstOrDefault(c => c.Id == order.CustomerId) : null;
                Warehouse? warehouse;
                string? warehouseRegion;

                if (IsPastOrigin(p))
                {
                    // Kiện đã rời kho nguồn: thuộc kho nhận ở VN, KHÔNG quy về kho nguồn theo phiếu tiếp nhận.
                    approvedInboundWarehouse.TryGetValue(p.Id, out var inboundWarehouseId);
                    shipmentDestinationByParcel.TryGetValue(p.Id, out var destinationWarehouseId);
                    var currentWarehouseId = inboundWarehouseId ?? destinationWarehouseId;
                    warehouse = currentWarehouseId.HasValue
                        ? allWarehouses.FirstOrDefault(w => w.Id == currentWarehouseId.Value)
                        : null;
                    warehouseRegion = warehouse?.Region?.ToString() ?? "VN";
                }
                else
                {
                    var wrn = order != null ? allWrns.FirstOrDefault(w => w.ConsignmentOrderId == order.Id) : null;
                    warehouse = wrn != null ? allWarehouses.FirstOrDefault(w => w.Id == wrn.WarehouseId) : null;
                    warehouseRegion = warehouse?.Region?.ToString();
                }

                if (string.IsNullOrWhiteSpace(warehouseRegion) && order != null && !string.IsNullOrWhiteSpace(order.Route))
                {
                    var routeUpper = order.Route.Trim().ToUpperInvariant();
                    if (routeUpper.StartsWith("JP") || routeUpper.StartsWith("NB")) warehouseRegion = "JP";
                    else if (routeUpper.StartsWith("CN") || routeUpper.StartsWith("TQ")) warehouseRegion = "CN";
                    else if (routeUpper.StartsWith("KR")) warehouseRegion = "KR";
                    else if (routeUpper.StartsWith("VN")) warehouseRegion = "VN";
                }

                if (string.IsNullOrWhiteSpace(warehouseRegion) && p.CheckedInById.HasValue)
                {
                    var checkedInUser = allUsers.FirstOrDefault(u => u.Id == p.CheckedInById.Value);
                    warehouseRegion = checkedInUser?.Region?.ToString();
                }

                OrderItem? orderItem = null;
                if (p.OrderItemId.HasValue)
                {
                    orderItemMap.TryGetValue(p.OrderItemId.Value, out orderItem);
                }

                return new PendingPutawayParcelDto
                {
                    ReceiverName = order?.ReceiverName,
                    ReceiverPhone = order?.ReceiverPhone,
                    ReceiverAddress = order?.ReceiverAddress,
                    OrderItemId = p.OrderItemId,
                    ProductName = orderItem?.ProductName ?? string.Empty,
                    ProductType = orderItem?.ProductType ?? string.Empty,
                    Quantity = orderItem?.Quantity ?? 0,
                    ReferenceUrls = SplitReferenceUrls(orderItem?.ReferenceUrl),
                    Id = p.Id,
                    PackageCode = p.PackageCode ?? string.Empty,
                    ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                    CustomerName = customer?.FullName ?? string.Empty,
                    ActualWeight = p.ActualWeight,
                    Length = p.Length,
                    Width = p.Width,
                    Height = p.Height,
                    VolumetricWeight = p.VolumetricWeight.HasValue ? Math.Round(p.VolumetricWeight.Value, 4) : null,
                    ChargeableWeight = p.ChargeableWeight.HasValue ? Math.Round(p.ChargeableWeight.Value, 4) : null,
                    PackageStatus = p.PackageStatus ?? string.Empty,
                    CheckInTime = p.CheckInTime,
                    WarehouseId = warehouse?.Id,
                    WarehouseRegion = warehouseRegion
                };
            }).AsQueryable();

            // Nhân viên kho đã được gán kho: chỉ kiện của các kho đó. Kiện đời cũ không xác định được
            // kho (không có phiếu tiếp nhận) thì lọc theo vùng của các kho được gán.
            if (warehouseIds != null)
            {
                var assignedRegions = allWarehouses
                    .Where(w => warehouseIds.Contains(w.Id) && w.Region.HasValue)
                    .Select(w => w.Region!.Value.ToString())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                allMappedItems = allMappedItems.Where(d => d.WarehouseId.HasValue
                    ? warehouseIds.Contains(d.WarehouseId.Value)
                    : d.WarehouseRegion != null && assignedRegions.Contains(d.WarehouseRegion));
            }

            // Filter by Region if provided
            if (!string.IsNullOrWhiteSpace(region))
            {
                var regUpper = region.Trim().ToUpperInvariant();
                allMappedItems = allMappedItems.Where(d => !string.IsNullOrWhiteSpace(d.WarehouseRegion) && (
                    d.WarehouseRegion.Equals(regUpper, StringComparison.OrdinalIgnoreCase) ||
                    (regUpper == "JP" && d.WarehouseRegion.Equals("NB", StringComparison.OrdinalIgnoreCase)) ||
                    (regUpper == "NB" && d.WarehouseRegion.Equals("JP", StringComparison.OrdinalIgnoreCase)) ||
                    (regUpper == "CN" && d.WarehouseRegion.Equals("TQ", StringComparison.OrdinalIgnoreCase)) ||
                    (regUpper == "TQ" && d.WarehouseRegion.Equals("CN", StringComparison.OrdinalIgnoreCase))
                ));
            }

            var totalCount = allMappedItems.Count();

            // Pagination - order by CheckInTime descending
            var paginatedItems = allMappedItems
                .OrderByDescending(p => p.CheckInTime)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Thùng gỗ và dịch vụ của từng kiện để kho biết kiện nào cần xử lý gì trước khi xếp kệ.
            var pageConfigs = await ItemServiceRules.LoadPackageConfigurationsAsync(_unitOfWork);
            var pageServices = await ItemServiceRules.LoadDtosAsync(
                _unitOfWork, paginatedItems.Where(p => p.OrderItemId.HasValue).Select(p => p.OrderItemId!.Value));
            var pageIds = paginatedItems.Select(p => p.Id).ToHashSet();
            var pageParcels = (await parcelRepo.GetAllAsync())
                .Where(p => pageIds.Contains(p.Id))
                .ToDictionary(p => p.Id);
            foreach (var dto in paginatedItems)
            {
                pageParcels.TryGetValue(dto.Id, out var pageParcel);
                OrderItem? pageItem = null;
                if (dto.OrderItemId.HasValue) orderItemMap.TryGetValue(dto.OrderItemId.Value, out pageItem);
                dto.PackageConfiguration = ItemServiceRules.MapPackageConfiguration(
                    pageParcel?.PackageConfigurationId ?? pageItem?.PackageConfigurationId, pageConfigs);
                dto.Services = ItemServiceRules.For(pageServices, dto.OrderItemId);
            }

            return new PaginatedResponse<PendingPutawayParcelDto>
            {
                Items = paginatedItems,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        /// <summary>
        /// Kiện về từ lô quốc tế chỉ được xếp kệ khi nằm trong một phiếu nhập kho VN đã duyệt.
        /// Ném lỗi kèm đúng lý do đang vướng để kho biết phải làm gì tiếp.
        /// </summary>
        /// <summary>
        /// Kiện nhận tại kho gốc chỉ được xếp kệ sau khi OM duyệt phiếu tiếp nhận.
        /// </summary>
        /// <remarks>
        /// Cùng ý với <see cref="EnsureApprovedInboundRequestAsync"/> ở phía kho Việt Nam: kho cân
        /// đếm xong là xong phần kho, còn cho hàng vào sổ hay không là quyết định của OM. Không có
        /// chốt này thì biên bản đối chiếu chênh lệch thành tờ giấy không ai đọc — hàng cứ lên kệ
        /// rồi mới có người hỏi sao thiếu.
        /// </remarks>
        private async Task EnsureApprovedReceivingNoteAsync(Parcel parcel, Guid warehouseId)
        {
            if (!parcel.ConsignmentId.HasValue) return;

            var code = parcel.PackageCode ?? parcel.Id.ToString();

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(parcel.ConsignmentId.Value);

            // Hàng mua hộ vào kho qua bước OM duyệt nhập kho mua hộ, không có phiếu tiếp nhận ký gửi.
            if (string.Equals(order?.OrderType, "PURCHASE", StringComparison.OrdinalIgnoreCase)) return;

            var notes = (await _unitOfWork.Repository<WarehouseReceivingNote>().GetAllAsync())
                .Where(n => n.ConsignmentOrderId == parcel.ConsignmentId.Value)
                .OrderByDescending(n => n.CreatedAt)
                .ToList();

            if (notes.Count == 0)
            {
                throw new ArgumentException(
                    $"Kiện {code} không có phiếu tiếp nhận nào. Hàng ký gửi chỉ xếp kệ được theo phiếu đã được quản lý kho duyệt.");
            }

            var lines = (await _unitOfWork.Repository<WarehouseReceivingNoteItem>().GetAllAsync()).ToList();
            var allowed = notes.FirstOrDefault(n =>
                WarehouseReceivingNoteService.AllowsPutAway(n, lines.Where(l => l.ReceivingNoteId == n.Id)));

            if (allowed != null)
            {
                // Xếp kệ đúng kho đã duyệt nhận hàng, không xếp nhầm sang kho khác.
                if (allowed.WarehouseId != warehouseId)
                {
                    throw new ArgumentException(
                        $"Kiện {code} được duyệt nhận tại kho khác với kho đang xếp kệ. Kiểm tra lại kho của phiếu {allowed.ReceivingNoteCode}.");
                }

                return;
            }

            var latest = notes.First();

            if (string.Equals(latest.Status, WarehouseReceivingNoteService.StatusRejected, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Phiếu tiếp nhận {latest.ReceivingNoteCode} của kiện {code} đã bị từ chối"
                    + (string.IsNullOrWhiteSpace(latest.RejectionReason) ? "." : $": {latest.RejectionReason}"));
            }

            if (string.Equals(latest.Status, WarehouseReceivingNoteService.StatusPendingApproval, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Phiếu tiếp nhận {latest.ReceivingNoteCode} của kiện {code} đang chờ quản lý kho duyệt cho nhận hàng.");
            }

            if (WarehouseReceivingNoteService.RequiresReview(lines.Where(l => l.ReceivingNoteId == latest.Id)))
            {
                throw new ArgumentException(
                    $"Biên bản phiếu {latest.ReceivingNoteCode} có chênh lệch, đang chờ quản lý kho xem trước khi xếp kệ kiện {code}.");
            }

            throw new ArgumentException(
                $"Phiếu tiếp nhận {latest.ReceivingNoteCode} của kiện {code} chưa cho phép xếp kệ (hiện tại: {StatusNames.Quote(latest.Status)}).");
        }

        private async Task EnsureApprovedInboundRequestAsync(Parcel parcel)
        {
            var code = parcel.PackageCode ?? parcel.Id.ToString();

            var inboundItems = (await _unitOfWork.Repository<WarehouseInboundItem>().GetAllAsync())
                .Where(i => i.ParcelId == parcel.Id)
                .ToList();

            if (inboundItems.Count == 0)
            {
                throw new ArgumentException(
                    $"Kiện {code} chưa có phiếu nhập kho. Lập phiếu nhập kho VN và chờ Operations " +
                    "Manager duyệt rồi mới xếp lên kệ được.");
            }

            var requestIds = inboundItems.Select(i => i.InboundRequestId).ToHashSet();
            var requests = (await _unitOfWork.Repository<WarehouseInboundRequest>().GetAllAsync())
                .Where(r => requestIds.Contains(r.Id))
                .ToList();

            if (requests.Any(r => string.Equals(r.Status, "INBOUND_APPROVED", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var pending = requests.FirstOrDefault(r => string.Equals(r.Status, "INBOUND_PENDING", StringComparison.OrdinalIgnoreCase));
            if (pending != null)
            {
                throw new ArgumentException(
                    $"Phiếu nhập kho {pending.InboundCode} của kiện {code} đang chờ Operations Manager duyệt.");
            }

            var rejected = requests.FirstOrDefault();
            throw new ArgumentException(
                $"Phiếu nhập kho {rejected?.InboundCode} của kiện {code} đã bị từ chối" +
                (string.IsNullOrWhiteSpace(rejected?.RejectionReason) ? "." : $": {rejected.RejectionReason}"));
        }

        public async Task<ParcelPutAwayResponse> PutAwayParcelAsync(Guid parcelId, ParcelPutAwayRequest request)
        {
            if (request == null)
                throw new ArgumentException("Yêu cầu nhập vị trí kho không được để trống.");

            // 1. Tìm Parcel theo parcelId
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcel = await parcelRepo.GetByIdAsync(parcelId);
            if (parcel == null)
            {
                throw new NotFoundException("Không tìm thấy kiện hàng.");
            }

            // 2. Không cho nhập vị trí lại nếu Parcel đã STORED
            if (string.Equals(parcel.PackageStatus, "STORED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Kiện hàng đã được nhập vị trí kho (STORED).");
            }

            /*
             * BẮT BUỘC CÓ ẢNH KIỆN NẰM TRONG Ô KỆ.
             *
             * Sổ sách nói kiện nằm ở ô nào thì phải có ảnh chứng minh nó thật sự nằm ở đó —
             * cùng nguyên tắc với "đặt NCC phải có ảnh đơn" và "giao hàng phải có ảnh ký
             * nhận": không có nút nào cho nhân viên bấm cho nó sang bước sau.
             *
             * Ảnh gắn vào CHÍNH KIỆN (entityType = PARCEL) chứ không gắn vào đơn: một đơn
             * nhiều kiện, mỗi kiện nằm một ô khác nhau.
             */
            var putAwayPhotos = (await _unitOfWork.Repository<Attachment>().GetAllAsync())
                .Count(a => a.EntityId == parcelId
                            && string.Equals(a.EntityType, AttachmentEntityTypes.Parcel, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(a.DocumentType, AttachmentDocumentTypes.PutAwayProof, StringComparison.OrdinalIgnoreCase));

            if (putAwayPhotos == 0)
            {
                throw new ArgumentException(
                    "Phải chụp ảnh kiện trong ô kệ trước khi ghi nhận xếp kệ "
                    + $"(documentType = {AttachmentDocumentTypes.PutAwayProof}, "
                    + $"entityType = {AttachmentEntityTypes.Parcel}, entityId = {parcelId}).");
            }

            // 3. Chỉ cho nhập vị trí kho với Parcel đã được nhận tại kho:
            //    - RECEIVED: check-in trực tiếp tại kho/điểm tập kết
            //    - RECEIVED_AT_DESTINATION: nhận từ lô vận chuyển quốc tế tại kho đích (VCL-118)
            var putAwayEligibleStatuses = new[] { "RECEIVED", "RECEIVED_AT_DESTINATION" };
            if (!putAwayEligibleStatuses.Any(s => string.Equals(s, parcel.PackageStatus, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Chỉ cho phép nhập vị trí kho với kiện hàng đã được kho nhận (kho nguồn đã nhận hoặc kho Việt Nam đã kiểm đếm).");
            }

            // 3b. Kiện về từ lô quốc tế thì phải có phiếu nhập kho VN đã được OM duyệt mới
            // được xếp kệ. Không có chốt này thì kho cất hàng lên kệ lúc nào cũng được, phiếu
            // nhập kho thành thủ tục trang trí, và đồng hồ phí lưu kho VN bắt đầu chạy trước
            // khi ai đó đồng ý cho hàng nằm lại.
            //
            // Kiện check-in thẳng tại kho nguồn (RECEIVED) đi qua phiếu TIẾP NHẬN, không phải
            // phiếu nhập kho VN — hai loại phiếu khác nhau nhưng cùng một luật: OM gật rồi mới
            // lên kệ.
            if (string.Equals(parcel.PackageStatus, "RECEIVED_AT_DESTINATION", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureApprovedInboundRequestAsync(parcel);
            }
            else if (string.Equals(parcel.PackageStatus, "RECEIVED", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureApprovedReceivingNoteAsync(parcel, request.WarehouseId);
            }

            // 4. Ô kệ: quét mã (binCode) hoặc chọn theo ID; zoneId/shelfId cũ gửi kèm thì phải khớp.
            var bin = await BinPlacementPolicy.ResolveBinAsync(_unitOfWork, request.WarehouseId, request.BinId, request.BinCode);
            var placement = await BinPlacementPolicy.EnsureCanStoreAsync(_unitOfWork, bin, request.WarehouseId, new[] { parcel });
            var (warehouse, zone, shelf) = (placement.Warehouse, placement.Zone, placement.Shelf);

            if (request.ZoneId.HasValue && request.ZoneId.Value != Guid.Empty && request.ZoneId.Value != zone.Id)
                throw new ArgumentException($"Ô kệ {bin.BinCode} không thuộc khu vực kho đã chọn.");
            if (request.ShelfId.HasValue && request.ShelfId.Value != Guid.Empty && request.ShelfId.Value != shelf.Id)
                throw new ArgumentException($"Ô kệ {bin.BinCode} không thuộc kệ kho đã chọn.");

            // 5. Cập nhật vị trí và trạng thái của kiện
            var parcelVolume = BinPlacementPolicy.Volume(parcel);
            var putAwayTime = DateTime.UtcNow;
            parcel.BinId = bin.Id;
            parcel.PackageStatus = "STORED";
            parcel.PutAwayTime = putAwayTime;
            bin.CurrentVolume = placement.OccupiedVolumeAfter;

            parcelRepo.Update(parcel);
            _unitOfWork.Repository<Bin>().Update(bin);

            // 6. TỰ ĐỘNG GHI NHẬN TỒN KHO (INVENTORY) & CẬP NHẬT TRẠNG THÁI ORDER (TRONG CÙNG TRANSACTION)
            if (parcel.ConsignmentId.HasValue)
            {
                var orderRepo = _unitOfWork.Repository<Order>();
                var order = await orderRepo.GetByIdAsync(parcel.ConsignmentId.Value);
                if (order == null)
                {
                    throw new NotFoundException("Không tìm thấy thông tin yêu cầu ký gửi liên kết với kiện hàng.");
                }

                if (!order.CustomerId.HasValue)
                {
                    throw new ArgumentException("Yêu cầu ký gửi không liên kết với khách hàng nào để tạo tồn kho.");
                }

                var inventoryRepo = _unitOfWork.Repository<Inventory>();
                var allInventories = await inventoryRepo.GetAllAsync();

                // Tồn kho gắn với ĐÚNG ô kệ đang xếp vào, không phải "kiện này từng có tồn kho
                // ở đâu đó". Kiện đi kho nguồn → kho đích luôn để lại dòng cũ trỏ về ô kệ bên
                // kho nguồn (dòng RELEASED của phần đã xuất, và cả dòng AVAILABLE của phần chưa
                // xuất hết). Hỏi chung chung theo ParcelId thì gặp mấy dòng đó rồi kết luận
                // "có rồi", nên put-away ở kho đích lặng lẽ bỏ qua bước tạo tồn kho: hàng về VN
                // không vào sổ và cũng không lên được phiếu xuất kho.
                var existingInventory = allInventories.FirstOrDefault(i =>
                    i.ParcelId == parcel.Id
                    && i.BinId == bin.Id
                    && !string.Equals(i.Status, "RELEASED", StringComparison.OrdinalIgnoreCase));

                // Giữ tham chiếu để ghi nhật ký ngay bên dưới. Trước đây đoạn dưới tra lại
                // tồn kho bằng GetAllAsync, mà dòng vừa AddAsync chưa SaveChanges nên truy vấn
                // không thấy — kết quả là nhật ký NHẬP kho chưa từng được ghi lần nào, sổ vận
                // động chỉ có mỗi chiều xuất.
                var inventoryForParcel = existingInventory;

                if (existingInventory == null)
                {
                    // Tồn của MỘT kiện là lượng hàng trong kiện đó, không phải tổng cả đơn. Trước đây
                    // mỗi kiện đều nhận tổng lượng mọi dòng của đơn nên đơn nhiều kiện bị nhân tồn.
                    var (orderItemId, quantity) = await ParcelQuantityResolver.ResolveAsync(_unitOfWork, parcel, order.Id);

                    var newInventory = new Inventory
                    {
                        Id = Guid.NewGuid(),
                        CustomerId = order.CustomerId.Value,
                        OrderId = order.Id,
                        OrderItemId = orderItemId,
                        ParcelId = parcel.Id,
                        BinId = bin.Id,
                        Quantity = quantity,
                        Status = "AVAILABLE",
                        CreatedAt = DateTime.UtcNow
                    };

                    await inventoryRepo.AddAsync(newInventory);
                    inventoryForParcel = newInventory;
                }

                // Chuyển trạng thái đơn khi đã hoàn tất put-away và có inventory.
                // Kho đích phải dùng mã riêng: nếu vẫn đặt WAREHOUSE_RECEIVED thì đơn đã đi
                // hết chặng quốc tế bị kéo ngược về trạng thái của kho nguồn.
                var isDestinationWarehouse = string.Equals(
                    warehouse.WarehouseType?.Trim(), "DESTINATION", StringComparison.OrdinalIgnoreCase);
                order.Status = isDestinationWarehouse ? "AT_DESTINATION_WAREHOUSE" : "WAREHOUSE_RECEIVED";
                orderRepo.Update(order);

                // Nhật ký nhập kho. Mốc thời gian ở đây cũng là mốc bắt đầu tính phí lưu kho
                // tại kho đích, nên thiếu dòng này là phí lưu kho mất căn cứ.
                if (inventoryForParcel != null)
                {
                    await _unitOfWork.Repository<InventoryMovement>().AddAsync(new InventoryMovement
                    {
                        Id = Guid.NewGuid(),
                        InventoryId = inventoryForParcel.Id,
                        ParcelId = parcel.Id,
                        FromBinId = null,
                        ToBinId = bin.Id,
                        MovementType = "INBOUND",
                        Reason = $"Nhập kho kiện {parcel.PackageCode} vào ô {bin.BinCode}",
                        Quantity = inventoryForParcel.Quantity,
                        MovedById = parcel.CheckedInById,
                        MovedAt = putAwayTime
                    });
                }

                // Lịch sử xếp dỡ cho mô hình gợi ý vị trí. Bảng này trước đây chỉ có code đọc
                // nên AI luôn học từ dữ liệu giả; phải ghi ở đây thì gợi ý mới khá dần lên.
                var categoryItem = (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                    .FirstOrDefault(i => i.Id == parcel.OrderItemId || i.OrderId == order.Id);

                await _unitOfWork.Repository<PutAwayHistory>().AddAsync(new PutAwayHistory
                {
                    Id = Guid.NewGuid(),
                    WarehouseId = warehouse.Id,
                    ZoneId = zone.Id,
                    ShelfId = shelf.Id,
                    BinId = bin.Id,
                    ParcelWeight = parcel.ActualWeight ?? 0m,
                    Volume = parcelVolume,
                    Category = string.IsNullOrWhiteSpace(categoryItem?.ProductName) ? "General" : categoryItem.ProductName,
                    ChosenAt = putAwayTime
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // 7. Trả về thông tin kết quả
            return new ParcelPutAwayResponse
            {
                PackageCode = parcel.PackageCode ?? string.Empty,
                WarehouseName = warehouse.WarehouseName ?? string.Empty,
                ZoneName = zone.ZoneName ?? string.Empty,
                ZoneType = ZoneTypes.Normalize(zone.ZoneType) ?? string.Empty,
                ShelfCode = shelf.ShelfCode ?? string.Empty,
                BinCode = bin.BinCode ?? string.Empty,
                PackageStatus = parcel.PackageStatus,
                PutAwayTime = putAwayTime
            };
        }

        /// <summary>
        /// VCL-90: Customer tra cứu danh sách kiện hàng thuộc yêu cầu ký gửi của chính mình.
        /// </summary>
        public async Task<List<CustomerParcelDto>> GetCustomerParcelsAsync(Guid userId, string? consignmentCode, string? searchCode)
        {
            // 1. Tìm Customer tương ứng với tài khoản
            var allCustomers = await _unitOfWork.Repository<Customer>().GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
            if (customer == null)
            {
                // Không có hồ sơ khách hàng -> danh sách rỗng
                return new List<CustomerParcelDto>();
            }

            // 2. Các đơn ký gửi thuộc customer
            var allOrders = await _unitOfWork.Repository<Order>().GetAllAsync();
            var myOrders = allOrders.Where(o => o.CustomerId == customer.Id).ToList();
            var myOrderIds = myOrders.Select(o => o.Id).ToHashSet();

            // 3. Nếu lọc theo mã yêu cầu ký gửi -> kiểm tra quyền sở hữu
            if (!string.IsNullOrWhiteSpace(consignmentCode))
            {
                var targetOrder = allOrders.FirstOrDefault(o =>
                    o.ConsignmentCode != null &&
                    string.Equals(o.ConsignmentCode, consignmentCode.Trim(), StringComparison.OrdinalIgnoreCase));

                if (targetOrder != null && targetOrder.CustomerId != customer.Id)
                {
                    throw new UnauthorizedAccessException("Bạn không có quyền xem kiện hàng của yêu cầu ký gửi này.");
                }

                // Chỉ giữ đúng đơn được lọc (nếu không tìm thấy -> không có kiện nào)
                myOrderIds = targetOrder != null && targetOrder.CustomerId == customer.Id
                    ? new HashSet<Guid> { targetOrder.Id }
                    : new HashSet<Guid>();
            }

            if (myOrderIds.Count == 0)
            {
                return new List<CustomerParcelDto>();
            }

            // 4. Lấy các kiện đã check-in thuộc các đơn này
            var allParcels = await _unitOfWork.Repository<Parcel>().GetAllAsync();
            var parcels = allParcels
                .Where(p => p.ConsignmentId.HasValue && myOrderIds.Contains(p.ConsignmentId.Value))
                .Where(p => p.CheckInTime != null
                    || string.Equals(p.PackageStatus, "RECEIVED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.PackageStatus, "STORED", StringComparison.OrdinalIgnoreCase));

            // 5. Tìm kiếm theo mã kiện
            if (!string.IsNullOrWhiteSpace(searchCode))
            {
                var keyword = searchCode.Trim();
                parcels = parcels.Where(p =>
                    p.PackageCode != null && p.PackageCode.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            var parcelList = parcels.OrderByDescending(p => p.CheckInTime).ToList();

            // 6. Map kèm thông tin đơn + sản phẩm
            var allOrderItems = await _unitOfWork.Repository<OrderItem>().GetAllAsync();
            var orderMap = myOrders.ToDictionary(o => o.Id);

            var result = parcelList.Select(p =>
            {
                Order? order = p.ConsignmentId.HasValue && orderMap.ContainsKey(p.ConsignmentId.Value)
                    ? orderMap[p.ConsignmentId.Value]
                    : null;

                var items = order != null
                    ? allOrderItems
                        .Where(i => i.OrderId == order.Id)
                        .Select(i => new CustomerParcelItemDto
                        {
                            ProductName = i.ProductName ?? string.Empty,
                            Quantity = i.Quantity ?? 0,
                            DeclaredValue = i.DeclaredValue
                        })
                        .ToList()
                    : new List<CustomerParcelItemDto>();

                return new CustomerParcelDto
                {
                    ParcelId = p.Id,
                    PackageCode = p.PackageCode ?? string.Empty,
                    ConsignmentId = p.ConsignmentId,
                    ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                    PackageStatus = p.PackageStatus ?? string.Empty,
                    ActualWeight = p.ActualWeight,
                    Length = p.Length,
                    Width = p.Width,
                    Height = p.Height,
                    VolumetricWeight = p.VolumetricWeight,
                    ChargeableWeight = p.ChargeableWeight,
                    CheckInTime = p.CheckInTime,
                    PutAwayTime = p.PutAwayTime,
                    SaleNote = order?.Note,
                    Items = items
                };
            }).ToList();

            return result;
        }

        /// <summary>Trạng thái kiện hàng còn được phép gộp hoặc tách</summary>
        private static readonly string[] StructurableStatuses =
        {
            "RECEIVED", "RECEIVED_AT_DESTINATION", "STORED"
        };

        public async Task<ParcelStructureResponseDto> SplitParcelAsync(Guid parcelId, SplitParcelRequestDto request, Guid? userId)
        {
            if (request?.Children == null || request.Children.Count < 2)
            {
                throw new ArgumentException("Cần ít nhất 2 kiện con để tách kiện.");
            }

            if (request.Children.Any(c => c.ActualWeight <= 0))
            {
                throw new ArgumentException("Cân nặng thực tế của mỗi kiện con phải lớn hơn 0.");
            }

            if (request.Children.Any(c => c.Quantity < 0))
            {
                throw new ArgumentException("Số lượng hàng trong kiện con không được nhỏ hơn 0.");
            }

            await ExportLockGuard.EnsureNotInActiveReleaseAsync(_unitOfWork, new[] { parcelId }, "tách kiện");

            // 1. Kiện gốc phải tồn tại và đang ở trạng thái cho phép tách
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var source = await parcelRepo.GetByIdAsync(parcelId);
            if (source == null)
            {
                throw new NotFoundException($"Không tìm thấy kiện hàng với ID: {parcelId}");
            }

            if (!StructurableStatuses.Contains(source.PackageStatus, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Không thể tách kiện đang ở trạng thái {StatusNames.Quote(source.PackageStatus)}.");
            }

            // 2. Không tách kiện đang được giữ cho phiếu xuất kho (BR-91)
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();
            var sourceInventory = allInventories.FirstOrDefault(i => i.ParcelId == source.Id);

            if (sourceInventory != null
                && !string.Equals(sourceInventory.Status, "AVAILABLE", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Không thể tách kiện vì hàng tồn đang ở trạng thái {StatusNames.Quote(sourceInventory.Status)}.");
            }

            // 3. Tổng cân nặng các kiện con không được vượt cân nặng kiện gốc
            var totalChildWeight = request.Children.Sum(c => c.ActualWeight);
            if (source.ActualWeight.HasValue && totalChildWeight > source.ActualWeight.Value)
            {
                throw new ArgumentException($"Tổng cân nặng kiện con ({totalChildWeight}) vượt quá cân nặng kiện gốc ({source.ActualWeight.Value}).");
            }

            // 4. Tổng số lượng các kiện con phải khớp số lượng hàng tồn của kiện gốc
            if (sourceInventory != null)
            {
                var totalChildQuantity = request.Children.Sum(c => c.Quantity);
                if (totalChildQuantity != sourceInventory.Quantity)
                {
                    throw new ArgumentException($"Tổng số lượng kiện con ({totalChildQuantity}) phải bằng số lượng hàng tồn của kiện gốc ({sourceInventory.Quantity}).");
                }
            }

            var binRepo = _unitOfWork.Repository<Bin>();
            Bin? sourceBin = null;
            if (source.BinId is Guid sourceBinId)
            {
                sourceBin = await binRepo.GetByIdAsync(sourceBinId);
            }

            var divisor = await GetVolumetricDivisorAsync();
            var allParcels = (await parcelRepo.GetAllAsync()).ToList();
            var usedCodes = allParcels.Select(p => p.PackageCode ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var createdChildren = new List<(Parcel Parcel, int Quantity)>();

            foreach (var child in request.Children)
            {
                // Kiện con để nguyên ô của kiện gốc, hoặc sang ô khác. Kiện chưa lên kệ thì tách
                // xong mới xếp kệ từng kiện con qua put-away.
                Bin? childBin = sourceBin;
                if (child.BinId is Guid childBinId && childBinId != source.BinId)
                {
                    if (sourceBin == null)
                    {
                        throw new ArgumentException("Kiện gốc chưa xếp kệ nên kiện con chưa chọn ô được. Tách xong hãy xếp kệ từng kiện con.");
                    }

                    childBin = await binRepo.GetByIdAsync(childBinId)
                        ?? throw new NotFoundException("Không tìm thấy ô kệ được chọn cho kiện con.");
                }

                var newParcel = new Parcel
                {
                    Id = Guid.NewGuid(),
                    ConsignmentId = source.ConsignmentId,
                    ParentParcelId = source.Id,
                    OrderItemId = source.OrderItemId,
                    PackageCode = ResolvePackageCode(child.PackageCode, usedCodes),
                    ActualWeight = child.ActualWeight,
                    Length = child.Length,
                    Width = child.Width,
                    Height = child.Height,
                    PackageStatus = source.PackageStatus,
                    CheckInTime = source.CheckInTime,
                    PutAwayTime = source.PutAwayTime,
                    BinId = childBin?.Id,
                    PackageConfigurationId = source.PackageConfigurationId
                };

                ApplyWeights(newParcel, divisor);
                createdChildren.Add((newParcel, child.Quantity));
            }

            // Mỗi ô nhận kiện con phải là ô khu lưu kho cùng kho, còn đủ chỗ sau khi kiện gốc rời đi.
            var touchedBins = new Dictionary<Guid, Bin>();
            foreach (var group in createdChildren.Where(c => c.Parcel.BinId.HasValue).GroupBy(c => c.Parcel.BinId!.Value))
            {
                var bin = group.Key == sourceBin?.Id ? sourceBin! : (await binRepo.GetByIdAsync(group.Key))!;
                var placement = await BinPlacementPolicy.EnsureCanStoreAsync(
                    _unitOfWork, bin, sourceBin!.WarehouseId ?? Guid.Empty,
                    group.Select(c => c.Parcel).ToList(), new[] { source.Id });
                bin.CurrentVolume = placement.OccupiedVolumeAfter;
                touchedBins[bin.Id] = bin;
            }

            if (sourceBin != null && !touchedBins.ContainsKey(sourceBin.Id))
            {
                sourceBin.CurrentVolume = await BinPlacementPolicy.VolumeAfterLeavingAsync(_unitOfWork, sourceBin.Id, new[] { source.Id });
                touchedBins[sourceBin.Id] = sourceBin;
            }

            foreach (var (newParcel, quantity) in createdChildren)
            {
                await parcelRepo.AddAsync(newParcel);

                // Tách hàng tồn theo số lượng của từng kiện con
                if (sourceInventory != null)
                {
                    await inventoryRepo.AddAsync(new Inventory
                    {
                        Id = Guid.NewGuid(),
                        CustomerId = sourceInventory.CustomerId,
                        OrderId = sourceInventory.OrderId,
                        OrderItemId = sourceInventory.OrderItemId,
                        ParcelId = newParcel.Id,
                        BinId = newParcel.BinId ?? sourceInventory.BinId,
                        Quantity = quantity,
                        Status = "AVAILABLE",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            foreach (var bin in touchedBins.Values)
            {
                binRepo.Update(bin);
            }

            source.PackageStatus = "SPLIT";
            parcelRepo.Update(source);

            if (sourceInventory != null)
            {
                await RetireInventoriesAsync(new[] { sourceInventory });
            }

            await _unitOfWork.SaveChangesAsync();

            return await BuildStructureResponseAsync("SPLIT", source, createdChildren.Select(c => c.Parcel).ToList());
        }

        public async Task<ParcelStructureResponseDto> MergeParcelsAsync(MergeParcelsRequestDto request, Guid? userId)
        {
            if (request?.SourceParcelIds == null || request.SourceParcelIds.Distinct().Count() < 2)
            {
                throw new ArgumentException("Cần ít nhất 2 kiện hàng khác nhau để gộp kiện.");
            }

            if (request.ActualWeight <= 0)
            {
                throw new ArgumentException("Cân nặng thực tế của kiện sau khi gộp phải lớn hơn 0.");
            }

            await ExportLockGuard.EnsureNotInActiveReleaseAsync(_unitOfWork, request.SourceParcelIds, "gộp kiện");

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = (await parcelRepo.GetAllAsync()).ToList();

            var sourceIds = request.SourceParcelIds.Distinct().ToList();
            var sources = allParcels.Where(p => sourceIds.Contains(p.Id)).ToList();

            if (sources.Count != sourceIds.Count)
            {
                throw new NotFoundException("Không tìm thấy một hoặc nhiều kiện hàng cần gộp.");
            }

            // 1. Chỉ gộp các kiện thuộc cùng một yêu cầu ký gửi để không lẫn quyền sở hữu hàng tồn
            var consignmentIds = sources.Select(p => p.ConsignmentId).Distinct().ToList();
            if (consignmentIds.Count > 1)
            {
                throw new ArgumentException("Chỉ được gộp các kiện hàng thuộc cùng một yêu cầu ký gửi.");
            }

            var invalidStatus = sources.FirstOrDefault(p => !StructurableStatuses.Contains(p.PackageStatus, StringComparer.OrdinalIgnoreCase));
            if (invalidStatus != null)
            {
                throw new ArgumentException($"Không thể gộp kiện {invalidStatus.PackageCode} đang ở trạng thái {StatusNames.Quote(invalidStatus.PackageStatus)}.");
            }

            // 2. Hàng tồn của các kiện phải đang khả dụng (BR-91)
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();
            var sourceInventories = allInventories.Where(i => sourceIds.Contains(i.ParcelId)).ToList();

            var lockedInventory = sourceInventories.FirstOrDefault(i => !string.Equals(i.Status, "AVAILABLE", StringComparison.OrdinalIgnoreCase));
            if (lockedInventory != null)
            {
                throw new ArgumentException($"Không thể gộp kiện vì có hàng tồn đang ở trạng thái {StatusNames.Quote(lockedInventory.Status)}.");
            }

            // 3. Các kiện phải nằm trong cùng một kho
            var binRepo = _unitOfWork.Repository<Bin>();
            var sourceBinIds = sources.Select(p => p.BinId).OfType<Guid>().Distinct().ToList();
            var sourceBins = (await binRepo.GetAllAsync()).Where(b => sourceBinIds.Contains(b.Id)).ToDictionary(b => b.Id);

            var warehouseIds = sourceBins.Values.Select(b => b.WarehouseId).OfType<Guid>().Distinct().ToList();
            if (warehouseIds.Count > 1)
            {
                throw new ArgumentException("Chỉ được gộp các kiện hàng đang nằm trong cùng một kho.");
            }

            // 4. Ô kệ chứa kiện sau khi gộp: ô được chọn, hoặc ô của kiện đầu tiên đang nằm kệ.
            Bin? targetBin = null;
            if (request.BinId is Guid requestedBinId)
            {
                targetBin = await binRepo.GetByIdAsync(requestedBinId)
                    ?? throw new NotFoundException("Không tìm thấy ô kệ đích cho kiện sau khi gộp.");
            }
            else
            {
                var firstBinId = sources.Select(p => p.BinId).OfType<Guid>().FirstOrDefault();
                if (firstBinId != Guid.Empty)
                {
                    sourceBins.TryGetValue(firstBinId, out targetBin);
                }
            }

            var divisor = await GetVolumetricDivisorAsync();
            var usedCodes = allParcels.Select(p => p.PackageCode ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var mergedParcel = new Parcel
            {
                Id = Guid.NewGuid(),
                ConsignmentId = consignmentIds.FirstOrDefault(),
                ParentParcelId = null,
                PackageCode = ResolvePackageCode(request.PackageCode, usedCodes),
                ActualWeight = request.ActualWeight,
                Length = request.Length,
                Width = request.Width,
                Height = request.Height,
                PackageStatus = targetBin != null ? "STORED" : sources.First().PackageStatus,
                CheckInTime = sources.Min(p => p.CheckInTime),
                PutAwayTime = sources.Max(p => p.PutAwayTime),
                BinId = targetBin?.Id,
                PackageConfigurationId = sources.First().PackageConfigurationId
            };

            ApplyWeights(mergedParcel, divisor);

            if (targetBin != null)
            {
                var warehouseId = warehouseIds.Count == 1 ? warehouseIds[0] : targetBin.WarehouseId ?? Guid.Empty;
                var placement = await BinPlacementPolicy.EnsureCanStoreAsync(
                    _unitOfWork, targetBin, warehouseId, new[] { mergedParcel }, sourceIds);
                targetBin.CurrentVolume = placement.OccupiedVolumeAfter;
                binRepo.Update(targetBin);
            }

            await parcelRepo.AddAsync(mergedParcel);

            // 5. Trả chỗ ở các ô cũ và đánh dấu kiện gốc đã gộp
            foreach (var bin in sourceBins.Values.Where(b => b.Id != targetBin?.Id))
            {
                bin.CurrentVolume = await BinPlacementPolicy.VolumeAfterLeavingAsync(_unitOfWork, bin.Id, sourceIds);
                binRepo.Update(bin);
            }

            foreach (var source in sources)
            {
                source.PackageStatus = "MERGED";
                source.ParentParcelId = mergedParcel.Id;
                parcelRepo.Update(source);
            }

            // 6. Dồn hàng tồn của các kiện gốc về kiện sau khi gộp
            if (sourceInventories.Count > 0)
            {
                var reference = sourceInventories.First();
                await inventoryRepo.AddAsync(new Inventory
                {
                    Id = Guid.NewGuid(),
                    CustomerId = reference.CustomerId,
                    OrderId = reference.OrderId,
                    OrderItemId = reference.OrderItemId,
                    ParcelId = mergedParcel.Id,
                    BinId = mergedParcel.BinId ?? reference.BinId,
                    Quantity = sourceInventories.Sum(i => i.Quantity),
                    Status = "AVAILABLE",
                    CreatedAt = DateTime.UtcNow
                });

                await RetireInventoriesAsync(sourceInventories);
            }

            await _unitOfWork.SaveChangesAsync();

            return await BuildStructureResponseAsync("MERGE", mergedParcel, sources);
        }

        /// <summary>
        /// Bỏ dòng tồn kho của kiện gốc đã tách/gộp sang kiện mới. Dòng nào có nhật ký vận động trỏ vào
        /// thì giữ lại, cho về 0 — xoá sẽ phá khoá ngoại sang INVENTORY_MOVEMENTS. Trước đây tách kiện
        /// xoá thẳng nên kiện đã lên kệ (có nhật ký nhập kho) tách là lỗi hệ thống.
        /// </summary>
        private async Task RetireInventoriesAsync(IReadOnlyCollection<Inventory> inventories)
        {
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var ids = inventories.Select(i => i.Id).ToHashSet();
            var referenced = (await _unitOfWork.Repository<InventoryMovement>().GetAllAsync())
                .Where(m => ids.Contains(m.InventoryId))
                .Select(m => m.InventoryId)
                .ToHashSet();

            foreach (var inventory in inventories)
            {
                if (referenced.Contains(inventory.Id))
                {
                    inventory.Quantity = 0;
                    inventory.Status = "RELEASED";
                    inventoryRepo.Update(inventory);
                    continue;
                }

                inventoryRepo.Delete(inventory);
            }
        }

        /// <summary>Thể tích của kiện hàng, cùng công thức với kiểm tra sức chứa ô kệ</summary>
        private static decimal GetVolume(Parcel parcel)
            => (parcel.Length ?? 0) * (parcel.Width ?? 0) * (parcel.Height ?? 0);

        /// <summary>Tính cân quy đổi thể tích và cân tính phí cho kiện hàng</summary>
        private static void ApplyWeights(Parcel parcel, decimal divisor)
        {
            if (parcel.Length.HasValue && parcel.Width.HasValue && parcel.Height.HasValue
                && parcel.Length.Value > 0 && parcel.Width.Value > 0 && parcel.Height.Value > 0
                && divisor > 0)
            {
                parcel.VolumetricWeight = Math.Round(GetVolume(parcel) / divisor, 4);
            }

            var actualWeight = parcel.ActualWeight ?? 0;
            var chargeable = parcel.VolumetricWeight.HasValue
                ? Math.Max(actualWeight, parcel.VolumetricWeight.Value)
                : actualWeight;

            parcel.ChargeableWeight = Math.Round(chargeable, 4);
        }

        /// <summary>Dùng mã kiện do người dùng nhập, hoặc sinh mã mới không trùng</summary>
        private static string ResolvePackageCode(string requestedCode, HashSet<string> usedCodes)
        {
            if (!string.IsNullOrWhiteSpace(requestedCode))
            {
                var trimmed = requestedCode.Trim().ToUpperInvariant();
                if (usedCodes.Contains(trimmed))
                {
                    throw new ArgumentException($"Mã kiện \"{trimmed}\" đã tồn tại.");
                }

                usedCodes.Add(trimmed);
                return trimmed;
            }

            var random = new Random();
            string code;
            do
            {
                code = $"PCL-{DateTime.UtcNow:yyyyMMddHHmmss}-{random.Next(100000, 999999)}";
            } while (usedCodes.Contains(code));

            usedCodes.Add(code);
            return code;
        }

        private async Task<ParcelStructureResponseDto> BuildStructureResponseAsync(string operation, Parcel parent, List<Parcel> children)
        {
            var binIds = children.Select(p => p.BinId).Append(parent.BinId).OfType<Guid>().ToHashSet();
            var bins = binIds.Count == 0
                ? new Dictionary<Guid, Bin>()
                : (await _unitOfWork.Repository<Bin>().GetAllAsync())
                    .Where(b => binIds.Contains(b.Id))
                    .ToDictionary(b => b.Id);

            var parcelIds = children.Select(p => p.Id).Append(parent.Id).ToHashSet();
            var inventories = (await _unitOfWork.Repository<Inventory>().GetAllAsync())
                .Where(i => parcelIds.Contains(i.ParcelId))
                .ToList();

            ParcelSummaryDto ToSummary(Parcel parcel)
            {
                Bin? bin = null;
                if (parcel.BinId is Guid binId)
                {
                    bins.TryGetValue(binId, out bin);
                }

                return new ParcelSummaryDto
                {
                    ParcelId = parcel.Id,
                    PackageCode = parcel.PackageCode ?? string.Empty,
                    ParentParcelId = parcel.ParentParcelId,
                    ActualWeight = parcel.ActualWeight,
                    Length = parcel.Length,
                    Width = parcel.Width,
                    Height = parcel.Height,
                    VolumetricWeight = parcel.VolumetricWeight,
                    ChargeableWeight = parcel.ChargeableWeight,
                    PackageStatus = parcel.PackageStatus ?? string.Empty,
                    BinId = parcel.BinId,
                    BinCode = bin?.BinCode ?? string.Empty,
                    Quantity = inventories.Where(i => i.ParcelId == parcel.Id).Sum(i => i.Quantity)
                };
            }

            return new ParcelStructureResponseDto
            {
                Operation = operation,
                Parent = ToSummary(parent),
                Children = children.Select(ToSummary).ToList()
            };
        }
    }
}
