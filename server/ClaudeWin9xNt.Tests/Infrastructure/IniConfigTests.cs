using Shouldly;
using ClaudeWin9xNtServer.Infrastructure;

namespace ClaudeWin9xNtServer.Tests.Infrastructure;

public class IniConfigTests
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"ini_{Guid.NewGuid()}");

    public IniConfigTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    private void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_PreservesCurrentState()
    {
        var defaultIni = Path.Combine(_tempDir, "defaults.ini");
        File.WriteAllText(defaultIni, """
            [server]
            api_port = 5000
            download_port = 5001
            upload_port = 5002
            skip_permissions = false
            """);
        IniConfig.Load(defaultIni);

        IniConfig.ApiPort.ShouldBe(5000);
        IniConfig.DownloadPort.ShouldBe(5001);
        IniConfig.UploadPort.ShouldBe(5002);

        var nonexistentPath = Path.Combine(_tempDir, "nonexistent.ini");
        IniConfig.Load(nonexistentPath);

        IniConfig.ApiPort.ShouldBe(5000);
        IniConfig.DownloadPort.ShouldBe(5001);
        IniConfig.UploadPort.ShouldBe(5002);

        Cleanup();
    }

    [Fact]
    public void Load_WhenValidIniExists_ParsesAllSettings()
    {
        var iniPath = Path.Combine(_tempDir, "proxy.ini");
        var content = """
            [server]
            api_port = 8080
            download_port = 8081
            upload_port = 8082
            """;
        File.WriteAllText(iniPath, content);

        IniConfig.Load(iniPath);

        IniConfig.ApiPort.ShouldBe(8080);
        IniConfig.DownloadPort.ShouldBe(8081);
        IniConfig.UploadPort.ShouldBe(8082);

        Cleanup();
    }

    [Fact]
    public void Load_WhenKeysAreMissing_LeavesUnspecifiedKeysUnchanged()
    {
        var defaultIni = Path.Combine(_tempDir, "defaults.ini");
        File.WriteAllText(defaultIni, """
            [server]
            api_port = 5000
            download_port = 5001
            upload_port = 5002
            skip_permissions = false
            """);
        IniConfig.Load(defaultIni);

        var partialIni = Path.Combine(_tempDir, "partial.ini");
        File.WriteAllText(partialIni, """
            [server]
            api_port = 9000
            """);
        IniConfig.Load(partialIni);

        IniConfig.ApiPort.ShouldBe(9000);
        IniConfig.DownloadPort.ShouldBe(5001);
        IniConfig.UploadPort.ShouldBe(5002);

        Cleanup();
    }

    [Fact]
    public void Load_WhenPortValueIsNotInt_IgnoresAndLeavesUnchanged()
    {
        var defaultIni = Path.Combine(_tempDir, "defaults.ini");
        File.WriteAllText(defaultIni, """
            [server]
            api_port = 7000
            """);
        IniConfig.Load(defaultIni);
        IniConfig.ApiPort.ShouldBe(7000);

        var iniPath = Path.Combine(_tempDir, "proxy.ini");
        var content = """
            [server]
            api_port = not_a_number
            """;
        File.WriteAllText(iniPath, content);

        IniConfig.Load(iniPath);

        IniConfig.ApiPort.ShouldBe(7000);
        Cleanup();
    }

    [Fact]
    public void Load_WhenLineIsMalformed_SkipsLine()
    {
        var iniPath = Path.Combine(_tempDir, "proxy.ini");
        var content = """
            [server]
            this_line_has_no_equals
            api_port = 7000
            another_bad_line:with:colons
            """;
        File.WriteAllText(iniPath, content);

        IniConfig.Load(iniPath);

        IniConfig.ApiPort.ShouldBe(7000);
        Cleanup();
    }

    [Fact]
    public void Load_WhenCommentLinesExist_IgnoresComments()
    {
        var iniPath = Path.Combine(_tempDir, "proxy.ini");
        var content = """
            ; This is a semicolon comment
            # This is a hash comment
            [server]
            ; api_port = 6000 (commented out)
            api_port = 6001
            # download_port = 6002 (also commented)
            # upload_port = 6003 (also commented)
            download_port = 5001
            upload_port = 5002
            """;
        File.WriteAllText(iniPath, content);

        IniConfig.Load(iniPath);

        IniConfig.ApiPort.ShouldBe(6001);
        IniConfig.DownloadPort.ShouldBe(5001);
        IniConfig.UploadPort.ShouldBe(5002);
        Cleanup();
    }

    [Fact]
    public void Load_WhenBlankLinesExist_IgnoresBlankLines()
    {
        var iniPath = Path.Combine(_tempDir, "proxy.ini");
        var content = """

            [server]

            api_port = 5050

            download_port = 5051

            """;
        File.WriteAllText(iniPath, content);

        IniConfig.Load(iniPath);

        IniConfig.ApiPort.ShouldBe(5050);
        IniConfig.DownloadPort.ShouldBe(5051);
        Cleanup();
    }

    [Fact]
    public void Load_WhenKeyIsCaseMixed_ParsesCorrectly()
    {
        var iniPath = Path.Combine(_tempDir, "proxy.ini");
        var content = """
            [server]
            API_PORT = 5100
            Download_Port = 5101
            UPLOAD_PORT = 5102
            """;
        File.WriteAllText(iniPath, content);

        IniConfig.Load(iniPath);

        IniConfig.ApiPort.ShouldBe(5100);
        IniConfig.DownloadPort.ShouldBe(5101);
        IniConfig.UploadPort.ShouldBe(5102);
        Cleanup();
    }

    [Fact]
    public void Load_WhenSectionIsCaseMixed_ParsesCorrectly()
    {
        var iniPath = Path.Combine(_tempDir, "proxy.ini");
        var content = """
            [SERVER]
            api_port = 5200
            """;
        File.WriteAllText(iniPath, content);

        IniConfig.Load(iniPath);

        IniConfig.ApiPort.ShouldBe(5200);
        Cleanup();
    }

    [Fact]
    public void Load_WhenValueHasWhitespace_TrimmedCorrectly()
    {
        var iniPath = Path.Combine(_tempDir, "proxy.ini");
        var content = """
            [server]
            api_port   =   5300
            """;
        File.WriteAllText(iniPath, content);

        IniConfig.Load(iniPath);

        IniConfig.ApiPort.ShouldBe(5300);
        Cleanup();
    }

    [Fact]
    public void Load_WhenOtherSectionsExist_IgnoresNonServerSections()
    {
        var iniPath = Path.Combine(_tempDir, "proxy.ini");
        var content = """
            [other_section]
            api_port = 9999
            [server]
            api_port = 5400
            [another_section]
            api_port = 8888
            """;
        File.WriteAllText(iniPath, content);

        IniConfig.Load(iniPath);

        IniConfig.ApiPort.ShouldBe(5400);
        Cleanup();
    }

    [Fact]
    public void Load_WhenKeyAppearsMultipleTimes_LastValueWins()
    {
        var iniPath = Path.Combine(_tempDir, "proxy.ini");
        var content = """
            [server]
            api_port = 5500
            api_port = 5501
            api_port = 5502
            """;
        File.WriteAllText(iniPath, content);

        IniConfig.Load(iniPath);

        IniConfig.ApiPort.ShouldBe(5502);
        Cleanup();
    }
}
