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

            var result = JsonSerializer.Deserialize<GoshipOrderResponse>(responseContent);
            if (result == null || result.Data == null)
            {
                _logger.LogError("GoShip Order Creation failed: {Message}", result?.Message ?? "Unknown Error");
                throw new InvalidOperationException($"GoShip Create Order Failed: {result?.Message}");
            }

            _logger.LogInformation("GoShip Order Created Successfully: {Code}", result.Data.Code);
            return result.Data;
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
                        parcel.PackageStatus = (payload.StatusText ?? "UPDATED").ToUpper();
                        parcelRepo.Update(parcel);
                        await _unitOfWork.SaveChangesAsync();
                        _logger.LogInformation("Updated Parcel status to {Status} via GoShip Webhook for TrackingCode: {Code}", payload.StatusText, payload.Code);
                        return true;
                    }
                }
                return true;
            }

            return false;
        }
    }
}
