using FluentAssertions;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests;

public class SqlReadOnlyValidatorTests
{
    private readonly SqlReadOnlyValidator _validator = new();

    [Theory(DisplayName = "Validate_唯讀語句_應放行")]
    [InlineData("SELECT * FROM Users")]
    [InlineData("WITH cte AS (SELECT Id FROM Users) SELECT * FROM cte")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("DECLARE @x INT; SET @x = 1; SELECT @x")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SELECT * FROM sys.tables")]
    [InlineData("SET NOCOUNT ON; SELECT 1")]
    public void Validate_ReadOnlyStatements_ShouldPass(string sql)
    {
        var result = _validator.Validate(sql);
        result.IsValid.Should().BeTrue();
    }

    [Theory(DisplayName = "Validate_寫入或不允許語句_應拒絕")]
    [InlineData("INSERT INTO T VALUES (1)")]
    [InlineData("UPDATE T SET A = 1")]
    [InlineData("DELETE FROM T")]
    [InlineData("MERGE INTO T USING S ON T.Id = S.Id WHEN MATCHED THEN UPDATE SET A = 1;")]
    [InlineData("WITH cte AS (SELECT Id FROM Users) DELETE FROM cte")]
    [InlineData("SELECT 1; DELETE FROM T")]
    [InlineData("SELECT * INTO T2 FROM T")]
    [InlineData("EXEC sp_who")]
    [InlineData("EXECUTE dbo.MyProc")]
    [InlineData("DROP TABLE T")]
    [InlineData("TRUNCATE TABLE T")]
    [InlineData("CREATE TABLE T (Id INT)")]
    [InlineData("ALTER TABLE T ADD B INT")]
    public void Validate_WriteOrDisallowedStatements_ShouldReject(string sql)
    {
        var result = _validator.Validate(sql);
        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().NotBeNullOrEmpty();
    }

    [Fact(DisplayName = "Validate_語法錯誤_應拒絕並含行列資訊")]
    public void Validate_SyntaxError_ShouldRejectWithLocation()
    {
        var result = _validator.Validate("SELEC * FROM T");
        result.IsValid.Should().BeFalse();
        result.RejectReason.Should().Contain("語法錯誤");
    }

    [Fact(DisplayName = "Validate_空字串_應拒絕")]
    public void Validate_Empty_ShouldReject()
    {
        var result = _validator.Validate("   ");
        result.IsValid.Should().BeFalse();
    }
}
