using FluentAssertions;
using Specurai.Cli.Commands;

namespace Specurai.Cli.Tests;

public class SqlCommandDryRunParseTests
{
    [Fact(DisplayName = "Create: sql 命令應包含 dry-run 子命令")]
    public void Create_ShouldContainDryRunSubcommand()
    {
        var command = SqlCommand.Create();

        var dryRun = command.Subcommands.FirstOrDefault(c => c.Name == "dry-run");

        dryRun.Should().NotBeNull();
        dryRun!.Description.Should().Contain("預演");
    }

    [Fact(DisplayName = "dry-run 命令應要求 sql 參數")]
    public void DryRunCommand_ShouldRequireSqlArgument()
    {
        var command = SqlCommand.Create();
        var dryRun = command.Subcommands.First(c => c.Name == "dry-run");

        dryRun.Arguments.Should().ContainSingle();
        dryRun.Arguments[0].Name.Should().Be("sql");
    }

    [Fact(DisplayName = "Create: sql 命令應包含 execute 子命令")]
    public void Create_ShouldContainExecuteSubcommand()
    {
        var command = SqlCommand.Create();

        var execute = command.Subcommands.FirstOrDefault(c => c.Name == "execute");

        execute.Should().NotBeNull();
        execute!.Description.Should().Contain("非正式環境");
    }

    [Fact(DisplayName = "execute 命令應要求 sql 參數並提供 --confirm 選項")]
    public void ExecuteCommand_ShouldRequireSqlArgumentAndProvideConfirmOption()
    {
        var command = SqlCommand.Create();
        var execute = command.Subcommands.First(c => c.Name == "execute");

        execute.Arguments.Should().ContainSingle();
        execute.Arguments[0].Name.Should().Be("sql");
        execute.Options.Should().Contain(o => o.Name == "confirm");
    }
}
