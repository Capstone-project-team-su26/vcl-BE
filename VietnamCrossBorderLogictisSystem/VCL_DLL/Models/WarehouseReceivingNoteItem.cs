using System;

namespace VCL_DLL.Models;

/// <summary>
/// Dòng chi tiết của biên bản nhập kho: đối chiếu số lượng và trọng lượng
/// khách hàng khai báo với số liệu nhân viên kho đếm và cân thực tế.
/// </summary>
public partial class WarehouseReceivingNoteItem
{
    public Guid Id { get; set; }

    public Guid ReceivingNoteId { get; set; }

    /// <summary>Dòng hàng khai báo tương ứng trong yêu cầu ký gửi, có thể trống nếu là hàng phát sinh</summary>
    public Guid? OrderItemId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string ProductType { get; set; } = string.Empty;

    /// <summary>Số lượng khách hàng khai báo</summary>
    public int DeclaredQuantity { get; set; }

    /// <summary>Số lượng nhân viên kho đếm thực tế</summary>
    public int ActualQuantity { get; set; }

    /// <summary>ActualQuantity - DeclaredQuantity</summary>
    public int QuantityDifference { get; set; }

    /// <summary>Trọng lượng khách hàng khai báo, đơn vị kg</summary>
    public decimal? DeclaredWeight { get; set; }

    /// <summary>Trọng lượng cân thực tế tại kho, đơn vị kg</summary>
    public decimal? ActualWeight { get; set; }

    /// <summary>ActualWeight - DeclaredWeight</summary>
    public decimal? WeightDifference { get; set; }

    /// <summary>Ghi chú của nhân viên kho cho dòng hàng này</summary>
    public string Note { get; set; } = string.Empty;

    public virtual WarehouseReceivingNote ReceivingNote { get; set; }

    public virtual OrderItem OrderItem { get; set; }
}
