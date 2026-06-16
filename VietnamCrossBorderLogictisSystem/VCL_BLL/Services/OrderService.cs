using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
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
    }
}
