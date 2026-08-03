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
            var allItems = await _unitOfWork.Repository<OrderItem>().GetAllAsync();
            var expectedItems = allItems
                .Where(i => i.OrderId == order.Id)
                .Select(i => new WarehouseReceivingNoteExpectedItemDto
                {
                    ProductName = i.ProductName ?? string.Empty,
                    ProductType = i.ProductType ?? string.Empty,
                    Quantity = i.Quantity ?? 0,
                    DeclaredValue = i.DeclaredValue
                })
                .ToList();

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

            // 2. Rule: Consignment must exist and be APPROVED
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(request.ConsignmentOrderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID: {request.ConsignmentOrderId}");

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
            note.Status = "RECEIVED";
            if (!string.IsNullOrWhiteSpace(request.WarehouseNote))
            {
                note.WarehouseNote = request.WarehouseNote.Trim();
            }

            noteRepo.Update(note);
            await _unitOfWork.SaveChangesAsync();

            return await BuildDetailAsync(note);
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
                HasDiscrepancy = items.Any(i => i.QuantityDifference != 0
                    || (i.WeightDifference.HasValue && i.WeightDifference.Value != 0)),
                Items = items
            };
        }
    }
}
