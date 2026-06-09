using System.Globalization;
using FluentAssertions;
using Specurai.Desktop.Converters;
using Specurai.Domain.Entities;

namespace Specurai.Desktop.Tests.Converters;

public class ConnectionProfileDisplayConverterTests
{
    private readonly ConnectionProfileDisplayConverter _converter = new();

    private static ConnectionProfile P(string name, DatabaseEnvironment env, bool isDefault = false) =>
        new() { Name = name, Server = "s", Database = "d", Environment = env, IsDefault = isDefault };

    [Theory]
    [InlineData(DatabaseEnvironment.Development, "【開發】Dev-Local")]
    [InlineData(DatabaseEnvironment.Testing, "【測試】Dev-Local")]
    [InlineData(DatabaseEnvironment.Staging, "【預備】Dev-Local")]
    [InlineData(DatabaseEnvironment.Production, "【正式】Dev-Local")]
    public void Convert_非預設_應為環境標籤加名稱(DatabaseEnvironment env, string expected)
    {
        var result = _converter.Convert(P("Dev-Local", env), typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().Be(expected);
    }

    [Fact]
    public void Convert_預設連線_應附加預設標記()
    {
        var result = _converter.Convert(
            P("MoldPlan-Schema", DatabaseEnvironment.Production, isDefault: true),
            typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().Be("【正式】MoldPlan-Schema (預設)");
    }

    [Fact]
    public void Convert_非ConnectionProfile_應回傳原值字串()
    {
        var result = _converter.Convert("其他", typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().Be("其他");
    }
}
