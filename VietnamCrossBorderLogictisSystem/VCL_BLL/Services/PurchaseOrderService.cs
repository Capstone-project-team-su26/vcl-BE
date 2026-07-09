using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class PurchaseOrderService : IPurchaseOrderService
    {
        private readonly IUnitOfWork _unitOfWork;

        // Các role được phép xử lý đơn mua hàng (Staff/Buying Team)
        private static readonly string[] BuyingRoles =
            { "ADMIN", "SALE", "SALES", "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "BUYING", "BUYINGTEAM", "BUYER" };

        // Thứ tự trạng thái xử lý mua hàng với nhà cung cấp
        private static readonly string[] StatusFlow =
            { "PURCHASE_ORDER_CREATED", "ORDERED", "PURCHASED", "WAITING_WAREHOUSE_RECEIVE" };

        public PurchaseOrderService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<PurchaseOrderResponseDto> CreatePurchaseOrderAsync(
            Guid requestId, Guid userId, string userRole, CreatePurchaseOrderDto request)
        {
            request ??= new CreatePurchaseOrderDto();

            // 1. Phân quyền: chỉ Staff/Buying Team được tạo đơn mua hàng
            if (!IsBuyingTeam(userRole))
            {
                throw new UnauthorizedAccessException("Chỉ Staff hoặc Buying Team được tạo đơn mua hàng.");
            }

            // 2. Tìm yêu cầu mua hộ (404 nếu không có)
            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId);
            if (purchaseRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            }

            // 3. Chỉ tạo đơn mua hàng khi Customer đã xác nhận báo giá (báo giá ACCEPTED)
            var quotationRepo = _unitOfWork.Repository<PurchaseQuotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            var acceptedQuotation = allQuotations
                .Where(q => q.PurchaseRequestId == requestId
                    && string.Equals(q.Status, "ACCEPTED", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();
            if (acceptedQuotation == null)
            {
                throw new ArgumentException("Không thể tạo đơn mua hàng do yêu cầu chưa được Customer xác nhận báo giá.");
            }

            // 4. Không cho tạo trùng đơn mua hàng đang hiệu lực cho cùng một yêu cầu
            var orderRepo = _unitOfWork.Repository<PurchaseOrder>();
            var allOrders = await orderRepo.GetAllAsync();
            bool hasActiveOrder = allOrders.Any(o =>
                o.PurchaseRequestId == requestId
                && !string.Equals(o.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase));
            if (hasActiveOrder)
            {
                throw new ArgumentException("Yêu cầu mua hộ này đã có đơn mua hàng.");
            }

            // 5. Sao chép danh sách sản phẩm từ báo giá đã được xác nhận
            var allQuotationItems = await _unitOfWork.Repository<PurchaseQuotationItem>().GetAllAsync();
            var quotationItems = allQuotationItems
                .Where(qi => qi.PurchaseQuotationId == acceptedQuotation.Id)
                .ToList();

            var allRequestItems = await _unitOfWork.Repository<PurchaseRequestItem>().GetAllAsync();
            var requestItemMap = allRequestItems
                .Where(i => i.PurchaseRequestId == requestId)
                .ToDictionary(i => i.Id);

            // 6. Tạo đơn mua hàng ở trạng thái khởi tạo
            var purchaseOrder = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = requestId,
                PurchaseQuotationId = acceptedQuotation.Id,
                PurchaseOrderCode = $"PO-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                SupplierName = request.SupplierName,
                PurchaseNote = request.PurchaseNote,
                TotalAmount = acceptedQuotation.TotalAmount,
                Status = "PURCHASE_ORDER_CREATED",
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };
            await orderRepo.AddAsync(purchaseOrder);

            var orderItemRepo = _unitOfWork.Repository<PurchaseOrderItem>();
            var itemDtos = new List<PurchaseOrderItemResponseDto>();

            foreach (var qi in quotationItems)
            {
                requestItemMap.TryGetValue(qi.PurchaseRequestItemId, out var reqItem);

                var orderItem = new PurchaseOrderItem
                {
                    Id = Guid.NewGuid(),
                    PurchaseOrderId = purchaseOrder.Id,
                    PurchaseRequestItemId = qi.PurchaseRequestItemId,
                    ProductName = reqItem?.ProductName,
                    UnitPrice = qi.UnitPrice,
                    Quantity = qi.Quantity,
                    LineTotal = qi.LineTotal
                };
                await orderItemRepo.AddAsync(orderItem);

                itemDtos.Add(new PurchaseOrderItemResponseDto
                {
                    OrderItemId = orderItem.Id,
                    PurchaseRequestItemId = orderItem.PurchaseRequestItemId,
                    ProductName = orderItem.ProductName,
                    UnitPrice = orderItem.UnitPrice,
                    Quantity = orderItem.Quantity,
                    LineTotal = orderItem.LineTotal
                });
            }

            await _unitOfWork.SaveChangesAsync();

            return new PurchaseOrderResponseDto
            {
                PurchaseOrderId = purchaseOrder.Id,
                PurchaseOrderCode = purchaseOrder.PurchaseOrderCode,
                PurchaseRequestId = requestId,
                PurchaseCode = purchaseRequest.PurchaseCode ?? string.Empty,
                PurchaseQuotationId = purchaseOrder.PurchaseQuotationId,
                SupplierName = purchaseOrder.SupplierName,
                PurchaseNote = purchaseOrder.PurchaseNote,
                TotalAmount = purchaseOrder.TotalAmount,
                Status = purchaseOrder.Status,
                StatusNote = purchaseOrder.StatusNote,
                CreatedAt = purchaseOrder.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = purchaseOrder.UpdatedAt,
                Items = itemDtos
            };
        }

        public async Task<UpdatePurchaseOrderStatusResponseDto> UpdatePurchaseOrderStatusAsync(
            Guid purchaseOrderId, Guid userId, string userRole, UpdatePurchaseOrderStatusRequestDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu cập nhật không được để trống.");

            // 1. Phân quyền: chỉ Staff/Buying Team được cập nhật
            if (!IsBuyingTeam(userRole))
            {
                throw new UnauthorizedAccessException("Chỉ Staff hoặc Buying Team được cập nhật trạng thái đơn mua hàng.");
            }

            // 2. Tìm đơn mua hàng (404 nếu không có)
            var orderRepo = _unitOfWork.Repository<PurchaseOrder>();
            var purchaseOrder = await orderRepo.GetByIdAsync(purchaseOrderId);
            if (purchaseOrder == null)
            {
                throw new NotFoundException($"Không tìm thấy đơn mua hàng với ID {purchaseOrderId}");
            }

            // 3. Validate trạng thái mới nằm trong luồng hợp lệ
            var targetStatus = request.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            var targetIndex = Array.IndexOf(StatusFlow, targetStatus);
            if (targetIndex < 0)
            {
                throw new ArgumentException(
                    "Trạng thái không hợp lệ. Hợp lệ: PURCHASE_ORDER_CREATED, ORDERED, PURCHASED, WAITING_WAREHOUSE_RECEIVE.");
            }

            // 4. Chỉ cho cập nhật tiến đúng thứ tự (đúng bước kế tiếp, không nhảy cóc/lùi)
            var currentStatus = purchaseOrder.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            var currentIndex = Array.IndexOf(StatusFlow, currentStatus);
            if (targetIndex != currentIndex + 1)
            {
                throw new ArgumentException(
                    $"Không thể chuyển trạng thái từ {purchaseOrder.Status} sang {targetStatus}. Chỉ được cập nhật sang bước kế tiếp theo đúng thứ tự.");
            }

            // 5. Cập nhật trạng thái + ghi chú xử lý + người/thời gian cập nhật
            purchaseOrder.Status = targetStatus;
            purchaseOrder.StatusNote = string.IsNullOrWhiteSpace(request.StatusNote) ? null : request.StatusNote.Trim();
            purchaseOrder.UpdatedBy = userId;
            purchaseOrder.UpdatedAt = DateTime.UtcNow;
            orderRepo.Update(purchaseOrder);
            await _unitOfWork.SaveChangesAsync();

            return new UpdatePurchaseOrderStatusResponseDto
            {
                PurchaseOrderId = purchaseOrder.Id,
                PurchaseOrderCode = purchaseOrder.PurchaseOrderCode ?? string.Empty,
                Status = purchaseOrder.Status,
                StatusNote = purchaseOrder.StatusNote,
                UpdatedAt = purchaseOrder.UpdatedAt
            };
        }

        private static bool IsBuyingTeam(string userRole)
        {
            return !string.IsNullOrWhiteSpace(userRole)
                && BuyingRoles.Contains(userRole.Trim().ToUpperInvariant());
        }
    }
}
