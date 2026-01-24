namespace TableSpec.Application.Services;

/// <summary>
/// 匯出服務介面
/// </summary>
public interface IExportService
{
    /// <summary>
    /// 匯出所有資料表規格到 Excel
    /// </summary>
    /// <returns>Excel 檔案的二進位內容</returns>
    Task<byte[]> ExportToExcelAsync(CancellationToken ct = default);

    /// <summary>
    /// 匯出指定資料表規格到 Excel
    /// </summary>
    /// <param name="schema">結構描述</param>
    /// <param name="tableName">表格名稱</param>
    /// <returns>Excel 檔案的二進位內容</returns>
    Task<byte[]> ExportTableToExcelAsync(
        string schema,
        string tableName,
        CancellationToken ct = default);
}
