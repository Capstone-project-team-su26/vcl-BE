using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VCL_BLL.DTOs
{
    public class GHNOrderRequest
    {
        [JsonPropertyName("payment_type_id")]
        public int PaymentTypeId { get; set; } = 1; // 1: Sender pays, 2: Recipient pays (COD)

        [JsonPropertyName("note")]
        public string Note { get; set; } = string.Empty;

        [JsonPropertyName("required_note")]
        public string RequiredNote { get; set; } = "KHONGCHOXEMHANG"; // KHONGCHOXEMHANG, CHOXEMHANGKHONGTHU, CHOTHUHANG

        [JsonPropertyName("return_phone")]
        public string ReturnPhone { get; set; } = string.Empty;

        [JsonPropertyName("return_address")]
        public string ReturnAddress { get; set; } = string.Empty;

        [JsonPropertyName("to_name")]
        public string ToName { get; set; } = string.Empty;

        [JsonPropertyName("to_phone")]
        public string ToPhone { get; set; } = string.Empty;

        [JsonPropertyName("to_address")]
        public string ToAddress { get; set; } = string.Empty;

        [JsonPropertyName("to_ward_code")]
        public string ToWardCode { get; set; } = string.Empty;

        [JsonPropertyName("to_district_id")]
        public int ToDistrictId { get; set; }

        [JsonPropertyName("cod_amount")]
        public int CodAmount { get; set; } = 0;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("weight")]
        public int Weight { get; set; } // in grams

        [JsonPropertyName("length")]
        public int Length { get; set; } // in cm

        [JsonPropertyName("width")]
        public int Width { get; set; } // in cm

        [JsonPropertyName("height")]
        public int Height { get; set; } // in cm

        [JsonPropertyName("insurance_value")]
        public int InsuranceValue { get; set; } = 0; // Max 5,000,000 VNĐ

        [JsonPropertyName("service_type_id")]
        public int ServiceTypeId { get; set; } = 2; // 2: Standard express

        [JsonPropertyName("items")]
        public List<GHNItem> Items { get; set; } = new List<GHNItem>();
    }

    public class GHNItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; } = 1;

        [JsonPropertyName("price")]
        public int Price { get; set; } = 0;

        [JsonPropertyName("weight")]
        public int Weight { get; set; } = 0;
    }

    public class GHNOrderResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public GHNOrderResponseData? Data { get; set; }
    }

    public class GHNOrderResponseData
    {
        [JsonPropertyName("order_code")]
        public string OrderCode { get; set; } = string.Empty;

        [JsonPropertyName("sort_code")]
        public string SortCode { get; set; } = string.Empty;

        [JsonPropertyName("trans_type")]
        public string TransType { get; set; } = string.Empty;

        [JsonPropertyName("total_fee")]
        public int TotalFee { get; set; }

        [JsonPropertyName("expected_delivery_time")]
        public string ExpectedDeliveryTime { get; set; } = string.Empty;
    }

    public class GHNCalculateFeeRequest
    {
        [JsonPropertyName("from_district_id")]
        public int FromDistrictId { get; set; }

        [JsonPropertyName("to_district_id")]
        public int ToDistrictId { get; set; }

        [JsonPropertyName("to_ward_code")]
        public string ToWardCode { get; set; } = string.Empty;

        [JsonPropertyName("service_type_id")]
        public int ServiceTypeId { get; set; } = 2;

        [JsonPropertyName("weight")]
        public int Weight { get; set; }

        [JsonPropertyName("length")]
        public int Length { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("insurance_value")]
        public int InsuranceValue { get; set; } = 0;
    }

    public class GHNFeeResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public GHNFeeResponseData? Data { get; set; }
    }

    public class GHNFeeResponseData
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("service_fee")]
        public int ServiceFee { get; set; }

        [JsonPropertyName("insurance_fee")]
        public int InsuranceFee { get; set; }
    }

    public class GHNWebhookPayload
    {
        [JsonPropertyName("OrderCode")]
        public string OrderCode { get; set; } = string.Empty;

        [JsonPropertyName("ClientOrderCode")]
        public string ClientOrderCode { get; set; } = string.Empty;

        [JsonPropertyName("Status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("Type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("Reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("ReasonCode")]
        public string ReasonCode { get; set; } = string.Empty;

        [JsonPropertyName("ShopID")]
        public int ShopID { get; set; }

        [JsonPropertyName("CODAmount")]
        public decimal CODAmount { get; set; }

        [JsonPropertyName("CODTransferDate")]
        public string? CODTransferDate { get; set; }

        [JsonPropertyName("ConvertedWeight")]
        public decimal ConvertedWeight { get; set; }

        [JsonPropertyName("Weight")]
        public decimal Weight { get; set; }

        [JsonPropertyName("Length")]
        public decimal Length { get; set; }

        [JsonPropertyName("Width")]
        public decimal Width { get; set; }

        [JsonPropertyName("Height")]
        public decimal Height { get; set; }

        [JsonPropertyName("TotalFee")]
        public decimal TotalFee { get; set; }

        [JsonPropertyName("PaymentType")]
        public int PaymentType { get; set; }

        [JsonPropertyName("IsPartialReturn")]
        public bool IsPartialReturn { get; set; }

        [JsonPropertyName("PartialReturnCode")]
        public string PartialReturnCode { get; set; } = string.Empty;

        [JsonPropertyName("Warehouse")]
        public string Warehouse { get; set; } = string.Empty;

        [JsonPropertyName("Time")]
        public string Time { get; set; } = string.Empty;

        [JsonPropertyName("Fee")]
        public GHNWebhookFeeDetail? Fee { get; set; }
    }

    public class GHNWebhookFeeDetail
    {
        [JsonPropertyName("CODFailedFee")]
        public decimal CODFailedFee { get; set; }

        [JsonPropertyName("CODFee")]
        public decimal CODFee { get; set; }

        [JsonPropertyName("Coupon")]
        public decimal Coupon { get; set; }

        [JsonPropertyName("DeliverRemoteAreasFee")]
        public decimal DeliverRemoteAreasFee { get; set; }

        [JsonPropertyName("DocumentReturn")]
        public decimal DocumentReturn { get; set; }

        [JsonPropertyName("DoubleCheck")]
        public decimal DoubleCheck { get; set; }

        [JsonPropertyName("Insurance")]
        public decimal Insurance { get; set; }

        [JsonPropertyName("MainService")]
        public decimal MainService { get; set; }

        [JsonPropertyName("PickRemoteAreasFee")]
        public decimal PickRemoteAreasFee { get; set; }

        [JsonPropertyName("R2S")]
        public decimal R2S { get; set; }

        [JsonPropertyName("Return")]
        public decimal Return { get; set; }

        [JsonPropertyName("StationDO")]
        public decimal StationDO { get; set; }

        [JsonPropertyName("StationPU")]
        public decimal StationPU { get; set; }

        [JsonPropertyName("Total")]
        public decimal Total { get; set; }
    }

    public class GHNOrderDetailResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public GHNOrderDetailData? Data { get; set; }
    }

    public class GHNOrderDetailData
    {
        [JsonPropertyName("order_code")]
        public string OrderCode { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("to_name")]
        public string ToName { get; set; } = string.Empty;

        [JsonPropertyName("to_phone")]
        public string ToPhone { get; set; } = string.Empty;

        [JsonPropertyName("to_address")]
        public string ToAddress { get; set; } = string.Empty;

        [JsonPropertyName("weight")]
        public int Weight { get; set; }

        [JsonPropertyName("converted_weight")]
        public int ConvertedWeight { get; set; }

        [JsonPropertyName("cod_amount")]
        public int CodAmount { get; set; }

        [JsonPropertyName("leadtime")]
        public DateTime? LeadTime { get; set; }

        [JsonPropertyName("created_date")]
        public string CreatedDate { get; set; } = string.Empty;
    }
}
