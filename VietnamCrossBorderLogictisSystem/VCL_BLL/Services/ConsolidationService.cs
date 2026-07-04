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
    public class ConsolidationService : IConsolidationService
    {
        private readonly IUnitOfWork _unitOfWork;

        public ConsolidationService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<ConsolidationResponseDto> CreateConsolidationAsync(CreateConsolidationRequest request)
        {
            if (request.OrderIds == null || request.OrderIds.Count == 0)
            {
                throw new ArgumentException("Yêu cầu gom hàng phải có ít nhất một đơn hàng (OrderId).");
            }

            var orderRepo = _unitOfWork.Repository<Order>();
            var allOrders = await orderRepo.GetAllAsync();
            var existingOrders = allOrders.Where(o => request.OrderIds.Contains(o.Id)).ToList();

            if (existingOrders.Count != request.OrderIds.Count)
            {
                var foundIds = existingOrders.Select(o => o.Id).ToList();
                var missingIds = request.OrderIds.Except(foundIds).ToList();
                throw new ArgumentException($"Không tìm thấy các đơn hàng sau: {string.Join(", ", missingIds)}");
            }

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();
            var parcelsToConsolidate = allParcels
                .Where(p => p.ConsignmentId.HasValue && request.OrderIds.Contains(p.ConsignmentId.Value))
                .ToList();

            if (parcelsToConsolidate.Count == 0)
            {
                throw new ArgumentException("Các đơn hàng được chọn không chứa kiện hàng (parcel) nào để gom.");
            }

            var alreadyConsolidated = parcelsToConsolidate.Where(p => p.MasterBoxId.HasValue && p.MasterBoxId != Guid.Empty).ToList();
            if (alreadyConsolidated.Count > 0)
            {
                var packageCodes = alreadyConsolidated.Select(p => p.PackageCode ?? p.Id.ToString()).ToList();
                throw new ArgumentException($"Các kiện hàng sau đã được gom ở MasterBox khác: {string.Join(", ", packageCodes)}");
            }

            var masterBoxRepo = _unitOfWork.Repository<MasterBox>();
            var allMasterBoxes = await masterBoxRepo.GetAllAsync();

            var random = new Random();
            string masterCode;
            do
            {
                var dateStr = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var suffix = random.Next(100000, 999999).ToString();
                masterCode = $"MBX-{dateStr}-{suffix}";
            } while (allMasterBoxes.Any(mb => mb.MasterCode.Equals(masterCode, StringComparison.OrdinalIgnoreCase)));

            var totalVolumeCm3 = parcelsToConsolidate.Sum(p => (p.Length ?? 0m) * (p.Width ?? 0m) * (p.Height ?? 0m));
            var masterBox = new MasterBox
            {
                Id = Guid.NewGuid(),
                MasterCode = masterCode,
                Status = string.IsNullOrWhiteSpace(request.Status) ? "CONSOLIDATED" : request.Status.Trim(),
                TotalWeight = parcelsToConsolidate.Sum(p => p.ActualWeight ?? p.ChargeableWeight ?? 0m),
                TotalVolume = totalVolumeCm3 / 1000000m
            };

            await masterBoxRepo.AddAsync(masterBox);

            foreach (var parcel in parcelsToConsolidate)
            {
                parcel.MasterBoxId = masterBox.Id;
                parcelRepo.Update(parcel);
            }

            await _unitOfWork.SaveChangesAsync();

            return new ConsolidationResponseDto
            {
                Id = masterBox.Id,
                MasterCode = masterBox.MasterCode,
                TotalWeight = masterBox.TotalWeight,
                TotalVolume = masterBox.TotalVolume,
                Status = masterBox.Status,
                ShipmentId = masterBox.ShipmentId,
                Orders = existingOrders.Select(o => new ConsolidatedOrderDto
                {
                    Id = o.Id,
                    ConsignmentCode = o.ConsignmentCode ?? string.Empty,
                    Status = o.Status ?? string.Empty,
                    Route = o.Route ?? string.Empty,
                    Parcels = parcelsToConsolidate.Where(p => p.ConsignmentId == o.Id).Select(p => new ConsolidatedParcelDto
                    {
                        Id = p.Id,
                        PackageCode = p.PackageCode ?? string.Empty,
                        ActualWeight = p.ActualWeight,
                        Length = p.Length,
                        Width = p.Width,
                        Height = p.Height,
                        VolumetricWeight = p.VolumetricWeight,
                        ChargeableWeight = p.ChargeableWeight,
                        PackageStatus = p.PackageStatus ?? string.Empty
                    }).ToList()
                }).ToList()
            };
        }

        public async Task<ConsolidationResponseDto> GetConsolidationByIdAsync(Guid id)
        {
            var masterBoxRepo = _unitOfWork.Repository<MasterBox>();
            var masterBox = await masterBoxRepo.GetByIdAsync(id);
            if (masterBox == null)
            {
                throw new NotFoundException($"Không tìm thấy MasterBox với ID {id}");
            }

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();
            var parcels = allParcels.Where(p => p.MasterBoxId == id).ToList();

            var orderRepo = _unitOfWork.Repository<Order>();
            var allOrders = await orderRepo.GetAllAsync();
            var orderIds = parcels.Where(p => p.ConsignmentId.HasValue).Select(p => p.ConsignmentId!.Value).Distinct().ToList();
            var orders = allOrders.Where(o => orderIds.Contains(o.Id)).ToList();

            return new ConsolidationResponseDto
            {
                Id = masterBox.Id,
                MasterCode = masterBox.MasterCode,
                TotalWeight = masterBox.TotalWeight,
                TotalVolume = masterBox.TotalVolume,
                Status = masterBox.Status,
                ShipmentId = masterBox.ShipmentId,
                Orders = orders.Select(o => new ConsolidatedOrderDto
                {
                    Id = o.Id,
                    ConsignmentCode = o.ConsignmentCode ?? string.Empty,
                    Status = o.Status ?? string.Empty,
                    Route = o.Route ?? string.Empty,
                    Parcels = parcels.Where(p => p.ConsignmentId == o.Id).Select(p => new ConsolidatedParcelDto
                    {
                        Id = p.Id,
                        PackageCode = p.PackageCode ?? string.Empty,
                        ActualWeight = p.ActualWeight,
                        Length = p.Length,
                        Width = p.Width,
                        Height = p.Height,
                        VolumetricWeight = p.VolumetricWeight,
                        ChargeableWeight = p.ChargeableWeight,
                        PackageStatus = p.PackageStatus ?? string.Empty
                    }).ToList()
                }).ToList()
            };
        }

        public async Task<IEnumerable<ConsolidationResponseDto>> GetConsolidationsAsync(string? status, string? search)
        {
            var masterBoxRepo = _unitOfWork.Repository<MasterBox>();
            var masterBoxes = await masterBoxRepo.GetAllAsync();
            var query = masterBoxes.AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(mb => mb.Status != null && mb.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.ToLower();
                query = query.Where(mb => mb.MasterCode != null && mb.MasterCode.ToLower().Contains(searchLower));
            }

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();

            var orderRepo = _unitOfWork.Repository<Order>();
            var allOrders = await orderRepo.GetAllAsync();

            var resultList = new List<ConsolidationResponseDto>();
            foreach (var mb in query)
            {
                var parcels = allParcels.Where(p => p.MasterBoxId == mb.Id).ToList();
                var orderIds = parcels.Where(p => p.ConsignmentId.HasValue).Select(p => p.ConsignmentId!.Value).Distinct().ToList();
                var orders = allOrders.Where(o => orderIds.Contains(o.Id)).ToList();

                resultList.Add(new ConsolidationResponseDto
                {
                    Id = mb.Id,
                    MasterCode = mb.MasterCode,
                    TotalWeight = mb.TotalWeight,
                    TotalVolume = mb.TotalVolume,
                    Status = mb.Status,
                    ShipmentId = mb.ShipmentId,
                    Orders = orders.Select(o => new ConsolidatedOrderDto
                    {
                        Id = o.Id,
                        ConsignmentCode = o.ConsignmentCode ?? string.Empty,
                        Status = o.Status ?? string.Empty,
                        Route = o.Route ?? string.Empty,
                        Parcels = parcels.Where(p => p.ConsignmentId == o.Id).Select(p => new ConsolidatedParcelDto
                        {
                            Id = p.Id,
                            PackageCode = p.PackageCode ?? string.Empty,
                            ActualWeight = p.ActualWeight,
                            Length = p.Length,
                            Width = p.Width,
                            Height = p.Height,
                            VolumetricWeight = p.VolumetricWeight,
                            ChargeableWeight = p.ChargeableWeight,
                            PackageStatus = p.PackageStatus ?? string.Empty
                        }).ToList()
                    }).ToList()
                });
            }

            return resultList;
        }

        public async Task<ConsolidationResponseDto> UpdateConsolidationAsync(Guid id, UpdateConsolidationRequest request)
        {
            var masterBoxRepo = _unitOfWork.Repository<MasterBox>();
            var masterBox = await masterBoxRepo.GetByIdAsync(id);
            if (masterBox == null)
            {
                throw new NotFoundException($"Không tìm thấy MasterBox với ID {id}");
            }

            if (request.MasterCode != null)
            {
                if (string.IsNullOrWhiteSpace(request.MasterCode))
                {
                    throw new ArgumentException("MasterCode không được để trống.");
                }

                var allMasterBoxes = await masterBoxRepo.GetAllAsync();
                if (allMasterBoxes.Any(mb => mb.Id != id && mb.MasterCode.Equals(request.MasterCode, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException($"MasterCode '{request.MasterCode}' đã tồn tại.");
                }

                masterBox.MasterCode = request.MasterCode.Trim();
            }

            if (request.Status != null)
            {
                masterBox.Status = request.Status.Trim();
            }

            if (request.ShipmentId.HasValue)
            {
                masterBox.ShipmentId = request.ShipmentId == Guid.Empty ? null : request.ShipmentId;
            }

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();

            var orderRepo = _unitOfWork.Repository<Order>();
            var allOrders = await orderRepo.GetAllAsync();

            if (request.OrderIds != null)
            {
                var currentParcels = allParcels.Where(p => p.MasterBoxId == id).ToList();
                foreach (var p in currentParcels)
                {
                    p.MasterBoxId = null;
                    parcelRepo.Update(p);
                }

                if (request.OrderIds.Count > 0)
                {
                    var existingOrders = allOrders.Where(o => request.OrderIds.Contains(o.Id)).ToList();
                    if (existingOrders.Count != request.OrderIds.Count)
                    {
                        var foundIds = existingOrders.Select(o => o.Id).ToList();
                        var missingIds = request.OrderIds.Except(foundIds).ToList();
                        throw new ArgumentException($"Không tìm thấy các đơn hàng sau: {string.Join(", ", missingIds)}");
                    }

                    var parcelsToConsolidate = allParcels
                        .Where(p => p.ConsignmentId.HasValue && request.OrderIds.Contains(p.ConsignmentId.Value))
                        .ToList();

                    if (parcelsToConsolidate.Count == 0)
                    {
                        throw new ArgumentException("Các đơn hàng được chọn không chứa kiện hàng (parcel) nào để gom.");
                    }

                    var alreadyConsolidated = parcelsToConsolidate
                        .Where(p => p.MasterBoxId.HasValue && p.MasterBoxId != Guid.Empty && p.MasterBoxId != id)
                        .ToList();
                    if (alreadyConsolidated.Count > 0)
                    {
                        var packageCodes = alreadyConsolidated.Select(p => p.PackageCode ?? p.Id.ToString()).ToList();
                        throw new ArgumentException($"Các kiện hàng sau đã được gom ở MasterBox khác: {string.Join(", ", packageCodes)}");
                    }

                    foreach (var p in parcelsToConsolidate)
                    {
                        p.MasterBoxId = id;
                        parcelRepo.Update(p);
                    }

                    var totalVolumeCm3 = parcelsToConsolidate.Sum(p => (p.Length ?? 0m) * (p.Width ?? 0m) * (p.Height ?? 0m));
                    masterBox.TotalVolume = totalVolumeCm3 / 1000000m;
                    masterBox.TotalWeight = parcelsToConsolidate.Sum(p => p.ActualWeight ?? p.ChargeableWeight ?? 0m);
                }
                else
                {
                    masterBox.TotalVolume = 0m;
                    masterBox.TotalWeight = 0m;
                }
            }
            else
            {
                var currentParcels = allParcels.Where(p => p.MasterBoxId == id).ToList();
                var totalVolumeCm3 = currentParcels.Sum(p => (p.Length ?? 0m) * (p.Width ?? 0m) * (p.Height ?? 0m));
                masterBox.TotalVolume = totalVolumeCm3 / 1000000m;
                masterBox.TotalWeight = currentParcels.Sum(p => p.ActualWeight ?? p.ChargeableWeight ?? 0m);
            }

            masterBoxRepo.Update(masterBox);
            await _unitOfWork.SaveChangesAsync();

            return await GetConsolidationByIdAsync(id);
        }

        public async Task<bool> DeleteConsolidationAsync(Guid id)
        {
            var masterBoxRepo = _unitOfWork.Repository<MasterBox>();
            var masterBox = await masterBoxRepo.GetByIdAsync(id);
            if (masterBox == null)
            {
                throw new NotFoundException($"Không tìm thấy MasterBox với ID {id}");
            }

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();
            var currentParcels = allParcels.Where(p => p.MasterBoxId == id).ToList();

            foreach (var p in currentParcels)
            {
                p.MasterBoxId = null;
                parcelRepo.Update(p);
            }

            masterBoxRepo.Delete(masterBox);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }
    }
}
