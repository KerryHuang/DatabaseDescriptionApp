using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace Specurai.Cli.Output;

/// <summary>
/// CLI 輸出管理器，支援人類可讀格式和 JSON 格式
/// </summary>
public static class CliOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 是否為 JSON 輸出模式
    /// </summary>
    public static bool JsonMode { get; set; }

    /// <summary>
    /// 輸出成功結果
    /// </summary>
    public static void Success<T>(T data, int? count = null)
    {
        if (JsonMode)
        {
            var result = new CliResult<T>
            {
                Success = true,
                Data = data,
                Metadata = count.HasValue ? new ResultMetadata { Count = count.Value } : null
            };
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
    }

    /// <summary>
    /// 輸出錯誤結果
    /// </summary>
    public static void Error(string message)
    {
        if (JsonMode)
        {
            var result = new CliResult<object>
            {
                Success = false,
                Error = message
            };
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗ {message.EscapeMarkup()}[/]");
        }
    }

    /// <summary>
    /// 輸出資訊訊息（僅人類模式）
    /// </summary>
    public static void Info(string message)
    {
        if (!JsonMode)
        {
            AnsiConsole.MarkupLine($"[grey]{message.EscapeMarkup()}[/]");
        }
    }

    /// <summary>
    /// 輸出成功訊息（僅人類模式）
    /// </summary>
    public static void SuccessMessage(string message)
    {
        if (!JsonMode)
        {
            AnsiConsole.MarkupLine($"[green]✓ {message.EscapeMarkup()}[/]");
        }
    }

    /// <summary>
    /// 輸出警告訊息
    /// </summary>
    public static void Warning(string message)
    {
        if (JsonMode)
        {
            Console.Error.WriteLine(message);
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ {message.EscapeMarkup()}[/]");
        }
    }
}

/// <summary>
/// CLI JSON 輸出結構
/// </summary>
public class CliResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
    public ResultMetadata? Metadata { get; set; }
}

/// <summary>
/// 結果元資料
/// </summary>
public class ResultMetadata
{
    public int Count { get; set; }
}
