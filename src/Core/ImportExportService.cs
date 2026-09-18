using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        if (filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return ImportFromGsmGuard(filePath);
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

    public static List<ImportExportItem> ImportFromGsmGuard(string filePath)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(1251);

        var objectsByPhone = new Dictionary<string, ImportExportItem>(StringComparer.OrdinalIgnoreCase);

        bool inObjectSection = false;
        string currentName = string.Empty;
        string currentDistrict = string.Empty;
        string currentDevicePhone = string.Empty;
        string currentPhone1 = string.Empty;
        string currentRawModel = string.Empty;
        string currentPassword = string.Empty;

        void FlushCurrentObject()
        {
            if (!inObjectSection) return;

            string rawPhone = !string.IsNullOrWhiteSpace(currentDevicePhone) ? currentDevicePhone : currentPhone1;
            string normalizedPhone = PhoneNumber.Normalize(rawPhone);

            if (!string.IsNullOrWhiteSpace(normalizedPhone))
            {
                var item = new ImportExportItem
                {
                    Id = Guid.NewGuid().ToString(),
                    District = string.IsNullOrWhiteSpace(currentDistrict) ? "Основной район" : currentDistrict,
                    Name = string.IsNullOrWhiteSpace(currentName) ? $"Объект {normalizedPhone}" : currentName,
                    PhoneNumber = normalizedPhone,
                    DeviceType = ParseDeviceType(currentRawModel),
                    Password = string.IsNullOrWhiteSpace(currentPassword) ? "00000" : currentPassword
                };

                if (!objectsByPhone.ContainsKey(normalizedPhone))
                {
                    objectsByPhone[normalizedPhone] = item;
                }
            }

            inObjectSection = false;
            currentName = string.Empty;
            currentDistrict = string.Empty;
            currentDevicePhone = string.Empty;
            currentPhone1 = string.Empty;
            currentRawModel = string.Empty;
            currentPassword = string.Empty;
        }

        foreach (var rawLine in File.ReadLines(filePath, encoding))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                FlushCurrentObject();

                if (line.StartsWith("[Object_", StringComparison.OrdinalIgnoreCase))
                {
                    string inner = line.Substring(8, line.Length - 9);
                    if (int.TryParse(inner, out _))
                    {
                        inObjectSection = true;
                    }
                }
                continue;
            }

            if (inObjectSection)
            {
                int eqIdx = line.IndexOf('=');
                if (eqIdx <= 0) continue;

                string key = line.Substring(0, eqIdx).Trim();
                string val = line.Substring(eqIdx + 1).Trim().Trim('"');

                if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    currentName = val;
                }
                else if (key.Equals("Address", StringComparison.OrdinalIgnoreCase))
                {
                    currentDistrict = val;
                }
                else if (key.Equals("DevicePhone", StringComparison.OrdinalIgnoreCase))
                {
                    currentDevicePhone = val;
                }
                else if (key.Equals("Phone1", StringComparison.OrdinalIgnoreCase))
                {
                    currentPhone1 = val;
                }
                else if (key.Equals("DeviceModel", StringComparison.OrdinalIgnoreCase))
                {
                    currentRawModel = val;
                }
                else if (key.Equals("Password", StringComparison.OrdinalIgnoreCase))
                {
                    currentPassword = val;
                }
            }
        }

        FlushCurrentObject();

        return objectsByPhone.Values.ToList();
    }

    public static DeviceType ParseDeviceType(string? rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType)) return DeviceType.Ksital;

        string t = rawType.Trim().ToLowerInvariant();

        if (t.Contains("ццу") || t.Contains("ccu") || t.Contains("rads"))
            return DeviceType.Ccu825;

        if (t.Contains("овен") || t.Contains("oven") || t.Contains("плк"))
            return DeviceType.OwenPlc;

        return DeviceType.Ksital;
    }
}
