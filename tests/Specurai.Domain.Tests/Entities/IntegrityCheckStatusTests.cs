using FluentAssertions;
using Specurai.Domain.Entities;
using Xunit;

namespace Specurai.Domain.Tests.Entities;

public class IntegrityCheckStatusTests
{
    [Fact]
    public void 建立_所有屬性正確設定()
    {
        var s = new IntegrityCheckStatus
        {
            DatabaseName = "DB",
            LastKnownGood = new DateTime(2026, 5, 1),
            DaysSince = 14,
            Health = IntegrityHealth.Warning
        };
        s.DatabaseName.Should().Be("DB");
        s.Health.Should().Be(IntegrityHealth.Warning);
    }

    [Fact]
    public void 從未檢查_LastKnownGood可為null()
    {
        var s = new IntegrityCheckStatus { DatabaseName = "DB", Health = IntegrityHealth.Critical };
        s.LastKnownGood.Should().BeNull();
        s.DaysSince.Should().BeNull();
    }
}
