using FluentAssertions;
using Specurai.Infrastructure.Services;
using Xunit;

namespace Specurai.Infrastructure.Tests.Services;

public class SqlDdlScriptAnalyzerTests
{
    private readonly SqlDdlScriptAnalyzer _analyzer = new();

    [Theory(DisplayName = "Analyze_白名單DDL_應通過並回報類型")]
    [InlineData("CREATE TABLE dbo.T1 (Id INT NOT NULL)", "CREATE TABLE")]
    [InlineData("ALTER TABLE dbo.T1 ADD C2 NVARCHAR(50) NULL", "ALTER TABLE")]
    [InlineData("DROP TABLE dbo.T1", "DROP TABLE")]
    [InlineData("CREATE NONCLUSTERED INDEX IX_T1_C2 ON dbo.T1 (C2)", "CREATE INDEX")]
    [InlineData("ALTER INDEX IX_T1_C2 ON dbo.T1 REBUILD", "ALTER INDEX")]
    [InlineData("DROP INDEX IX_T1_C2 ON dbo.T1", "DROP INDEX")]
    [InlineData("CREATE VIEW dbo.V1 AS SELECT 1 AS A", "CREATE VIEW")]
    [InlineData("CREATE OR ALTER VIEW dbo.V1 AS SELECT 1 AS A", "CREATE OR ALTER VIEW")]
    [InlineData("DROP VIEW dbo.V1", "DROP VIEW")]
    [InlineData("CREATE PROCEDURE dbo.P1 AS BEGIN SELECT 1 END", "CREATE PROCEDURE")]
    [InlineData("CREATE OR ALTER PROCEDURE dbo.P1 AS BEGIN SELECT 1 END", "CREATE OR ALTER PROCEDURE")]
    [InlineData("DROP PROCEDURE dbo.P1", "DROP PROCEDURE")]
    [InlineData("CREATE FUNCTION dbo.F1() RETURNS INT AS BEGIN RETURN 1 END", "CREATE FUNCTION")]
    [InlineData("DROP FUNCTION dbo.F1", "DROP FUNCTION")]
    [InlineData("CREATE TRIGGER dbo.TR1 ON dbo.T1 AFTER INSERT AS BEGIN SET NOCOUNT ON END", "CREATE TRIGGER")]
    [InlineData("DROP TRIGGER dbo.TR1", "DROP TRIGGER")]
    [InlineData("CREATE SCHEMA app", "CREATE SCHEMA")]
    [InlineData("DROP SCHEMA app", "DROP SCHEMA")]
    public void Analyze_白名單DDL_應通過並回報類型(string sql, string expectedType)
    {
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeTrue(result.RejectReason);
        result.Statements.Should().HaveCount(1);
        result.Statements[0].Type.Should().Be(expectedType);
        result.Batches.Should().HaveCount(1);
    }

    [Theory(DisplayName = "Analyze_非白名單語句_應拒絕")]
    [InlineData("CREATE DATABASE X")]
    [InlineData("ALTER DATABASE X SET RECOVERY SIMPLE")]
    [InlineData("DROP DATABASE X")]
    [InlineData("TRUNCATE TABLE dbo.T1")]
    [InlineData("GRANT SELECT ON dbo.T1 TO SomeUser")]
    [InlineData("CREATE USER SomeUser WITHOUT LOGIN")]
    [InlineData("CREATE LOGIN SomeLogin WITH PASSWORD = 'x'")]
    [InlineData("EXEC dbo.P1")]
    [InlineData("SELECT 1")]
    [InlineData("INSERT INTO dbo.T1 (Id) VALUES (1)")]
    [InlineData("UPDATE dbo.T1 SET Id = 1")]
    [InlineData("DELETE FROM dbo.T1")]
    [InlineData("CREATE SPATIAL INDEX IX ON dbo.T1(GeoCol)")]
    [InlineData("CREATE COLUMNSTORE INDEX IX ON dbo.T1 (C1)")]
    [InlineData("CREATE FULLTEXT INDEX ON dbo.T1(C) KEY INDEX PK1")]
    public void Analyze_非白名單語句_應拒絕(string sql)
    {
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("白名單");
    }

