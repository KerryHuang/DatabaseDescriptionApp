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
    [InlineData(DatabaseEnvironment.Development, "【開發】【自建】Dev-Local")]
    [InlineData(DatabaseEnvironment.Testing, "【測試】【自建】Dev-Local")]
    [InlineData(DatabaseEnvironment.Staging, "【預備】【自建】Dev-Local")]
    [InlineData(DatabaseEnvironment.Production, "【正式】【自建】Dev-Local")]
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

        result.Should().Be("【正式】【自建】MoldPlan-Schema (預設)");
    }

    [Fact]
    public void Convert_外部連線_應標記外部()
    {
        var profile = new ConnectionProfile
        {
            Name = "嘉泰 Production", Server = "s", Database = "d",
            Environment = DatabaseEnvironment.Production, IsExternal = true
        };

        var result = _converter.Convert(profile, typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().Be("【正式】【外部】嘉泰 Production");
    }

    [Fact]
    public void Convert_非ConnectionProfile_應回傳原值字串()
    {
        var result = _converter.Convert("其他", typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().Be("其他");
    }
}
