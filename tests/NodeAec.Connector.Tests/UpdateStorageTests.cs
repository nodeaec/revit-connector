using System;
using System.IO;
using NodeAec.Connector.Storage;
using NodeAec.Connector.Updates;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Unit tests for %APPDATA%\NodeAec\update-status.json persistence.
/// </summary>
public class UpdateStorageTests : IDisposable
{
    private readonly string _tempDir;

    public UpdateStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecUpdateStorageTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LeaseStorage.SetCustomBasePath(_tempDir);
    }

    public void Dispose()
    {
        LeaseStorage.SetCustomBasePath(null);
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void LoadUpdateStatus_WhenMissing_ReturnsNullWithoutThrowing()
    {
        var status = UpdateStorage.LoadUpdateStatus();
        Assert.Null(status);
    }

    [Fact]
    public void LoadUpdateStatus_WhenCorruptJson_ReturnsNullWithoutThrowing()
    {
        string path = UpdateStorage.GetUpdateStatusFilePath();
        File.WriteAllText(path, "{ corrupt json ... invalid syntax");

        var status = UpdateStorage.LoadUpdateStatus();
        Assert.Null(status);
    }

    [Fact]
    public void LoadUpdateStatus_WhenEmptyFile_ReturnsNullWithoutThrowing()
    {
        string path = UpdateStorage.GetUpdateStatusFilePath();
        File.WriteAllBytes(path, Array.Empty<byte>());

        var status = UpdateStorage.LoadUpdateStatus();
        Assert.Null(status);
    }

    [Fact]
    public void SaveAndLoad_ValidData_PersistsAndReadsCorrectly()
    {
        bool saved = UpdateStorage.SaveUpdateStatus("0.4.0", "0.3.0");
        Assert.True(saved);

        var loaded = UpdateStorage.LoadUpdateStatus();
        Assert.NotNull(loaded);
        Assert.Equal("0.4.0", loaded!.LatestVersion);
        Assert.Equal("0.3.0", loaded.MinimumVersion);
        Assert.NotNull(loaded.UpdatedAt);
    }

    [Fact]
    public void SaveAndLoad_NullMinimumVersion_PersistsAndReadsNull()
    {
        bool saved = UpdateStorage.SaveUpdateStatus("0.5.0", null);
        Assert.True(saved);

        var loaded = UpdateStorage.LoadUpdateStatus();
        Assert.NotNull(loaded);
        Assert.Equal("0.5.0", loaded!.LatestVersion);
        Assert.Null(loaded.MinimumVersion);
    }
}
