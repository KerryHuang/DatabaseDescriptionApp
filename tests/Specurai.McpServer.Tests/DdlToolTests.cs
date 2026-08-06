using FluentAssertions;
using NSubstitute;
using Specurai.Application.Services;
using Specurai.Domain.Entities;
using Specurai.McpServer.Tools;
using Xunit;

namespace Specurai.McpServer.Tests;

public class DdlToolTests
{
    private readonly IDdlExecutionService _service = Substitute.For<IDdlExecutionService>();

    private const string Ddl = "CREATE TABLE dbo.T1 (Id INT)";

    private static DdlStatementSummary Summary() => new()
    {
        Index = 1, Type = "CREATE TABLE", ObjectName = "[dbo].[T1]", BatchIndex = 1
    };

    [Fact(DisplayName = "execute_ddl: confirm 應原樣傳遞給服務")]
    public async Task ExecuteDdl_應原樣傳遞confirm()
    {
        _service.ExecuteAsync(Ddl, true, null, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult { IsValid = true, Statements = [Summary()], Committed = true });

        await SqlTools.ExecuteDdl(_service, Ddl, confirm: true);

        await _service.Received(1).ExecuteAsync(Ddl, true, null, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "execute_ddl: 預演結果應含 Hint 且 DatabaseChanged=false")]
    public async Task ExecuteDdl_預演_應含Hint()
    {
        _service.ExecuteAsync(Ddl, false, null, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult { IsValid = true, Statements = [Summary()], Committed = false });

        var json = await SqlTools.ExecuteDdl(_service, Ddl, confirm: false);

        json.Should().Contain("confirm:true");
        json.Should().Contain("\"DatabaseChanged\": false");
        json.Should().Contain("[dbo].[T1]");
    }

    [Fact(DisplayName = "execute_ddl: 拒絕時應回報原因")]
    public async Task ExecuteDdl_拒絕_應回報原因()
    {
        _service.ExecuteAsync(Ddl, false, null, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult { IsValid = false, RejectReason = "連線「X」為正式環境，不允許執行 DDL。" });

        var json = await SqlTools.ExecuteDdl(_service, Ddl, confirm: false);

        json.Should().Contain("正式環境");
        json.Should().Contain("\"Valid\": false");
    }

    [Fact(DisplayName = "execute_ddl: 執行失敗應回報失敗批次")]
    public async Task ExecuteDdl_執行失敗_應回報失敗批次()
    {
        _service.ExecuteAsync(Ddl, true, null, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult
            {
                IsValid = true, Statements = [Summary()],
                ExecutionError = "第 1 批執行失敗（整批已回滾）：物件已存在", FailedBatchIndex = 1
            });

        var json = await SqlTools.ExecuteDdl(_service, Ddl, confirm: true);

        json.Should().Contain("\"FailedBatchIndex\": 1");
        json.Should().Contain("\"Committed\": false");
    }

    [Fact(DisplayName = "execute_ddl: CommitUncertain 時 Committed/DatabaseChanged 應為 null")]
    public async Task ExecuteDdl_CommitUncertain_三態應為null()
    {
        _service.ExecuteAsync(Ddl, true, null, Arg.Any<CancellationToken>())
            .Returns(new DdlExecutionResult
            {
                IsValid = true, Statements = [Summary()],
                ExecutionError = "COMMIT 失敗，交易結果不確定，請查詢資料庫確認：斷線", CommitUncertain = true
            });

        var json = await SqlTools.ExecuteDdl(_service, Ddl, confirm: true);

        json.Should().Contain("\"Committed\": null");
        json.Should().Contain("\"DatabaseChanged\": null");
        json.Should().Contain("\"CommitUncertain\": true");
    }
}
