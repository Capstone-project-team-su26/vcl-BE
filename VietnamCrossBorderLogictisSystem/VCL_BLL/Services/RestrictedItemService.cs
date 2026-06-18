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
    public class RestrictedItemService : IRestrictedItemService
    {
        private readonly IUnitOfWork _unitOfWork;

        public RestrictedItemService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IEnumerable<RestrictedItemDto>> GetAllAsync()
        {
            var itemRepo = _unitOfWork.Repository<RestrictedItem>();
            var items = await itemRepo.GetAllAsync();

            return items.Select(item => new RestrictedItemDto
            {
                Id = item.Id,
                ItemName = item.ItemName,
                Country = item.Country,
                RestrictionType = item.RestrictionType,
                Note = item.Note,
                IsActive = item.IsActive
            });
        }

        public async Task<RestrictedItemDto> GetByIdAsync(Guid id)
        {
            var itemRepo = _unitOfWork.Repository<RestrictedItem>();
            var item = await itemRepo.GetByIdAsync(id);

            if (item == null)
            {
                throw new NotFoundException($"Không tìm thấy mặt hàng cấm/hạn chế với ID: {id}");
            }

            return new RestrictedItemDto
            {
                Id = item.Id,
                ItemName = item.ItemName,
                Country = item.Country,
                RestrictionType = item.RestrictionType,
                Note = item.Note,
                IsActive = item.IsActive
            };
        }

        public async Task<RestrictedItemDto> CreateAsync(CreateRestrictedItemRequest request)
        {
            var itemRepo = _unitOfWork.Repository<RestrictedItem>();
            var allItems = await itemRepo.GetAllAsync();

            // Check duplicate
            var isDuplicate = allItems.Any(item =>
                string.Equals(item.ItemName?.Trim(), request.ItemName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Country?.Trim() ?? string.Empty, request.Country?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.RestrictionType?.Trim(), request.RestrictionType.Trim(), StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                throw new ArgumentException("Không cho tạo trùng mặt hàng cùng quốc gia và cùng loại hạn chế.");
            }

            var newItem = new RestrictedItem
            {
                Id = Guid.NewGuid(),
                ItemName = request.ItemName.Trim(),
                Country = request.Country?.Trim(),
                RestrictionType = request.RestrictionType.Trim(),
                Note = request.Note,
                IsActive = request.IsActive
            };

            await itemRepo.AddAsync(newItem);
            await _unitOfWork.SaveChangesAsync();

            return new RestrictedItemDto
            {
                Id = newItem.Id,
                ItemName = newItem.ItemName,
                Country = newItem.Country,
                RestrictionType = newItem.RestrictionType,
                Note = newItem.Note,
                IsActive = newItem.IsActive
            };
        }

        public async Task UpdateAsync(Guid id, UpdateRestrictedItemRequest request)
        {
            var itemRepo = _unitOfWork.Repository<RestrictedItem>();
            var item = await itemRepo.GetByIdAsync(id);

            if (item == null)
            {
                throw new NotFoundException($"Không tìm thấy mặt hàng cấm/hạn chế với ID: {id}");
            }

            // Check duplicate
            var allItems = await itemRepo.GetAllAsync();
            var isDuplicate = allItems.Any(i =>
                i.Id != id &&
                string.Equals(i.ItemName?.Trim(), request.ItemName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(i.Country?.Trim() ?? string.Empty, request.Country?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(i.RestrictionType?.Trim(), request.RestrictionType.Trim(), StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                throw new ArgumentException("Mặt hàng đã tồn tại cùng quốc gia và cùng loại hạn chế.");
            }

            item.ItemName = request.ItemName.Trim();
            item.Country = request.Country?.Trim();
            item.RestrictionType = request.RestrictionType.Trim();
            item.Note = request.Note;
            item.IsActive = request.IsActive;

            itemRepo.Update(item);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var itemRepo = _unitOfWork.Repository<RestrictedItem>();
            var item = await itemRepo.GetByIdAsync(id);

            if (item == null)
            {
                throw new NotFoundException($"Không tìm thấy mặt hàng cấm/hạn chế với ID: {id}");
            }

            itemRepo.Delete(item);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
