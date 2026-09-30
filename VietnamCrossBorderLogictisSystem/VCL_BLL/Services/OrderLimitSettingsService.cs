using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Đọc / sửa giới hạn tạo đơn (SYSTEM_SETTINGS nhóm ORDER_LIMITS). Mỗi lần Admin đổi giá trị
    /// ghi một dòng SYSTEM_SETTING_HISTORIES (ai, lúc nào, cũ → mới).
    /// </summary>
    public class OrderLimitSettingsService : IOrderLimitSettingsService
    {
        private readonly IUnitOfWork _unitOfWork;

        public OrderLimitSettingsService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<OrderLimitSettingsResponseDto> GetAsync(bool includeAudit)
        {
            var rows = (await _unitOfWork.Repository<SystemSetting>()
                .FindAsync(s => s.SettingGroup == OrderLimitSettings.Group)).ToList();
            var values = OrderLimitSettings.ResolveValues(rows);
            var limits = OrderLimitSettings.Resolve(rows);

            var rowByKey = rows
                .GroupBy(r => r.SettingKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            Dictionary<Guid, string> names = new();
            if (includeAudit)
            {
                var ids = rows.Where(r => r.UpdatedById.HasValue).Select(r => r.UpdatedById!.Value).Distinct().ToList();
                names = await LoadUserNamesAsync(ids);
            }

            var items = OrderLimitSettings.Definitions.Select(def =>
            {
                rowByKey.TryGetValue(def.Key, out var row);
                return new OrderLimitSettingItemDto
                {
                    Key = def.Key,
                    Scope = def.Scope,
                    Label = def.Label,
                    Unit = def.Unit,
                    Description = def.Description,
                    Value = values[def.Key],
                    DefaultValue = def.DefaultValue,
                    AllowUnlimited = def.AllowUnlimited,
                    IsInteger = def.IsInteger,
                    MinValue = def.MinValue,
                    MaxValue = def.MaxValue,
                    UpdatedAt = includeAudit ? row?.UpdatedAt : null,
                    UpdatedById = includeAudit ? row?.UpdatedById : null,
                    UpdatedByName = includeAudit && row?.UpdatedById is Guid uid && names.TryGetValue(uid, out var n) ? n : null,
                };
            }).ToList();

            var latest = includeAudit
                ? items.Where(i => i.UpdatedAt.HasValue).OrderByDescending(i => i.UpdatedAt).FirstOrDefault()
                : null;

            return new OrderLimitSettingsResponseDto
            {
                Consignment = new ConsignmentOrderLimitsDto
                {
                    MaxParcelWeightKg = limits.MaxParcelWeightKg,
                    MaxParcelLengthCm = limits.MaxParcelLengthCm,
                    MaxParcelWidthCm = limits.MaxParcelWidthCm,
                    MaxParcelHeightCm = limits.MaxParcelHeightCm,
                    MaxParcelQuantity = limits.MaxParcelQuantity,
                    MaxItemDeclaredValue = limits.MaxItemDeclaredValue,
                    MaxTotalWeightKg = limits.MaxTotalWeightKg,
                    MaxTotalDeclaredValue = limits.MaxTotalDeclaredValue,
                    MaxPackages = limits.MaxPackages,
                },
                Purchase = new PurchaseOrderLimitsDto
                {
                    MaxItems = limits.MaxPurchaseItems,
                    MaxItemQuantity = limits.MaxPurchaseItemQuantity,
                },
                Items = items,
                UpdatedAt = latest?.UpdatedAt,
                UpdatedByName = latest?.UpdatedByName,
            };
        }

        public async Task<OrderLimitSettingsResponseDto> UpdateAsync(UpdateOrderLimitSettingsRequestDto request, Guid adminUserId)
        {
            if (request?.Items == null || request.Items.Count == 0)
                throw new ArgumentException("Danh sách giới hạn không được để trống.");

            // 1. Kiểm từng khoá: có thật, không trùng, giá trị hợp lệ.
            var incoming = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();
            foreach (var item in request.Items)
            {
                var def = OrderLimitSettings.Find(item?.Key);
                if (def == null)
                {
                    errors.Add($"Không có giới hạn mã \"{item?.Key}\".");
                    continue;
                }
                if (incoming.ContainsKey(def.Key))
                {
                    errors.Add($"Giới hạn \"{def.Label}\" bị gửi trùng.");
                    continue;
                }
                var error = OrderLimitSettings.Validate(def, item!.Value);
                if (error != null) errors.Add(error);
                incoming[def.Key] = item.Value;
            }
            if (errors.Count > 0)
                throw new ArgumentException(string.Join(" ", errors));

            // 2. Kiểm chéo trên bộ giá trị SAU khi áp: tổng cả đơn không được nhỏ hơn mức mỗi kiện.
            var repo = _unitOfWork.Repository<SystemSetting>();
            var rows = (await repo.FindAsync(s => s.SettingGroup == OrderLimitSettings.Group)).ToList();
            var current = OrderLimitSettings.ResolveValues(rows);
            var merged = new Dictionary<string, decimal?>(current, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in incoming) merged[kv.Key] = kv.Value;

            EnsureTotalNotBelowParcel(merged, OrderLimitSettings.MaxTotalWeightKgKey, OrderLimitSettings.MaxParcelWeightKgKey);
            EnsureTotalNotBelowParcel(merged, OrderLimitSettings.MaxTotalDeclaredValueKey, OrderLimitSettings.MaxItemDeclaredValueKey);

            // 3. Ghi giá trị đổi + nhật ký.
            var now = DateTime.UtcNow;
            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            var historyRepo = _unitOfWork.Repository<SystemSettingHistory>();
            var changed = 0;
            foreach (var kv in incoming)
            {
                var def = OrderLimitSettings.Find(kv.Key)!;
                var oldValue = current[def.Key];
                var row = rows.FirstOrDefault(r => string.Equals(r.SettingKey, def.Key, StringComparison.OrdinalIgnoreCase));
                if (row != null && oldValue == kv.Value && row.DecimalValue == kv.Value)
                    continue;

                if (row == null)
                {
                    row = new SystemSetting
                    {
                        Id = Guid.NewGuid(),
                        SettingKey = def.Key,
                        SettingGroup = OrderLimitSettings.Group,
                        Description = def.Label,
                        CreatedAt = now,
                    };
                    await repo.AddAsync(row);
                    rows.Add(row);
                }
                else
                {
                    repo.Update(row);
                }

                row.DecimalValue = kv.Value;
                row.UpdatedById = adminUserId;
                row.UpdatedAt = now;

                if (oldValue != kv.Value)
                {
                    await historyRepo.AddAsync(new SystemSettingHistory
                    {
                        Id = Guid.NewGuid(),
                        SettingKey = def.Key,
                        OldValue = oldValue,
                        NewValue = kv.Value,
                        ChangedById = adminUserId,
                        ChangedAt = now,
                        Note = note,
                    });
                }
                changed++;
            }

            if (changed > 0)
                await _unitOfWork.SaveChangesAsync();

            return await GetAsync(includeAudit: true);
        }

        public async Task<List<OrderLimitHistoryItemDto>> GetHistoryAsync(int take)
        {
            take = Math.Clamp(take, 1, 200);
            var keys = OrderLimitSettings.Definitions.Select(d => d.Key).ToList();
            var rows = (await _unitOfWork.Repository<SystemSettingHistory>()
                    .FindAsync(h => keys.Contains(h.SettingKey)))
                .OrderByDescending(h => h.ChangedAt)
                .Take(take)
                .ToList();

            var names = await LoadUserNamesAsync(rows.Where(r => r.ChangedById.HasValue).Select(r => r.ChangedById!.Value).Distinct().ToList());

            return rows.Select(h =>
            {
                var def = OrderLimitSettings.Find(h.SettingKey);
                return new OrderLimitHistoryItemDto
                {
                    Id = h.Id,
                    Key = h.SettingKey,
                    Label = def?.Label ?? h.SettingKey,
                    Unit = def?.Unit ?? string.Empty,
                    OldValue = h.OldValue,
                    NewValue = h.NewValue,
                    ChangedAt = h.ChangedAt,
                    ChangedById = h.ChangedById,
                    ChangedByName = h.ChangedById is Guid uid && names.TryGetValue(uid, out var n) ? n : null,
                    Note = h.Note,
                };
            }).ToList();
        }

        private static void EnsureTotalNotBelowParcel(IReadOnlyDictionary<string, decimal?> values, string totalKey, string parcelKey)
        {
            var total = values[totalKey];
            var parcel = values[parcelKey];
            if (total.HasValue && parcel.HasValue && total.Value < parcel.Value)
            {
                var totalDef = OrderLimitSettings.Find(totalKey)!;
                var parcelDef = OrderLimitSettings.Find(parcelKey)!;
                throw new ArgumentException(
                    $"\"{totalDef.Label}\" ({OrderLimitSettings.FormatValue(totalDef, total.Value)}) không được nhỏ hơn "
                    + $"\"{parcelDef.Label}\" ({OrderLimitSettings.FormatValue(parcelDef, parcel.Value)}).");
            }
        }

        private async Task<Dictionary<Guid, string>> LoadUserNamesAsync(List<Guid> ids)
        {
            if (ids.Count == 0) return new Dictionary<Guid, string>();
            var users = await _unitOfWork.Repository<User>().FindAsync(u => ids.Contains(u.Id));
            return users.ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? (u.Email ?? string.Empty) : u.FullName);
        }
    }
}
