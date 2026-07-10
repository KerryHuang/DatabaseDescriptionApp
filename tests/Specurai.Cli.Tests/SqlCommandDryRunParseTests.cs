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
}
