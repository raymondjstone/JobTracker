using JobTracker.Models;
using ClosedXML.Excel;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Pdf;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;
using WordPageSize = DocumentFormat.OpenXml.Wordprocessing.PageSize;
using WordColor = DocumentFormat.OpenXml.Wordprocessing.Color;

namespace JobTracker.Services;

public class JsaReportService
{
    private readonly JobHistoryService _historyService;
    private readonly JobListingService _jobService;

    // Default JSA-relevant action types
    public static readonly HashSet<HistoryActionType> DefaultJsaActionTypes = new()
    {
        HistoryActionType.JobAdded,
        HistoryActionType.AppliedStatusChanged,
        HistoryActionType.ApplicationStageChanged,
        HistoryActionType.InterestChanged,
        HistoryActionType.SuitabilityChanged
    };

    public JsaReportService(JobHistoryService historyService, JobListingService jobService)
    {
        _historyService = historyService;
        _jobService = jobService;
    }

    public List<JsaReportGroup> GenerateReport(JsaReportFilter filter)
    {
        _historyService.ForceReload();

        // Get all history (unpaged)
        var allHistory = _historyService.GetHistory(null, 1, int.MaxValue);

        var entries = allHistory.Entries.AsEnumerable();

        // Filter by selected action types
        if (filter.SelectedActionTypes.Count > 0)
            entries = entries.Where(e => filter.SelectedActionTypes.Contains(e.ActionType));

        // Filter by date range
        if (filter.FromDate.HasValue)
            entries = entries.Where(e => e.Timestamp >= filter.FromDate.Value);
        if (filter.ToDate.HasValue)
            entries = entries.Where(e => e.Timestamp <= filter.ToDate.Value.AddDays(1));

        // Filter by change source
        if (filter.ChangeSource.HasValue)
            entries = entries.Where(e => e.ChangeSource == filter.ChangeSource.Value);

        // Filter by search term
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.ToLowerInvariant();
            entries = entries.Where(e =>
                e.JobTitle.ToLowerInvariant().Contains(term) ||
                e.Company.ToLowerInvariant().Contains(term) ||
                (e.Details?.ToLowerInvariant().Contains(term) ?? false));
        }

        var filteredEntries = entries.ToList();

        // Group by JobId
        var groups = filteredEntries
            .Where(e => e.JobId != Guid.Empty)
            .GroupBy(e => e.JobId)
            .Select(g =>
            {
                var latestEntry = g.OrderByDescending(e => e.Timestamp).First();
                // Try to get the current job listing for the Source field
                JobListing? job = null;
                try { job = _jobService.GetJobById(g.Key); } catch { }

                return new JsaReportGroup
                {
                    JobId = g.Key,
                    JobTitle = latestEntry.JobTitle,
                    Company = latestEntry.Company,
                    JobUrl = latestEntry.JobUrl,
                    Source = job?.Source ?? "",
                    LatestActivity = g.Max(e => e.Timestamp),
                    Entries = g.OrderByDescending(e => e.Timestamp).ToList(),
                    JobExists = job != null
                };
            })
            .OrderByDescending(g => g.LatestActivity)
            .ToList();

