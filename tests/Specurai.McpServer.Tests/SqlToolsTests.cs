using System.Data;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using Specurai.Domain.Entities;
using Specurai.Domain.Interfaces;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class SqlToolsTests
{
    [Fact(DisplayName = "dry_run_sql: 驗證失敗應回傳拒絕原因且標記未變更")]
    public async Task DryRunSql_Invalid_ShouldReturnRejectReason()
    {
        var repo = Substitute.For<ISqlDryRunRepository>();
        repo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = false,
                RejectReason = "僅支援 INSERT/UPDATE/DELETE 的 dry run"
            });

        var result = await SqlTools.DryRunSql(repo, "DROP TABLE T");

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Valid").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("RejectReason").GetString().Should().Contain("僅支援");
        doc.RootElement.GetProperty("DatabaseChanged").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "dry_run_sql: 語法錯誤應回傳行列明細")]
    public async Task DryRunSql_SyntaxError_ShouldReturnErrorDetails()
    {
        var repo = Substitute.For<ISqlDryRunRepository>();
        repo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = false,
                SyntaxErrors = [new DryRunSyntaxError { Line = 1, Column = 18, Message = "Incorrect syntax near WHERE" }]
            });

        var result = await SqlTools.DryRunSql(repo, "UPDATE T SET WHERE");

        using var doc = JsonDocument.Parse(result);
        var error = doc.RootElement.GetProperty("SyntaxErrors").EnumerateArray().First();
        error.GetProperty("Line").GetInt32().Should().Be(1);
        error.GetProperty("Column").GetInt32().Should().Be(18);
        error.GetProperty("Message").GetString().Should().Contain("WHERE");
    }

    [Fact(DisplayName = "dry_run_sql: 成功預演應回傳筆數、預覽與 RolledBack=true")]
    public async Task DryRunSql_Success_ShouldReturnPreviewAndRolledBack()
    {
        var preview = new DataTable();
        preview.Columns.Add("舊_Name", typeof(object));
        preview.Columns.Add("新_Name", typeof(object));
        preview.Rows.Add("張三", "張三丰");

        var repo = Substitute.For<ISqlDryRunRepository>();
        repo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Update,
                AffectedRowCount = 1,
                PreviewTable = preview,
                Warnings = ["警告一"]
            });

        var result = await SqlTools.DryRunSql(repo, "UPDATE Users SET Name = N'張三丰' WHERE Id = 1");

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Valid").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("StatementType").GetString().Should().Be("Update");
        doc.RootElement.GetProperty("AffectedRowCount").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("RolledBack").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("DatabaseChanged").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("PreviewColumns").EnumerateArray()
            .Select(e => e.GetString()).Should().ContainInOrder("舊_Name", "新_Name");
        doc.RootElement.GetProperty("PreviewRows").EnumerateArray().Should().HaveCount(1);
        doc.RootElement.GetProperty("Warnings").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("警告一");
    }

    [Fact(DisplayName = "dry_run_sql: 執行期錯誤應回傳 ExecutionError")]
    public async Task DryRunSql_ExecutionError_ShouldReturnError()
    {
        var repo = Substitute.For<ISqlDryRunRepository>();
        repo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DryRunResult
            {
                IsValid = true,
                StatementType = DryRunStatementType.Delete,
                ExecutionError = "此語句實際執行將會失敗：REFERENCE 條件約束衝突"
            });

        var result = await SqlTools.DryRunSql(repo, "DELETE FROM Users WHERE Id = 1");

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Valid").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("ExecutionError").GetString().Should().Contain("REFERENCE");
        doc.RootElement.GetProperty("DatabaseChanged").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "dry_run_sql: Repository 擲例外應回傳友善錯誤")]
    public async Task DryRunSql_RepositoryThrows_ShouldReturnFriendlyError()
    {
        var repo = Substitute.For<ISqlDryRunRepository>();
        repo.DryRunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<DryRunResult>>(_ => throw new InvalidOperationException("未設定資料庫連線"));

        var result = await SqlTools.DryRunSql(repo, "DELETE FROM T");

        result.Should().Contain("Dry run 執行失敗");
        result.Should().Contain("未設定資料庫連線");
    }
}
