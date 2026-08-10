using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VCL_BLL.DTOs
{
    public class GoshipRateRequest
    {
        [JsonPropertyName("shipment")]
        public GoshipRateShipment Shipment { get; set; } = new GoshipRateShipment();
    }

    public class GoshipRateShipment
    {
        [JsonPropertyName("address_from")]
        public GoshipAddressBasic AddressFrom { get; set; } = new GoshipAddressBasic();

        [JsonPropertyName("address_to")]
        public GoshipAddressBasic AddressTo { get; set; } = new GoshipAddressBasic();

        [JsonPropertyName("parcel")]
        public GoshipParcelBasic Parcel { get; set; } = new GoshipParcelBasic();
    }

    public class GoshipAddressBasic
    {
        [JsonPropertyName("city")]
        public string City { get; set; } = string.Empty;

        [JsonPropertyName("district")]
        public string District { get; set; } = string.Empty;
    }

    public class GoshipParcelBasic
    {
        [JsonPropertyName("cod")]
        public int Cod { get; set; } = 0;

        [JsonPropertyName("weight")]
        public int Weight { get; set; } = 200; // in grams

        [JsonPropertyName("width")]
        public int Width { get; set; } = 10;

        [JsonPropertyName("height")]
        public int Height { get; set; } = 10;

        [JsonPropertyName("length")]
        public int Length { get; set; } = 10;
    }

    public class GoshipRateResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public List<GoshipRateOption>? Data { get; set; }
    }

    public class GoshipRateOption
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty; // Base64 Rate ID

        [JsonPropertyName("carrier_name")]
        public string CarrierName { get; set; } = string.Empty;

        [JsonPropertyName("carrier_logo")]
        public string CarrierLogo { get; set; } = string.Empty;

        [JsonPropertyName("service_name")]
        public string ServiceName { get; set; } = string.Empty;

        [JsonPropertyName("total_fee")]
        public decimal TotalFee { get; set; }

        [JsonPropertyName("total_amount")]
        public decimal TotalAmount { get; set; }

        [JsonPropertyName("expected")]
        public string Expected { get; set; } = string.Empty;
    }

    public class GoshipOrderRequest
    {
        [JsonPropertyName("shipment")]
        public GoshipOrderShipment Shipment { get; set; } = new GoshipOrderShipment();

        /// <summary>
        /// Đơn ký gửi/mua hộ trong hệ thống. Có id này thì BE tự điền tên, số điện thoại và
        /// địa chỉ người nhận từ đơn, đồng thời lưu vận đơn nội địa để về sau đồng bộ được
        /// trạng thái. Bỏ trống thì chỉ gọi sang GoShip rồi thôi, không lưu gì.
        /// </summary>
        [JsonPropertyName("orderId")]
        public Guid? OrderId { get; set; }

        /// <summary>
        /// Các kiện đem giao. Bỏ trống thì lấy toàn bộ kiện của đơn.
        /// </summary>
        [JsonPropertyName("parcelIds")]
        public List<Guid>? ParcelIds { get; set; }
    }

    public class GoshipOrderShipment
    {
        [JsonPropertyName("rate")]
        public string Rate { get; set; } = string.Empty;

        /// <summary>
        /// Mã đơn của mình gửi sang GoShip. GoShip cho tra ngược vận đơn bằng chính giá trị này
        /// (<c>GET /shipments/search?code=</c>) nên đây là sợi dây nối hai hệ thống.
        /// </summary>
        [JsonPropertyName("order_id")]
        public string? OrderId { get; set; }

        [JsonPropertyName("address_from")]
        public GoshipAddressFull AddressFrom { get; set; } = new GoshipAddressFull();

        [JsonPropertyName("address_to")]
        public GoshipAddressFull AddressTo { get; set; } = new GoshipAddressFull();

        [JsonPropertyName("parcel")]
        public GoshipParcelFull Parcel { get; set; } = new GoshipParcelFull();
    }

    public class GoshipAddressFull
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("phone")]
        public string Phone { get; set; } = string.Empty;

        [JsonPropertyName("street")]
        public string Street { get; set; } = string.Empty;

        [JsonPropertyName("city")]
        public string City { get; set; } = string.Empty;

        [JsonPropertyName("district")]
        public string District { get; set; } = string.Empty;

        [JsonPropertyName("ward")]
        public string Ward { get; set; } = string.Empty;
    }

    public class GoshipParcelFull : GoshipParcelBasic
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("metadata")]
        public string Metadata { get; set; } = string.Empty;
    }

    public class GoshipOrderResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public GoshipOrderResponseData? Data { get; set; }
    }

    public class GoshipOrderResponseData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("carrier_name")]
        public string CarrierName { get; set; } = string.Empty;

        [JsonPropertyName("fee")]
        public decimal Fee { get; set; }
    }

    /// <summary>
    /// Kết quả một lần kho bấm đồng bộ trạng thái với GoShip.
    /// </summary>
    public class GoshipSyncResultDto
    {
        public string TrackingCode { get; set; } = string.Empty;

        /// <summary>Mã trạng thái GoShip trả về (900–919, 1000).</summary>
        public int StatusCode { get; set; }

        /// <summary>Nguyên văn mô tả trạng thái của GoShip.</summary>
        public string StatusText { get; set; } = string.Empty;

        /// <summary>Trạng thái kiện hàng trong hệ thống sau khi đồng bộ.</summary>
        public string PackageStatus { get; set; } = string.Empty;

        /// <summary>Trạng thái đơn hàng sau khi đồng bộ, null nếu đơn không đổi.</summary>
        public string? OrderStatus { get; set; }

        public int UpdatedParcels { get; set; }

        public DateTime SyncedAt { get; set; }
    }

    public class GoshipWebhookPayload
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("status_text")]
        public string StatusText { get; set; } = string.Empty;

        [JsonPropertyName("weight")]
        public int Weight { get; set; }

        [JsonPropertyName("fee")]
        public decimal Fee { get; set; }
    }
}
