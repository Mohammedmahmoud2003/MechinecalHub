using System.Globalization;
using System.Security;
using System.Text;
using System.Xml.Linq;
using System.IO.Compression;
using HRSystem.Models;

namespace HRSystem.Infrastructure;

public static class RotationWorkbookExporter
{
    private const string SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static byte[] BuildWorkbook(Rotation rotation, IReadOnlyList<RotationScheduleEntry> scheduleEntries, string sheetName)
    {
        var orderedEntries = scheduleEntries
            .OrderBy(entry => entry.WorkDate)
            .ThenBy(entry => entry.EmployeeName)
            .ThenBy(entry => entry.EmployeeCode)
            .ToList();

        var rows = orderedEntries
            .GroupBy(entry => $"{entry.EmployeeCode}|{entry.EmployeeName}|{entry.JobTitle}")
            .OrderBy(group => group.First().EmployeeName)
            .ThenBy(group => group.First().EmployeeCode)
            .ToList();

        var dates = orderedEntries
            .Select(entry => entry.WorkDate)
            .Distinct()
            .OrderBy(date => date)
            .ToList();

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", BuildContentTypesXml());
            AddEntry(archive, "_rels/.rels", BuildRootRelationshipsXml());
            AddEntry(archive, "xl/workbook.xml", BuildWorkbookXml(sheetName));
            AddEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsXml());
            AddEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(rotation, dates, rows, sheetName));
        }

        return memoryStream.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string BuildContentTypesXml() =>
        $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="{{ContentTypesNs}}">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        </Types>
        """;

    private static string BuildRootRelationshipsXml() =>
        $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="{{RelNs}}">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string BuildWorkbookXml(string sheetName) =>
        $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="{{SpreadsheetNs}}" xmlns:r="{{OfficeRelNs}}">
          <sheets>
            <sheet name="{{EscapeXml(sheetName)}}" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """;

    private static string BuildWorkbookRelationshipsXml() =>
        $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="{{RelNs}}">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        </Relationships>
        """;

    private static string BuildWorksheetXml(
        Rotation rotation,
        IReadOnlyList<DateOnly> dates,
        IReadOnlyList<IGrouping<string, RotationScheduleEntry>> rows,
        string sheetName)
    {
        var sheetRows = new List<string>();
        sheetRows.Add(BuildTextRow(1, $"Rotation: {rotation.Title}"));
        sheetRows.Add(BuildTextRow(2, $"Sheet: {sheetName}"));
        sheetRows.Add(BuildTextRow(3, $"Imported: {rotation.ImportedAtUtc:yyyy-MM-dd HH:mm:ss}"));

        var headerCells = new List<string>
        {
            BuildTextCell("A5", "Code"),
            BuildTextCell("B5", "Arabic name"),
            BuildTextCell("C5", "Job title")
        };

        for (var i = 0; i < dates.Count; i++)
        {
            headerCells.Add(BuildTextCell($"{GetColumnName(4 + i)}5", dates[i].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        sheetRows.Add($"""<row r="5">{string.Join(string.Empty, headerCells)}</row>""");

        var rowNumber = 6;
        foreach (var group in rows)
        {
            var first = group.First();
            var cells = new List<string>
            {
                BuildTextCell($"A{rowNumber}", first.EmployeeCode),
                BuildTextCell($"B{rowNumber}", first.EmployeeName),
                BuildTextCell($"C{rowNumber}", first.JobTitle)
            };

            var lookup = group.ToDictionary(entry => entry.WorkDate, entry => entry.Status);
            for (var index = 0; index < dates.Count; index++)
            {
                lookup.TryGetValue(dates[index], out var status);
                cells.Add(BuildTextCell($"{GetColumnName(4 + index)}{rowNumber}", status ?? string.Empty));
            }

            sheetRows.Add($"""<row r="{rowNumber}">{string.Join(string.Empty, cells)}</row>""");
            rowNumber++;
        }

        return $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <worksheet xmlns="{{SpreadsheetNs}}">
          <sheetData>
            {{string.Join(string.Empty, sheetRows)}}
          </sheetData>
        </worksheet>
        """;
    }

    private static string BuildTextRow(int rowNumber, string text) =>
        $"""<row r="{rowNumber}">{BuildTextCell($"A{rowNumber}", text)}</row>""";

    private static string BuildTextCell(string cellRef, string value) =>
        $"""<c r="{cellRef}" t="inlineStr"><is><t xml:space="preserve">{EscapeXml(value)}</t></is></c>""";

    private static string GetColumnName(int columnIndex)
    {
        var dividend = columnIndex;
        var columnName = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar(65 + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName;
    }

    private static string EscapeXml(string value) => SecurityElement.Escape(value) ?? string.Empty;
}
