using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Specurai.Cli.Output;

namespace Specurai.Cli.Tests;

public class CliOutputTests : IDisposable
{
    private readonly TextWriter _originalOut;

    public CliOutputTests()
    {
        _originalOut = Console.Out;
        CliOutput.JsonMode = true;
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        CliOutput.JsonMode = false;
    }

    [Fact(DisplayName = "Success: JSON 模式應輸出正確結構")]
    public void Success_JsonMode_ShouldOutputCorrectStructure()
    {
        var output = Capture(() => CliOutput.Success(new { Name = "test", Value = 42 }));

        var doc = JsonDocument.Parse(output);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("data").GetProperty("name").GetString().Should().Be("test");
        doc.RootElement.GetProperty("data").GetProperty("value").GetInt32().Should().Be(42);
    }

    [Fact(DisplayName = "Success: 有 Count 時 Metadata 應包含 Count")]
    public void Success_WithCount_MetadataShouldContainCount()
    {
        var output = Capture(() => CliOutput.Success(new[] { 1, 2, 3 }, count: 3));

        var doc = JsonDocument.Parse(output);
        doc.RootElement.GetProperty("metadata").GetProperty("count").GetInt32().Should().Be(3);
    }

    [Fact(DisplayName = "Success: 無 Count 時 Metadata 應省略")]
    public void Success_NoCount_MetadataShouldBeOmitted()
    {
        var output = Capture(() => CliOutput.Success("simple data"));

        var doc = JsonDocument.Parse(output);
        doc.RootElement.TryGetProperty("metadata", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "Success: 中文資料不應被 Unicode 轉義")]
    public void Success_ChineseData_ShouldNotBeEscaped()
    {
        var output = Capture(() => CliOutput.Success(new { Name = "測試環境" }));

        output.Should().Contain("測試環境");
        output.Should().NotContain("\\u");
    }

    [Fact(DisplayName = "Error: JSON 模式應輸出錯誤結構")]
    public void Error_JsonMode_ShouldOutputErrorStructure()
    {
        var output = Capture(() => CliOutput.Error("連線失敗"));

        var doc = JsonDocument.Parse(output);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Be("連線失敗");
    }

    [Fact(DisplayName = "Success: 複雜物件應正確 camelCase 序列化")]
    public void Success_ComplexObject_ShouldUseCamelCase()
    {
        var output = Capture(() => CliOutput.Success(new { FirstName = "Tony", IsActive = true }));

        var doc = JsonDocument.Parse(output);
        var data = doc.RootElement.GetProperty("data");
        data.TryGetProperty("firstName", out _).Should().BeTrue();
        data.TryGetProperty("isActive", out _).Should().BeTrue();
        data.TryGetProperty("FirstName", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "Success: Null 資料時 data 應為 null")]
    public void Success_NullData_ShouldOutputNull()
    {
        var output = Capture(() => CliOutput.Success<string?>(null));

        output.Should().Contain("\"success\"");
        output.Should().Contain("true");
    }

    // 全域 JsonOptions 設有 WhenWritingNull，一般屬性為 null 時整個 key 會被省略。
    // SqlCommand.OutputJson 對 RolledBack／DatabaseChanged／Committed 需要「值為 null 但 key 仍存在」
    // （COMMIT 結果不確定時），因此標註 JsonIgnore(Condition = Never) 覆寫此行為。
    // 這裡驗證該覆寫機制本身有效，不受全域省略設定影響。
    private class NullableFieldDto
    {
        public bool Normal { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool? AlwaysPresent { get; init; }
    }

    [Fact(DisplayName = "Success: JsonIgnore(Never) 屬性應保留 null 值而非被省略")]
    public void Success_JsonIgnoreNeverProperty_ShouldKeepNullKey()
    {
        var output = Capture(() => CliOutput.Success(new NullableFieldDto { Normal = true, AlwaysPresent = null }));

        var doc = JsonDocument.Parse(output);
        var data = doc.RootElement.GetProperty("data");
        data.TryGetProperty("alwaysPresent", out var field).Should().BeTrue();
        field.ValueKind.Should().Be(JsonValueKind.Null);
    }

    private string Capture(Action action)
    {
        using var sw = new StringWriter();
        Console.SetOut(sw);
        action();
        Console.SetOut(_originalOut);
        return sw.ToString().Trim();
    }
}
