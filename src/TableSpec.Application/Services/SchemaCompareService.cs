using TableSpec.Domain.Entities.SchemaCompare;
using TableSpec.Domain.Enums;
using TableSpec.Domain.Interfaces;

namespace TableSpec.Application.Services;

/// <summary>
/// Schema 比對服務實作
/// </summary>
public class SchemaCompareService : ISchemaCompareService
{
    private readonly ISchemaCollector _schemaCollector;

    public SchemaCompareService(ISchemaCollector schemaCollector)
    {
        _schemaCollector = schemaCollector;
    }

    /// <inheritdoc />
    public Task<SchemaComparison> CompareAsync(DatabaseSchema baseSchema, DatabaseSchema targetSchema)
    {
        var comparison = new SchemaComparison
        {
            BaseEnvironment = baseSchema.ConnectionName,
            TargetEnvironment = targetSchema.ConnectionName,
            ComparedAt = DateTime.Now,
            Differences = new List<SchemaDifference>()
        };

        // 比對表格
        CompareTables(baseSchema, targetSchema, comparison);

        // 比對程式物件
        CompareViews(baseSchema, targetSchema, comparison);
        CompareStoredProcedures(baseSchema, targetSchema, comparison);
        CompareFunctions(baseSchema, targetSchema, comparison);
        CompareTriggers(baseSchema, targetSchema, comparison);

        return Task.FromResult(comparison);
    }

    /// <inheritdoc />
    public async Task<IList<SchemaComparison>> CompareMultipleAsync(
        DatabaseSchema baseSchema,
        IList<DatabaseSchema> targetSchemas)
    {
        var results = new List<SchemaComparison>();

        foreach (var targetSchema in targetSchemas)
        {
            var comparison = await CompareAsync(baseSchema, targetSchema);
            results.Add(comparison);
        }

        return results;
    }

    #region 表格比對

    private void CompareTables(DatabaseSchema baseSchema, DatabaseSchema targetSchema, SchemaComparison comparison)
    {
        // 基準有，目標沒有 → 目標需要新增
        foreach (var baseTable in baseSchema.Tables)
        {
            var targetTable = targetSchema.GetTable(baseTable.Schema, baseTable.Name);
            if (targetTable == null)
            {
                comparison.Differences.Add(new SchemaDifference
                {
                    ObjectType = SchemaObjectType.Table,
                    ObjectName = baseTable.FullName,
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low,
                    Description = $"表格 {baseTable.FullName} 不存在於目標環境，需要新增"
                });
            }
            else
            {
                // 比對欄位
                CompareColumns(baseTable, targetTable, comparison);

                // 比對索引
                CompareIndexes(baseTable, targetTable, comparison);

                // 比對約束
                CompareConstraints(baseTable, targetTable, comparison);
            }
        }

        // 目標有，基準沒有 → 基準需要新增（最大化原則）
        foreach (var targetTable in targetSchema.Tables)
        {
            var baseTable = baseSchema.GetTable(targetTable.Schema, targetTable.Name);
            if (baseTable == null)
            {
                comparison.Differences.Add(new SchemaDifference
                {
                    ObjectType = SchemaObjectType.Table,
                    ObjectName = targetTable.FullName,
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low,
                    Description = $"表格 {targetTable.FullName} 不存在於基準環境，基準需要新增"
                });
            }
        }
    }

    #endregion

    #region 欄位比對

