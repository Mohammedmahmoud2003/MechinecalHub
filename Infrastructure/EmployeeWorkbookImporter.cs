using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HRSystem.Infrastructure;

public sealed record EmployeeWorkbookRow(string Code, string Name, string JobTitle);

public static class EmployeeWorkbookImporter
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly Regex CellRefRegex = new(@"^([A-Z]+)(\d+)$", RegexOptions.Compiled);

    public static IReadOnlyList<EmployeeWorkbookRow> ReadEmployees(Stream workbookStream)
    {
        using var archive = new ZipArchive(workbookStream, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = ReadSharedStrings(archive);
        var worksheetPath = ResolveFirstWorksheetPath(archive);
        var worksheet = XDocument.Load(archive.GetEntry(worksheetPath)!.Open());

        var employees = new List<EmployeeWorkbookRow>();
        foreach (var row in ReadSheetRows(worksheet, sharedStrings))
        {
            if (row.Count < 5)
            {
                continue;
            }

            var code = (row[2] ?? string.Empty).Trim();
            var name = (row[3] ?? string.Empty).Trim();
            var jobTitle = (row[4] ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!code.All(char.IsDigit))
            {
                continue;
            }

            employees.Add(new EmployeeWorkbookRow(code, name, jobTitle));
        }

        return employees;
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

    private static IReadOnlyList<IReadOnlyList<string?>> ReadSheetRows(XDocument worksheet, IReadOnlyList<string> sharedStrings)
    {
        var rows = new List<IReadOnlyList<string?>>();

        foreach (var row in worksheet.Root?.Element(SpreadsheetNs + "sheetData")?.Elements(SpreadsheetNs + "row") ?? [])
        {
            var cells = new SortedDictionary<int, string?>();

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

            var values = new string?[cells.Count == 0 ? 0 : cells.Keys.Max()];
            foreach (var pair in cells)
            {
                values[pair.Key - 1] = pair.Value;
            }

            rows.Add(values);
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
