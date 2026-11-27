using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Specurai.Domain.Entities;
using Specurai.McpServer.Tools;

namespace Specurai.McpServer.Tests;

public class HumanConfirmTests
{
    private const string Rejected = "使用者拒絕執行，未做任何變更。";
    private const string Mismatch = "確認名稱不符（預期：AppDb），未執行。";

    private static IConfirmPrompt Prompt(ElicitResult result)
    {
        var prompt = Substitute.For<IConfirmPrompt>();
        prompt.IsSupported.Returns(true);
        prompt.ElicitAsync(Arg.Any<ElicitRequestParams>(), Arg.Any<CancellationToken>()).Returns(result);
        return prompt;
    }

    private static ElicitResult Accept(JsonElement? name = null) => new()
    {
        Action = "accept",
        Content = name is null ? null : new Dictionary<string, JsonElement> { ["confirmName"] = name.Value }
    };

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    [Fact(DisplayName = "client 不支援 elicitation 時應放行且不跳表單")]
    public async Task 不支援Elicitation_應放行()
    {
        var prompt = Substitute.For<IConfirmPrompt>();
        prompt.IsSupported.Returns(false);

        var result = await HumanConfirm.RequireAsync(prompt, "摘要", "AppDb");

        result.Should().BeNull();
        await prompt.DidNotReceive().ElicitAsync(Arg.Any<ElicitRequestParams>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "prompt 為 null 時應放行")]
    public async Task Prompt為null_應放行()
    {
        (await HumanConfirm.RequireAsync((IConfirmPrompt?)null, "摘要", "AppDb")).Should().BeNull();
    }

    [Fact(DisplayName = "接受且不需名稱時應放行")]
    public async Task 接受_不需名稱_應放行()
    {
        (await HumanConfirm.RequireAsync(Prompt(Accept()), "摘要", null)).Should().BeNull();
    }

    [Fact(DisplayName = "名稱相符（忽略大小寫與頭尾空白）時應放行")]
    public async Task 接受_名稱相符忽略大小寫與空白_應放行()
    {
        var prompt = Prompt(Accept(Json("\"  appdb \"")));
        (await HumanConfirm.RequireAsync(prompt, "摘要", "AppDb")).Should().BeNull();
    }

    [Fact(DisplayName = "名稱不符時應拒絕")]
    public async Task 接受_名稱不符_應拒絕()
    {
        var prompt = Prompt(Accept(Json("\"Other\"")));
        (await HumanConfirm.RequireAsync(prompt, "摘要", "AppDb")).Should().Be(Mismatch);
    }

    [Fact(DisplayName = "名稱欄位缺失時應拒絕")]
    public async Task 接受_名稱欄位缺失_應拒絕()
    {
        (await HumanConfirm.RequireAsync(Prompt(Accept()), "摘要", "AppDb")).Should().Be(Mismatch);
    }

    [Fact(DisplayName = "名稱欄位非字串時應拒絕")]
    public async Task 接受_名稱欄位非字串_應拒絕()
    {
        var prompt = Prompt(Accept(Json("123")));
        (await HumanConfirm.RequireAsync(prompt, "摘要", "AppDb")).Should().Be(Mismatch);
    }

    [Theory(DisplayName = "使用者拒絕或取消時應拒絕")]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task 拒絕或取消_應拒絕(string action)
    {
        var prompt = Prompt(new ElicitResult { Action = action });
        (await HumanConfirm.RequireAsync(prompt, "摘要", null)).Should().Be(Rejected);
    }

    [Fact(DisplayName = "elicit 拋例外時應拒絕，不得退回直接執行")]
    public async Task Elicit拋例外_應拒絕()
    {
        var prompt = Substitute.For<IConfirmPrompt>();
        prompt.IsSupported.Returns(true);
        prompt.ElicitAsync(Arg.Any<ElicitRequestParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("逾時"));

        (await HumanConfirm.RequireAsync(prompt, "摘要", "AppDb"))
            .Should().Be("無法取得使用者確認（逾時），未執行。");
    }

