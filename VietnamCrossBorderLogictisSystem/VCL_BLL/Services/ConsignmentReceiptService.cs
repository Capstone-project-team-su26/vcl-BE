using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Sinh phiếu xác nhận ký gửi (PDF) từ dữ liệu đơn, báo giá và thanh toán.
    /// Dùng QuestPDF để chữ ở dạng vector: kho zoom/tìm kiếm được, khác với cách chụp DOM thành ảnh ở frontend.
    /// </summary>
    public class ConsignmentReceiptService : IConsignmentReceiptService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IFileUploadService _fileUploadService;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Font phải phủ được tiếng Việt. Ảnh runtime aspnet không kèm font nào,
        /// nên Dockerfile cài fonts-liberation; máy dev Windows rơi về Arial.
        /// </summary>
        private static readonly string[] FontCandidates = { "Liberation Sans", "Arial", "DejaVu Sans" };

        private const string Ink = "#0f172a";
        private const string Muted = "#475569";
        private const string Line = "#cbd5e1";
        private const string Head = "#1e293b";
        private const string Soft = "#f1f5f9";
        private const decimal DefaultVolumetricDivisor = 5000m;
        private const int MaxImagesPerItem = 2;
        private const int ImageDownloadTimeoutSeconds = 10;

        private static readonly CultureInfo Vi = new("vi-VN");

        // Phiếu là chứng từ cho người đọc, không in mã hệ thống.
        // Giữ đồng bộ với map nhãn của web nội bộ trong WarehouseReceiptCreate.jsx.
        private static readonly Dictionary<string, string> ConsignmentTypeLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["STANDARD"] = "Tiêu chuẩn",
            ["EXPRESS"] = "Hỏa tốc",
            ["ECONOMY"] = "Tiết kiệm"
        };

        private static readonly Dictionary<string, string> OrderStatusLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PENDING_REVIEW"] = "Chờ duyệt",
            ["QUOTATION_SENT"] = "Đã gửi báo giá",
            ["QUOTATION_CONFIRMED"] = "Đã chốt báo giá",
            ["QUOTATION_REJECTED"] = "Báo giá bị từ chối",
            ["WAITING_DEPOSIT"] = "Chờ đặt cọc",
            ["DEPOSIT_PAID"] = "Đã đặt cọc",
            ["PAYMENT_CONFIRMED"] = "Đã xác nhận thanh toán",
            ["APPROVED"] = "Đã xác nhận",
            ["CHECKED_IN"] = "Đã nhập kho",
            ["WAREHOUSE_RECEIVED"] = "Đã lưu kho",
            ["PROCESSING"] = "Đang xử lý",
            ["IN_TRANSIT"] = "Đang vận chuyển",
            ["COMPLETED"] = "Hoàn thành",
            ["CANCELLED"] = "Đã hủy",
            ["REJECTED"] = "Đã từ chối"
        };

        private static readonly Dictionary<string, string> QuotationStatusLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PENDING"] = "Chờ xác nhận",
            ["SENT"] = "Đã gửi",
            ["ACCEPTED"] = "Đã chấp nhận",
            ["REJECTED"] = "Đã từ chối",
            ["EXPIRED"] = "Đã hết hạn"
        };

        // Phụ phí lưu RuleName do sale đặt, nhiều rule seed sẵn bằng mã tiếng Anh.
        // Phiếu là chứng từ cho khách nên đổi sang tiếng Việt; mã lạ thì giữ nguyên tên đã lưu.
        private static readonly Dictionary<string, string> SurchargeLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["WOOD_CRATE"] = "Phí đóng thùng gỗ",
            ["WOODEN_BOX"] = "Phí đóng thùng gỗ",
            ["INSPECTION"] = "Phí kiểm hàng",
            ["PACKING"] = "Phí đóng gói",
            ["INSURANCE"] = "Phí bảo hiểm hàng hóa",
            ["FRAGILE"] = "Phụ phí hàng dễ vỡ",
            ["OVERWEIGHT"] = "Phụ phí quá tải trọng",
            ["OVERSIZE"] = "Phụ phí quá khổ",
            ["REMOTE_AREA"] = "Phụ phí vùng xa",
            ["OTHER"] = "Phụ phí khác"
        };

        private static readonly Dictionary<string, string> PaymentMethodLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SEPAY"] = "Chuyển khoản SePay",
            ["PAYOS"] = "Chuyển khoản payOS",
            ["BANK_TRANSFER"] = "Chuyển khoản ngân hàng",
            ["CASH"] = "Tiền mặt",
            ["VNPAY"] = "VNPay",
            ["MOMO"] = "MoMo"
        };

        private static readonly Dictionary<string, string> PaymentStatusLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PAID"] = "Đã thanh toán",
            ["SUCCESS"] = "Thanh toán thành công",
            ["COMPLETED"] = "Hoàn thành",
            ["PENDING"] = "Chờ thanh toán",
            ["PROCESSING"] = "Đang xử lý",
            ["FAILED"] = "Thanh toán thất bại",
            ["CANCELLED"] = "Đã hủy"
        };

        /// <summary>Đổi mã hệ thống sang nhãn tiếng Việt; mã lạ thì in nguyên để không mất thông tin.</summary>
        private static string? MapLabel(Dictionary<string, string> labels, string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            return labels.TryGetValue(code.Trim(), out var label) ? label : code;
        }

        public ConsignmentReceiptService(
            IUnitOfWork unitOfWork,
            IFileUploadService fileUploadService,
            IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _fileUploadService = fileUploadService;
            _configuration = configuration;

            QuestPDF.Settings.License = LicenseType.Community;
        }

        /// <summary>
        /// Phiếu được phục vụ từ chính API này thay vì lưu ở dịch vụ ngoài: link luôn khớp
        /// dữ liệu mới nhất, không phụ thuộc quota hay chính sách chặn file của bên thứ ba,
        /// và không đổi URL mỗi lần sinh lại.
        /// </summary>
        public async Task<string?> GenerateAndStoreReceiptAsync(Guid orderId, Guid? generatedByUserId)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID {orderId}");

            // Sinh thử để lỗi dữ liệu lộ ra ngay lúc này, thay vì lúc kho bấm mở link.
            await BuildReceiptPdfAsync(orderId, generatedByUserId);

            order.ReceiptPublicKey ??= Guid.NewGuid();
            order.ReceiptPdfUrl = BuildPublicReceiptUrl(order.ReceiptPublicKey.Value);
            order.ReceiptGeneratedAt = DateTime.UtcNow;
            order.ReceiptGeneratedById = generatedByUserId;

            orderRepo.Update(order);
            await _unitOfWork.SaveChangesAsync();

            return order.ReceiptPdfUrl;
        }

        public async Task<(byte[] Content, string FileName)> GetReceiptByPublicKeyAsync(Guid publicKey)
        {
            var order = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .FirstOrDefault(o => o.ReceiptPublicKey == publicKey)
                ?? throw new NotFoundException("Không tìm thấy phiếu xác nhận ký gửi.");

            return await BuildReceiptPdfAsync(order.Id, order.ReceiptGeneratedById);
        }

        private string BuildPublicReceiptUrl(Guid publicKey)
        {
            var baseUrl = (_configuration["App:PublicBaseUrl"] ?? string.Empty).TrimEnd('/');

            return $"{baseUrl}/api/public/receipts/{publicKey}";
        }

        public async Task<(byte[] Content, string FileName)> BuildReceiptPdfAsync(Guid orderId, Guid? generatedByUserId)
        {
            var data = await LoadReceiptDataAsync(orderId, generatedByUserId);
            var content = BuildDocument(data).GeneratePdf();

            return (content, BuildFileName(data));
        }

        private async Task<ReceiptData> LoadReceiptDataAsync(Guid orderId, Guid? generatedByUserId)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID {orderId}");

            var customer = order.CustomerId.HasValue
                ? (await _unitOfWork.Repository<Customer>().GetAllAsync()).FirstOrDefault(c => c.Id == order.CustomerId)
                : null;

            var items = (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                .Where(i => i.OrderId == orderId)
                .ToList();

            var quotation = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == orderId)
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            Warehouse? warehouse = null;
            if (quotation?.WarehouseId != null)
            {
                warehouse = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                    .FirstOrDefault(w => w.Id == quotation.WarehouseId.Value);
            }

            var fees = quotation == null
                ? new List<QuotationFee>()
                : (await _unitOfWork.Repository<QuotationFee>().GetAllAsync())
                    .Where(f => f.QuotationId == quotation.Id)
                    .ToList();

            var domesticFee = fees
                .Where(f => string.Equals(f.FeeType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase))
                .Sum(f => f.Amount);

            // Chỉ in phụ phí thực sự phát sinh; rule được lưu cả khi Amount = 0 để giữ lịch sử.
            var surcharges = fees
                .Where(f => string.Equals(f.FeeType, "SURCHARGE", StringComparison.OrdinalIgnoreCase) && f.Amount > 0)
                .OrderByDescending(f => f.Amount)
                .ToList();

            Payment? payment = null;
            var paidAmount = 0m;
            if (quotation != null)
            {
                var invoiceIds = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                    .Where(i => i.QuotationId == quotation.Id)
                    .Select(i => i.Id)
                    .ToHashSet();

                var payments = (await _unitOfWork.Repository<Payment>().GetAllAsync())
                    .Where(p => p.InvoiceId.HasValue && invoiceIds.Contains(p.InvoiceId.Value))
                    .OrderByDescending(p => p.PaidAt ?? p.CreatedAt)
                    .ToList();

                payment = payments.FirstOrDefault();

                // Đơn trả làm hai đợt (đặt cọc rồi tất toán) nên phải cộng dồn,
                // lấy mỗi giao dịch gần nhất sẽ báo thiếu đúng bằng phần đã cọc.
                paidAmount = payments
                    .Where(p => IsSettledPayment(p.PaymentStatus))
                    .Sum(p => p.Amount ?? 0m);
            }

            User? preparedBy = null;
            if (generatedByUserId.HasValue)
            {
                preparedBy = (await _unitOfWork.Repository<User>().GetAllAsync())
                    .FirstOrDefault(u => u.Id == generatedByUserId.Value);
            }

            var packageConfigurations = (await _unitOfWork.Repository<PackageConfiguration>().GetAllAsync())
                .ToDictionary(p => p.Id, p => p);

            // Kiện đã check-in: dùng để in số thực nhận và tên nhân viên kho lên phiếu.
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId == orderId
                            && !string.Equals(p.PackageStatus, "SPLIT", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var parcelByItemId = parcels
                .Where(p => p.OrderItemId.HasValue)
                .GroupBy(p => p.OrderItemId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            ServicePricing? servicePricing = null;
            if (quotation?.ServicePricingId != null)
            {
                servicePricing = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync())
                    .FirstOrDefault(p => p.Id == quotation.ServicePricingId.Value);
            }

            User? warehouseStaff = null;
            var checkedInById = parcels
                .Where(p => p.CheckedInById.HasValue)
                .OrderByDescending(p => p.CheckInTime)
                .Select(p => p.CheckedInById!.Value)
                .FirstOrDefault();

            if (checkedInById != Guid.Empty)
            {
                warehouseStaff = (await _unitOfWork.Repository<User>().GetAllAsync())
                    .FirstOrDefault(u => u.Id == checkedInById);
            }

            // OrderItem.ProductType lưu Id của PRODUCT_TYPES; dữ liệu cũ còn lưu thẳng tên nên
            // chỉ tra bảng khi giá trị là GUID, còn lại in nguyên như trước.
            var productTypeNames = (await _unitOfWork.Repository<ProductType>().GetAllAsync())
                .ToDictionary(p => p.Id, p => p.Name);

            return new ReceiptData
            {
                Surcharges = surcharges,
                ProductTypeNames = productTypeNames,
                PaidAmount = paidAmount,
                ItemImages = await DownloadItemImagesAsync(items),
                ParcelByItemId = parcelByItemId,
                ServicePricing = servicePricing,
                WarehouseStaff = warehouseStaff,
                CheckedInAt = parcels.Where(p => p.CheckInTime.HasValue).Max(p => p.CheckInTime),
                Order = order,
                Customer = customer,
                Items = items,
                Quotation = quotation,
                Warehouse = warehouse,
                Payment = payment,
                PreparedBy = preparedBy,
                DomesticFee = domesticFee,
                PackageConfigurations = packageConfigurations
            };
        }

        /// <summary>
        /// Tải ảnh sản phẩm về để nhúng vào PDF. Ảnh hỏng hoặc mạng chậm không được
        /// làm hỏng cả phiếu, nên lỗi ảnh nào thì bỏ ảnh đó.
        /// </summary>
        private static async Task<Dictionary<Guid, List<byte[]>>> DownloadItemImagesAsync(List<OrderItem> items)
        {
            var result = new Dictionary<Guid, List<byte[]>>();

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(ImageDownloadTimeoutSeconds) };

            foreach (var item in items)
            {
                var urls = SplitImageUrls(item.ReferenceUrl).Take(MaxImagesPerItem).ToList();
                if (urls.Count == 0)
                {
                    continue;
                }

                var downloaded = new List<byte[]>();
                foreach (var url in urls)
                {
                    try
                    {
                        downloaded.Add(await httpClient.GetByteArrayAsync(url));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"RECEIPT IMAGE DOWNLOAD SKIPPED ({url}): {ex.Message}");
                    }
                }

                if (downloaded.Count > 0)
                {
                    result[item.Id] = downloaded;
                }
            }

            return result;
        }

        private static IEnumerable<string> SplitImageUrls(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Enumerable.Empty<string>();
            }

            return raw
                .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        }

        private static string GetPackageConfigName(ReceiptData data, OrderItem item)
        {
            if (item.PackageConfigurationId.HasValue &&
                data.PackageConfigurations.TryGetValue(item.PackageConfigurationId.Value, out var config))
            {
                return config.ConfigName ?? config.ConfigCode ?? "—";
            }

            return "Chưa xác định";
        }

        /// <summary>
        /// Tên loại hàng để in lên phiếu. ProductType lưu Id của PRODUCT_TYPES từ khi có bảng này;
        /// đơn cũ lưu thẳng tên nên giá trị không phải GUID thì in nguyên.
        /// </summary>
        private static string GetProductTypeName(ReceiptData data, OrderItem item)
        {
            if (string.IsNullOrWhiteSpace(item.ProductType))
            {
                return "—";
            }

            var raw = item.ProductType.Trim();

            if (Guid.TryParse(raw, out var productTypeId) &&
                data.ProductTypeNames.TryGetValue(productTypeId, out var name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return raw;
        }

        /// <summary>Tên phụ phí cho khách đọc; ưu tiên nhãn tiếng Việt, không có thì dùng tên sale đã đặt.</summary>
        private static string FormatSurchargeName(QuotationFee fee)
        {
            var raw = fee.FeeName?.Trim();

            if (!string.IsNullOrWhiteSpace(raw) && SurchargeLabels.TryGetValue(raw, out var mapped))
            {
                return mapped;
            }

            return string.IsNullOrWhiteSpace(raw) ? "Phụ phí khác" : raw;
        }

        /// <summary>Giao dịch đã thực sự vào tiền; các trạng thái chờ/thất bại không được tính là đã trả.</summary>
        private static bool IsSettledPayment(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status.Trim().ToUpperInvariant() is "PAID" or "SUCCESS" or "COMPLETED";
        }

        private static decimal GetPackageFee(ReceiptData data, OrderItem item)
        {
            if (item.PackageConfigurationId.HasValue &&
                data.PackageConfigurations.TryGetValue(item.PackageConfigurationId.Value, out var config))
            {
                return config.PackageFee;
            }

            return 0m;
        }

        private static void Total(RowDescriptor row, string label, string value)
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text(label).FontSize(8.5f).FontColor(Muted);
                c.Item().Text(value).Bold().FontSize(10);
            });
        }

        private static string BuildFileName(ReceiptData data)
        {
            var code = Sanitize(data.Order.ConsignmentCode ?? data.Order.Id.ToString());
            var preparer = Sanitize(RemoveDiacritics(data.PreparedBy?.FullName ?? "he-thong"));

            return $"PHIEU-KYGUI-NHAPKHO-{code}-{preparer}.pdf";
        }

        private Document BuildDocument(ReceiptData data)
        {
            var order = data.Order;
            var quotation = data.Quotation;

            var declaredPackageCount = data.Items.Count;
            var totalQuantity = data.Items.Sum(i => i.Quantity ?? 0);
            var totalDeclaredValue = data.Items.Sum(i => i.DeclaredValue ?? 0m);
            var totalWeight = order.TotalWeight ?? data.Items.Sum(i => i.Weight ?? 0m);
            var totalVolumetric = data.Items.Sum(GetVolumetricWeight);

            var freight = quotation?.EstimatedFreightCharge ?? 0m;
            var serviceFee = Math.Max(0m, (quotation?.ServiceFee ?? 0m) - data.DomesticFee);
            var tax = quotation?.TaxAndDuty ?? 0m;
            var total = freight + (quotation?.ServiceFee ?? 0m) + tax;

            var paidAmount = data.PaidAmount;
            var remaining = Math.Max(0m, total - paidAmount);

            return Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(11, Unit.Millimetre);
                    page.DefaultTextStyle(t => t.FontFamily(FontCandidates).FontSize(10).FontColor(Ink).LineHeight(1.3f));

                    // Header nằm trong Content để chỉ in ở trang đầu; page.Header() sẽ lặp lại mọi trang.
                    page.Content().Column(root =>
                    {
                        root.Item().Element(e => ComposeHeader(e, data));
                        root.Item().PaddingTop(9).Column(body =>
                        {
                            body.Spacing(11);

                            body.Item().Element(e => ComposeCustomer(e, data));
                            body.Item().Element(e => ComposeOrderSummary(e, data, declaredPackageCount, totalWeight));
                            body.Item().Element(e => ComposePackages(e, data, totalQuantity, totalWeight, totalVolumetric, totalDeclaredValue));
                            body.Item().Element(e => ComposeFinance(e, data, freight, serviceFee, tax, total, paidAmount, remaining));
                            body.Item().Element(e => ComposeWarehouse(e, data));

                            body.Item().PaddingTop(3)
                                .Text("Phiếu này được lập từ dữ liệu đơn ký gửi, báo giá và giao dịch thanh toán đã được hệ thống ghi nhận. Kho đối chiếu phiếu này khi nhận hàng.")
                                .FontSize(8.5f).Italic().FontColor(Muted);

                            body.Item().PaddingTop(16).ShowEntire().Element(e => ComposeSignatures(e, data));
                        });
                    });

                    page.Footer().BorderTop(0.5f).BorderColor(Line).PaddingTop(5).Row(row =>
                    {
                        row.RelativeItem().Text($"Mã ký gửi {order.ConsignmentCode}").FontSize(8.5f).FontColor(Muted);
                        row.RelativeItem().AlignCenter().Text("Chứng từ điện tử nội bộ").FontSize(8.5f).FontColor(Muted);
                        row.RelativeItem().AlignRight().Text(t =>
                        {
                            t.Span("Trang ").FontSize(8.5f).FontColor(Muted);
                            t.CurrentPageNumber().FontSize(8.5f).FontColor(Muted);
                            t.Span(" / ").FontSize(8.5f).FontColor(Muted);
                            t.TotalPages().FontSize(8.5f).FontColor(Muted);
                        });
                    });
                });
            });
        }

        private void ComposeHeader(IContainer container, ReceiptData data)
        {
            // Config để trống vẫn là chuỗi rỗng chứ không null, nên ?? không đủ để rơi về mặc định.
            var companyName = Fallback(_configuration["Company:Name"], "CÔNG TY TNHH VIETNAM LOGICTIC");
            var taxCode = Fallback(_configuration["Company:TaxCode"], "—");
            var phone = Fallback(_configuration["Company:Phone"], "—");

            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem(1.15f).Column(c =>
                    {
                        c.Item().Text(companyName).Bold().FontSize(12);
                        c.Item().Text(data.Warehouse?.Address ?? "Địa chỉ kho chưa được xác định").FontSize(9).FontColor(Muted);
                        c.Item().Text($"MST: {taxCode}").FontSize(9).FontColor(Muted);
                        c.Item().Text($"Điện thoại: {phone}").FontSize(9).FontColor(Muted);
                    });

                    row.RelativeItem(0.85f).Column(c =>
                    {
                        // Tách cố định 2 dòng; để một dòng dài thì QuestPDF tự ngắt ở chỗ xấu.
                        c.Item().AlignRight().Text("PHIẾU XÁC NHẬN").Bold().FontSize(20).FontColor(Head);
                        c.Item().AlignRight().Text("KÝ GỬI NHẬP KHO").Bold().FontSize(20).FontColor(Head);
                        c.Item().AlignRight().Text("(Chứng từ giao kho đối chiếu)").FontSize(10).FontColor(Muted);
                        c.Item().PaddingTop(5).AlignRight().Text(t =>
                        {
                            t.Span("Mã ký gửi: ").FontSize(9).FontColor(Muted);
                            t.Span(data.Order.ConsignmentCode ?? "—").Bold().FontSize(11);
                        });
                        c.Item().AlignRight().Text(t =>
                        {
                            t.Span("Ngày lập: ").FontSize(9).FontColor(Muted);
                            t.Span(FormatDate(DateTime.UtcNow)).Bold();
                        });
                    });
                });

                col.Item().PaddingTop(7).LineHorizontal(2).LineColor(Head);
            });
        }

        private void ComposeCustomer(IContainer container, ReceiptData data)
        {
            container.Column(c =>
            {
                Title(c, "I. THÔNG TIN KHÁCH HÀNG");
                c.Item().Row(row =>
                {
                    row.RelativeItem().Column(x =>
                    {
                        Field(x, "Khách hàng", data.Customer?.FullName);
                        Field(x, "Số điện thoại", data.Customer?.Phone);
                        Field(x, "Người nhận", Join(data.Order.ReceiverName, data.Order.ReceiverPhone));
                    });
                    row.ConstantItem(12);
                    row.RelativeItem().Column(x =>
                    {
                        Field(x, "Mã khách hàng", data.Customer?.CustomerCode);
                        Field(x, "Email", data.Customer?.Email);
                        Field(x, "Địa chỉ nhận hàng", data.Order.ReceiverAddress);
                    });
                });
            });
        }

        private void ComposeOrderSummary(IContainer container, ReceiptData data, int packageCount, decimal totalWeight)
        {
            var order = data.Order;

            container.Column(c =>
            {
                Title(c, "II. THÔNG TIN ĐƠN KÝ GỬI");
                c.Item().Row(row =>
                {
                    row.RelativeItem().Column(x =>
                    {
                        Field(x, "Mã ký gửi", order.ConsignmentCode);
                        Field(x, "Ngày tạo đơn", FormatDateTime(order.CreatedAt));
                        Field(x, "Loại vận chuyển", MapLabel(ConsignmentTypeLabels, order.ConsignmentType));
                        Field(x, "Trạng thái đơn", MapLabel(OrderStatusLabels, order.Status));
                    });
                    row.ConstantItem(12);
                    row.RelativeItem().Column(x =>
                    {
                        Field(x, "Tuyến hàng", order.Route);
                        Field(x, "Xác nhận thanh toán", FormatDateTime(data.PaymentConfirmedAt));
                        Field(x, "Yêu cầu kiểm hàng", order.RequiresInspection ? "Có" : "Không");
                        Field(x, "Tổng trọng lượng", $"{FormatNumber(totalWeight)} kg");
                    });
                    row.ConstantItem(12);
                    row.RelativeItem().Column(x =>
                    {
                        Field(x, "Tổng thể tích", $"{FormatInteger(order.TotalVolume ?? 0m)} cm³");
                        Field(x, "Tổng số kiện", $"{packageCount} kiện");
                        Field(x, "Kho nhập", data.Warehouse?.WarehouseName ?? "Chưa chọn kho ở báo giá");
                        Field(x, "Mã kho", data.Warehouse?.WarehouseCode);
                    });
                });

                if (!string.IsNullOrWhiteSpace(order.Note))
                {
                    c.Item().PaddingTop(5).Background(Soft).Border(0.5f).BorderColor(Line).Padding(6).Text(t =>
                    {
                        t.Span("Ghi chú dịch vụ: ").FontSize(9).FontColor(Muted);
                        t.Span(order.Note).Bold();
                    });
                }
            });
        }

        private void ComposePackages(
            IContainer container,
            ReceiptData data,
            int totalQuantity,
            decimal totalWeight,
            decimal totalVolumetric,
            decimal totalDeclaredValue)
        {
            container.Column(c =>
            {
                Title(c, $"III. DANH SÁCH KIỆN HÀNG  ·  {data.Items.Count} kiện");

                if (data.Items.Count == 0)
                {
                    c.Item().Text("Chưa có dữ liệu kiện hàng.").FontSize(9).FontColor(Muted);
                    return;
                }

                var index = 1;
                foreach (var item in data.Items)
                {
                    var number = index;
                    c.Item().PaddingBottom(7).Border(0.75f).BorderColor(Line).Column(card =>
                    {
                        card.Item().Background("#e2e8f0").Padding(6).Row(r =>
                        {
                            r.ConstantItem(58).Text($"KIỆN {number:D2}").Bold().FontSize(11);
                            r.RelativeItem().Text(item.ProductName ?? "Chưa có tên sản phẩm").Bold().FontSize(11);
                            r.ConstantItem(110).AlignRight().Text(GetProductTypeName(data, item)).FontSize(9).Bold().FontColor(Head);
                        });

                        card.Item().Padding(7).Row(r =>
                        {
                            var images = data.ItemImages.TryGetValue(item.Id, out var bytes) ? bytes : new List<byte[]>();
                            if (images.Count > 0)
                            {
                                r.ConstantItem(116).Column(img =>
                                {
                                    img.Item().PaddingBottom(3).Text("Hình ảnh").FontSize(8).FontColor(Muted);
                                    img.Item().Row(g =>
                                    {
                                        foreach (var image in images.Take(2))
                                        {
                                            g.RelativeItem().PaddingRight(4).Border(0.5f).BorderColor(Line).Padding(2).Image(image);
                                        }
                                    });
                                });
                                r.ConstantItem(10);
                            }

                            r.RelativeItem().Row(rr =>
                            {
                                rr.RelativeItem().Column(a =>
                                {
                                    Field(a, "Số lượng", $"{item.Quantity ?? 0} sản phẩm");
                                    Field(a, "Trọng lượng", $"{FormatNumber(item.Weight ?? 0m)} kg");
                                    Field(a, "Kích thước", $"{FormatNumber(item.Length ?? 0m)} × {FormatNumber(item.Width ?? 0m)} × {FormatNumber(item.Height ?? 0m)} cm");
                                    Field(a, "DIM", $"{FormatNumber(GetVolumetricWeight(item))} kg");
                                });
                                rr.ConstantItem(10);
                                rr.RelativeItem().Column(a =>
                                {
                                    Field(a, "Giá trị khai báo", FormatCurrency(item.DeclaredValue ?? 0m));
                                    Field(a, "Mã vận đơn nội địa", item.DomesticTrackingCode);
                                    Field(a, "Cấu hình đóng gói", GetPackageConfigName(data, item));
                                    Field(a, "Phí đóng gói", FormatCurrency(GetPackageFee(data, item)));
                                });
                            });
                        });

                        data.ParcelByItemId.TryGetValue(item.Id, out var receivedParcel);

                        card.Item().BorderTop(0.5f).BorderColor("#94a3b8")
                            .Background(receivedParcel != null ? "#f0fdf4" : "#fffbeb").Padding(6).Row(r =>
                        {
                            if (receivedParcel == null)
                            {
                                r.RelativeItem().Text("Thực nhận: ............ kg").FontSize(9).FontColor(Muted);
                                r.RelativeItem().Text("Kích thước thực: ...... × ...... × ...... cm").FontSize(9).FontColor(Muted);
                                r.RelativeItem().Text("Tình trạng: ......................").FontSize(9).FontColor(Muted);
                                return;
                            }

                            r.RelativeItem().Text(t =>
                            {
                                t.Span("Thực nhận: ").FontSize(9).FontColor(Muted);
                                t.Span($"{FormatNumber(receivedParcel.ActualWeight ?? 0m)} kg").Bold().FontSize(9);
                            });
                            r.RelativeItem().Text(t =>
                            {
                                t.Span("Kích thước thực: ").FontSize(9).FontColor(Muted);
                                t.Span($"{FormatNumber(receivedParcel.Length ?? 0m)} × {FormatNumber(receivedParcel.Width ?? 0m)} × {FormatNumber(receivedParcel.Height ?? 0m)} cm").Bold().FontSize(9);
                            });
                            r.RelativeItem().Text(t =>
                            {
                                t.Span("Mã kiện: ").FontSize(9).FontColor(Muted);
                                t.Span(receivedParcel.PackageCode ?? "—").Bold().FontSize(9);
                            });
                        });
                    });

                    index++;
                }

                c.Item().Background(Soft).Border(0.75f).BorderColor(Line).Padding(7).Row(r =>
                {
                    Total(r, "Tổng số kiện", $"{data.Items.Count} kiện");
                    Total(r, "Tổng sản phẩm", $"{totalQuantity}");
                    Total(r, "Tổng trọng lượng", $"{FormatNumber(totalWeight)} kg");
                    Total(r, "Tổng DIM", $"{FormatNumber(totalVolumetric)} kg");
                    Total(r, "Tổng giá trị khai báo", FormatCurrency(totalDeclaredValue));
                });
            });
        }

        private void ComposeFinance(
            IContainer container,
            ReceiptData data,
            decimal freight,
            decimal serviceFee,
            decimal tax,
            decimal total,
            decimal paidAmount,
            decimal remaining)
        {
            container.Column(c =>
            {
                Title(c, "IV. BÁO GIÁ VÀ THANH TOÁN");
                c.Item().Row(row =>
                {
                    row.RelativeItem().Border(0.75f).BorderColor(Line).Padding(7).Column(x =>
                    {
                        x.Item().PaddingBottom(4).Text("Chi tiết báo giá").Bold().FontSize(10);
                        Field(x, "Trạng thái báo giá", MapLabel(QuotationStatusLabels, data.Quotation?.Status));
                        Field(x, "Phí vận chuyển quốc tế", FormatCurrency(freight));
                        Field(x, "Phí vận chuyển nội địa", FormatCurrency(data.DomesticFee));
                        Field(x, "Phí dịch vụ", FormatCurrency(serviceFee));
                        // Phụ phí đã được cộng vào ServiceFee, nên in thụt vào dưới dạng diễn giải
                        // thay vì dòng phí riêng — tránh người đọc cộng thêm lần nữa.
                        foreach (var surcharge in data.Surcharges)
                        {
                            x.Item().PaddingLeft(10).PaddingBottom(1).Text(t =>
                            {
                                t.Span($"• {FormatSurchargeName(surcharge)}: ").FontSize(8.5f).FontColor(Muted);
                                t.Span(FormatCurrency(surcharge.Amount)).FontSize(8.5f).FontColor(Muted);
                            });
                        }

                        Field(x, "Thuế và phí nhập khẩu", FormatCurrency(tax));

                        x.Item().PaddingTop(4).BorderTop(0.5f).BorderColor(Line).PaddingTop(4).Text(t =>
                        {
                            t.Span("TỔNG BÁO GIÁ: ").Bold().FontSize(10);
                            t.Span(FormatCurrency(total)).Bold().FontSize(12);
                        });
                    });

                    row.ConstantItem(10);

                    row.RelativeItem().Border(0.75f).BorderColor(Line).Padding(7).Column(x =>
                    {
                        x.Item().PaddingBottom(4).Text("Giao dịch thanh toán").Bold().FontSize(10);
                        Field(x, "Mã giao dịch", data.Payment?.TransactionCode);
                        Field(x, "Phương thức", MapLabel(PaymentMethodLabels, data.Payment?.PaymentMethod));
                        Field(x, "Trạng thái", MapLabel(PaymentStatusLabels, data.Payment?.PaymentStatus));
                        Field(x, "Thời gian", FormatDateTime(data.Payment?.PaidAt ?? data.Payment?.CreatedAt));
                        Field(x, "Đã thanh toán", FormatCurrency(paidAmount));
                        x.Item().PaddingTop(4).BorderTop(0.5f).BorderColor(Line).PaddingTop(4).Text(t =>
                        {
                            t.Span("CÒN PHẢI THANH TOÁN: ").Bold().FontSize(10);
                            t.Span(FormatCurrency(remaining)).Bold().FontSize(12);
                        });
                    });
                });
            });
        }

        private void ComposeWarehouse(IContainer container, ReceiptData data)
        {
            container.Column(c =>
            {
                Title(c, "V. KHO TIẾP NHẬN");
                c.Item().Row(row =>
                {
                    row.RelativeItem().Border(0.75f).BorderColor(Line).Padding(7).Column(x =>
                    {
                        x.Item().Text("Kho nhập hàng").FontSize(9).FontColor(Muted);
                        x.Item().Text(data.Warehouse?.WarehouseName ?? "Chưa xác định").Bold().FontSize(11);
                        x.Item().Text(data.Warehouse?.WarehouseCode ?? "Không có mã kho").FontSize(9).FontColor(Muted);
                        x.Item().PaddingTop(2).Text(data.Warehouse?.Address ?? "Chưa có địa chỉ kho.").FontSize(9);
                    });
                    row.ConstantItem(10);
                    row.RelativeItem().Border(0.75f).BorderColor(Line).Padding(7).Column(x =>
                    {
                        x.Item().Text("Bảng giá vận chuyển").FontSize(9).FontColor(Muted);
                        x.Item().Text(MapLabel(ConsignmentTypeLabels, data.Order.ConsignmentType) ?? "—").Bold().FontSize(11);
                        x.Item().Text(data.Order.Route ?? "—").FontSize(9).FontColor(Muted);
                        x.Item().PaddingTop(2).Text(FormatServicePrice(data.ServicePricing)).FontSize(9);
                        x.Item().Text($"Quốc gia kho: {data.Warehouse?.Country ?? "—"}").FontSize(9).FontColor(Muted);
                    });
                });
            });
        }

        private void ComposeSignatures(IContainer container, ReceiptData data)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignCenter().Text("NGƯỜI LẬP PHIẾU").Bold().FontSize(10);
                    c.Item().AlignCenter().Text("Sale").FontSize(9).FontColor(Muted);
                    c.Item().PaddingTop(26).AlignCenter().Text(data.PreparedBy?.FullName ?? "—").Bold().FontSize(11);
                });

                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignCenter().Text("DẤU CÔNG TY").Bold().FontSize(10);

                    // Mộc chỉ đóng khi kho đã nhận hàng; trước đó phiếu mới là bản giao kho, chưa có giá trị xác nhận.
                    if (data.WarehouseStaff == null)
                    {
                        c.Item().PaddingTop(6).AlignCenter().Width(80).Height(80)
                            .Border(1).BorderColor(Line).CornerRadius(40)
                            .AlignMiddle().AlignCenter()
                            .Text("Đóng dấu sau\nkhi nhập kho").FontSize(8).FontColor(Muted);
                        return;
                    }

                    c.Item().PaddingTop(6).AlignCenter().Width(80).Height(80)
                        .Border(1.5f).BorderColor("#b91c1c").CornerRadius(40)
                        .AlignMiddle().AlignCenter()
                        .Text("ĐÃ NHẬP KHO").FontSize(9).Bold().FontColor("#b91c1c");
                });

                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignCenter().Text("XÁC NHẬN KHO").Bold().FontSize(10);

                    if (data.WarehouseStaff == null)
                    {
                        c.Item().AlignCenter().Text("(Thủ kho ký khi nhận hàng)").FontSize(9).FontColor(Muted);
                        c.Item().PaddingTop(26).AlignCenter().Text("Ký và ghi rõ họ tên").FontSize(9).FontColor(Muted);
                        c.Item().AlignCenter().Text(FormatWarehouseLabel(data.Warehouse)).FontSize(8).FontColor(Muted);
                        return;
                    }

                    c.Item().AlignCenter().Text("Nhân viên kho").FontSize(9).FontColor(Muted);
                    c.Item().PaddingTop(20).AlignCenter().Text(data.WarehouseStaff.FullName ?? "—").Bold().FontSize(11);
                    c.Item().AlignCenter().Text($"Nhận hàng lúc {FormatDateTime(data.CheckedInAt)}").FontSize(8).FontColor(Muted);
                    c.Item().AlignCenter().Text(FormatWarehouseLabel(data.Warehouse)).FontSize(8).FontColor(Muted);
                });
            });
        }

        private static void Title(ColumnDescriptor c, string title)
        {
            c.Item().PaddingBottom(5).BorderBottom(1).BorderColor("#94a3b8").PaddingBottom(3)
                .Text(title).Bold().FontSize(11).FontColor(Head);
        }

        private static void Field(ColumnDescriptor c, string? label, string? value)
        {
            c.Item().PaddingBottom(2).Text(t =>
            {
                t.Span($"{label}: ").FontSize(9).FontColor(Muted);
                t.Span(string.IsNullOrWhiteSpace(value) ? "—" : value).Bold().FontSize(10);
            });
        }

        private static void Cell(TableDescriptor table, string? text)
        {
            table.Cell().Border(0.5f).BorderColor(Line).Padding(4)
                .Text(string.IsNullOrWhiteSpace(text) ? string.Empty : text).FontSize(9);
        }

        private static string Join(string? first, string? second)
        {
            var parts = new[] { first, second }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            return parts.Length == 0 ? "—" : string.Join(" · ", parts);
        }

        /// <summary>
        /// Cân nặng quy đổi của một kiện. Mỗi dòng hàng là một kiện nên kích thước đã là của
        /// cả kiện, không nhân số lượng — giữ đúng công thức của API chi tiết đơn và báo giá.
        /// </summary>
        private decimal GetVolumetricWeight(OrderItem item)
        {
            if (item.Length is not > 0 || item.Width is not > 0 || item.Height is not > 0)
            {
                return 0m;
            }

            var divisor = decimal.TryParse(_configuration["PricingSettings:VolumetricDivisor"], out var configured) && configured > 0
                ? configured
                : DefaultVolumetricDivisor;

            return Math.Round(item.Length.Value * item.Width.Value * item.Height.Value / divisor, 4);
        }

        /// <summary>Đơn giá cước theo bảng giá đã áp; không có bảng giá thì nói rõ là theo báo giá của đơn.</summary>
        private static string FormatServicePrice(ServicePricing? pricing)
        {
            if (pricing?.Price is not > 0)
            {
                return "Theo báo giá chính thức của đơn";
            }

            var unit = string.IsNullOrWhiteSpace(pricing.UnitType) ? "kg" : pricing.UnitType;

            return $"{FormatCurrency(pricing.Price.Value)} / {unit}";
        }

        /// <summary>Nhãn kho dạng "Tên kho · MÃ", rỗng thì nêu rõ lý do để người đọc phiếu biết phải làm gì.</summary>
        private static string FormatWarehouseLabel(Warehouse? warehouse)
        {
            if (warehouse == null)
            {
                return "Chưa chọn kho ở báo giá";
            }

            return string.IsNullOrWhiteSpace(warehouse.WarehouseCode)
                ? warehouse.WarehouseName ?? "—"
                : $"{warehouse.WarehouseName} · {warehouse.WarehouseCode}";
        }

        private static string Fallback(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static string FormatCurrency(decimal value) => $"{Math.Round(value, 0).ToString("N0", Vi)} ₫";

        private static string FormatNumber(decimal value) => Math.Round(value, 2).ToString("N2", Vi);

        /// <summary>Số nguyên có phân tách nghìn — dùng cho thể tích, phần thập phân ở đây không có ý nghĩa.</summary>
        private static string FormatInteger(decimal value) => Math.Round(value, 0).ToString("N0", Vi);

        private static string FormatDate(DateTime? value) => value?.ToString("dd/MM/yyyy", Vi) ?? "—";

        private static string FormatDateTime(DateTime? value) => value?.ToString("dd/MM/yyyy HH:mm", Vi) ?? "—";

        private static string Sanitize(string value)
        {
            var cleaned = new string(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
            return string.Join("-", cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string RemoveDiacritics(string value)
        {
            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();

            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC).Replace('Đ', 'D').Replace('đ', 'd');
        }

        private sealed class ReceiptData
        {
            public Order Order { get; init; } = null!;
            public Customer? Customer { get; init; }
            public List<OrderItem> Items { get; init; } = new();
            public Quotation? Quotation { get; init; }
            public Warehouse? Warehouse { get; init; }
            public Payment? Payment { get; init; }
            public User? PreparedBy { get; init; }
            public decimal DomesticFee { get; init; }

            /// <summary>Tổng tiền đã thu của đơn, cộng dồn mọi đợt thanh toán thành công.</summary>
            public decimal PaidAmount { get; init; }

            /// <summary>Phụ phí phát sinh, đã nằm trong ServiceFee của báo giá.</summary>
            public List<QuotationFee> Surcharges { get; init; } = new();

            /// <summary>Tên loại hàng theo Id, rỗng nếu bảng PRODUCT_TYPES chưa có dữ liệu.</summary>
            public Dictionary<Guid, string> ProductTypeNames { get; init; } = new();

            /// <summary>
            /// Mốc xác nhận thanh toán: ưu tiên cột trên đơn, thiếu thì lấy theo giao dịch.
            /// Đơn do webhook chốt đôi khi chưa kịp ghi cột nên không được để trống trên phiếu.
            /// </summary>
            public DateTime? PaymentConfirmedAt =>
                Order.PaymentConfirmedAt ?? Payment?.PaidAt ?? Payment?.CreatedAt;
            public Dictionary<Guid, PackageConfiguration> PackageConfigurations { get; init; } = new();
            public Dictionary<Guid, List<byte[]>> ItemImages { get; init; } = new();

            /// <summary>Kiện đã check-in theo từng dòng hàng, rỗng nếu kho chưa nhận.</summary>
            public Dictionary<Guid, Parcel> ParcelByItemId { get; init; } = new();

            /// <summary>Bảng giá cước áp cho đơn, null nếu báo giá không gắn bảng giá nào.</summary>
            public ServicePricing? ServicePricing { get; init; }

            /// <summary>Nhân viên kho check-in gần nhất, null khi kho chưa nhận hàng.</summary>
            public User? WarehouseStaff { get; init; }

            public DateTime? CheckedInAt { get; init; }
        }
    }
}
