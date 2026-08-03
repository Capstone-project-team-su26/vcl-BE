using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VCL_BLL.DTOs;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class GoshipService : IGoshipService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoshipService> _logger;
        private readonly IUnitOfWork _unitOfWork;

        public GoshipService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GoshipService> logger,
            IUnitOfWork unitOfWork)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _unitOfWork = unitOfWork;
        }

        public async Task<List<GoshipRateOption>> GetRatesAsync(GoshipRateRequest request, string token)
        {
            var baseUrl = _configuration["GoshipSettings:ApiUrl"] ?? "https://api.goship.io/api/v2/";
            var endpoint = $"{baseUrl.TrimEnd('/')}/rates";

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpRequest.Content = JsonContent.Create(request);

            _logger.LogInformation("Sending Get Rates request to GoShip API: {Endpoint}", endpoint);

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GoShip Rates API error {StatusCode}: {Content}", response.StatusCode, responseContent);
                throw new InvalidOperationException($"GoShip Rates Error: {responseContent}");
            }

            var result = JsonSerializer.Deserialize<GoshipRateResponse>(responseContent);
            if (result == null || result.Data == null)
            {
                throw new InvalidOperationException("GoShip Rates API returned invalid response.");
            }

            return result.Data;
        }

        public async Task<GoshipOrderResponseData> CreateOrderAsync(GoshipOrderRequest request, string token)
        {
            var baseUrl = _configuration["GoshipSettings:ApiUrl"] ?? "https://api.goship.io/api/v2/";
            var endpoint = $"{baseUrl.TrimEnd('/')}/shipments";

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpRequest.Content = JsonContent.Create(request);

            _logger.LogInformation("Sending Create Shipment request to GoShip API: {Endpoint}", endpoint);

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GoShip Create Order API error {StatusCode}: {Content}", response.StatusCode, responseContent);
                throw new InvalidOperationException($"GoShip Create Order Error: {responseContent}");
            }

            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            var orderData = new GoshipOrderResponseData();

            // 1. Try to read from "data" property if it's a JSON Object
            if (root.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Object)
            {
                if (dataElem.TryGetProperty("id", out var idElem)) orderData.Id = idElem.GetString() ?? string.Empty;
                if (dataElem.TryGetProperty("code", out var codeElem)) orderData.Code = codeElem.GetString() ?? string.Empty;
                if (dataElem.TryGetProperty("carrier_name", out var cnElem)) orderData.CarrierName = cnElem.GetString() ?? string.Empty;
                else if (dataElem.TryGetProperty("carrier", out var cElem)) orderData.CarrierName = cElem.GetString() ?? string.Empty;
                if (dataElem.TryGetProperty("fee", out var feeElem) && feeElem.ValueKind == JsonValueKind.Number) orderData.Fee = feeElem.GetDecimal();
            }
            else
            {
                // 2. Fallback to reading directly from root object (Goship v2 shipments response format)
                if (root.TryGetProperty("id", out var idElem)) orderData.Id = idElem.GetString() ?? string.Empty;
                if (root.TryGetProperty("code", out var codeElem)) orderData.Code = codeElem.GetString() ?? string.Empty;
                else orderData.Code = orderData.Id;

                if (root.TryGetProperty("carrier_name", out var cnElem)) orderData.CarrierName = cnElem.GetString() ?? string.Empty;
                else if (root.TryGetProperty("carrier", out var cElem)) orderData.CarrierName = cElem.GetString() ?? string.Empty;

                if (root.TryGetProperty("fee", out var feeElem) && feeElem.ValueKind == JsonValueKind.Number) orderData.Fee = feeElem.GetDecimal();
            }

            if (string.IsNullOrEmpty(orderData.Id) && string.IsNullOrEmpty(orderData.Code))
            {
                var msg = root.TryGetProperty("message", out var mElem) ? mElem.GetString() : "Unknown Error";
                _logger.LogError("GoShip Order Creation failed: {Message}", msg);
                throw new InvalidOperationException($"GoShip Create Order Failed: {msg}");
            }

            _logger.LogInformation("GoShip Order Created Successfully: {Code} (ID: {Id})", orderData.Code, orderData.Id);
            return orderData;
        }

        public async Task<bool> ProcessWebhookAsync(GoshipWebhookPayload payload)
        {
            _logger.LogInformation("Received GoShip Webhook update - TrackingCode: {Code}, Status: {StatusText} ({Status})",
                payload.Code, payload.StatusText, payload.Status);

            if (string.IsNullOrEmpty(payload.Code))
            {
                return false;
            }

            var waybillRepo = _unitOfWork.Repository<DomesticWaybill>();
            var waybill = await waybillRepo.FirstOrDefaultAsync(w => w.TrackingCode == payload.Code);

            if (waybill != null)
            {
                if (waybill.ParcelId.HasValue)
                {
                    var parcelRepo = _unitOfWork.Repository<Parcel>();
                    var parcel = await parcelRepo.GetByIdAsync(waybill.ParcelId.Value);
                    if (parcel != null)
                    {
                        var isDelivered = payload.Status == 901 || (payload.StatusText != null && payload.StatusText.Contains("thành công", StringComparison.OrdinalIgnoreCase));
                        parcel.PackageStatus = isDelivered ? "DELIVERED" : (payload.StatusText ?? "UPDATED").ToUpper();
                        parcelRepo.Update(parcel);

                        // If parcel has parent Order (ConsignmentId), update Order Status to COMPLETED if all parcels are delivered
                        if (parcel.ConsignmentId.HasValue)
                        {
                            var orderRepo = _unitOfWork.Repository<Order>();
                            var order = await orderRepo.GetByIdAsync(parcel.ConsignmentId.Value);
                            if (order != null)
                            {
                                var allParcels = await parcelRepo.GetAllAsync();
                                var orderParcels = allParcels.Where(p => p.ConsignmentId == order.Id);
                                if (orderParcels.Any() && orderParcels.All(p => p.PackageStatus == "DELIVERED"))
                                {
                                    order.Status = "COMPLETED";
                                    orderRepo.Update(order);
                                    _logger.LogInformation("All parcels delivered. Updated Order {OrderId} status to COMPLETED via GoShip Webhook.", order.Id);
                                }
                            }
                        }

                        await _unitOfWork.SaveChangesAsync();
                        _logger.LogInformation("Updated Parcel status to {Status} via GoShip Webhook for TrackingCode: {Code}", parcel.PackageStatus, payload.Code);
                        return true;
                    }
                }
                return true;
            }

            return false;
        }
    }
}
