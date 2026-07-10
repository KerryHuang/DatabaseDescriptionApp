using System.Globalization;
using System.Threading;
using FluentAssertions;
using Specurai.Application.Models;
using Specurai.Application.Services;
using Specurai.Domain.Entities;

namespace Specurai.Application.Tests.Services;

public class UpdateSqlGeneratorTests
{
    private readonly UpdateSqlGenerator _generator = new();

    private static QueryColumnMetadata Col(string name, bool isKey = false, bool isReadOnly = false, Type? clrType = null) => new()
    {
        ColumnName = name,
        BaseSchema = "dbo",
        BaseTable = "SYS010",
        BaseColumn = name,
        IsKey = isKey,
        IsReadOnly = isReadOnly,
        ClrType = clrType ?? typeof(string)
    };

    private static UpdateSqlRequest Request(
        IReadOnlyList<QueryColumnMetadata> columns,
        IReadOnlyList<string> keys,
        params UpdateSqlRow[] rows) => new()
    {
        TargetSchema = "dbo",
        TargetTable = "SYS010",
        Columns = columns,
        KeyColumns = keys,
        Rows = rows
    };

    private static UpdateSqlRow Row(Dictionary<string, object?> original, Dictionary<string, object?> current) =>
        new() { Original = original, Current = current };

    [Fact(DisplayName = "單欄異動：SET 只含改過的欄位，WHERE 用主鍵原值")]
    public void Generate_單欄異動_應產生正確UPDATE()
    {
        var columns = new[] { Col("EMP_ID", isKey: true), Col("EMP_NAME") };
        var request = Request(columns, ["EMP_ID"],
            Row(new() { ["EMP_ID"] = "100719", ["EMP_NAME"] = "洪玉如" },
                new() { ["EMP_ID"] = "100719", ["EMP_NAME"] = "洪小玉" }));

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(1);
        result.Sql.Should().Contain("UPDATE [dbo].[SYS010]");
        result.Sql.Should().Contain("SET [EMP_NAME] = N'洪小玉'");
        result.Sql.Should().Contain("WHERE [EMP_ID] = N'100719'");
        result.Sql.Should().NotContain("[EMP_ID] = N'100719',"); // EMP_ID 未改，不進 SET
        result.Sql.TrimEnd().Should().EndWith(";");
    }

    [Fact(DisplayName = "無異動：StatementCount 為 0")]
    public void Generate_無異動_應回傳零句()
    {
        var columns = new[] { Col("EMP_ID", isKey: true), Col("EMP_NAME") };
        var request = Request(columns, ["EMP_ID"],
            Row(new() { ["EMP_ID"] = "1", ["EMP_NAME"] = "甲" },
                new() { ["EMP_ID"] = "1", ["EMP_NAME"] = "甲" }));

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(0);
        result.Sql.Should().BeEmpty();
    }

    [Fact(DisplayName = "多列多欄異動：每列一句 UPDATE")]
    public void Generate_多列異動_應每列一句()
    {
        var columns = new[] { Col("EMP_ID", isKey: true), Col("EMP_NAME"), Col("PWD") };
        var request = Request(columns, ["EMP_ID"],
            Row(new() { ["EMP_ID"] = "1", ["EMP_NAME"] = "甲", ["PWD"] = "a" },
                new() { ["EMP_ID"] = "1", ["EMP_NAME"] = "乙", ["PWD"] = "b" }),
            Row(new() { ["EMP_ID"] = "2", ["EMP_NAME"] = "丙", ["PWD"] = "c" },
                new() { ["EMP_ID"] = "2", ["EMP_NAME"] = "丙", ["PWD"] = "c" }),
            Row(new() { ["EMP_ID"] = "3", ["EMP_NAME"] = "丁", ["PWD"] = "d" },
                new() { ["EMP_ID"] = "3", ["EMP_NAME"] = "戊", ["PWD"] = "d" }));

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(2);
        result.Sql.Should().Contain("SET [EMP_NAME] = N'乙', [PWD] = N'b'");
        result.Sql.Should().Contain("SET [EMP_NAME] = N'戊'");
    }

