using FluentAssertions;
using Specurai.Domain.Entities;

namespace Specurai.Domain.Tests.Entities;

/// <summary>
/// ConnectionProfileComparer 排序測試（預設→環境→名稱）
/// </summary>
public class ConnectionProfileComparerTests
{
    private static ConnectionProfile P(string name, DatabaseEnvironment env, bool isDefault = false) =>
        new() { Name = name, Server = "s", Database = "d", Environment = env, IsDefault = isDefault };

    [Fact]
    public void 預設連線_應排在非預設之前()
    {
        var list = new List<ConnectionProfile>
        {
            P("Zzz", DatabaseEnvironment.Development),
            P("Aaa", DatabaseEnvironment.Production, isDefault: true)
        };

        list.Sort(ConnectionProfileComparer.Instance);

        list[0].Name.Should().Be("Aaa"); // 預設優先，即使環境較後、名稱較後
    }

    [Fact]
    public void 同為非預設_應依環境列舉順序排序()
    {
        var list = new List<ConnectionProfile>
        {
            P("a", DatabaseEnvironment.Production),
            P("b", DatabaseEnvironment.Development),
            P("c", DatabaseEnvironment.Staging),
            P("d", DatabaseEnvironment.Testing)
        };

        list.Sort(ConnectionProfileComparer.Instance);

        list.Select(p => p.Environment).Should().ContainInOrder(
            DatabaseEnvironment.Development,
            DatabaseEnvironment.Testing,
            DatabaseEnvironment.Staging,
            DatabaseEnvironment.Production);
    }

    [Fact]
    public void 同環境同預設狀態_應依名稱不分大小寫排序()
    {
        var list = new List<ConnectionProfile>
        {
            P("banana", DatabaseEnvironment.Staging),
            P("Apple", DatabaseEnvironment.Staging)
        };

        list.Sort(ConnectionProfileComparer.Instance);

        list[0].Name.Should().Be("Apple");
        list[1].Name.Should().Be("banana");
    }

    [Fact]
    public void Null_應排在最後()
    {
        var a = P("a", DatabaseEnvironment.Staging);

        ConnectionProfileComparer.Instance.Compare(a, null).Should().BeNegative();
        ConnectionProfileComparer.Instance.Compare(null, a).Should().BePositive();
        ConnectionProfileComparer.Instance.Compare(null, null).Should().Be(0);
    }
}
