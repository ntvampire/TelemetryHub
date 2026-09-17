using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace KsitalTelemetryHub.Core;

public class ImportExportItem
{
    public string Id { get; set; } = string.Empty;
    public string District { get; set; } = "Основной район";
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DeviceType DeviceType { get; set; } = DeviceType.Ksital;
    public string DeviceTypeName => DeviceType.ToString();
    public string Password { get; set; } = "00000";
}

public static class ImportExportService
{
    public static void ExportToExcel(string filePath, IEnumerable<ImportExportItem> targets)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Объекты");

        worksheet.Cell(1, 1).Value = "Район/Участок";
        worksheet.Cell(1, 2).Value = "Название";
        worksheet.Cell(1, 3).Value = "Телефон";
        worksheet.Cell(1, 4).Value = "Тип оборудования";
        worksheet.Cell(1, 5).Value = "Пароль устройства";

        int row = 2;
        foreach (var target in targets)
        {
            worksheet.Cell(row, 1).Value = string.IsNullOrWhiteSpace(target.District) ? "Основной район" : target.District;
            worksheet.Cell(row, 2).Value = target.Name;
            worksheet.Cell(row, 3).Value = target.PhoneNumber;
            worksheet.Cell(row, 4).Value = target.DeviceTypeName;
            worksheet.Cell(row, 5).Value = target.Password;
            row++;
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }

    public static List<ImportExportItem> ImportFromFile(string filePath)
    {
        if (filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return ImportFromCsv(filePath);
        }

        return ImportFromExcel(filePath);
    }

    public static List<ImportExportItem> ImportFromCsv(string filePath)
    {
        var targets = new List<ImportExportItem>();
        var lines = File.ReadAllLines(filePath);

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(new[] { ',', ';' });
            if (parts.Length < 2) continue;

            string district = "Основной район";
            string name = string.Empty;
            string phone = string.Empty;
            string rawType = string.Empty;
            string password = "00000";

            if (parts.Length >= 4)
            {
                district = parts[0].Trim();
                name = parts[1].Trim();
                phone = parts[2].Trim();
                rawType = parts[3].Trim();
                if (parts.Length >= 5) password = parts[4].Trim();
            }
            else if (parts.Length == 3)
            {
                // Старый 3-колоночный формат: Название, Телефон, Тип
                name = parts[0].Trim();
                phone = parts[1].Trim();
                rawType = parts[2].Trim();
            }
            else if (parts.Length == 2)
            {
                name = parts[0].Trim();
                phone = parts[1].Trim();
            }

            if (!string.IsNullOrWhiteSpace(phone))
            {
                targets.Add(new ImportExportItem
                {
                    Id = Guid.NewGuid().ToString(),
                    District = string.IsNullOrWhiteSpace(district) ? "Основной район" : district,
                    Name = string.IsNullOrWhiteSpace(name) ? $"Объект {phone}" : name,
                    PhoneNumber = PhoneNumber.Normalize(phone),
                    DeviceType = ParseDeviceType(rawType),
                    Password = string.IsNullOrWhiteSpace(password) ? "00000" : password
                });
            }
        }

        return targets;
    }

    public static List<ImportExportItem> ImportFromExcel(string filePath)
    {
        var targets = new List<ImportExportItem>();

        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheet(1);
        var range = worksheet.RangeUsed();
        if (range == null) return targets;

        var rows = range.RowsUsed().Skip(1);

        foreach (var row in rows)
        {
            int colCount = row.CellCount();
            if (colCount < 2) continue;

            string district = "Основной район";
            string name = string.Empty;
            string phone = string.Empty;
            string rawType = string.Empty;
            string password = "00000";

            if (colCount >= 4)
            {
                district = row.Cell(1).GetString().Trim();
                name = row.Cell(2).GetString().Trim();
                phone = row.Cell(3).GetString().Trim();
                rawType = row.Cell(4).GetString().Trim();
                if (colCount >= 5) password = row.Cell(5).GetString().Trim();
            }
            else if (colCount == 3)
            {
                name = row.Cell(1).GetString().Trim();
                phone = row.Cell(2).GetString().Trim();
                rawType = row.Cell(3).GetString().Trim();
            }
            else
            {
                name = row.Cell(1).GetString().Trim();
                phone = row.Cell(2).GetString().Trim();
            }

            if (!string.IsNullOrWhiteSpace(phone))
            {
                targets.Add(new ImportExportItem
                {
                    Id = Guid.NewGuid().ToString(),
                    District = string.IsNullOrWhiteSpace(district) ? "Основной район" : district,
                    Name = string.IsNullOrWhiteSpace(name) ? $"Объект {phone}" : name,
                    PhoneNumber = PhoneNumber.Normalize(phone),
                    DeviceType = ParseDeviceType(rawType),
                    Password = string.IsNullOrWhiteSpace(password) ? "00000" : password
                });
            }
        }

        return targets;
    }

    public static DeviceType ParseDeviceType(string? rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType)) return DeviceType.Ksital;

        string t = rawType.Trim().ToLowerInvariant();

        if (t.StartsWith("ццу") || t.StartsWith("ccu") || t.StartsWith("rads"))
            return DeviceType.Ccu825;

        if (t.StartsWith("овен") || t.StartsWith("oven") || t.StartsWith("плк"))
            return DeviceType.OwenPlc;

        return DeviceType.Ksital;
    }
}
