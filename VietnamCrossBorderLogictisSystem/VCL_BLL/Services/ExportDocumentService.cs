using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Sinh chứng từ PDF cho luồng xuất kho quốc tế.
    /// <para>
    /// Dùng QuestPDF giống <see cref="ConsignmentReceiptService"/> để chữ ở dạng vector: kho và hải quan
    /// zoom/tìm kiếm/scan được, khác với cách chụp DOM thành ảnh ở frontend.
    /// </para>
    /// <para>
    /// Ba chứng từ ở đây cùng đọc một bộ dữ liệu (phiếu → kiện → đơn → khách → ô kệ → kho), nên phần nạp
    /// dữ liệu được gom vào <see cref="LoadLookupsAsync"/>: mỗi bảng chỉ <c>GetAllAsync</c> đúng một lần rồi
    /// dựng dictionary tra cứu. Trước đây kiểu tra từng kiện một sẽ thành N+1 — một lô vài trăm kiện là
    /// vài trăm lượt truy vấn cho một lần in.
    /// </para>
    /// </summary>
    public class ExportDocumentService : IExportDocumentService
    {
        private readonly IUnitOfWork _unitOfWork;
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

        /// <summary>Nền dòng kiện đã bốc trên danh sách soạn hàng — nhìn lướt là biết chỗ nào bỏ qua.</summary>
        private const string Done = "#f0fdf4";

        private static readonly CultureInfo Vi = new("vi-VN");

        /// <summary>Phương thức vận chuyển của lô là mã hệ thống; chứng từ cho người đọc nên dịch sang tiếng Việt.</summary>
        private static readonly Dictionary<string, string> TransportModeLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AIR"] = "Đường hàng không",
            ["SEA"] = "Đường biển",
            ["ROAD"] = "Đường bộ",
            ["RAIL"] = "Đường sắt",
            ["EXPRESS"] = "Chuyển phát nhanh"
        };

        private static readonly Dictionary<string, string> ShipmentStatusLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            [ExportShipmentStatuses.Draft] = "Đang gom hàng",
            [ExportShipmentStatuses.HandedOver] = "Đã bàn giao hãng vận chuyển",
            [ExportShipmentStatuses.Departed] = "Đã rời kho nguồn",
            [ExportShipmentStatuses.InTransit] = "Đang vận chuyển",
            [ExportShipmentStatuses.Delayed] = "Chậm tiến độ",
            [ExportShipmentStatuses.OnHold] = "Đang tạm giữ",
            [ExportShipmentStatuses.CustomsCleared] = "Đã thông quan",
            [ExportShipmentStatuses.ArrivedVn] = "Đã về Việt Nam",
            [ExportShipmentStatuses.ArrivedDestination] = "Đã tới kho đích",
            [ExportShipmentStatuses.Cancelled] = "Đã huỷ"
        };

        public ExportDocumentService(IUnitOfWork unitOfWork, IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _configuration = configuration;

            QuestPDF.Settings.License = LicenseType.Community;
        }

        // ──────────────────────────────── 1. PHIẾU XUẤT KHO ────────────────────────────────

        public async Task<(byte[] Content, string FileName)> BuildReleaseNoteAsync(Guid wroRequestId)
        {
            var data = await LoadWroDataAsync(wroRequestId);

            // Kiện đã bị gỡ khỏi phiếu không rời kho theo phiếu này; in ra chỉ làm người ký đối chiếu nhầm.
            var lines = data.Lines
                .Where(l => !IsRemoved(l.Status))
                .OrderBy(l => l.SourceBinSortKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.PackageCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var content = BuildReleaseNoteDocument(data, lines).GeneratePdf();

            return (content, $"phieu-xuat-kho-{FileCode(data.Request.WroCode, data.Request.Id)}.pdf");
        }

        private Document BuildReleaseNoteDocument(WroData data, List<ParcelLine> lines)
        {
            var request = data.Request;
            var totalWeight = lines.Sum(l => l.Weight);

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
                        root.Item().Element(e => ComposeDocumentHeader(
                            e,
                            "PHIẾU XUẤT KHO",
                            "(Chứng từ xuất hàng đi quốc tế)",
                            "Mã phiếu",
                            request.WroCode,
                            request.CreatedAt,
                            data.OriginWarehouse));

                        root.Item().PaddingTop(9).Column(body =>
                        {
                            body.Spacing(11);

                            body.Item().Element(e => ComposeReleaseInfo(e, data));
                            body.Item().Element(e => ComposeReleaseParcels(e, lines, totalWeight));

                            body.Item().PaddingTop(3)
                                .Text("Phiếu được lập từ dữ liệu phiếu xuất kho đã được quản lý kho duyệt. Hai bên đối chiếu số kiện và trọng lượng thực tế trước khi ký.")
                                .FontSize(8.5f).Italic().FontColor(Muted);

                            body.Item().PaddingTop(16).ShowEntire().Element(e => ComposeReleaseSignatures(e, data));
                        });
                    });

                    page.Footer().Element(e => ComposeFooter(e, $"Phiếu xuất kho {request.WroCode}"));
                });
            });
        }

        private void ComposeReleaseInfo(IContainer container, WroData data)
        {
            var request = data.Request;

            container.Column(c =>
            {
                Title(c, "I. THÔNG TIN PHIẾU XUẤT");

                // Chặng kho nguồn → kho đích in thành một dải riêng: đây là thông tin người bốc hàng và
                // người nhận nhìn đầu tiên, nhét chung vào lưới field thì phải dò mới thấy.
                c.Item().PaddingBottom(6).Border(0.75f).BorderColor(Line).Background(Soft).Padding(7).Row(row =>
                {
                    row.RelativeItem().Column(x =>
                    {
                        x.Item().Text("KHO NGUỒN").FontSize(8.5f).FontColor(Muted);
                        x.Item().Text(FormatWarehouseLabel(data.OriginWarehouse)).Bold().FontSize(11);
                        x.Item().Text(data.OriginWarehouse?.Address ?? "—").FontSize(8.5f).FontColor(Muted);
                    });
                    row.ConstantItem(26).AlignMiddle().AlignCenter().Text("→").Bold().FontSize(14).FontColor(Head);
                    row.RelativeItem().Column(x =>
                    {
                        x.Item().Text("KHO ĐÍCH").FontSize(8.5f).FontColor(Muted);
                        x.Item().Text(FormatWarehouseLabel(data.DestinationWarehouse)).Bold().FontSize(11);
                        x.Item().Text(data.DestinationWarehouse?.Address ?? "—").FontSize(8.5f).FontColor(Muted);
                    });
                });

                c.Item().Row(row =>
                {
                    row.RelativeItem().Column(x =>
                    {
                        Field(x, "Mã phiếu", request.WroCode);
                        Field(x, "Trạng thái", WroStatuses.Describe(request.Status));
                        Field(x, "Ngày lập phiếu", FormatDateTime(request.CreatedAt));
                        Field(x, "Lý do xuất", request.ExportReason);
                    });
                    row.ConstantItem(12);
                    row.RelativeItem().Column(x =>
                    {
                        Field(x, "Tuyến vận chuyển", FormatRouteName(data.Route));
                        Field(x, "Chặng", FormatRouteLeg(data.Route));
                        Field(x, "Phương thức", MapLabel(TransportModeLabels, data.Route?.TransportMode));

                        // Phiếu tách ra từ phiếu khác phải nói rõ nguồn gốc: kho hay hỏi "sao thiếu kiện so
                        // với lần duyệt trước", câu trả lời nằm ở phiếu gốc.
                        if (data.SplitFromCode != null)
                        {
                            Field(x, "Tách từ phiếu", data.SplitFromCode);
                        }
                    });
                    row.ConstantItem(12);
                    row.RelativeItem().Column(x =>
                    {
                        Field(x, "Người lập phiếu", data.SubmittedBy?.FullName);
                        Field(x, "Thời điểm gửi duyệt", FormatDateTime(request.SubmittedAt));
                        Field(x, "Người duyệt", data.DecidedBy?.FullName);
                        Field(x, "Thời điểm duyệt", FormatDateTime(request.DecidedAt));
                    });
                });

                if (!string.IsNullOrWhiteSpace(request.DecisionNote))
                {
                    c.Item().PaddingTop(5).Background(Soft).Border(0.5f).BorderColor(Line).Padding(6).Text(t =>
                    {
                        t.Span("Ghi chú của quản lý kho: ").FontSize(9).FontColor(Muted);
                        t.Span(request.DecisionNote).Bold();
                    });
                }
            });
        }

        private void ComposeReleaseParcels(IContainer container, List<ParcelLine> lines, decimal totalWeight)
        {
            container.Column(c =>
            {
                Title(c, $"II. DANH SÁCH KIỆN XUẤT  ·  {lines.Count} kiện");

                if (lines.Count == 0)
                {
                    c.Item().Text("Phiếu chưa có kiện nào.").FontSize(9).FontColor(Muted);
                    return;
                }

                c.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(26);   // STT
                        columns.RelativeColumn(1.5f); // Mã kiện
                        columns.RelativeColumn(1.4f); // Mã đơn
                        columns.RelativeColumn(1.5f); // Khách hàng
                        columns.RelativeColumn(2.1f); // Tên hàng
                        columns.ConstantColumn(52);   // Cân nặng
                        columns.ConstantColumn(88);   // Kích thước
                        columns.RelativeColumn(1.4f); // Vị trí
                    });

                    // Header lặp lại mỗi trang: bảng kiện của một lô lớn tràn sang trang 2-3,
                    // không có tiêu đề cột thì người ký không biết cột nào là cân nặng.
                    table.Header(header =>
                    {
                        HeadCell(header.Cell(), "STT");
                        HeadCell(header.Cell(), "Mã kiện");
                        HeadCell(header.Cell(), "Mã đơn");
                        HeadCell(header.Cell(), "Khách hàng");
                        HeadCell(header.Cell(), "Tên hàng");
                        HeadCell(header.Cell(), "KG", true);
                        HeadCell(header.Cell(), "Kích thước (cm)");
                        HeadCell(header.Cell(), "Vị trí");
                    });

                    var index = 1;
                    foreach (var line in lines)
                    {
                        BodyCell(table.Cell(), index.ToString(CultureInfo.InvariantCulture));
                        BodyCell(table.Cell(), line.PackageCode, bold: true);
                        BodyCell(table.Cell(), line.ConsignmentCode);
                        BodyCell(table.Cell(), line.CustomerName);
                        BodyCell(table.Cell(), line.ProductName);
                        BodyCell(table.Cell(), FormatNumber(line.Weight), alignRight: true);
                        BodyCell(table.Cell(), line.Dimensions);
                        BodyCell(table.Cell(), line.SourceBinLabel);
                        index++;
                    }
                });

                c.Item().PaddingTop(6).Background(Soft).Border(0.75f).BorderColor(Line).Padding(7).Row(r =>
                {
                    Total(r, "Tổng số kiện", $"{lines.Count} kiện");
                    Total(r, "Tổng trọng lượng", $"{FormatNumber(totalWeight)} kg");
                    Total(r, "Số đơn liên quan", $"{lines.Select(l => l.ConsignmentCode).Distinct().Count()} đơn");
                });
            });
        }

        private void ComposeReleaseSignatures(IContainer container, WroData data)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignCenter().Text("NHÂN VIÊN KHO").Bold().FontSize(10);
                    c.Item().AlignCenter().Text("(Người bốc và giao hàng)").FontSize(9).FontColor(Muted);
                    c.Item().PaddingTop(30).AlignCenter().Text("Ký và ghi rõ họ tên").FontSize(9).FontColor(Muted);
                });

                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignCenter().Text("QUẢN LÝ KHO").Bold().FontSize(10);
                    c.Item().AlignCenter().Text("(Người duyệt phiếu xuất)").FontSize(9).FontColor(Muted);

                    // Đã duyệt thì in sẵn tên và mốc duyệt — chữ ký giấy chỉ để xác nhận lại,
                    // còn ai duyệt là dữ liệu hệ thống đã ghi, không để kho tự điền tay.
                    if (data.DecidedBy != null)
                    {
                        c.Item().PaddingTop(24).AlignCenter().Text(data.DecidedBy.FullName ?? "—").Bold().FontSize(11);
                        c.Item().AlignCenter().Text($"Duyệt lúc {FormatDateTime(data.Request.DecidedAt)}")
                            .FontSize(8).FontColor(Muted);
                        return;
                    }

                    c.Item().PaddingTop(30).AlignCenter().Text("Ký và ghi rõ họ tên").FontSize(9).FontColor(Muted);
                });
            });
        }

        // ────────────────────────────── 2. DANH SÁCH SOẠN HÀNG ──────────────────────────────

        public async Task<(byte[] Content, string FileName)> BuildPickingSheetAsync(Guid wroRequestId)
        {
            var data = await LoadWroDataAsync(wroRequestId);

            // Chỉ kiện còn phải xử lý trong kho: đã bàn giao thì hàng không còn ở kho nguồn nữa,
            // đã gỡ khỏi phiếu thì không được bốc. Giữ lại PICKED để bản in lại giữa chừng vẫn
            // là bức tranh đầy đủ của phiếu — người bốc biết mình đã làm tới đâu.
            var lines = data.Lines
                .Where(l => IsReserved(l.Status) || IsPicked(l.Status))
                .OrderBy(l => l.SourceBinSortKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.PackageCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var content = BuildPickingSheetDocument(data, lines).GeneratePdf();

            return (content, $"danh-sach-soan-hang-{FileCode(data.Request.WroCode, data.Request.Id)}.pdf");
        }

        private Document BuildPickingSheetDocument(WroData data, List<ParcelLine> lines)
        {
            var request = data.Request;
            var pickedCount = lines.Count(l => IsPicked(l.Status));
            var remaining = lines.Count - pickedCount;

            return Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(11, Unit.Millimetre);
                    page.DefaultTextStyle(t => t.FontFamily(FontCandidates).FontSize(10).FontColor(Ink).LineHeight(1.3f));

                    page.Content().Column(root =>
                    {
                        root.Item().Element(e => ComposeDocumentHeader(
                            e,
                            "DANH SÁCH SOẠN HÀNG",
                            "(Bản in nội bộ dùng khi đi bốc hàng)",
                            "Mã phiếu",
                            request.WroCode,
                            DateTime.UtcNow,
                            data.OriginWarehouse));

                        root.Item().PaddingTop(9).Column(body =>
                        {
                            body.Spacing(10);

                            body.Item().Row(row =>
                            {
                                row.RelativeItem().Column(x =>
                                {
                                    Field(x, "Kho bốc hàng", FormatWarehouseLabel(data.OriginWarehouse));
                                    Field(x, "Tuyến", FormatRouteName(data.Route));
                                });
                                row.ConstantItem(12);
                                row.RelativeItem().Column(x =>
                                {
                                    Field(x, "Trạng thái phiếu", WroStatuses.Describe(request.Status));
                                    Field(x, "Ngày in", FormatDateTime(DateTime.UtcNow));
                                });
                                row.ConstantItem(12);
                                row.RelativeItem().Column(x =>
                                {
                                    Field(x, "Tổng số kiện", $"{lines.Count} kiện");
                                    Field(x, "Còn phải bốc", $"{remaining} kiện (đã bốc {pickedCount})");
                                });
                            });

                            body.Item().Element(e => ComposePickingTable(e, lines));

                            body.Item().PaddingTop(2)
                                .Text("Danh sách sắp theo mã ô kệ để đi một vòng kho. Kiện đã bốc in nền nhạt — bỏ qua, không bốc lại. Quét mã kiện và mã ô khu xuất trên hệ thống ngay khi đặt hàng xuống; bản in này chỉ là bản nháp đi đường.")
                                .FontSize(8.5f).Italic().FontColor(Muted);

                            body.Item().PaddingTop(14).ShowEntire().Row(row =>
                            {
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Người soạn hàng: ..................................................").FontSize(9);
                                    c.Item().PaddingTop(8).Text("Bắt đầu lúc: ............ giờ ............ ngày ...... / ...... / ............").FontSize(9);
                                });
                                row.ConstantItem(16);
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("Sự cố khi bốc (thiếu, hỏng, sai ô): ...............................").FontSize(9);
                                    c.Item().PaddingTop(8).Text("Kết thúc lúc: ............ giờ ............ ngày ...... / ...... / ............").FontSize(9);
                                });
                            });
                        });
                    });

                    page.Footer().Element(e => ComposeFooter(e, $"Soạn hàng phiếu {request.WroCode}"));
                });
            });
        }

        private void ComposePickingTable(IContainer container, List<ParcelLine> lines)
        {
            container.Column(c =>
            {
                if (lines.Count == 0)
                {
                    c.Item().Text("Không còn kiện nào cần bốc cho phiếu này.").FontSize(9).FontColor(Muted);
                    return;
                }

                c.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.8f); // Ô kệ nguồn
                        columns.RelativeColumn(1.7f); // Mã kiện
                        columns.RelativeColumn(1.5f); // Mã đơn
                        columns.ConstantColumn(52);   // Cân nặng
                        columns.RelativeColumn(1.5f); // Ô khu xuất
                        columns.ConstantColumn(58);   // Tick
                    });

                    table.Header(header =>
                    {
                        HeadCell(header.Cell(), "Ô kệ nguồn");
                        HeadCell(header.Cell(), "Mã kiện");
                        HeadCell(header.Cell(), "Mã đơn");
                        HeadCell(header.Cell(), "KG", true);
                        HeadCell(header.Cell(), "Ô khu xuất");
                        HeadCell(header.Cell(), "Đã bốc");
                    });

                    foreach (var line in lines)
                    {
                        var picked = IsPicked(line.Status);
                        var background = picked ? Done : null;

                        // Ô kệ in đậm và to hơn phần còn lại: người bốc đọc cột này trước để tìm đường,
                        // các cột kia chỉ dùng để đối chiếu khi đã đứng trước kệ.
                        BodyCell(table.Cell(), line.SourceBinLabel, bold: true, background: background, fontSize: 10);
                        BodyCell(table.Cell(), line.PackageCode, bold: true, background: background);
                        BodyCell(table.Cell(), line.ConsignmentCode, background: background);
                        BodyCell(table.Cell(), FormatNumber(line.Weight), alignRight: true, background: background);
                        BodyCell(table.Cell(), picked ? line.StagingBinLabel : string.Empty, background: background);

                        if (picked)
                        {
                            // Đã bốc thì in chữ thay vì ô trống: một ô trống dễ bị tick nhầm rồi bốc lại,
                            // trong khi kiện đang nằm ở khu xuất chứ không còn ở ô kệ ghi bên trái.
                            BodyCell(table.Cell(), "ĐÃ BỐC", bold: true, background: background, alignCenter: true, fontSize: 8);
                            continue;
                        }

                        TickCell(table.Cell());
                    }
                });
            });
        }

        // ──────────────────────────────── 3. MANIFEST LÔ ────────────────────────────────

        public async Task<(byte[] Content, string FileName)> BuildShipmentManifestAsync(Guid shipmentId)
        {
            var shipment = await _unitOfWork.Repository<Shipment>().GetByIdAsync(shipmentId)
                ?? throw new NotFoundException($"Không tìm thấy lô vận chuyển với ID {shipmentId}");

            var lookups = await LoadLookupsAsync();

            var wroIds = (await _unitOfWork.Repository<ShipmentWroRequest>().GetAllAsync())
                .Where(link => link.ShipmentId == shipmentId)
                .Select(link => link.WroRequestId)
                .ToHashSet();

            var requests = (await _unitOfWork.Repository<WroRequest>().GetAllAsync())
                .Where(r => wroIds.Contains(r.Id))
                .OrderBy(r => r.WroCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Kiện của mọi phiếu trong lô nạp một lượt rồi nhóm lại, thay vì lọc lại bảng cho từng phiếu.
            var linksByWro = (await _unitOfWork.Repository<WroParcel>().GetAllAsync())
                .Where(p => wroIds.Contains(p.WroRequestId) && !IsRemoved(p.Status))
                .GroupBy(p => p.WroRequestId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var groups = requests
                .Select(request => new ManifestGroup
                {
                    Request = request,
                    Lines = BuildParcelLines(
                            linksByWro.TryGetValue(request.Id, out var links) ? links : new List<WroParcel>(),
                            lookups)
                        .OrderBy(l => l.PackageCode, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToList();

            var carrier = shipment.CarrierId.HasValue && lookups.Carriers.TryGetValue(shipment.CarrierId.Value, out var c)
                ? c
                : null;

            var data = new ManifestData
            {
                Shipment = shipment,
                Carrier = carrier,
                OriginWarehouse = Lookup(lookups.Warehouses, shipment.OriginWarehouseId),
                DestinationWarehouse = Lookup(lookups.Warehouses, shipment.DestinationWarehouseId),
                Groups = groups
            };

            var content = BuildManifestDocument(data).GeneratePdf();

            return (content, $"manifest-{FileCode(shipment.ShipmentCode, shipment.Id)}.pdf");
        }

        private Document BuildManifestDocument(ManifestData data)
        {
            var shipment = data.Shipment;
            var allLines = data.Groups.SelectMany(g => g.Lines).ToList();
            var totalWeight = allLines.Sum(l => l.Weight);

            return Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(11, Unit.Millimetre);
                    page.DefaultTextStyle(t => t.FontFamily(FontCandidates).FontSize(10).FontColor(Ink).LineHeight(1.3f));

                    page.Content().Column(root =>
                    {
                        root.Item().Element(e => ComposeDocumentHeader(
                            e,
                            "MANIFEST LÔ VẬN CHUYỂN",
                            "(Bản kê hàng hoá đi kèm lô)",
                            "Mã lô",
                            shipment.ShipmentCode,
                            shipment.HandedOverAt ?? DateTime.UtcNow,
                            data.OriginWarehouse));

                        root.Item().PaddingTop(9).Column(body =>
                        {
                            body.Spacing(11);

                            body.Item().Element(e => ComposeManifestInfo(e, data));
                            body.Item().Element(e => ComposeManifestTable(e, data, allLines.Count, totalWeight));

                            body.Item().PaddingTop(3)
                                .Text("Bản kê được sinh từ dữ liệu lô vận chuyển tại thời điểm in. Bên nhận đối chiếu số kiện thực tế với bản kê trước khi ký nhận.")
                                .FontSize(8.5f).Italic().FontColor(Muted);

                            body.Item().PaddingTop(16).ShowEntire().Element(ComposeManifestSignatures);
                        });
                    });

                    page.Footer().Element(e => ComposeFooter(e, $"Manifest lô {shipment.ShipmentCode}"));
                });
            });
        }

        private void ComposeManifestInfo(IContainer container, ManifestData data)
        {
            var shipment = data.Shipment;

            container.Column(c =>
            {
                Title(c, "I. THÔNG TIN LÔ VẬN CHUYỂN");

                c.Item().PaddingBottom(6).Border(0.75f).BorderColor(Line).Background(Soft).Padding(7).Row(row =>
                {
                    row.RelativeItem().Column(x =>
                    {
                        x.Item().Text("KHO NGUỒN").FontSize(8.5f).FontColor(Muted);
                        x.Item().Text(FormatWarehouseLabel(data.OriginWarehouse)).Bold().FontSize(11);
                        x.Item().Text(data.OriginWarehouse?.Address ?? "—").FontSize(8.5f).FontColor(Muted);
                    });
                    row.ConstantItem(26).AlignMiddle().AlignCenter().Text("→").Bold().FontSize(14).FontColor(Head);
                    row.RelativeItem().Column(x =>
                    {
                        x.Item().Text("KHO ĐÍCH").FontSize(8.5f).FontColor(Muted);
                        x.Item().Text(FormatWarehouseLabel(data.DestinationWarehouse)).Bold().FontSize(11);
                        x.Item().Text(data.DestinationWarehouse?.Address ?? "—").FontSize(8.5f).FontColor(Muted);
                    });
                });

                c.Item().Row(row =>
                {
                    row.RelativeItem().Column(x =>
                    {
                        Field(x, "Mã lô", shipment.ShipmentCode);
                        Field(x, "Trạng thái", MapLabel(ShipmentStatusLabels, shipment.Status));
                        Field(x, "Phương thức vận chuyển", MapLabel(TransportModeLabels, shipment.ShippingMethod));
                    });
                    row.ConstantItem(12);
                    row.RelativeItem().Column(x =>
                    {
                        Field(x, "Hãng vận chuyển", data.Carrier?.CarrierName);
                        Field(x, "Mã hãng", data.Carrier?.CarrierCode);
                        Field(x, "Liên hệ hãng", Join(data.Carrier?.ContactPhone, data.Carrier?.ContactEmail));
                    });
                    row.ConstantItem(12);
                    row.RelativeItem().Column(x =>
                    {
                        Field(x, "Mã vận đơn", shipment.CarrierTrackingCode);

                        // Lô chưa bàn giao vẫn in được manifest (để chuẩn bị chứng từ), nên nói rõ
                        // là chưa bàn giao thay vì in một gạch ngang khó hiểu.
                        Field(x, "Ngày bàn giao", shipment.HandedOverAt.HasValue
                            ? FormatDateTime(shipment.HandedOverAt)
                            : "Chưa bàn giao");
                        Field(x, "Ngày in", FormatDateTime(DateTime.UtcNow));
                    });
                });
            });
        }

        private void ComposeManifestTable(IContainer container, ManifestData data, int totalParcels, decimal totalWeight)
        {
            container.Column(c =>
            {
                Title(c, $"II. DANH SÁCH HÀNG HOÁ  ·  {data.Groups.Count} phiếu · {totalParcels} kiện");

                if (totalParcels == 0)
                {
                    c.Item().Text("Lô chưa có kiện nào.").FontSize(9).FontColor(Muted);
                    return;
                }

                c.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.5f); // Mã phiếu
                        columns.RelativeColumn(1.5f); // Mã kiện
                        columns.RelativeColumn(1.4f); // Mã đơn
                        columns.RelativeColumn(1.6f); // Khách hàng
                        columns.RelativeColumn(2.0f); // Tên hàng
                        columns.ConstantColumn(52);   // Cân nặng
                        columns.ConstantColumn(88);   // Kích thước
                    });

                    table.Header(header =>
                    {
                        HeadCell(header.Cell(), "Mã phiếu");
                        HeadCell(header.Cell(), "Mã kiện");
                        HeadCell(header.Cell(), "Mã đơn");
                        HeadCell(header.Cell(), "Khách hàng");
                        HeadCell(header.Cell(), "Tên hàng");
                        HeadCell(header.Cell(), "KG", true);
                        HeadCell(header.Cell(), "Kích thước (cm)");
                    });

                    foreach (var group in data.Groups)
                    {
                        // Phiếu rỗng vẫn phải xuất hiện: hải quan và bên nhận đối chiếu theo danh sách
                        // phiếu trong lô, thiếu một dòng là phải đi tìm xem phiếu đó đi đâu.
                        if (group.Lines.Count == 0)
                        {
                            table.Cell().ColumnSpan(7).Border(0.5f).BorderColor(Line).Padding(4)
                                .Text($"{group.Request.WroCode} — không còn kiện nào trong phiếu")
                                .FontSize(9).Italic().FontColor(Muted);
                            continue;
                        }

                        foreach (var line in group.Lines)
                        {
                            // In mã phiếu ở mọi dòng thay vì gộp ô: bảng tràn trang thì nhóm bị cắt đôi,
                            // và dòng đầu trang sau sẽ không còn biết thuộc phiếu nào.
                            BodyCell(table.Cell(), group.Request.WroCode, bold: true);
                            BodyCell(table.Cell(), line.PackageCode, bold: true);
                            BodyCell(table.Cell(), line.ConsignmentCode);
                            BodyCell(table.Cell(), line.CustomerName);
                            BodyCell(table.Cell(), line.ProductName);
                            BodyCell(table.Cell(), FormatNumber(line.Weight), alignRight: true);
                            BodyCell(table.Cell(), line.Dimensions);
                        }

                        table.Cell().ColumnSpan(5).Background(Soft).Border(0.5f).BorderColor(Line).Padding(4)
                            .Text($"Cộng phiếu {group.Request.WroCode}: {group.Lines.Count} kiện")
                            .FontSize(9).Bold();
                        table.Cell().ColumnSpan(2).Background(Soft).Border(0.5f).BorderColor(Line).Padding(4)
                            .AlignRight().Text($"{FormatNumber(group.Lines.Sum(l => l.Weight))} kg")
                            .FontSize(9).Bold();
                    }
                });

                c.Item().PaddingTop(6).Background(Soft).Border(0.75f).BorderColor(Line).Padding(7).Row(r =>
                {
                    Total(r, "Tổng số phiếu", $"{data.Groups.Count} phiếu");
                    Total(r, "Tổng số kiện", $"{totalParcels} kiện");
                    Total(r, "Tổng trọng lượng", $"{FormatNumber(totalWeight)} kg");
                });
            });
        }

        private static void ComposeManifestSignatures(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignCenter().Text("ĐẠI DIỆN KHO NGUỒN").Bold().FontSize(10);
                    c.Item().AlignCenter().Text("(Người bàn giao)").FontSize(9).FontColor(Muted);
                    c.Item().PaddingTop(30).AlignCenter().Text("Ký và ghi rõ họ tên").FontSize(9).FontColor(Muted);
                });

                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignCenter().Text("ĐẠI DIỆN HÃNG VẬN CHUYỂN").Bold().FontSize(10);
                    c.Item().AlignCenter().Text("(Người nhận hàng)").FontSize(9).FontColor(Muted);
                    c.Item().PaddingTop(30).AlignCenter().Text("Ký và ghi rõ họ tên").FontSize(9).FontColor(Muted);
                });
            });
        }

        // ──────────────────────────────── Nạp dữ liệu ────────────────────────────────

        /// <summary>
        /// Dữ liệu dùng chung của phiếu xuất và danh sách soạn hàng. Hai chứng từ khác nhau ở phần lọc
        /// và bố cục, còn nguồn dữ liệu y hệt nhau nên gom về một chỗ để không lệch số giữa hai bản in.
        /// </summary>
        private async Task<WroData> LoadWroDataAsync(Guid wroRequestId)
        {
            var request = await _unitOfWork.Repository<WroRequest>().GetByIdAsync(wroRequestId)
                ?? throw new NotFoundException($"Không tìm thấy phiếu xuất kho với ID {wroRequestId}");

            var lookups = await LoadLookupsAsync();

            var links = (await _unitOfWork.Repository<WroParcel>().GetAllAsync())
                .Where(p => p.WroRequestId == wroRequestId)
                .ToList();

            string? splitFromCode = null;
            if (request.SplitFromId.HasValue)
            {
                splitFromCode = (await _unitOfWork.Repository<WroRequest>().GetAllAsync())
                    .FirstOrDefault(r => r.Id == request.SplitFromId.Value)?.WroCode;
            }

            // Kho nguồn/đích chốt trên phiếu lúc lập; phiếu cũ (trước luồng mới) chưa có nên rơi về
            // kho của tuyến để bản in không trống hai ô quan trọng nhất.
            var route = Lookup(lookups.Routes, request.ShippingRouteId);
            var originWarehouse = Lookup(lookups.Warehouses, request.OriginWarehouseId ?? route?.OriginWarehouseId);
            var destinationWarehouse = Lookup(lookups.Warehouses, request.DestinationWarehouseId ?? route?.DestinationWarehouseId);

            return new WroData
            {
                Request = request,
                Route = route,
                OriginWarehouse = originWarehouse,
                DestinationWarehouse = destinationWarehouse,
                SubmittedBy = Lookup(lookups.Users, request.SubmittedById ?? request.UserId),
                DecidedBy = Lookup(lookups.Users, request.DecidedById),
                SplitFromCode = splitFromCode,
                Lines = BuildParcelLines(links, lookups)
            };
        }

        /// <summary>
        /// Nạp toàn bộ các bảng tra cứu đúng một lần. Các service khác trong dự án cũng đọc qua
        /// <c>GetAllAsync</c> rồi lọc trong bộ nhớ, giữ nguyên cách đó để không rẽ thêm một lối
        /// truy cập dữ liệu thứ hai cho riêng chỗ in chứng từ.
        /// </summary>
        private async Task<ExportLookups> LoadLookupsAsync()
        {
            return new ExportLookups
            {
                Parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync()).ToDictionary(p => p.Id),
                Orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id),
                OrderItems = (await _unitOfWork.Repository<OrderItem>().GetAllAsync()).ToDictionary(i => i.Id),
                Customers = (await _unitOfWork.Repository<Customer>().GetAllAsync()).ToDictionary(c => c.Id),
                Users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id),
                Bins = (await _unitOfWork.Repository<Bin>().GetAllAsync()).ToDictionary(b => b.Id),
                Shelves = (await _unitOfWork.Repository<Shelf>().GetAllAsync()).ToDictionary(s => s.Id),
                Zones = (await _unitOfWork.Repository<WarehouseZone>().GetAllAsync()).ToDictionary(z => z.Id),
                Warehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync()).ToDictionary(w => w.Id),
                Routes = (await _unitOfWork.Repository<ShippingRoute>().GetAllAsync()).ToDictionary(r => r.Id),
                Carriers = (await _unitOfWork.Repository<Carrier>().GetAllAsync()).ToDictionary(c => c.Id)
            };
        }

        /// <summary>
        /// Dựng dòng in cho từng kiện trong phiếu. Mọi giá trị hiển thị được tính sẵn ở đây để phần vẽ
        /// PDF chỉ còn đặt chữ vào ô — cùng một kiện in ra ba chứng từ phải ra đúng một chuỗi.
        /// </summary>
        private static List<ParcelLine> BuildParcelLines(List<WroParcel> links, ExportLookups lookups)
        {
            var lines = new List<ParcelLine>(links.Count);

            foreach (var link in links)
            {
                lookups.Parcels.TryGetValue(link.ParcelId, out var parcel);

                var order = parcel?.ConsignmentId != null && lookups.Orders.TryGetValue(parcel.ConsignmentId.Value, out var o)
                    ? o
                    : null;

                var item = parcel?.OrderItemId != null && lookups.OrderItems.TryGetValue(parcel.OrderItemId.Value, out var i)
                    ? i
                    : null;

                // Ô kệ nguồn ưu tiên giá trị đã chốt trên phiếu: sau khi bốc, PARCELS.bin_id đã trỏ sang
                // ô khu xuất, nên đọc kiện sẽ ra sai chỗ cần tới lấy hàng.
                var sourceBinId = link.SourceBinId ?? parcel?.BinId;

                lines.Add(new ParcelLine
                {
                    Status = link.Status,
                    PackageCode = Fallback(parcel?.PackageCode, "—"),
                    // Đánh dấu kiện mua hộ ngay trên cột mã đơn của phiếu xuất / phiếu soạn hàng / manifest
                    // để người bốc và người nhận ở kho đích tách được hai loại hàng.
                    ConsignmentCode = OrderPurchaseLabels.IsPurchaseOrder(order)
                        ? $"{Fallback(order?.ConsignmentCode, "—")} (Mua hộ)"
                        : Fallback(order?.ConsignmentCode, "—"),
                    CustomerName = ResolveCustomerName(order, lookups),
                    ProductName = Fallback(item?.ProductName, "—"),
                    Weight = parcel?.ActualWeight ?? 0m,
                    Dimensions = FormatDimensions(parcel?.Length, parcel?.Width, parcel?.Height),
                    SourceBinLabel = FormatBinLabel(sourceBinId, lookups),
                    SourceBinSortKey = FormatBinSortKey(sourceBinId, lookups),
                    StagingBinLabel = FormatBinLabel(link.StagingBinId, lookups)
                });
            }

            return lines;
        }

        /// <summary>
        /// Tên khách in trên chứng từ. CUSTOMERS.full_name là tên giao dịch của khách nên ưu tiên;
        /// hồ sơ khách tạo từ tài khoản đôi khi để trống cột này, khi đó lấy tên của USER đứng sau.
        /// </summary>
        private static string ResolveCustomerName(Order? order, ExportLookups lookups)
        {
            if (order?.CustomerId == null || !lookups.Customers.TryGetValue(order.CustomerId.Value, out var customer))
            {
                // Không tra được khách thì còn người nhận trên đơn — vẫn hơn một gạch ngang.
                return Fallback(order?.ReceiverName, "—");
            }

            if (!string.IsNullOrWhiteSpace(customer.FullName))
            {
                return customer.FullName.Trim();
            }

            var user = Lookup(lookups.Users, customer.UserId);

            return Fallback(user?.FullName ?? order.ReceiverName, "—");
        }

        // ──────────────────────────────── Định dạng dùng chung ────────────────────────────────

        private void ComposeDocumentHeader(
            IContainer container,
            string title,
            string subtitle,
            string codeLabel,
            string? code,
            DateTime? date,
            Warehouse? warehouse)
        {
            // Config để trống vẫn là chuỗi rỗng chứ không null, nên ?? không đủ để rơi về mặc định.
            var companyName = Fallback(_configuration["Company:Name"], "CÔNG TY TNHH VIETNAM LOGICTIC");
            var taxCode = Fallback(_configuration["Company:TaxCode"], "—");
            var phone = Fallback(_configuration["Company:Phone"], "—");

            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem(1.1f).Column(c =>
                    {
                        c.Item().Text(companyName).Bold().FontSize(12);
                        c.Item().Text(warehouse?.Address ?? "Địa chỉ kho chưa được xác định").FontSize(9).FontColor(Muted);
                        c.Item().Text($"MST: {taxCode}").FontSize(9).FontColor(Muted);
                        c.Item().Text($"Điện thoại: {phone}").FontSize(9).FontColor(Muted);
                    });

                    row.RelativeItem(0.9f).Column(c =>
                    {
                        c.Item().AlignRight().Text(title).Bold().FontSize(20).FontColor(Head);
                        c.Item().AlignRight().Text(subtitle).FontSize(10).FontColor(Muted);
                        c.Item().PaddingTop(5).AlignRight().Text(t =>
                        {
                            t.Span($"{codeLabel}: ").FontSize(9).FontColor(Muted);
                            t.Span(Fallback(code, "—")).Bold().FontSize(12);
                        });
                        c.Item().AlignRight().Text(t =>
                        {
                            t.Span("Ngày: ").FontSize(9).FontColor(Muted);
                            t.Span(FormatDate(date)).Bold();
                        });
                    });
                });

                col.Item().PaddingTop(7).LineHorizontal(2).LineColor(Head);
            });
        }

        private static void ComposeFooter(IContainer container, string leftText)
        {
            container.BorderTop(0.5f).BorderColor(Line).PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Text(leftText).FontSize(8.5f).FontColor(Muted);
                row.RelativeItem().AlignCenter().Text("Chứng từ điện tử nội bộ").FontSize(8.5f).FontColor(Muted);
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span("Trang ").FontSize(8.5f).FontColor(Muted);
                    t.CurrentPageNumber().FontSize(8.5f).FontColor(Muted);
                    t.Span(" / ").FontSize(8.5f).FontColor(Muted);
                    t.TotalPages().FontSize(8.5f).FontColor(Muted);
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

        private static void Total(RowDescriptor row, string label, string value)
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text(label).FontSize(8.5f).FontColor(Muted);
                c.Item().Text(value).Bold().FontSize(10);
            });
        }

        private static void HeadCell(IContainer cell, string text, bool alignRight = false)
        {
            var content = cell.Background(Soft).Border(0.5f).BorderColor(Line).Padding(4);

            if (alignRight)
            {
                content = content.AlignRight();
            }

            content.Text(text).Bold().FontSize(8.5f).FontColor(Head);
        }

        private static void BodyCell(
            IContainer cell,
            string? text,
            bool bold = false,
            bool alignRight = false,
            bool alignCenter = false,
            string? background = null,
            float fontSize = 9f)
        {
            var content = cell.Border(0.5f).BorderColor(Line);

            if (background != null)
            {
                content = content.Background(background);
            }

            content = content.Padding(4);

            if (alignRight)
            {
                content = content.AlignRight();
            }
            else if (alignCenter)
            {
                content = content.AlignCenter();
            }

            var span = content.Text(string.IsNullOrWhiteSpace(text) ? string.Empty : text).FontSize(fontSize);

            if (bold)
            {
                span.Bold();
            }
        }

        /// <summary>Ô vuông để người bốc tick bằng bút — cố tình để trống, không in ký tự nào.</summary>
        private static void TickCell(IContainer cell)
        {
            cell.Border(0.5f).BorderColor(Line).Padding(4).AlignCenter().AlignMiddle()
                .Width(13).Height(13).Border(0.9f).BorderColor("#94a3b8")
                .Text(string.Empty);
        }

        /// <summary>Đổi mã hệ thống sang nhãn tiếng Việt; mã lạ thì in nguyên để không mất thông tin.</summary>
        private static string? MapLabel(Dictionary<string, string> labels, string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            return labels.TryGetValue(code.Trim(), out var label) ? label : code;
        }

        private static T? Lookup<T>(Dictionary<Guid, T> source, Guid? id) where T : class
        {
            if (id == null || !source.TryGetValue(id.Value, out var value))
            {
                return null;
            }

            return value;
        }

        private static bool IsStatus(string? status, string expected) =>
            string.Equals(status?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

        private static bool IsRemoved(string? status) => IsStatus(status, WroParcelStatuses.Removed);
        private static bool IsReserved(string? status) => IsStatus(status, WroParcelStatuses.Reserved);
        private static bool IsPicked(string? status) => IsStatus(status, WroParcelStatuses.Picked);

        /// <summary>
        /// Vị trí kiện dạng "KHU · KỆ · Ô". In đủ ba cấp vì mã ô kệ chỉ duy nhất trong một kệ —
        /// đưa mỗi mã ô cho người bốc thì kho nhiều khu sẽ có vài chỗ trùng tên.
        /// </summary>
        private static string FormatBinLabel(Guid? binId, ExportLookups lookups)
        {
            var bin = Lookup(lookups.Bins, binId);
            if (bin == null)
            {
                return "—";
            }

            var shelf = Lookup(lookups.Shelves, bin.ShelfId);
            var zone = Lookup(lookups.Zones, shelf?.ZoneId);

            var parts = new[] { zone?.ZoneCode ?? zone?.ZoneName, shelf?.ShelfCode, bin.BinCode }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();

            return parts.Length == 0 ? "—" : string.Join(" · ", parts);
        }

        /// <summary>
        /// Khoá sắp xếp cho danh sách soạn hàng: đi theo khu → kệ → ô để người bốc chỉ phải đi một vòng kho,
        /// thay vì sắp theo mã kiện rồi chạy qua chạy lại giữa các dãy.
        /// Kiện chưa có ô kệ đẩy xuống cuối (tiền tố "zzz") để không cắt ngang lộ trình.
        /// </summary>
        private static string FormatBinSortKey(Guid? binId, ExportLookups lookups)
        {
            var bin = Lookup(lookups.Bins, binId);
            if (bin == null)
            {
                return "zzz";
            }

            var shelf = Lookup(lookups.Shelves, bin.ShelfId);
            var zone = Lookup(lookups.Zones, shelf?.ZoneId);

            return string.Join("|", zone?.ZoneCode ?? string.Empty, shelf?.ShelfCode ?? string.Empty, bin.BinCode ?? string.Empty);
        }

        private static string FormatWarehouseLabel(Warehouse? warehouse)
        {
            if (warehouse == null)
            {
                return "Chưa xác định";
            }

            return string.IsNullOrWhiteSpace(warehouse.WarehouseCode)
                ? Fallback(warehouse.WarehouseName, "Chưa xác định")
                : $"{Fallback(warehouse.WarehouseName, "Kho")} · {warehouse.WarehouseCode}";
        }

        private static string FormatRouteName(ShippingRoute? route)
        {
            if (route == null)
            {
                return "Chưa gắn tuyến";
            }

            return string.IsNullOrWhiteSpace(route.RouteCode)
                ? Fallback(route.RouteName, "Chưa gắn tuyến")
                : $"{route.RouteCode} — {Fallback(route.RouteName, "—")}";
        }

        private static string FormatRouteLeg(ShippingRoute? route)
        {
            if (route == null || (string.IsNullOrWhiteSpace(route.OriginCountry) && string.IsNullOrWhiteSpace(route.DestinationCountry)))
            {
                return "—";
            }

            return $"{Fallback(route.OriginCountry, "—")} → {Fallback(route.DestinationCountry, "—")}";
        }

        private static string Join(string? first, string? second)
        {
            var parts = new[] { first, second }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

            return parts.Length == 0 ? "—" : string.Join(" · ", parts);
        }

        private static string Fallback(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static string FormatNumber(decimal value) => Math.Round(value, 2).ToString("N2", Vi);

        /// <summary>
        /// Bộ ba kích thước dạng "40 × 30 × 20". Số đo kho nhập thường là số nguyên nên bỏ
        /// phần thập phân khi không cần, tránh đọc thành "40,00 × 30,00 × 20,00".
        /// </summary>
        private static string FormatDimensions(decimal? length, decimal? width, decimal? height)
        {
            if (length is not > 0 && width is not > 0 && height is not > 0)
            {
                return "—";
            }

            static string One(decimal? value)
            {
                var v = Math.Round(value ?? 0m, 2);
                return v == Math.Truncate(v) ? v.ToString("N0", Vi) : v.ToString("0.##", Vi);
            }

            return $"{One(length)} × {One(width)} × {One(height)}";
        }

        private static string FormatDate(DateTime? value) => value?.ToString("dd/MM/yyyy", Vi) ?? "—";

        private static string FormatDateTime(DateTime? value) => value?.ToString("dd/MM/yyyy HH:mm", Vi) ?? "—";

        /// <summary>
        /// Phần mã trong tên file tải về. Mã phiếu/lô do hệ thống sinh nên gần như luôn là ASCII,
        /// nhưng vẫn bỏ dấu và ký tự lạ vì tên file đi qua header HTTP và ổ đĩa của người tải —
        /// dấu tiếng Việt hoặc dấu cách ở đó là nguồn lỗi kinh điển.
        /// </summary>
        private static string FileCode(string? code, Guid fallbackId)
        {
            var raw = string.IsNullOrWhiteSpace(code) ? fallbackId.ToString() : code;
            var cleaned = new string(RemoveDiacritics(raw).Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());

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

        // ──────────────────────────────── Kiểu dữ liệu nội bộ ────────────────────────────────

        /// <summary>Bộ bảng tra cứu nạp một lần cho cả lần in, khoá theo Id.</summary>
        private sealed class ExportLookups
        {
            public Dictionary<Guid, Parcel> Parcels { get; init; } = new();
            public Dictionary<Guid, Order> Orders { get; init; } = new();
            public Dictionary<Guid, OrderItem> OrderItems { get; init; } = new();
            public Dictionary<Guid, Customer> Customers { get; init; } = new();
            public Dictionary<Guid, User> Users { get; init; } = new();
            public Dictionary<Guid, Bin> Bins { get; init; } = new();
            public Dictionary<Guid, Shelf> Shelves { get; init; } = new();
            public Dictionary<Guid, WarehouseZone> Zones { get; init; } = new();
            public Dictionary<Guid, Warehouse> Warehouses { get; init; } = new();
            public Dictionary<Guid, ShippingRoute> Routes { get; init; } = new();
            public Dictionary<Guid, Carrier> Carriers { get; init; } = new();
        }

        /// <summary>Một dòng kiện đã được dựng sẵn mọi chuỗi hiển thị.</summary>
        private sealed class ParcelLine
        {
            /// <summary>Trạng thái kiện trong phiếu (RESERVED / PICKED / HANDED_OVER / REMOVED).</summary>
            public string? Status { get; init; }

            public string PackageCode { get; init; } = "—";
            public string ConsignmentCode { get; init; } = "—";
            public string CustomerName { get; init; } = "—";
            public string ProductName { get; init; } = "—";
            public decimal Weight { get; init; }
            public string Dimensions { get; init; } = "—";

            /// <summary>Ô kệ phải tới lấy hàng — đã chốt trên phiếu nên không đổi sau khi kiện được bốc đi.</summary>
            public string SourceBinLabel { get; init; } = "—";

            public string SourceBinSortKey { get; init; } = string.Empty;

            /// <summary>Ô ở khu xuất kiện đang nằm; chỉ có nghĩa với kiện đã bốc.</summary>
            public string StagingBinLabel { get; init; } = "—";
        }

        private sealed class WroData
        {
            public WroRequest Request { get; init; } = null!;
            public ShippingRoute? Route { get; init; }
            public Warehouse? OriginWarehouse { get; init; }
            public Warehouse? DestinationWarehouse { get; init; }

            /// <summary>Người gửi phiếu đi duyệt; phiếu cũ chưa có cột này thì rơi về người tạo phiếu.</summary>
            public User? SubmittedBy { get; init; }

            /// <summary>Quản lý kho đã duyệt, null khi phiếu chưa được quyết định.</summary>
            public User? DecidedBy { get; init; }

            /// <summary>Mã phiếu gốc nếu phiếu này được tách ra, null với phiếu lập mới.</summary>
            public string? SplitFromCode { get; init; }

            public List<ParcelLine> Lines { get; init; } = new();
        }

        /// <summary>Một phiếu xuất và các kiện của nó trong manifest lô.</summary>
        private sealed class ManifestGroup
        {
            public WroRequest Request { get; init; } = null!;
            public List<ParcelLine> Lines { get; init; } = new();
        }

        private sealed class ManifestData
        {
            public Shipment Shipment { get; init; } = null!;
            public Carrier? Carrier { get; init; }
            public Warehouse? OriginWarehouse { get; init; }
            public Warehouse? DestinationWarehouse { get; init; }
            public List<ManifestGroup> Groups { get; init; } = new();
        }
    }
}
