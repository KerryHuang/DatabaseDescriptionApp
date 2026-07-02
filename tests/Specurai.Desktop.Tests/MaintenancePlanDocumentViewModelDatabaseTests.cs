using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Desktop.ViewModels;
using Specurai.Domain.Interfaces;
using Xunit;

namespace Specurai.Desktop.Tests;

public class MaintenancePlanDocumentViewModelDatabaseTests
{
    private static MaintenancePlanDocumentViewModel Build(IMaintenancePlanService plan)
    {
        var job = Substitute.For<IAgentJobService>();
        var gen = Substitute.For<IMaintenancePlanSqlGenerator>();
        var conn = Substitute.For<IConnectionManager>();
        var backup = Substitute.For<IBackupService>();
        return new MaintenancePlanDocumentViewModel(job, plan, gen, conn, backup);
    }

    [Fact]
    public async Task LoadAvailableDatabasesAsync_有資料庫_應填入清單()
    {
        // Arrange
        var plan = Substitute.For<IMaintenancePlanService>();
        plan.GetServerDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "AlphaDB", "BetaDB" });
        var vm = Build(plan);

        // Act
        await vm.LoadAvailableDatabasesAsync();

        // Assert
        vm.AvailableDatabases.Should().BeEquivalentTo("AlphaDB", "BetaDB");
    }

    [Fact]
    public async Task LoadAvailableDatabasesAsync_查詢拋例外_清單維持空且不丟例外()
    {
        // Arrange
        var plan = Substitute.For<IMaintenancePlanService>();
        plan.GetServerDatabasesAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<string>>(_ => throw new System.Exception("boom"));
        var vm = Build(plan);

        // Act
        var act = async () => await vm.LoadAvailableDatabasesAsync();

        // Assert
        await act.Should().NotThrowAsync();
        vm.AvailableDatabases.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAvailableDatabasesAsync_設計時建構函式無服務_不丟例外且清單空()
    {
        // Arrange
        var vm = new MaintenancePlanDocumentViewModel();

        // Act
        var act = async () => await vm.LoadAvailableDatabasesAsync();

        // Assert
        await act.Should().NotThrowAsync();
        vm.AvailableDatabases.Should().BeEmpty();
    }
}
