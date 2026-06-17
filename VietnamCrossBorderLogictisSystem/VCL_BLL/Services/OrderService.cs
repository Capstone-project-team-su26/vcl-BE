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
    public class OrderService : IOrderService
    {
        private readonly IUnitOfWork _unitOfWork;

        public OrderService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<UpdateConsignmentStatusResponse> UpdateConsignmentStatusAsync(Guid orderId, UpdateConsignmentStatusRequest request)
        {
            var validStatuses = new[] { "PENDING_REVIEW", "APPROVED", "REJECTED" };
            if (string.IsNullOrWhiteSpace(request.Status) || !validStatuses.Contains(request.Status.ToUpper()))
            {
                throw new ArgumentException("Trạng thái không hợp lệ. Các trạng thái hợp lệ: PENDING_REVIEW, APPROVED, REJECTED.");
            }

            var normalizedStatus = request.Status.ToUpper();

            if (normalizedStatus == "REJECTED" && string.IsNullOrWhiteSpace(request.RejectionReason))
            {
                throw new ArgumentException("Vui lòng cung cấp lý do từ chối (RejectionReason).");
            }

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);

            if (order == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID {orderId}");
            }

            // Cannot update if CANCELLED or WAITING_FOR_PARCEL
            var invalidCurrentStatuses = new[] { "CANCELLED", "WAITING_FOR_PARCEL", "WAREHOUSE_RECEIVED" };
            if (order.Status != null && invalidCurrentStatuses.Contains(order.Status.ToUpper()))
            {
                throw new ArgumentException($"Không thể cập nhật trạng thái yêu cầu khi yêu cầu đang ở trạng thái {order.Status}");
            }

            if (normalizedStatus == "APPROVED" && string.IsNullOrEmpty(order.ConsignmentCode))
            {
                var allOrders = await orderRepo.GetAllAsync();
                var random = new Random();
                string generatedCode;
                do
                {
                    var now = DateTime.UtcNow;
                    var randomSuffix = random.Next(100000, 999999).ToString();
                    generatedCode = $"VCL-{now:yyyyMMddHHmmss}-{randomSuffix}";
                } while (allOrders.Any(o => o.ConsignmentCode == generatedCode));

                order.ConsignmentCode = generatedCode;
            }

            var oldStatus = order.Status;
            order.Status = normalizedStatus;
            
            if (normalizedStatus == "REJECTED")
            {
                order.Note = request.RejectionReason;
            }

            if (oldStatus != normalizedStatus)
            {
                if (normalizedStatus == "APPROVED" || normalizedStatus == "REJECTED")
                {
                    var customerRepo = _unitOfWork.Repository<Customer>();
                    var customer = await customerRepo.GetByIdAsync(order.CustomerId);
                    if (customer != null)
                    {
                        var notification = new Notification
                        {
                            Id = Guid.NewGuid(),
                            UserId = customer.UserId,
                            Title = normalizedStatus == "APPROVED" ? "Yêu cầu ký gửi đã được duyệt" : "Yêu cầu ký gửi bị từ chối",
                            Content = normalizedStatus == "APPROVED"
                                ? $"Yêu cầu ký gửi (Mã: {order.ConsignmentCode ?? order.Id.ToString()}) của bạn đã được duyệt."
                                : $"Yêu cầu ký gửi (Mã: {order.ConsignmentCode ?? order.Id.ToString()}) của bạn đã bị từ chối.{(string.IsNullOrWhiteSpace(request.RejectionReason) ? "" : $" Lý do: {request.RejectionReason}")}",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        };
                        var notificationRepo = _unitOfWork.Repository<Notification>();
                        await notificationRepo.AddAsync(notification);
                    }
                }
            }

            orderRepo.Update(order);
            await _unitOfWork.SaveChangesAsync();

            return new UpdateConsignmentStatusResponse
            {
                Status = order.Status,
                ConsignmentCode = order.ConsignmentCode
            };
        }

        public async Task<PaginatedResponse<ConsignmentResponseDto>> GetConsignmentsAsync(Guid? userId, string role, int pageNumber, int pageSize, string? status, string? searchCode)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var customerRepo = _unitOfWork.Repository<Customer>();

            // Get all data into memory - suitable for GenericRepository pattern
            var allOrders = await orderRepo.GetAllAsync();
            var allCustomers = await customerRepo.GetAllAsync();

            var query = allOrders.AsQueryable();

            // Customer only sees their own requests
            if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                if (userId == null)
                {
                    throw new UnauthorizedAccessException("Người dùng không hợp lệ.");
                }

                var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
                if (customer == null)
                {
                    return new PaginatedResponse<ConsignmentResponseDto>
                    {
                        Items = new List<ConsignmentResponseDto>(),
                        TotalCount = 0,
                        PageNumber = pageNumber,
                        PageSize = pageSize
                    };
                }

                query = query.Where(o => o.CustomerId == customer.Id);
            }

            // Filter by status
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(o => string.Equals(o.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            // Search by consignment code
            if (!string.IsNullOrWhiteSpace(searchCode))
            {
                query = query.Where(o => o.ConsignmentCode != null && o.ConsignmentCode.Contains(searchCode, StringComparison.OrdinalIgnoreCase));
            }

            var totalCount = query.Count();

            // Pagination
            var paginatedOrders = query
                .OrderByDescending(o => o.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Map to DTO
            var resultItems = paginatedOrders.Select(o => 
            {
                var customerInfo = allCustomers.FirstOrDefault(c => c.Id == o.CustomerId);
                return new ConsignmentResponseDto
                {
                    OrderId = o.Id,
                    ConsignmentCode = o.ConsignmentCode ?? string.Empty,
                    CustomerName = customerInfo?.FullName ?? string.Empty,
                    ConsignmentType = o.ConsignmentType ?? string.Empty,
                    Status = o.Status ?? string.Empty,
                    TotalWeight = o.TotalWeight,
                    TotalVolume = o.TotalVolume,
                    CreatedAt = o.CreatedAt
                };
            }).ToList();

            return new PaginatedResponse<ConsignmentResponseDto>
            {
                Items = resultItems,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<CancelConsignmentResponse> CancelConsignmentAsync(Guid orderId, Guid userId, CancelConsignmentRequest request)
        {
            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
            if (customer == null)
            {
                throw new UnauthorizedAccessException("Người dùng không phải là khách hàng hợp lệ.");
            }

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID {orderId}");
            }

            if (order.CustomerId != customer.Id)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền hủy yêu cầu ký gửi này.");
            }

            var invalidCancelStatuses = new[] { "CANCELLED", "WAREHOUSE_RECEIVED" };
            if (order.Status != null && invalidCancelStatuses.Contains(order.Status.ToUpper()))
            {
                throw new ArgumentException($"Không thể hủy yêu cầu ký gửi khi yêu cầu đang ở trạng thái {order.Status}");
            }

            order.Status = "CANCELLED";
            if (!string.IsNullOrWhiteSpace(request.CancelReason))
            {
                order.Note = request.CancelReason;
            }

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = customer.UserId,
                Title = "Yêu cầu ký gửi đã bị hủy",
                Content = $"Yêu cầu ký gửi (Mã: {order.ConsignmentCode ?? order.Id.ToString()}) của bạn đã bị hủy.{(string.IsNullOrWhiteSpace(request.CancelReason) ? "" : $" Lý do: {request.CancelReason}")}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            var notificationRepo = _unitOfWork.Repository<Notification>();
            await notificationRepo.AddAsync(notification);

            orderRepo.Update(order);
            await _unitOfWork.SaveChangesAsync();

            return new CancelConsignmentResponse
            {
                Status = order.Status,
                ConsignmentCode = order.ConsignmentCode
            };
        }
    }
}
