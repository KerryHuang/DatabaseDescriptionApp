using FluentAssertions;
using NSubstitute;
using TableSpec.Application.Models;
using TableSpec.Application.Services;
using TableSpec.Desktop.ViewModels;
using TableSpec.Domain.Enums;

namespace TableSpec.Desktop.Tests.ViewModels;

/// <summary>
/// MaintenancePlanWizardViewModel 測試
/// </summary>
public class MaintenancePlanWizardViewModelTests
{
    private readonly IMaintenancePlanService _planService;
    private readonly IMaintenancePlanSqlGenerator _sqlGenerator;

    public MaintenancePlanWizardViewModelTests()
    {
        _planService = Substitute.For<IMaintenancePlanService>();
        _sqlGenerator = Substitute.For<IMaintenancePlanSqlGenerator>();
    }

    #region 建構函式測試

    [Fact]
    public void Constructor_無參數_應可建立實例()
    {
        // Act
        var vm = new MaintenancePlanWizardViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.CurrentStep.Should().Be(1);
    }

    #endregion

    #region 初始狀態測試

    [Fact]
    public void 初始狀態_預設值應正確()
    {
        // Act
        var vm = new MaintenancePlanWizardViewModel();

        // Assert
        vm.LoginName.Should().Be("mis");
        vm.BackupTime.Should().Be(new TimeSpan(2, 0, 0));
        vm.RestoreTime.Should().Be(new TimeSpan(3, 0, 0));
        vm.IsStep1Visible.Should().BeTrue();
        vm.IsStep2Visible.Should().BeFalse();
        vm.IsStep3Visible.Should().BeFalse();
    }

    #endregion

    #region 步驟切換測試

    [Fact]
    public void NextStep_從步驟1到步驟2_應切換顯示()
    {
        // Arrange
        var vm = new MaintenancePlanWizardViewModel(_planService, _sqlGenerator)
        {
            DatabaseName = "TestDB",
            BackupPath = @"C:\Backup\",
            RestorePath = @"C:\Restore\",
            LoginName = "mis",
            LoginPassword = "password123"
        };

        // Act
        vm.NextStepCommand.Execute(null);

        // Assert
        vm.CurrentStep.Should().Be(2);
        vm.IsStep1Visible.Should().BeFalse();
        vm.IsStep2Visible.Should().BeTrue();
        vm.IsStep3Visible.Should().BeFalse();
    }

    [Fact]
    public void PreviousStep_從步驟2到步驟1_應切換顯示()
    {
        // Arrange
        var vm = new MaintenancePlanWizardViewModel(_planService, _sqlGenerator)
        {
            DatabaseName = "TestDB",
            BackupPath = @"C:\Backup\",
            RestorePath = @"C:\Restore\",
            LoginName = "mis",
            LoginPassword = "password123"
        };
        vm.NextStepCommand.Execute(null);
        vm.CurrentStep.Should().Be(2);

        // Act
        vm.PreviousStepCommand.Execute(null);

        // Assert
        vm.CurrentStep.Should().Be(1);
        vm.IsStep1Visible.Should().BeTrue();
        vm.IsStep2Visible.Should().BeFalse();
    }

    #endregion

    #region CanNextStep 測試

    [Fact]
    public void CanNextStep_步驟1欄位未填_應為False()
    {
        // Arrange
        var vm = new MaintenancePlanWizardViewModel(_planService, _sqlGenerator);

        // Assert
        vm.NextStepCommand.CanExecute(null).Should().BeFalse();
    }

    #endregion

    #region 自動帶入測試

    [Fact]
    public void TestDatabaseName_應自動帶入()
    {
        // Arrange
        var vm = new MaintenancePlanWizardViewModel(_planService, _sqlGenerator);

        // Act
        vm.DatabaseName = "WayDoSoft01";

        // Assert
        vm.TestDatabaseName.Should().Be("WayDoSoft01-test");
    }

    #endregion

    #region 步驟選擇預設值測試

    [Fact]
    public void SelectedSteps_CreateRestoreJob_預設不勾選()
    {
        // Arrange
        var vm = new MaintenancePlanWizardViewModel();

        // Assert
        vm.IsSetRecoveryModelSelected.Should().BeTrue();
        vm.IsRenameLogicalFilesSelected.Should().BeTrue();
        vm.IsCreateLoginAndUserSelected.Should().BeTrue();
        vm.IsAddToDbOwnerSelected.Should().BeTrue();
        vm.IsCreateBackupJobSelected.Should().BeTrue();
        vm.IsCreateRestoreJobSelected.Should().BeFalse();
    }

    #endregion
}
