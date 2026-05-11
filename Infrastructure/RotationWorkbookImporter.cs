using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HRSystem.Infrastructure;

public sealed record RotationWorkbookEmployee(string Code, string Name, string JobTitle);

public sealed record RotationWorkbookDayEntry(
    string Code,
    string Name,
    string JobTitle,
    DateOnly WorkDate,
    string Status);

public sealed class RotationWorkbookImportResult
{
    public string SheetName { get; init; } = string.Empty;

    public string RotationTitle { get; init; } = string.Empty;

    public DateOnly StartDate { get; init; }

    public DateOnly EndDate { get; init; }

    public IReadOnlyList<RotationWorkbookEmployee> Employees { get; init; } = [];

    public IReadOnlyList<RotationWorkbookDayEntry> DayEntries { get; init; } = [];
}

public static class RotationWorkbookImporter
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly Regex CellRefRegex = new(@"^([A-Z]+)(\d+)$", RegexOptions.Compiled);

    public static RotationWorkbookImportResult Read(Stream workbookStream)
    {
        using var archive = new ZipArchive(workbookStream, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = ReadSharedStrings(archive);
        var worksheetPath = ResolveFirstWorksheetPath(archive);
        var sheetName = ResolveFirstSheetName(archive);
        var worksheet = XDocument.Load(archive.GetEntry(worksheetPath)!.Open());
        var rows = ReadSheetRows(worksheet, sharedStrings);

        var headerCells = rows.TryGetValue(5, out var headerRowCells) ? headerRowCells : new Dictionary<int, string?>();
        var dateColumns = headerCells
            .Where(kvp => TryParseExcelDate(kvp.Value, out _))
            .ToDictionary(kvp => kvp.Key, kvp =>
            {
                TryParseExcelDate(kvp.Value, out var date);
                return date;
            });

        if (dateColumns.Count == 0)
        {
            throw new InvalidOperationException("The workbook does not contain any rotation date columns.");
        }

        var startDate = dateColumns.Values.Min();
        var endDate = dateColumns.Values.Max();
        var employees = new List<RotationWorkbookEmployee>();
        var dayEntries = new List<RotationWorkbookDayEntry>();

        foreach (var (rowIndex, cells) in rows.Where(x => x.Key >= 7).OrderBy(x => x.Key))
        {
            if (!cells.TryGetValue(3, out var codeValue) || !cells.TryGetValue(4, out var nameValue))
            {
                continue;
            }

            var code = (codeValue ?? string.Empty).Trim();
            var name = (nameValue ?? string.Empty).Trim();
            var jobTitle = cells.TryGetValue(5, out var jobTitleValue) ? (jobTitleValue ?? string.Empty).Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || !code.All(char.IsDigit))
            {
                continue;
            }

            employees.Add(new RotationWorkbookEmployee(code, name, jobTitle));

            foreach (var dateColumn in dateColumns)
            {
                if (!cells.TryGetValue(dateColumn.Key, out var statusValue))
                {
                    continue;
                }

                var status = (statusValue ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(status))
                {
                    continue;
                }

                dayEntries.Add(new RotationWorkbookDayEntry(code, name, jobTitle, dateColumn.Value, status));
            }
        }

        var rotationTitle = BuildRotationTitle(sheetName, startDate, endDate);
        return new RotationWorkbookImportResult
        {
            SheetName = sheetName,
            RotationTitle = rotationTitle,
            StartDate = startDate,
            EndDate = endDate,
            Employees = employees,
            DayEntries = dayEntries
        };
    }

    private static string BuildRotationTitle(string sheetName, DateOnly startDate, DateOnly endDate)
    {
        if (startDate.Month == endDate.Month && startDate.Year == endDate.Year)
        {
            return $"Mechanical Team Rotation - {startDate:MMMM yyyy}";
        }

        return $"Mechanical Team Rotation - {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd} ({sheetName})";
    }

    private static string ResolveFirstWorksheetPath(ZipArchive archive)
    {
        using var workbookStream = archive.GetEntry("xl/workbook.xml")!.Open();
        using var relsStream = archive.GetEntry("xl/_rels/workbook.xml.rels")!.Open();

        var workbook = XDocument.Load(workbookStream);
        var relationships = XDocument.Load(relsStream);

        var firstSheet = workbook.Root?
            .Element(SpreadsheetNs + "sheets")?
            .Elements(SpreadsheetNs + "sheet")
            .FirstOrDefault()
            ?? throw new InvalidOperationException("The workbook does not contain any sheets.");

        var relId = firstSheet.Attribute(OfficeRelNs + "id")?.Value
            ?? throw new InvalidOperationException("Unable to resolve the first sheet relationship.");

        var target = relationships.Root?
            .Elements(RelNs + "Relationship")
            .FirstOrDefault(x => string.Equals(x.Attribute("Id")?.Value, relId, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("Target")?.Value
            ?? throw new InvalidOperationException("Unable to resolve the first worksheet target.");

        target = target.TrimStart('/');
        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? target
            : $"xl/{target}";
    }

    private static string ResolveFirstSheetName(ZipArchive archive)
    {
        using var workbookStream = archive.GetEntry("xl/workbook.xml")!.Open();
        var workbook = XDocument.Load(workbookStream);
        return workbook.Root?
            .Element(SpreadsheetNs + "sheets")?
            .Elements(SpreadsheetNs + "sheet")
            .FirstOrDefault()?
            .Attribute("name")?.Value
            ?? string.Empty;
    }

    private static Dictionary<int, Dictionary<int, string?>> ReadSheetRows(XDocument worksheet, IReadOnlyList<string> sharedStrings)
    {
        var rows = new Dictionary<int, Dictionary<int, string?>>();

        foreach (var row in worksheet.Root?.Element(SpreadsheetNs + "sheetData")?.Elements(SpreadsheetNs + "row") ?? [])
        {
            var rowIndex = int.Parse(row.Attribute("r")?.Value ?? "0");
            var cells = new Dictionary<int, string?>();

            foreach (var cell in row.Elements(SpreadsheetNs + "c"))
            {
                var refValue = cell.Attribute("r")?.Value;
                if (string.IsNullOrWhiteSpace(refValue))
                {
                    continue;
                }

                var match = CellRefRegex.Match(refValue);
                if (!match.Success)
                {
                    continue;
                }

                var columnIndex = ColumnToIndex(match.Groups[1].Value);
                cells[columnIndex] = ReadCellValue(cell, sharedStrings);
            }

            rows[rowIndex] = cells;
        }

        return rows;
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var strings = new List<string>();

        foreach (var item in document.Root?.Elements(SpreadsheetNs + "si") ?? [])
        {
            strings.Add(string.Concat(item.Descendants(SpreadsheetNs + "t").Select(x => x.Value)));
        }

        return strings;
    }

    private static string? ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var cellType = cell.Attribute("t")?.Value;
        var value = cell.Element(SpreadsheetNs + "v")?.Value;

        return cellType switch
        {
            "s" when int.TryParse(value, out var index) && index >= 0 && index < sharedStrings.Count => sharedStrings[index],
            "inlineStr" => string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(x => x.Value)),
            _ => value
        };
    }

    private static bool TryParseExcelDate(string? value, out DateOnly date)
    {
        date = default;
        if (!double.TryParse(value, out var serial))
        {
            return false;
        }

        date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
        return true;
    }

    private static int ColumnToIndex(string columnName)
    {
        var result = 0;
        foreach (var ch in columnName)
        {
            result = (result * 26) + (ch - 'A' + 1);
        }

        return result;
    }
}
