using System.Text.Json;
using FluentAssertions;
using Specurai.Cli;
using Specurai.Domain.Entities;

namespace Specurai.Cli.Tests;

public class ConnectionProfileParserTests
{
    #region ParseMultiple

    [Fact(DisplayName = "ParseMultiple: JSON 陣列應解析多個 Profile")]
    public void ParseMultiple_Array_ShouldReturnMultipleProfiles()
    {
        var json = """
        [
            {"name":"DEV","server":"dev-srv","database":"DevDB","user":"sa","password":"pw1"},
            {"name":"PROD","server":"prod-srv","database":"ProdDB","user":"sa","password":"pw2"}
        ]
        """;

        var result = ConnectionProfileParser.ParseMultiple(json);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("DEV");
        result[0].Server.Should().Be("dev-srv");
        result[1].Name.Should().Be("PROD");
        result[1].Server.Should().Be("prod-srv");
    }

    [Fact(DisplayName = "ParseMultiple: 單一物件應解析為一個 Profile")]
    public void ParseMultiple_SingleObject_ShouldReturnOneProfile()
    {
        var json = """{"name":"TEST","server":"test-srv","database":"TestDB","user":"sa","password":"pw"}""";

        var result = ConnectionProfileParser.ParseMultiple(json);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("TEST");
    }

    [Fact(DisplayName = "ParseMultiple: mpe 和簡易格式混合陣列應都能解析")]
    public void ParseMultiple_MixedArray_ShouldParseBothFormats()
    {
        var json = """
        [
            {"displayName":"MPE環境","mssql":{"host":"mpe-host","userId":"u","password":"p","applicationDatabase":"mpe-db"}},
            {"name":"簡易環境","server":"simple-host","database":"simple-db","user":"u","password":"p"}
        ]
        """;

        var result = ConnectionProfileParser.ParseMultiple(json);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("MPE環境");
        result[0].Server.Should().Be("mpe-host");
        result[1].Name.Should().Be("簡易環境");
        result[1].Server.Should().Be("simple-host");
    }

    [Fact(DisplayName = "ParseMultiple: 空陣列應回傳空清單")]
    public void ParseMultiple_EmptyArray_ShouldReturnEmpty()
    {
        ConnectionProfileParser.ParseMultiple("[]").Should().BeEmpty();
    }

    [Fact(DisplayName = "ParseMultiple: 空物件應回傳空清單")]
    public void ParseMultiple_EmptyObject_ShouldReturnEmpty()
    {
        ConnectionProfileParser.ParseMultiple("{}").Should().BeEmpty();
    }

    [Fact(DisplayName = "ParseMultiple: 不合法 JSON 應擲出 JsonException")]
    public void ParseMultiple_InvalidJson_ShouldThrow()
    {
        var act = () => ConnectionProfileParser.ParseMultiple("not-json");

        act.Should().Throw<JsonException>();
    }

    [Fact(DisplayName = "ParseMultiple: 陣列中無效元素應被跳過")]
    public void ParseMultiple_ArrayWithInvalidElement_ShouldSkipInvalid()
    {
        var json = """
        [
            {"name":"有效","server":"srv","database":"db","user":"u","password":"p"},
            {"foo":"bar"},
            {"name":"也有效","server":"srv2","database":"db2","user":"u2","password":"p2"}
        ]
        """;

        var result = ConnectionProfileParser.ParseMultiple(json);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("有效");
        result[1].Name.Should().Be("也有效");
    }

    #endregion

    #region ParseSingle

    [Fact(DisplayName = "ParseSingle: PascalCase 欄位應正確解析")]
    public void ParseSingle_PascalCaseFields_ShouldParse()
    {
        var json = """{"Name":"Test","Server":"host","Database":"db","Username":"user","Password":"pass"}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnectionProfileParser.ParseSingle(doc.RootElement);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
        result.Server.Should().Be("host");
        result.Username.Should().Be("user");
        result.Password.Should().Be("pass");
    }

    [Fact(DisplayName = "ParseSingle: camelCase 欄位應正確解析")]
    public void ParseSingle_CamelCaseFields_ShouldParse()
    {
        var json = """{"name":"test","server":"host","database":"db","user":"u","password":"p"}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnectionProfileParser.ParseSingle(doc.RootElement);

        result.Should().NotBeNull();
        result!.Username.Should().Be("u");
    }

    [Fact(DisplayName = "ParseSingle: 無 server 也無 mssql 應回傳 null")]
    public void ParseSingle_NoServerNoMssql_ShouldReturnNull()
    {
        var json = """{"foo":"bar","baz":123}""";
        using var doc = JsonDocument.Parse(json);

        ConnectionProfileParser.ParseSingle(doc.RootElement).Should().BeNull();
    }

    [Fact(DisplayName = "ParseSingle: 無 name 欄位時應以 server 自動命名")]
    public void ParseSingle_NoName_ShouldAutoGenerateFromServer()
    {
        var json = """{"server":"auto-srv","database":"db","user":"u","password":"p"}""";
        using var doc = JsonDocument.Parse(json);

        var result = ConnectionProfileParser.ParseSingle(doc.RootElement);

        result.Should().NotBeNull();
        result!.Name.Should().Be("auto-srv");
    }

    #endregion
}
