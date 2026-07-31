using System;

namespace VCL_DLL.Models;

/// <summary>
/// Nhà cung cấp dịch vụ phục vụ luồng xuất kho và mua hộ tại nước ngoài,
/// ví dụ đơn vị trung chuyển, đơn vị lấy hàng, nhà cung cấp hàng hoá.
/// </summary>
public partial class Supplier
{
    public Guid Id { get; set; }

    public string SupplierCode { get; set; } = string.Empty;

    public string SupplierName { get; set; } = string.Empty;

    /// <summary>TRANSIT, PICKUP, GOODS hoặc SERVICE</summary>
    public string SupplierType { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string ContactPerson { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
