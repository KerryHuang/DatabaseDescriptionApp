using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Specurai.Domain.Entities;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// SQL DDL script 分析器：以 ScriptDom 解析、逐句比對白名單、依 GO 切批（純離線，不碰資料庫）。
/// 白名單採 fail-closed：不在名單的語句類型一律拒絕。
/// </summary>
public class SqlDdlScriptAnalyzer
{
    /// <summary>
    /// 解析並驗證 DDL script：每一句都必須是白名單內的物件級 DDL
    /// </summary>
    public SqlDdlScriptAnalysis Analyze(string script)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader(script), out var parseErrors);

        if (parseErrors.Count > 0)
        {
            return new SqlDdlScriptAnalysis
            {
                IsValid = false,
                SyntaxErrors = parseErrors
                    .Select(e => new DryRunSyntaxError { Line = e.Line, Column = e.Column, Message = e.Message })
                    .ToList()
            };
        }

        var statements = new List<DdlStatementSummary>();
        var batches = new List<string>();
        var index = 0;

        foreach (var batch in ((TSqlScript)fragment).Batches)
        {
            if (batch.Statements.Count == 0)
                continue;

            var batchIndex = batches.Count + 1;
            foreach (var statement in batch.Statements)
            {
                index++;
                var type = ClassifyAllowed(statement);
                if (type == null)
                {
                    return new SqlDdlScriptAnalysis
                    {
                        IsValid = false,
                        RejectReason = $"第 {index} 句（{statement.GetType().Name}）不在允許的 DDL 白名單；" +
                            "僅允許 TABLE/INDEX/VIEW/PROCEDURE/FUNCTION/TRIGGER/SCHEMA 的物件級 CREATE/ALTER/DROP，" +
                            "庫級操作、TRUNCATE、權限語句、EXEC 與 DML 一律拒絕。"
                    };
                }

                statements.Add(new DdlStatementSummary
                {
                    Index = index,
                    Type = type,
                    ObjectName = GetObjectName(statement),
                    BatchIndex = batchIndex
                });
            }

            batches.Add(GetBatchText(batch));
        }

        if (statements.Count == 0)
        {
            return new SqlDdlScriptAnalysis
            {
                IsValid = false,
                RejectReason = "未偵測到任何 SQL 陳述式。"
            };
        }

        return new SqlDdlScriptAnalysis
        {
            IsValid = true,
            Statements = statements,
            Batches = batches
        };
    }

    /// <summary>
    /// 白名單分類：允許的語句回傳顯示用類型名稱，否則回傳 null（fail-closed）。
    /// AlterTableStatement 是所有 ALTER TABLE 變體的抽象基底，單一 pattern 即涵蓋；
    /// XML/Spatial/Columnstore/FullText 等特殊索引是獨立類別，不繼承 CreateIndexStatement，自然被擋。
    /// </summary>
    private static string? ClassifyAllowed(TSqlStatement statement) => statement switch
    {
        CreateTableStatement => "CREATE TABLE",
        AlterTableStatement => "ALTER TABLE",
        DropTableStatement => "DROP TABLE",
        CreateIndexStatement => "CREATE INDEX",
        AlterIndexStatement => "ALTER INDEX",
        DropIndexStatement => "DROP INDEX",
        CreateViewStatement => "CREATE VIEW",
        AlterViewStatement => "ALTER VIEW",
        CreateOrAlterViewStatement => "CREATE OR ALTER VIEW",
        DropViewStatement => "DROP VIEW",
        CreateProcedureStatement => "CREATE PROCEDURE",
        AlterProcedureStatement => "ALTER PROCEDURE",
        CreateOrAlterProcedureStatement => "CREATE OR ALTER PROCEDURE",
        DropProcedureStatement => "DROP PROCEDURE",
        CreateFunctionStatement => "CREATE FUNCTION",
        AlterFunctionStatement => "ALTER FUNCTION",
        CreateOrAlterFunctionStatement => "CREATE OR ALTER FUNCTION",
        DropFunctionStatement => "DROP FUNCTION",
        CreateTriggerStatement => "CREATE TRIGGER",
        AlterTriggerStatement => "ALTER TRIGGER",
        CreateOrAlterTriggerStatement => "CREATE OR ALTER TRIGGER",
        DropTriggerStatement => "DROP TRIGGER",
        CreateSchemaStatement => "CREATE SCHEMA",
        AlterSchemaStatement => "ALTER SCHEMA",
        DropSchemaStatement => "DROP SCHEMA",
        _ => null
    };

    /// <summary>
    /// 解析目標物件名稱（顯示用，best-effort）：無法解析時回傳 null（如 ALTER INDEX ALL）。
    /// Drop 類別繼承 DropObjectsStatement，與 ViewStatementBody 等 Body 抽象基底無繼承關係，
    /// pattern 順序不影響比對結果，僅依可讀性排列。
    /// </summary>
    private static string? GetObjectName(TSqlStatement statement) => statement switch
    {
        CreateTableStatement s => Format(s.SchemaObjectName),
        AlterTableStatement s => Format(s.SchemaObjectName),
        DropTableStatement s => Format(s.Objects.FirstOrDefault()),
        CreateIndexStatement s => s.Name?.Value,
        AlterIndexStatement s => s.Name?.Value,
        DropIndexStatement s => s.DropIndexClauses.OfType<DropIndexClause>().FirstOrDefault()?.Index?.Value,
        DropViewStatement s => Format(s.Objects.FirstOrDefault()),
        ViewStatementBody s => Format(s.SchemaObjectName),
        DropProcedureStatement s => Format(s.Objects.FirstOrDefault()),
        ProcedureStatementBody s => Format(s.ProcedureReference?.Name),
        DropFunctionStatement s => Format(s.Objects.FirstOrDefault()),
        FunctionStatementBody s => Format(s.Name),
        DropTriggerStatement s => Format(s.Objects.FirstOrDefault()),
        TriggerStatementBody s => Format(s.Name),
        CreateSchemaStatement s => s.Name?.Value,
        AlterSchemaStatement s => s.Name?.Value,
        DropSchemaStatement s => Format(s.Schema),
        _ => null
    };

    private static string? Format(SchemaObjectName? name) =>
        name == null ? null : string.Join(".", name.Identifiers.Select(i => $"[{i.Value}]"));

    /// <summary>
    /// 以 token 流重建批次原文（保留原始格式與註解）
    /// </summary>
    private static string GetBatchText(TSqlBatch batch)
    {
        var tokens = batch.ScriptTokenStream;
        var sb = new StringBuilder();
        for (var i = batch.FirstTokenIndex; i <= batch.LastTokenIndex; i++)
            sb.Append(tokens[i].Text);
        return sb.ToString();
    }
}
