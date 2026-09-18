using System;
using System.IO;
using System.Linq;
using System.Text;
using KsitalTelemetryHub.Core;
using Xunit;

namespace KsitalTelemetryHub.Parser.Ccu825.Tests;

public class GsmGuardImportTests
{
    private static string CreateSyntheticGsmGuardFile()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var enc1251 = Encoding.GetEncoding(1251);

        string tempPath = Path.Combine(Path.GetTempPath(), $"gsmguard_test_{Guid.NewGuid():N}.txt");

        // Синтетический файл формата GSMGuard в кодировке Windows-1251
        var content = @"[Count]
Value=5
[Object_0]
dbID=1
Name=""Котельная Северная""
Address=""Северный район""
Phone1=+79270000001
DevicePhone=+79270000001
DeviceModel=Кситал
Password=12345
[Object_0_Command_0]
Name=""Запрос""
Command=""Kak dela? #PWD#""
[Object_1]
dbID=2
Name=""Котельная Южная (CCU)""
Address=""Южный район""
Phone1=+79270000002
DevicePhone=+79270000002
DeviceModel=CCU825
Password=00000
[Object_2]
dbID=3
Name=""Котельная Восточная (ОВЕН)""
Address=""Восточный район""
Phone1=+79270000003
DevicePhone=+79270000003
DeviceModel=ОВЕН ПЛК
Password=00000
[Object_3]
dbID=4
Name=""Котельная Северная Дубликат""
Address=""Северный район""
Phone1=+79270000001
DevicePhone=+79270000001
DeviceModel=Кситал
Password=12345
[Object_4]
dbID=5
Name=""Котельная Западная (АСТ)""
Address=""Западный район""
Phone1=+79270000005
DevicePhone=+79270000005
DeviceModel=АСТ
Password=
[Object_4_Command_0]
Name=""Тест""
Command=""TEST""
";

        File.WriteAllText(tempPath, content, enc1251);
        return tempPath;
    }

    [Fact]
    public void ImportFromGsmGuard_ParsesSyntheticFile_CorrectDeduplicationAndTypes()
    {
        string filePath = CreateSyntheticGsmGuardFile();
        try
        {
            var items = ImportExportService.ImportFromGsmGuard(filePath);

            // 1. Из 5 секций объектов с одним дубликатом должно остаться ровно 4 уникальных объекта
            Assert.Equal(4, items.Count);

            // 2. Все номера нормализованы
            Assert.All(items, item =>
            {
                Assert.StartsWith("+7", item.PhoneNumber);
                Assert.False(string.IsNullOrWhiteSpace(item.Name));
                Assert.False(string.IsNullOrWhiteSpace(item.District));
                Assert.False(string.IsNullOrWhiteSpace(item.Password));
            });

            // 3. Проверка типов оборудования
            var ksital = items.FirstOrDefault(i => i.PhoneNumber == "+79270000001");
            Assert.NotNull(ksital);
            Assert.Equal(DeviceType.Ksital, ksital.DeviceType);
            Assert.Equal("Северный район", ksital.District);
            Assert.Equal("12345", ksital.Password);

            var ccu = items.FirstOrDefault(i => i.PhoneNumber == "+79270000002");
            Assert.NotNull(ccu);
            Assert.Equal(DeviceType.Ccu825, ccu.DeviceType);

            var owen = items.FirstOrDefault(i => i.PhoneNumber == "+79270000003");
            Assert.NotNull(owen);
            Assert.Equal(DeviceType.OwenPlc, owen.DeviceType);

            // 4. Оборудование АСТ смапплено в Кситал по умолчанию, пустой пароль заменен на 00000
            var ast = items.FirstOrDefault(i => i.PhoneNumber == "+79270000005");
            Assert.NotNull(ast);
            Assert.Equal(DeviceType.Ksital, ast.DeviceType);
            Assert.Equal("00000", ast.Password);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                try { File.Delete(filePath); } catch { }
            }
        }
    }

    [Fact]
    public void ImportFromFile_TxtExtension_RoutesToGsmGuard()
    {
        string filePath = CreateSyntheticGsmGuardFile();
        try
        {
            var items = ImportExportService.ImportFromFile(filePath);
            Assert.Equal(4, items.Count);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                try { File.Delete(filePath); } catch { }
            }
        }
    }
}
