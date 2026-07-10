using Microsoft.SqlServer.TransactSql.ScriptDom;
using Specurai.Domain.Entities;

namespace Specurai.Infrastructure.Services;

/// <summary>
/// SQL Dry Run 分析器：以 ScriptDom 解析、驗證、分類單一 DML（純離線，不碰資料庫）
/// </summary>
public class SqlDryRunAnalyzer
{
    /// <summary>
    /// 解析並驗證 SQL：必須恰好一句 INSERT/UPDATE/DELETE
    /// </summary>
    public SqlDryRunAnalysis Analyze(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader(sql), out var parseErrors);

        if (parseErrors.Count > 0)
        {
            return new SqlDryRunAnalysis
            {
                IsValid = false,
                SyntaxErrors = parseErrors
                    .Select(e => new DryRunSyntaxError { Line = e.Line, Column = e.Column, Message = e.Message })
                    .ToList()
            };
        }

        var statements = ((TSqlScript)fragment).Batches
            .SelectMany(b => b.Statements)
            .ToList();

        if (statements.Count == 0)
        {
            return new SqlDryRunAnalysis
            {
                IsValid = false,
                RejectReason = "未偵測到任何 SQL 陳述式。"
            };
        }

        if (statements.Count > 1)
        {
            return new SqlDryRunAnalysis
            {
                IsValid = false,
                RejectReason = $"偵測到 {statements.Count} 個陳述式，dry run 僅允許單一 DML 陳述式。"
            };
        }

        return statements[0] switch
        {
            InsertStatement insert => AnalyzeInsert(insert),
            UpdateStatement update => AnalyzeUpdate(update),
            DeleteStatement delete => AnalyzeDelete(delete),
            _ => new SqlDryRunAnalysis
            {
                IsValid = false,
                RejectReason = "僅支援 INSERT/UPDATE/DELETE 的 dry run；SELECT、DDL、EXEC、TRUNCATE 等語法不允許。"
            }
        };
    }

    private static SqlDryRunAnalysis AnalyzeInsert(InsertStatement insert)
    {
        var spec = insert.InsertSpecification;

        // INSERT ... EXEC 會執行預存程序，無法安全預演
        if (spec.InsertSource is ExecuteInsertSource)
        {
            return new SqlDryRunAnalysis
            {
                IsValid = false,
                RejectReason = "INSERT ... EXEC 會執行預存程序，無法安全預演，不允許 dry run。"
            };
        }

        var (schema, table) = ResolveTarget(spec.Target, fromClause: null, insert.WithCtesAndXmlNamespaces);
        return new SqlDryRunAnalysis
        {
            IsValid = true,
            StatementType = DryRunStatementType.Insert,
            TargetSchema = schema,
            TargetTable = table,
            HasUserOutputClause = spec.OutputClause != null || spec.OutputIntoClause != null
        };
    }

    private static SqlDryRunAnalysis AnalyzeUpdate(UpdateStatement update)
    {
        var spec = update.UpdateSpecification;
        var (schema, table) = ResolveTarget(spec.Target, spec.FromClause, update.WithCtesAndXmlNamespaces);
        return new SqlDryRunAnalysis
        {
            IsValid = true,
            StatementType = DryRunStatementType.Update,
            TargetSchema = schema,
            TargetTable = table,
            HasUserOutputClause = spec.OutputClause != null || spec.OutputIntoClause != null
        };
    }

    private static SqlDryRunAnalysis AnalyzeDelete(DeleteStatement delete)
    {
        var spec = delete.DeleteSpecification;
        var (schema, table) = ResolveTarget(spec.Target, spec.FromClause, delete.WithCtesAndXmlNamespaces);
        return new SqlDryRunAnalysis
        {
            IsValid = true,
            StatementType = DryRunStatementType.Delete,
            TargetSchema = schema,
            TargetTable = table,
            HasUserOutputClause = spec.OutputClause != null || spec.OutputIntoClause != null
        };
    }

    /// <summary>
    /// 解析 DML 目標為實際資料表名稱：
    /// 目標是 CTE 時無法解析（回傳 null）；目標是 FROM 子句別名時解析回實際資料表。
    /// </summary>
    private static (string? Schema, string? Table) ResolveTarget(
        TableReference target, FromClause? fromClause, WithCtesAndXmlNamespaces? ctes)
    {
        if (target is not NamedTableReference named)
            return (null, null);

        var baseName = named.SchemaObject.BaseIdentifier.Value;

        // 目標名稱是 CTE：不是實體資料表
        if (ctes?.CommonTableExpressions.Any(c =>
                string.Equals(c.ExpressionName.Value, baseName, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return (null, null);
        }

        // 目標名稱是 FROM 子句中的別名：解析為實際資料表
        if (fromClause != null)
        {
            foreach (var reference in FlattenTableReferences(fromClause.TableReferences))
            {
                if (reference is NamedTableReference n &&
                    string.Equals(n.Alias?.Value, baseName, StringComparison.OrdinalIgnoreCase))
                {
                    return (n.SchemaObject.SchemaIdentifier?.Value, n.SchemaObject.BaseIdentifier.Value);
                }
            }
        }

        return (named.SchemaObject.SchemaIdentifier?.Value, baseName);
    }

    private static IEnumerable<TableReference> FlattenTableReferences(IEnumerable<TableReference> references)
    {
        foreach (var reference in references)
        {
            if (reference is QualifiedJoin join)
            {
                foreach (var inner in FlattenTableReferences([join.FirstTableReference, join.SecondTableReference]))
                    yield return inner;
            }
            else
            {
                yield return reference;
            }
        }
    }
}