    private void CompareColumns(SchemaTable baseTable, SchemaTable targetTable, SchemaComparison comparison)
    {
        // 基準有，目標沒有
        foreach (var baseColumn in baseTable.Columns)
        {
            var targetColumn = targetTable.GetColumn(baseColumn.Name);
            if (targetColumn == null)
            {
                var riskLevel = EvaluateAddColumnRisk(baseColumn);
                comparison.Differences.Add(new SchemaDifference
                {
                    ObjectType = SchemaObjectType.Column,
                    ObjectName = $"{baseTable.FullName}.[{baseColumn.Name}]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = riskLevel,
                    SourceValue = baseColumn.GetFullDataType(),
                    Description = $"欄位 {baseColumn.Name} 不存在於目標環境"
                });
            }
            else
            {
                // 比對欄位屬性
                CompareColumnProperties(baseTable, baseColumn, targetColumn, comparison);
            }
        }

        // 目標有，基準沒有（最大化原則）
        foreach (var targetColumn in targetTable.Columns)
        {
            var baseColumn = baseTable.GetColumn(targetColumn.Name);
            if (baseColumn == null)
            {
                var riskLevel = EvaluateAddColumnRisk(targetColumn);
                comparison.Differences.Add(new SchemaDifference
                {
                    ObjectType = SchemaObjectType.Column,
                    ObjectName = $"{baseTable.FullName}.[{targetColumn.Name}]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = riskLevel,
                    SourceValue = targetColumn.GetFullDataType(),
                    Description = $"欄位 {targetColumn.Name} 不存在於基準環境，基準需要新增"
                });
            }
        }
    }

    private void CompareColumnProperties(
        SchemaTable table,
        SchemaColumn baseColumn,
        SchemaColumn targetColumn,
        SchemaComparison comparison)
    {
        // 比對資料型別
        if (!string.Equals(baseColumn.DataType, targetColumn.DataType, StringComparison.OrdinalIgnoreCase))
        {
            comparison.Differences.Add(new SchemaDifference
            {
                ObjectType = SchemaObjectType.Column,
                ObjectName = $"{table.FullName}.[{baseColumn.Name}]",
                DifferenceType = DifferenceType.Modified,
                PropertyName = "DataType",
                SourceValue = baseColumn.DataType,
                TargetValue = targetColumn.DataType,
                RiskLevel = RiskLevel.High, // 改變資料型別是高風險
                Description = $"資料型別不同：{baseColumn.DataType} vs {targetColumn.DataType}"
            });
            return; // 型別不同就不需要比較其他屬性
        }

        // 比對長度
        if (baseColumn.MaxLength != targetColumn.MaxLength)
        {
            var riskLevel = EvaluateLengthChangeRisk(baseColumn.MaxLength, targetColumn.MaxLength);
            comparison.Differences.Add(new SchemaDifference
            {
                ObjectType = SchemaObjectType.Column,
                ObjectName = $"{table.FullName}.[{baseColumn.Name}]",
                DifferenceType = DifferenceType.Modified,
                PropertyName = "MaxLength",
                SourceValue = baseColumn.MaxLength?.ToString() ?? "NULL",
                TargetValue = targetColumn.MaxLength?.ToString() ?? "NULL",
                RiskLevel = riskLevel,
                Description = $"欄位長度不同：{baseColumn.MaxLength} vs {targetColumn.MaxLength}"
            });
        }

        // 比對 Nullable
        if (baseColumn.IsNullable != targetColumn.IsNullable)
        {
            var riskLevel = baseColumn.IsNullable ? RiskLevel.Medium : RiskLevel.Low;
            comparison.Differences.Add(new SchemaDifference
            {
                ObjectType = SchemaObjectType.Column,
                ObjectName = $"{table.FullName}.[{baseColumn.Name}]",
                DifferenceType = DifferenceType.Modified,
                PropertyName = "IsNullable",
                SourceValue = baseColumn.IsNullable.ToString(),
                TargetValue = targetColumn.IsNullable.ToString(),
                RiskLevel = riskLevel,
                Description = $"Nullable 不同：{baseColumn.IsNullable} vs {targetColumn.IsNullable}"
            });
        }

        // 比對預設值
        if (!string.Equals(baseColumn.DefaultValue, targetColumn.DefaultValue, StringComparison.OrdinalIgnoreCase))
        {
            comparison.Differences.Add(new SchemaDifference
            {
                ObjectType = SchemaObjectType.Column,
                ObjectName = $"{table.FullName}.[{baseColumn.Name}]",
                DifferenceType = DifferenceType.Modified,
                PropertyName = "DefaultValue",
                SourceValue = baseColumn.DefaultValue ?? "NULL",
                TargetValue = targetColumn.DefaultValue ?? "NULL",
                RiskLevel = RiskLevel.Low,
                Description = $"預設值不同：{baseColumn.DefaultValue} vs {targetColumn.DefaultValue}"
            });
        }

        // 比對 Identity
        if (baseColumn.IsIdentity != targetColumn.IsIdentity)
        {
            comparison.Differences.Add(new SchemaDifference
            {
                ObjectType = SchemaObjectType.Column,
                ObjectName = $"{table.FullName}.[{baseColumn.Name}]",
                DifferenceType = DifferenceType.Modified,
                PropertyName = "IsIdentity",
                SourceValue = baseColumn.IsIdentity.ToString(),
                TargetValue = targetColumn.IsIdentity.ToString(),
                RiskLevel = RiskLevel.High, // 改變 Identity 是高風險
                Description = $"Identity 不同：{baseColumn.IsIdentity} vs {targetColumn.IsIdentity}"
            });
        }

        // 比對 Collation
        if (!string.Equals(baseColumn.Collation, targetColumn.Collation, StringComparison.OrdinalIgnoreCase))
        {
            comparison.Differences.Add(new SchemaDifference
            {
                ObjectType = SchemaObjectType.Column,
                ObjectName = $"{table.FullName}.[{baseColumn.Name}]",
                DifferenceType = DifferenceType.Modified,
                PropertyName = "Collation",
                SourceValue = baseColumn.Collation ?? "NULL",
                TargetValue = targetColumn.Collation ?? "NULL",
                RiskLevel = RiskLevel.Medium,
                Description = $"Collation 不同：{baseColumn.Collation} vs {targetColumn.Collation}"
            });
        }
    }

