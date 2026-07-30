using System;
using System.Net.Http;
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
    public class GHNService : IGHNService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GHNService> _logger;
        private readonly IUnitOfWork _unitOfWork;

        public GHNService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GHNService> logger,
            IUnitOfWork unitOfWork)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _unitOfWork = unitOfWork;
        }

        public async Task<GHNOrderResponseData> CreateOrderAsync(GHNOrderRequest request, string token, int shopId)
        {
            var baseUrl = _configuration["GHNSettings:ApiUrl"] ?? "https://dev-online-gateway.ghn.vn/shiip/public-api/v2/";
            var endpoint = $"{baseUrl.TrimEnd('/')}/shipping-order/create";

            // Enforce max insurance value rule (5,000,000 VNĐ)
            if (request.InsuranceValue > 5000000)
            {
                _logger.LogInformation("Insurance value {Value} exceeds max limit of 5,000,000 VNĐ. Capping to 5,000,000 VNĐ.", request.InsuranceValue);
                request.InsuranceValue = 5000000;
            }

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("Token", token);
            httpRequest.Headers.Add("ShopId", shopId.ToString());
            httpRequest.Content = JsonContent.Create(request);

            _logger.LogInformation("Sending Create Order request to GHN API: {Endpoint} for ShopId: {ShopId}", endpoint, shopId);

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GHN API returned error {StatusCode}: {Content}", response.StatusCode, responseContent);
                throw new InvalidOperationException($"GHN API Error: {responseContent}");
            }

            var result = JsonSerializer.Deserialize<GHNOrderResponse>(responseContent);
            if (result == null || result.Code != 200 || result.Data == null)
            {
                _logger.LogError("GHN Order Creation failed: {Message}", result?.Message ?? "Unknown Error");
                throw new InvalidOperationException($"GHN Create Order Failed: {result?.Message}");
            }

            _logger.LogInformation("GHN Order Created Successfully: {OrderCode}", result.Data.OrderCode);
            return result.Data;
        }

        public async Task<GHNFeeResponseData> CalculateFeeAsync(GHNCalculateFeeRequest request, string token, int shopId)
        {
            var baseUrl = _configuration["GHNSettings:ApiUrl"] ?? "https://dev-online-gateway.ghn.vn/shiip/public-api/v2/";
            var endpoint = $"{baseUrl.TrimEnd('/')}/shipping-order/fee";

            if (request.InsuranceValue > 5000000)
            {
                request.InsuranceValue = 5000000;
            }

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("Token", token);
            httpRequest.Headers.Add("ShopId", shopId.ToString());
            httpRequest.Content = JsonContent.Create(request);

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GHN Calculate Fee API returned error {StatusCode}: {Content}", response.StatusCode, responseContent);
                throw new InvalidOperationException($"GHN Calculate Fee API Error: {responseContent}");
            }

            var result = JsonSerializer.Deserialize<GHNFeeResponse>(responseContent);
            if (result == null || result.Code != 200 || result.Data == null)
            {
                throw new InvalidOperationException($"GHN Calculate Fee Failed: {result?.Message}");
            }

            return result.Data;
        }

        public async Task<GHNOrderDetailData> GetOrderDetailAsync(string orderCode, string token)
        {
            var baseUrl = _configuration["GHNSettings:ApiUrl"] ?? "https://online-gateway.ghn.vn/shiip/public-api/v2/";
            var endpoint = $"{baseUrl.TrimEnd('/')}/shipping-order/detail";

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("Token", token);
            httpRequest.Content = JsonContent.Create(new { order_code = orderCode });

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GHN Get Order Detail API returned error {StatusCode}: {Content}", response.StatusCode, responseContent);
                throw new InvalidOperationException($"GHN Get Order Detail Error: {responseContent}");
            }

            var result = JsonSerializer.Deserialize<GHNOrderDetailResponse>(responseContent);
            if (result == null || result.Code != 200 || result.Data == null)
            {
                throw new InvalidOperationException($"GHN Get Order Detail Failed: {result?.Message}");
            }

            return result.Data;
        }

        public async Task<bool> RegisterWebhookUrlAsync(string webhookUrl, string token)
        {
            var baseUrl = _configuration["GHNSettings:ApiUrl"] ?? "https://online-gateway.ghn.vn/shiip/public-api/v2/";
            var endpoint = $"{baseUrl.TrimEnd('/')}/customer/update-webhook";

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("Token", token);
            httpRequest.Content = JsonContent.Create(new { webhook_url = webhookUrl });

            _logger.LogInformation("Sending Register Webhook URL request to GHN API: {WebhookUrl}", webhookUrl);

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("GHN Register Webhook API Response: {StatusCode} - {Content}", response.StatusCode, responseContent);

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ProcessWebhookAsync(GHNWebhookPayload payload)
        {
            _logger.LogInformation("Received GHN Webhook update for OrderCode: {OrderCode}, Status: {Status}", payload.OrderCode, payload.Status);

            if (string.IsNullOrEmpty(payload.OrderCode))
            {
                return false;
            }

            var waybillRepo = _unitOfWork.Repository<DomesticWaybill>();
            var waybill = await waybillRepo.FirstOrDefaultAsync(w => w.TrackingCode == payload.OrderCode);

            if (waybill != null)
            {
                if (waybill.ParcelId.HasValue)
                {
                    var parcelRepo = _unitOfWork.Repository<Parcel>();
                    var parcel = await parcelRepo.GetByIdAsync(waybill.ParcelId.Value);
                    if (parcel != null)
                    {
                        parcel.PackageStatus = payload.Status.ToUpper();
                        parcelRepo.Update(parcel);
                        await _unitOfWork.SaveChangesAsync();
                        _logger.LogInformation("Updated Parcel status to {Status} via GHN Webhook for TrackingCode: {OrderCode}", payload.Status, payload.OrderCode);
                        return true;
                    }
                }
                return true;
            }

            return false;
        }
    }
}
