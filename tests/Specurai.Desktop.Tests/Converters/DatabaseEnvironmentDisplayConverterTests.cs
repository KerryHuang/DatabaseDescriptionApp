using System.Globalization;
using FluentAssertions;
using Specurai.Desktop.Converters;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Tests.Converters;

public class DatabaseEnvironmentDisplayConverterTests
{
    private readonly DatabaseEnvironmentDisplayConverter _converter = new();

    [Theory]
    [InlineData(DatabaseEnvironment.Development, "開發環境")]
    [InlineData(DatabaseEnvironment.Testing, "測試環境")]
    [InlineData(DatabaseEnvironment.Staging, "預備環境")]
    [InlineData(DatabaseEnvironment.Production, "正式環境")]
    public void Convert_列舉值_應回傳對應繁中名稱(DatabaseEnvironment env, string expected)
    {
        // Act
        var result = _converter.Convert(env, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Convert_非列舉值_應回傳原值字串()
    {
        // Act
        var result = _converter.Convert("其他", typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be("其他");
    }
}