    #endregion

    #region 索引比對

    private void CompareIndexes(SchemaTable baseTable, SchemaTable targetTable, SchemaComparison comparison)
    {
        // 基準有，目標沒有
        foreach (var baseIndex in baseTable.Indexes)
        {
            var targetIndex = targetTable.Indexes.FirstOrDefault(i =>
                i.Name.Equals(baseIndex.Name, StringComparison.OrdinalIgnoreCase));

            if (targetIndex == null)
            {
                comparison.Differences.Add(new SchemaDifference
                {
                    ObjectType = SchemaObjectType.Index,
                    ObjectName = $"{baseTable.FullName}.[{baseIndex.Name}]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low, // 新增索引是低風險
                    Description = $"索引 {baseIndex.Name} 不存在於目標環境"
                });
            }
            else
            {
                // 比對索引屬性
                CompareIndexProperties(baseTable, baseIndex, targetIndex, comparison);
            }
        }

        // 目標有，基準沒有（最大化原則）
        foreach (var targetIndex in targetTable.Indexes)
        {
            var baseIndex = baseTable.Indexes.FirstOrDefault(i =>
                i.Name.Equals(targetIndex.Name, StringComparison.OrdinalIgnoreCase));

            if (baseIndex == null)
            {
                comparison.Differences.Add(new SchemaDifference
                {
                    ObjectType = SchemaObjectType.Index,
                    ObjectName = $"{baseTable.FullName}.[{targetIndex.Name}]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low,
                    Description = $"索引 {targetIndex.Name} 不存在於基準環境，基準需要新增"
                });
            }
        }
    }