    [Fact(DisplayName = "NULL 處理：SET 用 NULL、WHERE 原值 NULL 用 IS NULL")]
    public void Generate_NULL處理_應正確()
    {
        var columns = new[] { Col("EMP_ID"), Col("EMP_NAME") };
        var request = Request(columns, ["EMP_ID", "EMP_NAME"],
            Row(new() { ["EMP_ID"] = "1", ["EMP_NAME"] = null },
                new() { ["EMP_ID"] = "1", ["EMP_NAME"] = "新值" }));
        var result = _generator.Generate(request);
        result.Sql.Should().Contain("WHERE [EMP_ID] = N'1' AND [EMP_NAME] IS NULL");

        var request2 = Request(columns, ["EMP_ID"],
            Row(new() { ["EMP_ID"] = "1", ["EMP_NAME"] = "舊值" },
                new() { ["EMP_ID"] = "1", ["EMP_NAME"] = null }));
        var result2 = _generator.Generate(request2);
        result2.Sql.Should().Contain("SET [EMP_NAME] = NULL");
    }

    [Fact(DisplayName = "型別字面值：數字/日期/bit/Guid 格式正確")]
    public void Generate_型別字面值_應正確()
    {
        var columns = new[]
        {
            Col("Id", isKey: true, clrType: typeof(int)),
            Col("Amount", clrType: typeof(decimal)),
            Col("Birthday", clrType: typeof(DateTime)),
            Col("IsActive", clrType: typeof(bool)),
            Col("Token", clrType: typeof(Guid))
        };
        var guid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var request = Request(columns, ["Id"],
            Row(new() { ["Id"] = 5, ["Amount"] = 1.5m, ["Birthday"] = new DateTime(2026, 7, 10, 8, 30, 0), ["IsActive"] = false, ["Token"] = Guid.Empty },
                new() { ["Id"] = 5, ["Amount"] = 99.25m, ["Birthday"] = new DateTime(2026, 12, 31), ["IsActive"] = true, ["Token"] = guid }));

        var result = _generator.Generate(request);

        result.Sql.Should().Contain("[Amount] = 99.25");
        result.Sql.Should().Contain("[Birthday] = '2026-12-31 00:00:00.000'");
        result.Sql.Should().Contain("[IsActive] = 1");
        result.Sql.Should().Contain($"[Token] = '{guid}'");
        result.Sql.Should().Contain("WHERE [Id] = 5");
    }

    [Fact(DisplayName = "跳脫：字串單引號與識別字方括號")]
    public void Generate_跳脫_應正確()
    {
        var columns = new QueryColumnMetadata[]
        {
            new() { ColumnName = "Weird]Col", BaseSchema = "dbo", BaseTable = "T]1", BaseColumn = "Weird]Col", IsKey = true, ClrType = typeof(string) },
            new() { ColumnName = "Name", BaseSchema = "dbo", BaseTable = "T]1", BaseColumn = "Name", ClrType = typeof(string) }
        };
        var request = new UpdateSqlRequest
        {
            TargetSchema = "dbo",
            TargetTable = "T]1",
            Columns = columns,
            KeyColumns = ["Weird]Col"],
            Rows = [Row(new() { ["Weird]Col"] = "a", ["Name"] = "O'Brien" },
                        new() { ["Weird]Col"] = "a", ["Name"] = "O'Neil" })]
        };

        var result = _generator.Generate(request);

        result.Sql.Should().Contain("UPDATE [dbo].[T]]1]");
        result.Sql.Should().Contain("[Weird]]Col]");
        result.Sql.Should().Contain("N'O''Neil'");
    }

