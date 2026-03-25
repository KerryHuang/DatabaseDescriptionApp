using FluentAssertions;
using Specurai.Cli.Commands;

namespace Specurai.Cli.Tests;

public class TablesCommandParseTests
{
    [Theory(DisplayName = "ParseObjectName: 有 Schema 時應正確分割")]
    [InlineData("dbo.Users", "dbo", "Users")]
    [InlineData("sales.Orders", "sales", "Orders")]
    [InlineData("HumanResources.Employee", "HumanResources", "Employee")]
    public void ParseObjectName_WithSchema_ShouldSplit(string input, string expectedSchema, string expectedName)
    {
        var (schema, name) = TablesCommand.ParseObjectName(input);

        schema.Should().Be(expectedSchema);
        name.Should().Be(expectedName);
    }

    [Theory(DisplayName = "ParseObjectName: 無 Schema 時應預設 dbo")]
    [InlineData("Users", "dbo", "Users")]
    [InlineData("Orders", "dbo", "Orders")]
    public void ParseObjectName_NoSchema_ShouldDefaultToDbo(string input, string expectedSchema, string expectedName)
    {
        var (schema, name) = TablesCommand.ParseObjectName(input);

        schema.Should().Be(expectedSchema);
        name.Should().Be(expectedName);
    }

    [Fact(DisplayName = "ParseObjectName: 多個點時應只分割第一個")]
    public void ParseObjectName_MultipleDots_ShouldSplitOnlyFirst()
    {
        var (schema, name) = TablesCommand.ParseObjectName("dbo.sys.objects");

        schema.Should().Be("dbo");
        name.Should().Be("sys.objects");
    }

    [Fact(DisplayName = "ParseObjectName: 空字串應回傳 dbo 和空字串")]
    public void ParseObjectName_Empty_ShouldReturnDboAndEmpty()
    {
        var (schema, name) = TablesCommand.ParseObjectName("");

        schema.Should().Be("dbo");
        name.Should().Be("");
    }

    [Fact(DisplayName = "ParseObjectName: Schema 含特殊字元應保持原樣")]
    public void ParseObjectName_SpecialChars_ShouldPreserve()
    {
        var (schema, name) = TablesCommand.ParseObjectName("my-schema.my_table");

        schema.Should().Be("my-schema");
        name.Should().Be("my_table");
    }
}
