using FluentAssertions;
using Specurai.Cli.Commands;

namespace Specurai.Cli.Tests;

public class DatabasesCommandParseTests
{
    [Fact(DisplayName = "Create: 命令名稱應為 databases")]
    public void Create_ShouldBeNamedDatabases()
    {
        var command = DatabasesCommand.Create();

        command.Name.Should().Be("databases");
        command.Description.Should().Contain("使用者資料庫");
    }
}
