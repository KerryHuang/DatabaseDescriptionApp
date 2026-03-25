using System.Text.Json;
using FluentAssertions;
using Specurai.Cli.Commands;
using Specurai.Domain.Entities;

namespace Specurai.Cli.Tests;

public class ConnCommandImportTests
{
    #region ParseImportJson

    [Fact(DisplayName = "ParseImportJson: 單一 mpe 物件應解析為一個 Profile")]
    public void ParseImportJson_SingleMpeObject_ShouldReturnOneProfile()
    {
        var json = """
        {
            "name": "junhe-staging",
            "displayName": "均賀 Staging",
            "mssql": {
                "host": "192.168.1.100",
                "port": "1433",
                "userId": "mis",
                "password": "service",
                "applicationDatabase": "moldplan-test"
            }
        }
        """;

        var result = ConnCommand.ParseImportJson(json);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("均賀 Staging");
        result[0].Server.Should().Be("192.168.1.100");
        result[0].Database.Should().Be("moldplan-test");
    }

    [Fact(DisplayName = "ParseImportJson: 單一簡易物件應解析為一個 Profile")]
    public void ParseImportJson_SingleSimpleObject_ShouldReturnOneProfile()
    {
        var json = """{"server":"localhost","database":"MyDB","user":"sa","password":"P@ss"}""";

        var result = ConnCommand.ParseImportJson(json);

        result.Should().HaveCount(1);
        result[0].Server.Should().Be("localhost");
    }

    [Fact(DisplayName = "ParseImportJson: 陣列格式應解析多個 Profile")]
    public void ParseImportJson_Array_ShouldReturnMultipleProfiles()
    {
        var json = """
        [
            {"server":"host1","database":"db1","user":"u1","password":"p1"},
            {"server":"host2","database":"db2","user":"u2","password":"p2"}
        ]
        """;

        var result = ConnCommand.ParseImportJson(json);

        result.Should().HaveCount(2);
        result[0].Server.Should().Be("host1");
        result[1].Server.Should().Be("host2");
    }

    [Fact(DisplayName = "ParseImportJson: mpe 和簡易格式混合陣列應都能解析")]
    public void ParseImportJson_MixedArray_ShouldParseBothFormats()
    {
        var json = """
        [
            {"mssql":{"host":"mpe-host","userId":"u","password":"p","applicationDatabase":"mpe-db"}},
            {"server":"simple-host","database":"simple-db","user":"u","password":"p"}
        ]
        """;

        var result = ConnCommand.ParseImportJson(json);

        result.Should().HaveCount(2);
        result[0].Server.Should().Be("mpe-host");
        result[1].Server.Should().Be("simple-host");
    }

    [Fact(DisplayName = "ParseImportJson: 空物件應回傳空清單")]
    public void ParseImportJson_EmptyObject_ShouldReturnEmpty()
    {
        ConnCommand.ParseImportJson("{}").Should().BeEmpty();
    }

    [Fact(DisplayName = "ParseImportJson: 空陣列應回傳空清單")]
    public void ParseImportJson_EmptyArray_ShouldReturnEmpty()
    {
        ConnCommand.ParseImportJson("[]").Should().BeEmpty();
    }

    #endregion

    #region ParseSingleProfile

    [Fact(DisplayName = "ParseSingleProfile: mpe 格式 Port 為字串應正確解析")]
    public void ParseSingleProfile_MpeStringPort_ShouldParse()
    {
        var json = """{"mssql":{"host":"10.0.0.1","port":"5433","userId":"sa","password":"p","applicationDatabase":"db"}}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnCommand.ParseSingleProfile(doc.RootElement);

        result.Should().NotBeNull();
        result!.Server.Should().Be("10.0.0.1,5433");
    }

    [Fact(DisplayName = "ParseSingleProfile: PascalCase 欄位應正確解析")]
    public void ParseSingleProfile_PascalCaseFields_ShouldParse()
    {
        var json = """{"Name":"Test","Server":"host","Database":"db","Username":"user","Password":"pass"}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnCommand.ParseSingleProfile(doc.RootElement);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
        result.Server.Should().Be("host");
        result.Username.Should().Be("user");
    }

    [Fact(DisplayName = "ParseSingleProfile: camelCase 欄位應正確解析")]
    public void ParseSingleProfile_CamelCaseFields_ShouldParse()
    {
        var json = """{"name":"test","server":"host","database":"db","user":"u","password":"p"}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnCommand.ParseSingleProfile(doc.RootElement);

        result.Should().NotBeNull();
        result!.Username.Should().Be("u");
    }

    [Fact(DisplayName = "ParseSingleProfile: mpe 格式缺少可選欄位應使用預設值")]
    public void ParseSingleProfile_MpeMissingOptional_ShouldUseDefaults()
    {
        var json = """{"mssql":{"host":"minimal-host"}}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnCommand.ParseSingleProfile(doc.RootElement);

        result.Should().NotBeNull();
        result!.Server.Should().Be("minimal-host");
        result.Database.Should().Be("master");
    }

    [Fact(DisplayName = "ParseSingleProfile: 無 server 也無 mssql 應回傳 null")]
    public void ParseSingleProfile_NoServerNoMssql_ShouldReturnNull()
    {
        var json = """{"foo":"bar","baz":123}""";
        using var doc = JsonDocument.Parse(json);

        ConnCommand.ParseSingleProfile(doc.RootElement).Should().BeNull();
    }

    [Fact(DisplayName = "ParseSingleProfile: displayName 應優先於 name 作為名稱")]
    public void ParseSingleProfile_DisplayNamePriority_ShouldPreferDisplayName()
    {
        var json = """{"name":"code","displayName":"顯示名稱","mssql":{"host":"h","userId":"u","password":"p","applicationDatabase":"db"}}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnCommand.ParseSingleProfile(doc.RootElement);

        result!.Name.Should().Be("顯示名稱");
    }

    [Fact(DisplayName = "ParseSingleProfile: Port 1433 時不應附加 Port")]
    public void ParseSingleProfile_Port1433_ShouldNotAppend()
    {
        var json = """{"mssql":{"host":"h","port":"1433","userId":"u","password":"p","applicationDatabase":"db"}}""";
        using var doc = JsonDocument.Parse(json);

        ConnCommand.ParseSingleProfile(doc.RootElement)!.Server.Should().Be("h");
    }

    #endregion
}
