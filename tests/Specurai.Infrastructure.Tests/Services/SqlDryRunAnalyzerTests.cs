using FluentAssertions;
using Specurai.Domain.Entities;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

public class SqlDryRunAnalyzerTests
{
    private readonly SqlDryRunAnalyzer _analyzer = new();

    // 正規化只發生在測試比對端，生產輸出必須保持原樣
    private static string NormalizeWhitespace(string sql) => System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ");

    [Fact(DisplayName = "Analyze: 合法 INSERT 應通過並分類為 Insert")]
    public void Analyze_ValidInsert_ShouldBeValidInsert()
    {
        var result = _analyzer.Analyze("INSERT INTO dbo.Users (Name) VALUES (N'測試')");

        result.IsValid.Should().BeTrue();
        result.StatementType.Should().Be(DryRunStatementType.Insert);
        result.TargetSchema.Should().Be("dbo");
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "Analyze: 合法 UPDATE 應通過並分類為 Update")]
    public void Analyze_ValidUpdate_ShouldBeValidUpdate()
    {
        var result = _analyzer.Analyze("UPDATE Users SET Name = N'新名' WHERE Id = 1");

        result.IsValid.Should().BeTrue();
        result.StatementType.Should().Be(DryRunStatementType.Update);
        result.TargetSchema.Should().BeNull();
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "Analyze: 合法 DELETE 應通過並分類為 Delete")]
    public void Analyze_ValidDelete_ShouldBeValidDelete()
    {
        var result = _analyzer.Analyze("DELETE FROM dbo.Users WHERE Id = 1");

        result.IsValid.Should().BeTrue();
        result.StatementType.Should().Be(DryRunStatementType.Delete);
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "Analyze: 註解開頭的 DML 應通過（現有前綴檢查會誤擋的情況）")]
    public void Analyze_DmlWithLeadingComment_ShouldBeValid()
    {
        var result = _analyzer.Analyze("-- 調整名稱\nUPDATE Users SET Name = N'x' WHERE Id = 1");

        result.IsValid.Should().BeTrue();
        result.StatementType.Should().Be(DryRunStatementType.Update);
    }

    [Fact(DisplayName = "Analyze: CTE 包裝的 UPDATE 應通過，但目標表無法解析為 null")]
    public void Analyze_CteUpdate_ShouldBeValidWithNullTarget()
    {
        var sql = "WITH cte AS (SELECT * FROM Users WHERE Id < 10) UPDATE cte SET Name = N'x'";
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeTrue();
        result.StatementType.Should().Be(DryRunStatementType.Update);
        result.TargetTable.Should().BeNull();
    }

    [Fact(DisplayName = "Analyze: UPDATE 別名目標應解析回 FROM 子句中的實際資料表")]
    public void Analyze_UpdateWithAliasTarget_ShouldResolveActualTable()
    {
        var sql = "UPDATE u SET u.Name = N'x' FROM dbo.Users u JOIN dbo.Orders o ON o.UserId = u.Id WHERE o.Id = 5";
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeTrue();
        result.TargetSchema.Should().Be("dbo");
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "Analyze: UPDATE 逗號 JOIN 別名目標應解析回 FROM 子句中的實際資料表")]
    public void Analyze_UpdateWithCommaJoinAliasTarget_ShouldResolveActualTable()
    {
        var sql = "UPDATE u SET u.Name = N'x' FROM dbo.Users u, dbo.Orders o WHERE o.UserId = u.Id";
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeTrue();
        result.TargetSchema.Should().Be("dbo");
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "Analyze: DELETE CROSS JOIN 別名目標應解析回 FROM 子句中的實際資料表")]
    public void Analyze_DeleteWithCrossJoinAliasTarget_ShouldResolveActualTable()
    {
        var sql = "DELETE u FROM dbo.Users u CROSS JOIN dbo.Orders o";
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeTrue();
        result.TargetSchema.Should().Be("dbo");
        result.TargetTable.Should().Be("Users");
    }

    [Fact(DisplayName = "Analyze: SELECT 應被拒絕")]
    public void Analyze_Select_ShouldBeRejected()
    {
        var result = _analyzer.Analyze("SELECT * FROM Users");

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("僅支援 INSERT/UPDATE/DELETE");
    }

    [Theory(DisplayName = "Analyze: DDL/TRUNCATE/EXEC 應被拒絕")]
    [InlineData("DROP TABLE Users")]
    [InlineData("TRUNCATE TABLE Users")]
    [InlineData("EXEC sp_help")]
    [InlineData("CREATE TABLE T (Id INT)")]
    [InlineData("ALTER TABLE Users ADD C INT")]
    public void Analyze_NonDml_ShouldBeRejected(string sql)
    {
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("僅支援 INSERT/UPDATE/DELETE");
    }

    [Fact(DisplayName = "Analyze: 多個陳述式應被拒絕")]
    public void Analyze_MultipleStatements_ShouldBeRejected()
    {
        var result = _analyzer.Analyze("DELETE FROM A WHERE Id = 1; DELETE FROM B WHERE Id = 2;");

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("僅允許單一");
    }

    [Fact(DisplayName = "Analyze: 空白輸入應被拒絕")]
    public void Analyze_EmptyInput_ShouldBeRejected()
    {
        var result = _analyzer.Analyze("   ");

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("未偵測到");
    }

    [Fact(DisplayName = "Analyze: 語法錯誤應回報行列位置")]
    public void Analyze_SyntaxError_ShouldReportLineAndColumn()
    {
        var result = _analyzer.Analyze("UPDATE Users SET WHERE Id = 1");

        result.IsValid.Should().BeFalse();
        result.SyntaxErrors.Should().NotBeEmpty();
        result.SyntaxErrors[0].Line.Should().BeGreaterThan(0);
        result.SyntaxErrors[0].Column.Should().BeGreaterThan(0);
    }

    [Fact(DisplayName = "Analyze: INSERT ... EXEC 應被拒絕")]
    public void Analyze_InsertExec_ShouldBeRejected()
    {
        var result = _analyzer.Analyze("INSERT INTO T EXEC dbo.SomeProc");

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("INSERT ... EXEC");
    }

    [Fact(DisplayName = "Analyze: 字串常值內含 DELETE 關鍵字的 INSERT 應通過")]
    public void Analyze_KeywordInsideStringLiteral_ShouldBeValid()
    {
        var result = _analyzer.Analyze("INSERT INTO Logs (Message) VALUES (N'DELETE FROM X 已執行')");

        result.IsValid.Should().BeTrue();
        result.StatementType.Should().Be(DryRunStatementType.Insert);
    }

    [Fact(DisplayName = "Analyze: 使用者已自帶 OUTPUT 子句應標記 HasUserOutputClause")]
    public void Analyze_UserOutputClause_ShouldBeFlagged()
    {
        var result = _analyzer.Analyze("DELETE FROM Users OUTPUT deleted.* WHERE Id = 1");

        result.IsValid.Should().BeTrue();
        result.HasUserOutputClause.Should().BeTrue();
    }

    [Fact(DisplayName = "Analyze: 未帶 OUTPUT 子句 HasUserOutputClause 應為 false")]
    public void Analyze_NoOutputClause_ShouldNotBeFlagged()
    {
        var result = _analyzer.Analyze("DELETE FROM Users WHERE Id = 1");

        result.HasUserOutputClause.Should().BeFalse();
    }

    [Fact(DisplayName = "RewriteWithOutput: INSERT 應注入 OUTPUT inserted.*")]
    public void RewriteWithOutput_Insert_ShouldInjectInsertedStar()
    {
        var rewritten = _analyzer.RewriteWithOutput("INSERT INTO dbo.Users (Name) VALUES (N'測試')");

        NormalizeWhitespace(rewritten).Should().ContainEquivalentOf("output inserted.*");
    }

    [Fact(DisplayName = "RewriteWithOutput: DELETE 應注入 OUTPUT deleted.*")]
    public void RewriteWithOutput_Delete_ShouldInjectDeletedStar()
    {
        var rewritten = _analyzer.RewriteWithOutput("DELETE FROM dbo.Users WHERE Id = 1");

        var normalized = NormalizeWhitespace(rewritten);
        normalized.Should().ContainEquivalentOf("output deleted.*");
        normalized.Should().ContainEquivalentOf("where");
    }

    [Fact(DisplayName = "RewriteWithOutput: UPDATE 有欄位清單應注入舊/新別名欄位")]
    public void RewriteWithOutput_UpdateWithColumns_ShouldInjectAliasedColumns()
    {
        var rewritten = _analyzer.RewriteWithOutput(
            "UPDATE Users SET Name = N'x' WHERE Id = 1",
            ["Id", "Name"]);

        var normalized = NormalizeWhitespace(rewritten);
        normalized.Should().Contain("[舊_Id]");
        normalized.Should().Contain("[新_Id]");
        normalized.Should().Contain("[舊_Name]");
        normalized.Should().Contain("[新_Name]");
        normalized.Should().ContainEquivalentOf("deleted.[Id]");
        normalized.Should().ContainEquivalentOf("inserted.[Name]");
    }

    [Fact(DisplayName = "RewriteWithOutput: UPDATE 無欄位清單應退回 deleted.*, inserted.*")]
    public void RewriteWithOutput_UpdateWithoutColumns_ShouldFallbackToStar()
    {
        var rewritten = _analyzer.RewriteWithOutput("UPDATE Users SET Name = N'x' WHERE Id = 1");

        var normalized = NormalizeWhitespace(rewritten);
        normalized.Should().ContainEquivalentOf("output deleted.*");
        normalized.Should().ContainEquivalentOf("inserted.*");
    }

    [Fact(DisplayName = "RewriteWithOutput: 使用者已自帶 OUTPUT 應沿用不重複注入")]
    public void RewriteWithOutput_UserOutputClause_ShouldNotInjectAgain()
    {
        var rewritten = _analyzer.RewriteWithOutput("DELETE FROM Users OUTPUT deleted.Id WHERE Id = 1");

        // 只有一個 OUTPUT，且是使用者原本的欄位
        System.Text.RegularExpressions.Regex.Matches(rewritten, "OUTPUT", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Count.Should().Be(1);
        rewritten.Should().ContainEquivalentOf("deleted.Id");
    }

    [Fact(DisplayName = "RewriteWithOutput: 字串常值中的雙空白與 Tab 應保持原樣不被竄改")]
    public void RewriteWithOutput_StringLiteralWithDoubleSpaceAndTab_ShouldPreserveLiteralAsIs()
    {
        var rewritten = _analyzer.RewriteWithOutput("INSERT INTO dbo.Logs (Message) VALUES (N'保留  雙空白\t與Tab')");

        rewritten.Should().Contain("保留  雙空白\t與Tab");
    }
}
