using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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
}