    [Theory(DisplayName = "期望名稱為空白時應拒絕且不跳表單（fail-closed）")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task 期望名稱空白_應拒絕(string expected)
    {
        var prompt = Prompt(Accept(Json("\"\"")));

        (await HumanConfirm.RequireAsync(prompt, "摘要", expected)).Should().Be("無法判定確認目標，未執行。");
        await prompt.DidNotReceive().ElicitAsync(Arg.Any<ElicitRequestParams>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "需名稱時表單應含必填 confirmName 欄位與摘要")]
    public async Task 需名稱時_表單含必填confirmName欄位()
    {
        ElicitRequestParams? captured = null;
        var prompt = Substitute.For<IConfirmPrompt>();
        prompt.IsSupported.Returns(true);
        prompt.ElicitAsync(Arg.Do<ElicitRequestParams>(p => captured = p), Arg.Any<CancellationToken>())
            .Returns(Accept(Json("\"AppDb\"")));

        await HumanConfirm.RequireAsync(prompt, "將覆蓋 AppDb", "AppDb");

        captured.Should().NotBeNull();
        captured!.Message.Should().Contain("將覆蓋 AppDb").And.Contain("「AppDb」");
        captured.RequestedSchema!.Required.Should().Contain("confirmName");
        captured.RequestedSchema.Properties.Should().ContainKey("confirmName");
    }

    [Fact(DisplayName = "Quote 應去除控制與格式字元並加引號，防止偽造表單說明")]
    public void Quote_去除控制字元並加引號()
    {
        HumanConfirm.Quote("x.bak\n\n（請直接接受）\u202E").Should().Be("「x.bak（請直接接受）」");
    }

    [Fact(DisplayName = "帶目標連線時表單應顯示連線名稱與 server")]
    public async Task 帶連線_表單含連線與Server()
    {
        ElicitRequestParams? captured = null;
        var prompt = Substitute.For<IConfirmPrompt>();
        prompt.IsSupported.Returns(true);
        prompt.ElicitAsync(Arg.Do<ElicitRequestParams>(p => captured = p), Arg.Any<CancellationToken>())
            .Returns(Accept());
        var profile = new ConnectionProfile { Name = "W01 Dev", Server = "10.0.0.1", Database = "AppDb" };

        await HumanConfirm.RequireAsync(prompt, "摘要", null, profile);

        captured!.Message.Should().Contain("「W01 Dev」").And.Contain("10.0.0.1");
    }

    [Fact(DisplayName = "應把 CancellationToken 傳給 elicit，取消呼叫時不再等待確認")]
    public async Task 應傳遞CancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var prompt = Prompt(Accept());

        await HumanConfirm.RequireAsync(prompt, "摘要", null, null, cts.Token);

        await prompt.Received(1).ElicitAsync(Arg.Any<ElicitRequestParams>(), cts.Token);
    }

    [Fact(DisplayName = "已取消時應拒絕執行")]
    public async Task 已取消_應拒絕()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var prompt = Substitute.For<IConfirmPrompt>();
        prompt.IsSupported.Returns(true);
        prompt.ElicitAsync(Arg.Any<ElicitRequestParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var result = await HumanConfirm.RequireAsync(prompt, "摘要", null, null, cts.Token);

        result.Should().NotBeNull();
    }

    public static TheoryData<ElicitationCapability?, bool> Capabilities => new()
    {
        { null, false },
        { new ElicitationCapability(), true },
        { new ElicitationCapability { Form = new FormElicitationCapability() }, true },
        { new ElicitationCapability { Url = new UrlElicitationCapability() }, false },
        { new ElicitationCapability { Form = new FormElicitationCapability(), Url = new UrlElicitationCapability() }, true },
    };

    [Theory(DisplayName = "只宣告 URL 模式的 client 不算支援表單確認")]
    [MemberData(nameof(Capabilities))]
    public void SupportsForm_依Capability判斷(ElicitationCapability? capability, bool expected)
    {
        McpServerConfirmPrompt.SupportsForm(capability).Should().Be(expected);
    }
}