    private void CompareIndexProperties(
        SchemaTable table,
        SchemaIndex baseIndex,
        SchemaIndex targetIndex,
        SchemaComparison comparison)
    {
        // 比對索引類型
        if (baseIndex.IsClustered != targetIndex.IsClustered ||
            baseIndex.IsUnique != targetIndex.IsUnique)
        {
            comparison.Differences.Add(new SchemaDifference
            {
                ObjectType = SchemaObjectType.Index,
                ObjectName = $"{table.FullName}.[{baseIndex.Name}]",
                DifferenceType = DifferenceType.Modified,
                PropertyName = "IndexType",
                SourceValue = baseIndex.GetIndexType(),
                TargetValue = targetIndex.GetIndexType(),
                RiskLevel = RiskLevel.Medium,
                Description = $"索引類型不同：{baseIndex.GetIndexType()} vs {targetIndex.GetIndexType()}"
            });
        }

        // 比對欄位組成
        var baseSignature = baseIndex.GetColumnsSignature();
        var targetSignature = targetIndex.GetColumnsSignature();
        if (!string.Equals(baseSignature, targetSignature, StringComparison.OrdinalIgnoreCase))
        {
            comparison.Differences.Add(new SchemaDifference
            {
                ObjectType = SchemaObjectType.Index,
                ObjectName = $"{table.FullName}.[{baseIndex.Name}]",
                DifferenceType = DifferenceType.Modified,
                PropertyName = "Columns",
                SourceValue = baseSignature,
                TargetValue = targetSignature,
                RiskLevel = RiskLevel.Medium,
                Description = $"索引欄位組成不同：{baseSignature} vs {targetSignature}"
            });
        }
    }

    #endregion

    #region 約束比對

    private void CompareConstraints(SchemaTable baseTable, SchemaTable targetTable, SchemaComparison comparison)
    {
        // 基準有，目標沒有
        foreach (var baseConstraint in baseTable.Constraints)
        {
            var targetConstraint = targetTable.Constraints.FirstOrDefault(c =>
                c.Name.Equals(baseConstraint.Name, StringComparison.OrdinalIgnoreCase));

            if (targetConstraint == null)
            {
                var riskLevel = EvaluateConstraintRisk(baseConstraint);
                comparison.Differences.Add(new SchemaDifference
                {
                    ObjectType = SchemaObjectType.Constraint,
                    ObjectName = $"{baseTable.FullName}.[{baseConstraint.Name}]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = riskLevel,
                    Description = $"約束 {baseConstraint.Name} 不存在於目標環境"
                });
            }
        }

        // 目標有，基準沒有（最大化原則）
        foreach (var targetConstraint in targetTable.Constraints)
        {
            var baseConstraint = baseTable.Constraints.FirstOrDefault(c =>
                c.Name.Equals(targetConstraint.Name, StringComparison.OrdinalIgnoreCase));

            if (baseConstraint == null)
            {
                var riskLevel = EvaluateConstraintRisk(targetConstraint);
                comparison.Differences.Add(new SchemaDifference
                {
                    ObjectType = SchemaObjectType.Constraint,
                    ObjectName = $"{baseTable.FullName}.[{targetConstraint.Name}]",
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = riskLevel,
                    Description = $"約束 {targetConstraint.Name} 不存在於基準環境，基準需要新增"
                });
            }
        }
    }

    #endregion

    #region 程式物件比對

    private void CompareViews(DatabaseSchema baseSchema, DatabaseSchema targetSchema, SchemaComparison comparison)
    {
        CompareProgramObjects(
            baseSchema.Views,
            targetSchema.Views,
            SchemaObjectType.View,
            comparison);
    }

    private void CompareStoredProcedures(DatabaseSchema baseSchema, DatabaseSchema targetSchema, SchemaComparison comparison)
    {
        CompareProgramObjects(
            baseSchema.StoredProcedures,
            targetSchema.StoredProcedures,
            SchemaObjectType.StoredProcedure,
            comparison);
    }

    private void CompareFunctions(DatabaseSchema baseSchema, DatabaseSchema targetSchema, SchemaComparison comparison)
    {
        CompareProgramObjects(
            baseSchema.Functions,
            targetSchema.Functions,
            SchemaObjectType.Function,
            comparison);
    }

    private void CompareTriggers(DatabaseSchema baseSchema, DatabaseSchema targetSchema, SchemaComparison comparison)
    {
        CompareProgramObjects(
            baseSchema.Triggers,
            targetSchema.Triggers,
            SchemaObjectType.Trigger,
            comparison);
    }

