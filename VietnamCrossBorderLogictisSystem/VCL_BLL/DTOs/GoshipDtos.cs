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
    }

    public class GoshipOrderShipment
    {
        [JsonPropertyName("rate")]
        public string Rate { get; set; } = string.Empty;

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