    [Fact(DisplayName = "編輯後為字串的數字欄位：依 ClrType 轉型後輸出數字字面值")]
    public void Generate_字串編輯值轉型_應輸出正確字面值()
    {
        var columns = new[] { Col("Id", isKey: true, clrType: typeof(int)), Col("Qty", clrType: typeof(int)) };
        var request = Request(columns, ["Id"],
            Row(new() { ["Id"] = 1, ["Qty"] = 10 },
                new() { ["Id"] = 1, ["Qty"] = "25" }));   // DataGrid 編輯後常是字串

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(1);
        result.Sql.Should().Contain("[Qty] = 25");
    }

    [Fact(DisplayName = "編輯值無法轉型：跳過該列並回報警告")]
    public void Generate_轉型失敗_應跳過並警告()
    {
        var columns = new[] { Col("Id", isKey: true, clrType: typeof(int)), Col("Qty", clrType: typeof(int)) };
        var request = Request(columns, ["Id"],
            Row(new() { ["Id"] = 1, ["Qty"] = 10 },
                new() { ["Id"] = 1, ["Qty"] = "abc" }));

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(0);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("Qty");
    }

    [Fact(DisplayName = "部分列轉型失敗：SQL 開頭加警告註解，正常列仍產生 UPDATE")]
    public void Generate_部分列轉型失敗_應於SQL開頭加警告註解且保留正常列()
    {
        var columns = new[] { Col("Id", isKey: true, clrType: typeof(int)), Col("Qty", clrType: typeof(int)) };
        var request = Request(columns, ["Id"],
            Row(new() { ["Id"] = 1, ["Qty"] = 10 },
                new() { ["Id"] = 1, ["Qty"] = "abc" }),
            Row(new() { ["Id"] = 2, ["Qty"] = 10 },
                new() { ["Id"] = 2, ["Qty"] = 20 }));

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(1);
        result.Sql.Should().StartWith("-- 警告：第 1 列");
        result.Sql.Should().Contain("UPDATE");
    }

    [Fact(DisplayName = "唯讀欄位（timestamp/identity）不進 SET 也不進 WHERE")]
    public void Generate_唯讀欄位_應排除()
    {
        var columns = new[]
        {
            Col("Id", isKey: true, clrType: typeof(int)),
            Col("Name"),
            Col("Ver", isReadOnly: true, clrType: typeof(byte[]))
        };
        var request = Request(columns, ["Id", "Ver"],
            Row(new() { ["Id"] = 1, ["Name"] = "甲", ["Ver"] = new byte[] { 1 } },
                new() { ["Id"] = 1, ["Name"] = "乙", ["Ver"] = new byte[] { 1 } }));

        var result = _generator.Generate(request);

        result.Sql.Should().NotContain("[Ver]");
        result.Sql.Should().Contain("WHERE [Id] = 1");
    }

    [Fact(DisplayName = "全欄位 fallback：加警告註解")]
    public void Generate_Fallback定位_應加警告註解()
    {
        var columns = new[] { Col("A"), Col("B") };
        var request = new UpdateSqlRequest
        {
            TargetSchema = "dbo",
            TargetTable = "SYS010",
            Columns = columns,
            KeyColumns = ["A", "B"],
            IsFallbackKeys = true,
            Rows = [Row(new() { ["A"] = "1", ["B"] = "x" }, new() { ["A"] = "1", ["B"] = "y" })]
        };

        var result = _generator.Generate(request);

        result.Sql.Should().StartWith("-- 警告：無主鍵定位，執行前請先 Dry Run 確認影響筆數");
        result.Sql.Should().Contain("WHERE [A] = N'1' AND [B] = N'x'");
    }