    private void CompareProgramObjects(
        IList<SchemaProgramObject> baseObjects,
        IList<SchemaProgramObject> targetObjects,
        SchemaObjectType objectType,
        SchemaComparison comparison)
    {
        // 基準有，目標沒有
        foreach (var baseObj in baseObjects)
        {
            var targetObj = targetObjects.FirstOrDefault(o =>
                o.Schema.Equals(baseObj.Schema, StringComparison.OrdinalIgnoreCase) &&
                o.Name.Equals(baseObj.Name, StringComparison.OrdinalIgnoreCase));

            if (targetObj == null)
            {
                comparison.Differences.Add(new SchemaDifference
                {
                    ObjectType = objectType,
                    ObjectName = baseObj.FullName,
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low,
                    Description = $"{objectType} {baseObj.FullName} 不存在於目標環境"
                });
            }
            else
            {
                // 比對定義內容
                var baseNormalized = baseObj.GetNormalizedDefinition();
                var targetNormalized = targetObj.GetNormalizedDefinition();

                if (!string.Equals(baseNormalized, targetNormalized, StringComparison.OrdinalIgnoreCase))
                {
                    comparison.Differences.Add(new SchemaDifference
                    {
                        ObjectType = objectType,
                        ObjectName = baseObj.FullName,
                        DifferenceType = DifferenceType.Modified,
                        PropertyName = "Definition",
                        RiskLevel = RiskLevel.Low,
                        Description = $"{objectType} {baseObj.FullName} 定義內容不同"
                    });
                }
            }
        }

        // 目標有，基準沒有（最大化原則）
        foreach (var targetObj in targetObjects)
        {
            var baseObj = baseObjects.FirstOrDefault(o =>
                o.Schema.Equals(targetObj.Schema, StringComparison.OrdinalIgnoreCase) &&
                o.Name.Equals(targetObj.Name, StringComparison.OrdinalIgnoreCase));

            if (baseObj == null)
            {
                comparison.Differences.Add(new SchemaDifference
                {
                    ObjectType = objectType,
                    ObjectName = targetObj.FullName,
                    DifferenceType = DifferenceType.Added,
                    RiskLevel = RiskLevel.Low,
                    Description = $"{objectType} {targetObj.FullName} 不存在於基準環境，基準需要新增"
                });
            }
        }
    }

    #endregion

    #region 風險評估

    /// <summary>
    /// 評估新增欄位的風險
    /// </summary>
    private static RiskLevel EvaluateAddColumnRisk(SchemaColumn column)
    {
        // Nullable 欄位是低風險
        if (column.IsNullable)
        {
            return RiskLevel.Low;
        }

        // NOT NULL 但有預設值是中風險
        if (!string.IsNullOrEmpty(column.DefaultValue))
        {
            return RiskLevel.Medium;
        }

        // NOT NULL 且沒有預設值是高風險
        return RiskLevel.High;
    }

    /// <summary>
    /// 評估欄位長度變更的風險
    /// </summary>
    private static RiskLevel EvaluateLengthChangeRisk(int? baseLength, int? targetLength)
    {
        // 延長是低風險
        if (baseLength > targetLength)
        {
            return RiskLevel.Low;
        }

        // 縮短是高風險（可能資料遺失）
        return RiskLevel.High;
    }

    /// <summary>
    /// 評估約束的風險
    /// </summary>
    private static RiskLevel EvaluateConstraintRisk(SchemaConstraint constraint)
    {
        return constraint.ConstraintType switch
        {
            ConstraintType.PrimaryKey => RiskLevel.High,
            ConstraintType.ForeignKey => RiskLevel.Medium,
            ConstraintType.Unique => RiskLevel.Medium,
            ConstraintType.Check => RiskLevel.Medium,
            ConstraintType.Default => RiskLevel.Low,
            _ => RiskLevel.Medium
        };
    }

    #endregion
}
