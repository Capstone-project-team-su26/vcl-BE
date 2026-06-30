using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class WarehouseReceivingNoteService : IWarehouseReceivingNoteService
    {
        private readonly IUnitOfWork _unitOfWork;

        public WarehouseReceivingNoteService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
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

            var warehouse = await warehouseRepo.GetByIdAsync(matchedNote.WarehouseId);

            return new WarehouseReceivingNoteResponseDto
            {
                Id = matchedNote.Id,
                ReceivingNoteCode = matchedNote.ReceivingNoteCode ?? string.Empty,
                ConsignmentOrderId = matchedNote.ConsignmentOrderId,
                WarehouseId = matchedNote.WarehouseId,
                WarehouseName = warehouse?.WarehouseName ?? "Kho không xác định",
                WarehouseNote = matchedNote.WarehouseNote ?? string.Empty,
                Status = matchedNote.Status ?? "ACTIVE",
                CreatedAt = matchedNote.CreatedAt
            };
        }

        public async Task<WarehouseReceivingNoteResponseDto> CreateReceivingNoteAsync(CreateWarehouseReceivingNoteRequestDto request)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(request.ConsignmentOrderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID: {request.ConsignmentOrderId}");

            // Rule: Consignment must be APPROVED
            if (!string.Equals(order.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Chỉ có thể tạo phiếu tiếp nhận kho khi yêu cầu ký gửi đã được duyệt (APPROVED).");

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

            // Rule: Warehouse must exist and be active
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var warehouse = await warehouseRepo.GetByIdAsync(request.WarehouseId);
            if (warehouse == null)
                throw new NotFoundException($"Không tìm thấy kho với ID: {request.WarehouseId}");

            if (warehouse.IsActive == false)
                throw new ArgumentException("Kho tiếp nhận được chọn đang tạm ngưng hoạt động.");

            // Generate unique code: WRN-XXXXXXXX (8 digits)
            var random = new Random();
            string code;
            do
            {
                var randomDigits = random.Next(10000000, 99999999).ToString();
                code = $"WRN-{randomDigits}";
            } while (allNotes.Any(n => string.Equals(n.ReceivingNoteCode, code, StringComparison.OrdinalIgnoreCase)));

            var newNote = new WarehouseReceivingNote
            {
                Id = Guid.NewGuid(),
                ReceivingNoteCode = code,
                ConsignmentOrderId = request.ConsignmentOrderId,
                WarehouseId = request.WarehouseId,
                WarehouseNote = request.WarehouseNote ?? string.Empty,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };

            await wrnRepo.AddAsync(newNote);
            await _unitOfWork.SaveChangesAsync();

            return new WarehouseReceivingNoteResponseDto
            {
                Id = newNote.Id,
                ReceivingNoteCode = newNote.ReceivingNoteCode,
                ConsignmentOrderId = newNote.ConsignmentOrderId,
                WarehouseId = newNote.WarehouseId,
                WarehouseName = warehouse.WarehouseName ?? "Kho không xác định",
                WarehouseNote = newNote.WarehouseNote,
                Status = newNote.Status,
                CreatedAt = newNote.CreatedAt
            };
        }
    }
}
