using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
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
        /// Lấy hệ số quy đổi thể tích từ activeRules (VOLUMETRIC_DIVISOR),
        /// hoặc từ file cấu hình (PricingSettings:VolumetricDivisor),
        /// hoặc mặc định là 5000.
        /// </summary>
        private async Task<decimal> GetVolumetricDivisorAsync()
        {
            var pricingRuleRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await pricingRuleRepo.GetAllAsync();
            var rule = allRules.FirstOrDefault(r =>
                string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(r.RuleCode, "VOLUMETRIC_DIVISOR", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(r.RuleType, "VOLUMETRIC_DIVISOR", StringComparison.OrdinalIgnoreCase)));

            if (rule != null && rule.Value > 0)
            {
                return rule.Value;
            }

            if (_configuration != null)
            {
                var configValStr = _configuration["PricingSettings:VolumetricDivisor"];
                if (decimal.TryParse(configValStr, out var configVal) && configVal > 0)
                {
                    return configVal;
                }
            }

            return 5000m;
        }

        public async Task<ParcelCheckInResponse> CheckInParcelAsync(ParcelCheckInRequest request, Guid? actorUserId = null)
        {
            if (request == null)
                throw new ArgumentException("Yêu cầu check-in không được để trống.");

            // 1. Kiểm tra mã gửi hàng hợp lệ (bắt đầu bằng VCL-)
            if (string.IsNullOrWhiteSpace(request.ShippingCode) ||
                !request.ShippingCode.StartsWith("VCL-", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Mã gửi hàng không hợp lệ.");
            }

            // 2. Tìm yêu cầu ký gửi
            var orderRepo = _unitOfWork.Repository<Order>();
            var allOrders = await orderRepo.GetAllAsync();
            var order = allOrders.FirstOrDefault(o =>
                string.Equals(o.ConsignmentCode, request.ShippingCode, StringComparison.OrdinalIgnoreCase));

            if (order == null)
            {
                throw new NotFoundException("Không tìm thấy yêu cầu ký gửi.");
            }

            // 3. Cho phép check-in với các đơn đã chốt báo giá / đặt cọc hoặc đã duyệt
            var checkInEligibleStatuses = new[] { "APPROVED", "CHECKED_IN", "DEPOSIT_PAID", "PAYMENT_CONFIRMED", "WAITING_DEPOSIT", "QUOTATION_CONFIRMED" };
            if (!checkInEligibleStatuses.Any(s => string.Equals(s, order.Status, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Yêu cầu ký gửi không ở trạng thái cho phép check-in (Hiện tại: {order.Status}).");
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

            var priorStatuses = new[] { "APPROVED", "DEPOSIT_PAID", "PAYMENT_CONFIRMED", "WAITING_DEPOSIT", "QUOTATION_CONFIRMED" };
            if (isFullyReceived && priorStatuses.Any(s => string.Equals(s, order.Status, StringComparison.OrdinalIgnoreCase)))
            {
                order.Status = "CHECKED_IN";
                orderRepo.Update(order);
            }

            // 6b. Nếu có phiếu tiếp nhận kho đang hiệu lực (VCL-146) -> cập nhật sang RECEIVED
            var wrnRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
            var allNotes = await wrnRepo.GetAllAsync();
            var activeNote = allNotes.FirstOrDefault(n =>
                n.ConsignmentOrderId == order.Id &&
                (string.Equals(n.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(n.Status, "PARTIALLY_RECEIVED", StringComparison.OrdinalIgnoreCase)));
            if (activeNote != null)
            {
                activeNote.Status = isFullyReceived ? "RECEIVED" : "PARTIALLY_RECEIVED";
                wrnRepo.Update(activeNote);
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

            var packageProgress = orderItems.Select(item =>
            {
                parcelByItemId.TryGetValue(item.Id, out var matchedParcel);

                return new ConsignmentPackageProgress
                {
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

            ParcelCheckInResponse BuildResponse((Parcel Parcel, OrderItem? Item, List<string> Warnings, decimal? WeightDifference) created) =>
                new()
                {
                    ParcelId = created.Parcel.Id,
                    ShippingCode = request.ShippingCode,
                    ConsignmentId = order.Id,
                    ConsignmentCode = order.ConsignmentCode ?? string.Empty,
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
        private static OrderItem? ResolveOrderItem(
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

            return orderItems.FirstOrDefault(i => !receivedItemIds.Contains(i.Id));
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
                        ShelfCode = shelf?.ShelfCode ?? string.Empty,
                        BinCode = bin.BinCode ?? string.Empty
                    };
                }
            }

            // 3.5. Kiểm tra sự tồn tại của bản ghi tồn kho
            var allInventories = await _unitOfWork.Repository<Inventory>().GetAllAsync();
            bool hasInventory = allInventories.Any(i => i.ParcelId == parcel.Id);

            // 4. Map response
            return new ParcelDetailResponse
            {
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
            string? region = null)
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

            // Filter by status if provided; nếu không truyền, chỉ lấy kiện đã nhận tại kho đang chờ put-away
            // (RECEIVED = check-in trực tiếp, RECEIVED_AT_DESTINATION = nhận từ lô vận chuyển quốc tế)
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => string.Equals(p.PackageStatus, status, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                var pendingStatuses = new[] { "RECEIVED", "RECEIVED_AT_DESTINATION" };
                query = query.Where(p => p.PackageStatus != null
                    && pendingStatuses.Any(s => string.Equals(s, p.PackageStatus, StringComparison.OrdinalIgnoreCase)));
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
                var wrn = order != null ? allWrns.FirstOrDefault(w => w.ConsignmentOrderId == order.Id) : null;
                var warehouse = wrn != null ? allWarehouses.FirstOrDefault(w => w.Id == wrn.WarehouseId) : null;

                string? warehouseRegion = warehouse?.Region?.ToString();

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
                    WarehouseRegion = warehouseRegion
                };
            }).AsQueryable();

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

            return new PaginatedResponse<PendingPutawayParcelDto>
            {
                Items = paginatedItems,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
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

            // 3. Chỉ cho nhập vị trí kho với Parcel đã được nhận tại kho:
            //    - RECEIVED: check-in trực tiếp tại kho/điểm tập kết
            //    - RECEIVED_AT_DESTINATION: nhận từ lô vận chuyển quốc tế tại kho đích (VCL-118)
            var putAwayEligibleStatuses = new[] { "RECEIVED", "RECEIVED_AT_DESTINATION" };
            if (!putAwayEligibleStatuses.Any(s => string.Equals(s, parcel.PackageStatus, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Chỉ cho phép nhập vị trí kho với kiện hàng đã được nhận tại kho (RECEIVED hoặc RECEIVED_AT_DESTINATION).");
            }

            // 4. Tìm Warehouse, Zone, Shelf, Bin
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var warehouse = await warehouseRepo.GetByIdAsync(request.WarehouseId);
            if (warehouse == null)
            {
                throw new NotFoundException("Không tìm thấy kho hàng.");
            }

            var zoneRepo = _unitOfWork.Repository<WarehouseZone>();
            var zone = await zoneRepo.GetByIdAsync(request.ZoneId);
            if (zone == null)
            {
                throw new NotFoundException("Không tìm thấy khu vực kho (Zone).");
            }

            var shelfRepo = _unitOfWork.Repository<Shelf>();
            var shelf = await shelfRepo.GetByIdAsync(request.ShelfId);
            if (shelf == null)
            {
                throw new NotFoundException("Không tìm thấy kệ kho (Shelf).");
            }

            var binRepo = _unitOfWork.Repository<Bin>();
            var bin = await binRepo.GetByIdAsync(request.BinId);
            if (bin == null)
            {
                throw new NotFoundException("Không tìm thấy ô kệ (Bin).");
            }

            // 5. Kiểm tra tính hợp lệ của mối quan hệ vị trí kho (hierarchy validation)
            if (zone.WarehouseId != warehouse.Id)
            {
                throw new ArgumentException("Khu vực kho không thuộc kho hàng đã chọn.");
            }

            if (shelf.ZoneId != zone.Id)
            {
                throw new ArgumentException("Kệ kho không thuộc khu vực kho đã chọn.");
            }

            if (bin.ShelfId != shelf.Id)
            {
                throw new ArgumentException("Ô kệ không thuộc kệ kho đã chọn.");
            }

            if (bin.WarehouseId != warehouse.Id)
            {
                throw new ArgumentException("Ô kệ không thuộc kho hàng đã chọn.");
            }

            // 6. Kiểm tra sức chứa của Bin trước khi gán
            // Tính thể tích của parcel: Length * Width * Height
            decimal parcelVolume = (parcel.Length ?? 0) * (parcel.Width ?? 0) * (parcel.Height ?? 0);
            if (bin.MaxVolume.HasValue)
            {
                decimal availableVolume = bin.MaxVolume.Value - (bin.CurrentVolume ?? 0);
                if (parcelVolume > availableVolume)
                {
                    // Nêu rõ đơn vị: ô kệ khai theo lít thay vì cm³ sẽ rơi vào đây dù còn trống.
                    throw new ArgumentException(
                        $"Ô kệ {bin.BinCode} không đủ sức chứa. Kiện cần {parcelVolume:N0} cm³, ô kệ còn {availableVolume:N0} cm³ " +
                        $"(sức chứa khai báo {bin.MaxVolume.Value:N0} cm³). Nếu ô kệ thực tế lớn hơn, kiểm tra lại đơn vị của maxVolume — trường này tính bằng cm³.");
                }
            }

            // Tính cân nặng của parcel: ActualWeight
            if (bin.MaxWeight.HasValue)
            {
                var allParcels = await parcelRepo.GetAllAsync();
                var currentWeightInBin = allParcels
                    .Where(p => p.BinId == bin.Id && string.Equals(p.PackageStatus, "STORED", StringComparison.OrdinalIgnoreCase))
                    .Sum(p => p.ActualWeight ?? 0);

                decimal availableWeight = bin.MaxWeight.Value - currentWeightInBin;
                decimal parcelWeight = parcel.ActualWeight ?? 0;
                if (parcelWeight > availableWeight)
                {
                    throw new ArgumentException(
                        $"Ô kệ {bin.BinCode} không đủ tải trọng. Kiện nặng {parcelWeight:N2} kg, ô kệ còn chịu được {availableWeight:N2} kg " +
                        $"(tải trọng khai báo {bin.MaxWeight!.Value:N2} kg).");
                }
            }

            // 7. Cập nhật thông tin vị trí lưu trữ và trạng thái của Parcel
            var putAwayTime = DateTime.UtcNow;
            parcel.BinId = bin.Id;
            parcel.PackageStatus = "STORED";
            parcel.PutAwayTime = putAwayTime;

            // Cập nhật thể tích hiện tại của Bin
            bin.CurrentVolume = (bin.CurrentVolume ?? 0) + parcelVolume;

            parcelRepo.Update(parcel);
            binRepo.Update(bin);

            // 7b. TỰ ĐỘNG GHI NHẬN TỒN KHO (INVENTORY) & CẬP NHẬT TRẠNG THÁI ORDER (TRONG CÙNG TRANSACTION)
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
                var existingInventory = allInventories.FirstOrDefault(i => i.ParcelId == parcel.Id);

                if (existingInventory == null)
                {
                    var orderItemRepo = _unitOfWork.Repository<OrderItem>();
                    var allItems = await orderItemRepo.GetAllAsync();
                    var consignmentItems = allItems.Where(i => i.OrderId == order.Id).ToList();

                    Guid? orderItemId = consignmentItems.FirstOrDefault()?.Id;
                    int quantity = consignmentItems.Sum(item => item.Quantity ?? 1);
                    if (quantity <= 0)
                    {
                        quantity = 1;
                    }

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
                }

                // Chuyển trạng thái đơn sang WAREHOUSE_RECEIVED khi đã hoàn tất put-away và có inventory
                order.Status = "WAREHOUSE_RECEIVED";
                orderRepo.Update(order);
            }

            await _unitOfWork.SaveChangesAsync();

            // 8. Trả về thông tin kết quả
            return new ParcelPutAwayResponse
            {
                PackageCode = parcel.PackageCode ?? string.Empty,
                WarehouseName = warehouse.WarehouseName ?? string.Empty,
                ZoneName = zone.ZoneName ?? string.Empty,
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

            // 1. Kiện gốc phải tồn tại và đang ở trạng thái cho phép tách
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var source = await parcelRepo.GetByIdAsync(parcelId);
            if (source == null)
            {
                throw new NotFoundException($"Không tìm thấy kiện hàng với ID: {parcelId}");
            }

            if (!StructurableStatuses.Contains(source.PackageStatus, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Không thể tách kiện đang ở trạng thái {source.PackageStatus}.");
            }

            // 2. Không tách kiện đang được giữ cho phiếu xuất kho (BR-91)
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();
            var sourceInventory = allInventories.FirstOrDefault(i => i.ParcelId == source.Id);

            if (sourceInventory != null
                && !string.Equals(sourceInventory.Status, "AVAILABLE", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Không thể tách kiện vì hàng tồn đang ở trạng thái {sourceInventory.Status}.");
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

            var createdChildren = new List<Parcel>();

            foreach (var child in request.Children)
            {
                // Ô kệ của kiện con phải cùng kho với kiện gốc (BR-56)
                Bin? childBin = sourceBin;
                if (child.BinId is Guid childBinId && childBinId != source.BinId)
                {
                    childBin = await binRepo.GetByIdAsync(childBinId);
                    if (childBin == null)
                    {
                        throw new NotFoundException("Không tìm thấy ô kệ được chọn cho kiện con.");
                    }

                    if (!childBin.IsActive)
                    {
                        throw new ArgumentException($"Ô kệ {childBin.BinCode} đang ngừng sử dụng.");
                    }

                    if (sourceBin?.WarehouseId != null && childBin.WarehouseId != sourceBin.WarehouseId)
                    {
                        throw new ArgumentException("Kiện con phải được xếp trong cùng kho với kiện gốc.");
                    }
                }

                var newParcel = new Parcel
                {
                    Id = Guid.NewGuid(),
                    ConsignmentId = source.ConsignmentId,
                    ParentParcelId = source.Id,
                    PackageCode = ResolvePackageCode(child.PackageCode, usedCodes),
                    ActualWeight = child.ActualWeight,
                    Length = child.Length,
                    Width = child.Width,
                    Height = child.Height,
                    PackageStatus = source.PackageStatus,
                    CheckInTime = source.CheckInTime,
                    PutAwayTime = source.PutAwayTime,
                    BinId = childBin?.Id ?? source.BinId,
                    PackageConfigurationId = source.PackageConfigurationId
                };

                ApplyWeights(newParcel, divisor);

                await parcelRepo.AddAsync(newParcel);
                createdChildren.Add(newParcel);

                // Cộng thể tích kiện con vào ô kệ chứa nó
                if (childBin != null)
                {
                    childBin.CurrentVolume = (childBin.CurrentVolume ?? 0) + GetVolume(newParcel);
                    binRepo.Update(childBin);
                }

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
                        Quantity = child.Quantity,
                        Status = "AVAILABLE",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // 5. Kiện gốc chuyển sang SPLIT và trả lại thể tích cho ô kệ
            if (sourceBin != null)
            {
                var remaining = (sourceBin.CurrentVolume ?? 0) - GetVolume(source);
                sourceBin.CurrentVolume = remaining < 0 ? 0 : remaining;
                binRepo.Update(sourceBin);
            }

            source.PackageStatus = "SPLIT";
            parcelRepo.Update(source);

            if (sourceInventory != null)
            {
                inventoryRepo.Delete(sourceInventory);
            }

            await _unitOfWork.SaveChangesAsync();

            return await BuildStructureResponseAsync("SPLIT", source, createdChildren);
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
                throw new ArgumentException($"Không thể gộp kiện {invalidStatus.PackageCode} đang ở trạng thái {invalidStatus.PackageStatus}.");
            }

            // 2. Hàng tồn của các kiện phải đang khả dụng (BR-91)
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();
            var sourceInventories = allInventories.Where(i => sourceIds.Contains(i.ParcelId)).ToList();

            var lockedInventory = sourceInventories.FirstOrDefault(i => !string.Equals(i.Status, "AVAILABLE", StringComparison.OrdinalIgnoreCase));
            if (lockedInventory != null)
            {
                throw new ArgumentException($"Không thể gộp kiện vì có hàng tồn đang ở trạng thái {lockedInventory.Status}.");
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

            // 4. Xác định ô kệ chứa kiện sau khi gộp
            Bin? targetBin = null;
            if (request.BinId is Guid requestedBinId)
            {
                targetBin = await binRepo.GetByIdAsync(requestedBinId);
                if (targetBin == null)
                {
                    throw new NotFoundException("Không tìm thấy ô kệ đích cho kiện sau khi gộp.");
                }

                if (!targetBin.IsActive)
                {
                    throw new ArgumentException("Ô kệ đích đang ngừng sử dụng.");
                }

                if (warehouseIds.Count == 1 && targetBin.WarehouseId != warehouseIds[0])
                {
                    throw new ArgumentException("Ô kệ đích phải thuộc cùng kho với các kiện được gộp.");
                }
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
            await parcelRepo.AddAsync(mergedParcel);

            // 5. Trả thể tích của từng kiện gốc về ô kệ cũ và đánh dấu đã gộp
            foreach (var source in sources)
            {
                if (source.BinId is Guid binId && sourceBins.TryGetValue(binId, out var bin))
                {
                    var remaining = (bin.CurrentVolume ?? 0) - GetVolume(source);
                    bin.CurrentVolume = remaining < 0 ? 0 : remaining;
                    binRepo.Update(bin);
                }

                source.PackageStatus = "MERGED";
                source.ParentParcelId = mergedParcel.Id;
                parcelRepo.Update(source);
            }

            if (targetBin != null)
            {
                targetBin.CurrentVolume = (targetBin.CurrentVolume ?? 0) + GetVolume(mergedParcel);
                binRepo.Update(targetBin);
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

                foreach (var inventory in sourceInventories)
                {
                    inventoryRepo.Delete(inventory);
                }
            }

            await _unitOfWork.SaveChangesAsync();

            return await BuildStructureResponseAsync("MERGE", mergedParcel, sources);
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
