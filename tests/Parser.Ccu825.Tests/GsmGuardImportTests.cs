using System;
using System.IO;
using System.Linq;
using KsitalTelemetryHub.Core;
using Xunit;

namespace KsitalTelemetryHub.Parser.Ccu825.Tests;

public class GsmGuardImportTests
{
    private static string GetBackupFilePath()
    {
        string current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "dist", "BackupData20260918.txt");
            if (File.Exists(candidate)) return candidate;

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        throw new FileNotFoundException("Файл dist/BackupData20260918.txt не найден.");
    }

    [Fact]
    public void ImportFromGsmGuard_ParsesRealBackupFile_CorrectCountAndDeduplication()
    {
        string filePath = GetBackupFilePath();
        var items = ImportExportService.ImportFromGsmGuard(filePath);

        // 1. Проверка общего количества уникальных котельных (228 из 251 исходных секций)
        Assert.Equal(228, items.Count);

        // 2. Проверка уникальности всех номеров телефонов
        var uniquePhones = items.Select(i => i.PhoneNumber).Distinct().Count();
        Assert.Equal(228, uniquePhones);

        // 3. Проверка нормализации номеров телефонов (+79...)
        Assert.All(items, item =>
        {
            Assert.StartsWith("+7", item.PhoneNumber);
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
            Assert.False(string.IsNullOrWhiteSpace(item.District));
            Assert.False(string.IsNullOrWhiteSpace(item.Password));
        });

        // 4. Проверка разбивки по типам оборудования
        int ksitalCount = items.Count(i => i.DeviceType == DeviceType.Ksital);
        int ccuCount = items.Count(i => i.DeviceType == DeviceType.Ccu825);
        int owenCount = items.Count(i => i.DeviceType == DeviceType.OwenPlc);

        Assert.Equal(228, items.Count);
        Assert.Equal(200, ksitalCount);
        Assert.Equal(22, ccuCount);
        Assert.Equal(6, owenCount);
        Assert.Equal(228, ksitalCount + ccuCount + owenCount);

        // 5. Проверка дедуплицированного объекта: "Мордовский Белый Ключ"
        var mbk = items.FirstOrDefault(i => i.Name.Contains("Белый Ключ"));
        Assert.NotNull(mbk);
        Assert.Contains("Мордовский", mbk.Name);
        Assert.Equal("Вешкайма", mbk.District);
        Assert.Equal(DeviceType.Ksital, mbk.DeviceType);
        Assert.Equal("00000", mbk.Password);

        // 6. Проверка объекта с неизвестным типом / АСТ: "Димитровград лагерь Юность" (+79279883942)
        // Должен быть смапплен в Ksital по умолчанию согласно Варианту А
        var astObj = items.FirstOrDefault(i => i.PhoneNumber == "+79279883942");
        Assert.NotNull(astObj);
        Assert.Contains("Юность", astObj.Name);
        Assert.Equal(DeviceType.Ksital, astObj.DeviceType);
    }

    [Fact]
    public void ImportFromFile_TxtExtension_RoutesToGsmGuard()
    {
        string filePath = GetBackupFilePath();
        var items = ImportExportService.ImportFromFile(filePath);

        Assert.Equal(228, items.Count);
    }
}
