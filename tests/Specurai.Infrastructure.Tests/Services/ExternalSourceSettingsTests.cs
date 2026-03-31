using FluentAssertions;
using Specurai.Application.Services;
using Specurai.Infrastructure.Services;

namespace Specurai.Infrastructure.Tests.Services;

/// <summary>
/// 外部連線來源設定服務測試
/// </summary>
public class ExternalSourceSettingsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ExternalSourceSettings _sut;

    public ExternalSourceSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _sut = new ExternalSourceSettings(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_檔案不存在_回傳空設定()
    {
        var config = _sut.Load();
        config.SourceDirectory.Should().BeEmpty();
        config.KeyFilePath.Should().BeEmpty();
    }

    [Fact]
    public void SaveAndLoad_來回存取_資料一致()
    {
        var original = new ExternalSourceConfig(
            SourceDirectory: @"C:\repos\ansible",
            KeyFilePath: @"C:\vault-pass.txt");

        _sut.Save(original);
        var loaded = _sut.Load();

        loaded.SourceDirectory.Should().Be(original.SourceDirectory);
        loaded.KeyFilePath.Should().Be(original.KeyFilePath);
    }

    [Fact]
    public void Save_覆蓋存檔_以最新設定為準()
    {
        _sut.Save(new ExternalSourceConfig("first", "key1"));
        _sut.Save(new ExternalSourceConfig("second", "key2"));

        var loaded = _sut.Load();
        loaded.SourceDirectory.Should().Be("second");
        loaded.KeyFilePath.Should().Be("key2");
    }

    [Fact]
    public void Load_損毀的JSON_回傳空設定()
    {
        File.WriteAllText(Path.Combine(_tempDir, "external-source.json"), "not-valid-json{{");
        var config = _sut.Load();
        config.SourceDirectory.Should().BeEmpty();
        config.KeyFilePath.Should().BeEmpty();
    }
}