        return groups;
    }

    public JsaReportSummary GetSummary(List<JsaReportGroup> groups, JsaReportFilter filter)
    {
        var allEntries = groups.SelectMany(g => g.Entries).ToList();
        var summary = new JsaReportSummary
        {
            TotalJobs = groups.Count,
            TotalActivities = allEntries.Count,
            DateFrom = filter.FromDate ?? allEntries.MinBy(e => e.Timestamp)?.Timestamp.Date,
            DateTo = filter.ToDate ?? allEntries.MaxBy(e => e.Timestamp)?.Timestamp.Date,
            JobsAppliedTo = groups.Count(g => g.Entries.Any(e => e.ActionType == HistoryActionType.AppliedStatusChanged && e.NewValue == "Applied")),
            JobsAddedCount = allEntries.Count(e => e.ActionType == HistoryActionType.JobAdded),
            ActionTypeCounts = allEntries.GroupBy(e => e.ActionType).ToDictionary(g => g.Key, g => g.Count())
        };

        // Calculate weekly average
        if (summary.DateFrom.HasValue && summary.DateTo.HasValue)
        {
            var weeks = Math.Max(1, (summary.DateTo.Value - summary.DateFrom.Value).Days / 7.0);
            summary.ActivitiesPerWeek = Math.Round(allEntries.Count / weeks, 1);
        }

        return summary;
    }

    public byte[] ExportToExcel(List<JsaReportGroup> groups, JsaReportSummary summary, string appBaseUrl)
    {
        using var workbook = new XLWorkbook();

        // Summary sheet
        var summarySheet = workbook.Worksheets.Add("Summary");
        summarySheet.Cell(1, 1).Value = "JSA Job Search Activity Report";
        summarySheet.Cell(1, 1).Style.Font.Bold = true;
        summarySheet.Cell(1, 1).Style.Font.FontSize = 16;

        summarySheet.Cell(3, 1).Value = "Report Period:";
        summarySheet.Cell(3, 1).Style.Font.Bold = true;
        summarySheet.Cell(3, 2).Value = $"{summary.DateFrom:dd/MM/yyyy} - {summary.DateTo:dd/MM/yyyy}";

        summarySheet.Cell(4, 1).Value = "Total Jobs Tracked:";
        summarySheet.Cell(4, 1).Style.Font.Bold = true;
        summarySheet.Cell(4, 2).Value = summary.TotalJobs;

        summarySheet.Cell(5, 1).Value = "Jobs Applied To:";
        summarySheet.Cell(5, 1).Style.Font.Bold = true;
        summarySheet.Cell(5, 2).Value = summary.JobsAppliedTo;

        summarySheet.Cell(6, 1).Value = "Total Activities:";
        summarySheet.Cell(6, 1).Style.Font.Bold = true;
        summarySheet.Cell(6, 2).Value = summary.TotalActivities;

        summarySheet.Cell(7, 1).Value = "Avg Activities/Week:";
        summarySheet.Cell(7, 1).Style.Font.Bold = true;
        summarySheet.Cell(7, 2).Value = summary.ActivitiesPerWeek;

        summarySheet.Cell(9, 1).Value = "Activity Breakdown:";
        summarySheet.Cell(9, 1).Style.Font.Bold = true;
        var row = 10;
        foreach (var kvp in summary.ActionTypeCounts.OrderByDescending(x => x.Value))
        {
            summarySheet.Cell(row, 1).Value = GetActionTypeDisplay(kvp.Key);
            summarySheet.Cell(row, 2).Value = kvp.Value;
            row++;
        }
        summarySheet.Columns().AdjustToContents();

        // Detail sheet
        var detailSheet = workbook.Worksheets.Add("Job Search Activity");
        var headers = new[] { "Job Title", "Company/Advertiser", "Job Site", "Date", "Time", "Activity", "Details", "Old Value", "New Value", "Job Posting URL", "App Link" };
        for (int i = 0; i < headers.Length; i++)
        {
            detailSheet.Cell(1, i + 1).Value = headers[i];
            detailSheet.Cell(1, i + 1).Style.Font.Bold = true;
            detailSheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        }

        row = 2;
        foreach (var group in groups)
        {
            foreach (var entry in group.Entries)
            {
                detailSheet.Cell(row, 1).Value = group.JobTitle;
                detailSheet.Cell(row, 2).Value = group.Company;
                detailSheet.Cell(row, 3).Value = group.Source;
                detailSheet.Cell(row, 4).Value = entry.Timestamp.ToString("dd/MM/yyyy");
                detailSheet.Cell(row, 5).Value = entry.Timestamp.ToString("HH:mm");
                detailSheet.Cell(row, 6).Value = GetActionTypeDisplay(entry.ActionType);
                detailSheet.Cell(row, 7).Value = entry.Details ?? "";
                detailSheet.Cell(row, 8).Value = entry.OldValue ?? "";
                detailSheet.Cell(row, 9).Value = entry.NewValue ?? "";

                if (!string.IsNullOrEmpty(group.JobUrl))
                {
                    detailSheet.Cell(row, 10).SetHyperlink(new XLHyperlink(group.JobUrl));
                    detailSheet.Cell(row, 10).Value = group.JobUrl;
                    detailSheet.Cell(row, 10).Style.Font.FontColor = XLColor.Blue;
                }

                if (group.JobExists)
                {
                    var appLink = $"{appBaseUrl.TrimEnd('/')}/?jobId={group.JobId}";
                    detailSheet.Cell(row, 11).SetHyperlink(new XLHyperlink(appLink));
                    detailSheet.Cell(row, 11).Value = "Open in App";
                    detailSheet.Cell(row, 11).Style.Font.FontColor = XLColor.Blue;
                }

                row++;
            }
        }

        detailSheet.Columns().AdjustToContents();
        detailSheet.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ExportToPdf(List<JsaReportGroup> groups, JsaReportSummary summary, string appBaseUrl)
    {
        var document = new PdfDocument();
        document.Info.Title = "JSA Job Search Activity Report";

        var fontTitle = new XFont("Arial", 16, XFontStyle.Bold);
        var fontSubtitle = new XFont("Arial", 9, XFontStyle.Regular);
        var fontJobHeader = new XFont("Arial", 10, XFontStyle.Bold);
        var fontJobSource = new XFont("Arial", 8, XFontStyle.Regular);
        var fontUrl = new XFont("Arial", 7, XFontStyle.Regular);
        var fontTableHeader = new XFont("Arial", 7.5, XFontStyle.Bold);
        var fontTableCell = new XFont("Arial", 7.5, XFontStyle.Regular);
        var fontFooter = new XFont("Arial", 7, XFontStyle.Regular);

        // Column widths for the activity table (landscape A4 ~802 usable points at 40pt margins)
        double[] colWidths = { 65, 40, 80, 380, 110 };
        string[] colHeaders = { "Date", "Time", "Activity", "Details", "Change" };

        const double marginLeft = 40;
        const double marginTop = 40;
        const double marginBottom = 50;
        const double pageWidth = 842; // A4 landscape
        const double pageHeight = 595;
        const double usableHeight = pageHeight - marginTop - marginBottom;
        const double rowHeight = 14;
        const double headerRowHeight = 16;

        double y = marginTop;
        var page = document.AddPage();
        page.Width = pageWidth;
        page.Height = pageHeight;
        var gfx = XGraphics.FromPdfPage(page);
        int pageNum = 1;

        void DrawFooter(XGraphics g, int pNum)
        {
            g.DrawString($"Page {pNum}  |  Generated {DateTime.Now:dd/MM/yyyy HH:mm}",
                fontFooter, XBrushes.Gray,
                new XRect(0, pageHeight - 30, pageWidth, 20), XStringFormats.Center);
        }

        void NewPage()
        {
            DrawFooter(gfx, pageNum);
            page = document.AddPage();
            page.Width = pageWidth;
            page.Height = pageHeight;
            gfx = XGraphics.FromPdfPage(page);
            y = marginTop;
            pageNum++;
        }

        void EnsureSpace(double needed)
        {
            if (y + needed > marginTop + usableHeight)
                NewPage();
        }

        // Title
        gfx.DrawString("JSA Job Search Activity Report", fontTitle, XBrushes.Black,
            new XPoint(marginLeft, y + 16));
        y += 26;

        gfx.DrawString($"Report Period: {summary.DateFrom:dd/MM/yyyy} - {summary.DateTo:dd/MM/yyyy}", fontSubtitle, XBrushes.DarkGray,
            new XPoint(marginLeft, y + 10));
        y += 16;

        gfx.DrawString($"Total Jobs: {summary.TotalJobs}  |  Applied: {summary.JobsAppliedTo}  |  Activities: {summary.TotalActivities}  |  Avg/Week: {summary.ActivitiesPerWeek}",
            fontSubtitle, XBrushes.DarkGray, new XPoint(marginLeft, y + 10));
        y += 24;

        foreach (var group in groups)
        {
            // Estimate space needed: header + URL + table header + at least 1 row
            double estimatedSpace = 18 + (string.IsNullOrEmpty(group.JobUrl) ? 0 : 12) + headerRowHeight + rowHeight + 16;
            EnsureSpace(estimatedSpace);

            // Job header
            var headerText = group.JobTitle;
            if (!string.IsNullOrEmpty(group.Company))
                headerText += $"  -  {group.Company}";

            gfx.DrawString(headerText, fontJobHeader, XBrushes.Black, new XPoint(marginLeft, y + 10));

            if (!string.IsNullOrEmpty(group.Source))
            {
                gfx.DrawString(group.Source, fontJobSource, XBrushes.SteelBlue,
                    new XPoint(pageWidth - marginLeft - 80, y + 10));
            }
            y += 16;

            if (!string.IsNullOrEmpty(group.JobUrl))
            {
                gfx.DrawString(group.JobUrl, fontUrl, XBrushes.Blue, new XPoint(marginLeft, y + 8));
                y += 12;
            }

            // Table header
            EnsureSpace(headerRowHeight + rowHeight);
            double x = marginLeft;
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(220, 228, 240)), x, y, colWidths.Sum(), headerRowHeight);
            for (int i = 0; i < colHeaders.Length; i++)
            {
                gfx.DrawString(colHeaders[i], fontTableHeader, XBrushes.Black,
                    new XRect(x + 3, y, colWidths[i] - 6, headerRowHeight), XStringFormats.CenterLeft);
                x += colWidths[i];
            }
            y += headerRowHeight;

            // Table rows
            bool alternate = false;
            foreach (var entry in group.Entries)
            {
                EnsureSpace(rowHeight);

                if (alternate)
                    gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(245, 245, 250)), marginLeft, y, colWidths.Sum(), rowHeight);

                x = marginLeft;
                var cells = new[]
                {
                    entry.Timestamp.ToString("dd/MM/yyyy"),
                    entry.Timestamp.ToString("HH:mm"),
                    GetActionTypeDisplay(entry.ActionType),
                    TruncateText(entry.Details ?? "", 80),
                    !string.IsNullOrEmpty(entry.OldValue) && !string.IsNullOrEmpty(entry.NewValue)
                        ? $"{entry.OldValue} -> {entry.NewValue}" : ""
                };

                for (int i = 0; i < cells.Length; i++)
                {
                    gfx.DrawString(cells[i], fontTableCell, XBrushes.Black,
                        new XRect(x + 3, y, colWidths[i] - 6, rowHeight), XStringFormats.CenterLeft);
                    x += colWidths[i];
                }

                y += rowHeight;
                alternate = !alternate;
            }

            // Separator line
            y += 6;
            gfx.DrawLine(XPens.LightGray, marginLeft, y, pageWidth - marginLeft, y);
            y += 8;
        }

        DrawFooter(gfx, pageNum);

        using var ms = new MemoryStream();
        document.Save(ms, false);
        return ms.ToArray();
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..(maxLength - 3)] + "...";
    }

    public byte[] ExportToWord(List<JsaReportGroup> groups, JsaReportSummary summary, string appBaseUrl)
    {
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new WordDocument();
            var body = mainPart.Document.AppendChild(new Body());

            // Page setup - landscape
            var sectionProps = new SectionProperties(
                new WordPageSize { Width = 16838, Height = 11906, Orient = PageOrientationValues.Landscape },
                new PageMargin { Top = 720, Right = 720, Bottom = 720, Left = 720 }
            );

            // Title
            body.AppendChild(CreateParagraph("JSA Job Search Activity Report", true, "28", "000000"));
            body.AppendChild(CreateParagraph($"Report Period: {summary.DateFrom:dd/MM/yyyy} - {summary.DateTo:dd/MM/yyyy}", false, "20", "555555"));
            body.AppendChild(CreateParagraph($"Total Jobs: {summary.TotalJobs} | Applied: {summary.JobsAppliedTo} | Activities: {summary.TotalActivities} | Avg/Week: {summary.ActivitiesPerWeek}", false, "20", "555555"));
            body.AppendChild(CreateParagraph("", false, "20")); // Spacer

            foreach (var group in groups)
            {
                // Job header
                var jobTitle = $"{group.JobTitle}";
                if (!string.IsNullOrEmpty(group.Company))
                    jobTitle += $"  -  {group.Company}";
                if (!string.IsNullOrEmpty(group.Source))
                    jobTitle += $"  ({group.Source})";

                body.AppendChild(CreateParagraph(jobTitle, true, "22", "333333"));

                if (!string.IsNullOrEmpty(group.JobUrl))
                {
                    var urlPara = CreateHyperlinkParagraph(mainPart, group.JobUrl, group.JobUrl, "16");
                    body.AppendChild(urlPara);
                }

                if (group.JobExists)
                {
                    var appLink = $"{appBaseUrl.TrimEnd('/')}/?jobId={group.JobId}";
                    body.AppendChild(CreateHyperlinkParagraph(mainPart, appLink, "Open in Job Tracker", "16"));
                }

                // Activity table
                var table = new Table();
                var tableProperties = new TableProperties(
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Single, Size = 4 },
                        new BottomBorder { Val = BorderValues.Single, Size = 4 },
                        new LeftBorder { Val = BorderValues.Single, Size = 4 },
                        new RightBorder { Val = BorderValues.Single, Size = 4 },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                        new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }
                    ),
                    new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }
                );
                table.AppendChild(tableProperties);

                // Header row
                var headerRow = new TableRow();
                foreach (var h in new[] { "Date", "Time", "Activity", "Details", "Change" })
                {
                    headerRow.AppendChild(CreateTableCell(h, true, "D0D8E8"));
                }
                table.AppendChild(headerRow);

                foreach (var entry in group.Entries)
                {
                    var dataRow = new TableRow();
                    dataRow.AppendChild(CreateTableCell(entry.Timestamp.ToString("dd/MM/yyyy")));
                    dataRow.AppendChild(CreateTableCell(entry.Timestamp.ToString("HH:mm")));
                    dataRow.AppendChild(CreateTableCell(GetActionTypeDisplay(entry.ActionType)));
                    dataRow.AppendChild(CreateTableCell(entry.Details ?? ""));
                    dataRow.AppendChild(CreateTableCell(
                        !string.IsNullOrEmpty(entry.OldValue) && !string.IsNullOrEmpty(entry.NewValue)
                            ? $"{entry.OldValue} -> {entry.NewValue}" : ""));
                    table.AppendChild(dataRow);
                }

                body.AppendChild(table);
                body.AppendChild(CreateParagraph("", false, "16")); // Spacer
            }

            body.AppendChild(sectionProps);
            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    private static Paragraph CreateParagraph(string text, bool bold = false, string? fontSize = null, string? color = null)
    {
        var run = new Run();
        var runProps = new RunProperties();
        if (bold) runProps.AppendChild(new Bold());
        if (fontSize != null) runProps.AppendChild(new FontSize { Val = fontSize });
        if (color != null) runProps.AppendChild(new WordColor { Val = color });
        run.AppendChild(runProps);
        run.AppendChild(new Text(text));
        return new Paragraph(run);
    }

    private static Paragraph CreateHyperlinkParagraph(MainDocumentPart mainPart, string url, string displayText, string? fontSize = null)
    {
        var relId = mainPart.AddHyperlinkRelationship(new Uri(url), true).Id;
        var run = new Run(
            new RunProperties(
                new WordColor { Val = "0563C1" },
                new Underline { Val = UnderlineValues.Single },
                new FontSize { Val = fontSize ?? "18" }
            ),
            new Text(displayText)
        );
        var hyperlink = new Hyperlink(run) { Id = relId };
        return new Paragraph(hyperlink);
    }

    private static TableCell CreateTableCell(string text, bool bold = false, string? bgColor = null)
    {
        var run = new Run();
        var runProps = new RunProperties();
        if (bold) runProps.AppendChild(new Bold());
        runProps.AppendChild(new FontSize { Val = "18" });
        run.AppendChild(runProps);
        run.AppendChild(new Text(text));

        var para = new Paragraph(run);
        var cell = new TableCell(para);

        if (bgColor != null)
        {
            var cellProps = new TableCellProperties(new Shading { Fill = bgColor, Val = ShadingPatternValues.Clear });
            cell.PrependChild(cellProps);
        }

        return cell;
    }

    public static string GetActionTypeDisplay(HistoryActionType action)
    {
        return action switch
        {
            HistoryActionType.JobAdded => "Job Added",
            HistoryActionType.AppliedStatusChanged => "Applied",
            HistoryActionType.ApplicationStageChanged => "Stage Change",
            HistoryActionType.InterestChanged => "Interest",
            HistoryActionType.SuitabilityChanged => "Suitability",
            _ => action.ToString()
        };
    }
}

public class JsaReportFilter
{
    public HashSet<HistoryActionType> SelectedActionTypes { get; set; } = new(JsaReportService.DefaultJsaActionTypes);
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public HistoryChangeSource? ChangeSource { get; set; }
    public string? SearchTerm { get; set; }
}

public class JsaReportGroup
{
    public Guid JobId { get; set; }
    public string JobTitle { get; set; } = "";
    public string Company { get; set; } = "";
    public string? JobUrl { get; set; }
    public string Source { get; set; } = "";
    public DateTime LatestActivity { get; set; }
    public List<JobHistoryEntry> Entries { get; set; } = new();
    public bool JobExists { get; set; }
}

public class JsaReportSummary
{
    public int TotalJobs { get; set; }
    public int TotalActivities { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int JobsAppliedTo { get; set; }
    public int JobsAddedCount { get; set; }
    public double ActivitiesPerWeek { get; set; }
    public Dictionary<HistoryActionType, int> ActionTypeCounts { get; set; } = new();
}