    [Theory(DisplayName = "Analyze_白名單型別內夾帶違規內容_應拒絕")]
    [InlineData("CREATE SCHEMA app AUTHORIZATION dbo GRANT SELECT ON dbo.T1 TO SomeUser", "CREATE SCHEMA")]
    [InlineData("CREATE TRIGGER TR ON DATABASE AFTER DROP_TABLE AS PRINT 1", "TRIGGER")]
    [InlineData("CREATE TRIGGER TR ON ALL SERVER AFTER CREATE_DATABASE AS PRINT 1", "TRIGGER")]
    [InlineData("ALTER TABLE dbo.T1 SWITCH TO dbo.T2", "SWITCH")]
    public void Analyze_白名單型別內夾帶違規內容_應拒絕(string sql, string expectedReasonFragment)
    {
        var result = _analyzer.Analyze(sql);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain(expectedReasonFragment);
    }

    [Fact(DisplayName = "Analyze_CreateSchema無內嵌語句_應通過")]
    public void Analyze_CreateSchema無內嵌語句_應通過()
    {
        var result = _analyzer.Analyze("CREATE SCHEMA app AUTHORIZATION dbo");

        result.IsValid.Should().BeTrue(result.RejectReason);
        result.Statements[0].Type.Should().Be("CREATE SCHEMA");
    }

    [Fact(DisplayName = "Analyze_混合批次含DML_應拒絕並指明句序")]
    public void Analyze_混合批次含DML_應拒絕並指明句序()
    {
        var script = "CREATE TABLE dbo.T1 (Id INT)\nGO\nINSERT INTO dbo.T1 (Id) VALUES (1)";

        var result = _analyzer.Analyze(script);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("第 2 句");
    }

    [Fact(DisplayName = "Analyze_GO分批_應正確切批並標記批次索引")]
    public void Analyze_GO分批_應正確切批並標記批次索引()
    {
        var script = "CREATE TABLE dbo.T1 (Id INT)\nGO\nCREATE OR ALTER PROCEDURE dbo.P1 AS BEGIN SELECT 1 END";

        var result = _analyzer.Analyze(script);

        result.IsValid.Should().BeTrue(result.RejectReason);
        result.Batches.Should().HaveCount(2);
        result.Statements.Should().HaveCount(2);
        result.Statements[0].BatchIndex.Should().Be(1);
        result.Statements[1].BatchIndex.Should().Be(2);
        result.Statements[1].Index.Should().Be(2);
        result.Batches[1].Should().Contain("PROCEDURE");
        result.Batches[1].Should().NotContain("CREATE TABLE");
    }

    [Fact(DisplayName = "Analyze_同批次多句DDL_應全數列入摘要")]
    public void Analyze_同批次多句DDL_應全數列入摘要()
    {
        var script = "CREATE TABLE dbo.T1 (Id INT);\nCREATE NONCLUSTERED INDEX IX_T1_Id ON dbo.T1 (Id);";

        var result = _analyzer.Analyze(script);

        result.IsValid.Should().BeTrue(result.RejectReason);
        result.Batches.Should().HaveCount(1);
        result.Statements.Should().HaveCount(2);
        result.Statements[1].BatchIndex.Should().Be(1);
    }

    [Fact(DisplayName = "Analyze_應解析目標物件名稱")]
    public void Analyze_應解析目標物件名稱()
    {
        var result = _analyzer.Analyze("CREATE TABLE dbo.T1 (Id INT)");

        result.Statements[0].ObjectName.Should().Be("[dbo].[T1]");
    }

    [Fact(DisplayName = "Analyze_語法錯誤_應回報明細")]
    public void Analyze_語法錯誤_應回報明細()
    {
        var result = _analyzer.Analyze("CREATE TABBLE dbo.T1 (Id INT)");

        result.IsValid.Should().BeFalse();
        result.SyntaxErrors.Should().NotBeEmpty();
    }

    [Theory(DisplayName = "Analyze_空Script_應拒絕")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("GO")]
    public void Analyze_空Script_應拒絕(string script)
    {
        var result = _analyzer.Analyze(script);

        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("未偵測到");
    }
}
