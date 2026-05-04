using FluentAssertions;
using Specurai.Domain.Entities;

namespace Specurai.Domain.Tests.Entities;

public class DatabaseRecoveryModelTests
{
    [Fact]
    public void Constructor_應設定屬性()
    {
        var entity = new DatabaseRecoveryModel
        {
            DatabaseName = "leadtech",
            RecoveryModel = "FULL"
        };

        entity.DatabaseName.Should().Be("leadtech");
        entity.RecoveryModel.Should().Be("FULL");
    }
}