    [Fact(DisplayName = "複合主鍵：WHERE 帶入全部主鍵欄")]
    public void Generate_複合主鍵_應全數帶入WHERE()
    {
        var columns = new[] { Col("K1", isKey: true), Col("K2", isKey: true), Col("V") };
        var request = Request(columns, ["K1", "K2"],
            Row(new() { ["K1"] = "a", ["K2"] = "b", ["V"] = "1" },
                new() { ["K1"] = "a", ["K2"] = "b", ["V"] = "2" }));

        var result = _generator.Generate(request);

        result.Sql.Should().Contain("WHERE [K1] = N'a' AND [K2] = N'b'");
    }

    [Fact(DisplayName = "WHERE 全空（定位欄皆為 byte[]）：跳過該列並回報警告")]
    public void Generate_WHERE全空_應跳過並警告()
    {
        var columns = new[] { Col("Data"), Col("Ver", isReadOnly: true, clrType: typeof(byte[])) };
        var request = Request(columns, ["Ver"],
            Row(new() { ["Data"] = "舊", ["Ver"] = new byte[] { 1 } },
                new() { ["Data"] = "新", ["Ver"] = new byte[] { 1 } }));

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(0);
        result.Warnings.Should().Contain(w => w.Contains("無可用的定位欄位"));
    }

    [Fact(DisplayName = "原值與現值皆為顯示字串且未編輯：不應產生異動")]
    public void Generate_原值與現值皆為顯示字串且未編輯_不應產生異動()
    {
        // 模擬 Avalonia DataGrid TwoWay 綁定把顯示文字回寫進資料列的污染情境：
        // Original 與 Current 皆為字串，但實際值未變更，不應誤判為異動。
        var columns = new[]
        {
            Col("Id", isKey: true, clrType: typeof(int)),
            Col("IsActive", clrType: typeof(bool)),
            Col("Birthday", clrType: typeof(DateTime)),
            Col("Qty", clrType: typeof(int)),
            Col("Name")
        };
        var request = Request(columns, ["Id"],
            Row(
                new() { ["Id"] = "1", ["IsActive"] = "False", ["Birthday"] = "2022/6/30 上午 12:00:00", ["Qty"] = "1", ["Name"] = "洪玉如" },
                new() { ["Id"] = "1", ["IsActive"] = "False", ["Birthday"] = "2022/6/30 上午 12:00:00", ["Qty"] = "1", ["Name"] = "洪小玉" }));

        var result = _generator.Generate(request);

        result.StatementCount.Should().Be(1);
        result.Sql.Should().Contain("SET [Name] = N'洪小玉'");
        result.Sql.Should().NotContain("[IsActive]");
        result.Sql.Should().NotContain("[Birthday]");
        result.Sql.Should().NotContain("[Qty]");
    }

    [Fact(DisplayName = "原值為顯示字串的鍵欄：WHERE 應輸出型別正規化字面值")]
    public void Generate_原值為顯示字串的鍵欄_WHERE應輸出型別正規化字面值()
    {
        var columns = new[] { Col("Id", isKey: true, clrType: typeof(int)), Col("Name") };
        var request = Request(columns, ["Id"],
            Row(new() { ["Id"] = "100719", ["Name"] = "洪玉如" },
                new() { ["Id"] = "100719", ["Name"] = "洪小玉" }));

        var result = _generator.Generate(request);

        result.Sql.Should().Contain("[Id] = 100719");
        result.Sql.Should().NotContain("N'100719'");
    }

    [Fact(DisplayName = "日期字面值：非西曆文化下仍輸出西元年（InvariantCulture）")]
    public void Generate_日期字面值_應與文化無關()
    {
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("th-TH");

            var columns = new[] { Col("Id", isKey: true, clrType: typeof(int)), Col("Birthday", clrType: typeof(DateTime)) };
            var request = Request(columns, ["Id"],
                Row(new() { ["Id"] = 1, ["Birthday"] = new DateTime(2026, 1, 1) },
                    new() { ["Id"] = 1, ["Birthday"] = new DateTime(2026, 12, 31) }));

            var result = _generator.Generate(request);

            result.Sql.Should().Contain("[Birthday] = '2026-12-31 00:00:00.000'");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }
}
