namespace ClaudeWin9xNtServer.Infrastructure;

public static class IniConfig
{
    public const string ApiKey = "a3f8b2d1-7c4e-4a9f-b6e5-2d8c1f0e3a7b";
    public static int ApiPort { get; private set; } = 5000;
    public static int DownloadPort { get; private set; } = 5001;
    public static int UploadPort { get; private set; } = 5002;

    public static void Load(string filename = "proxy.ini")
    {
        var path = Path.Combine(AppContext.BaseDirectory, filename);
        if (!File.Exists(path))
        {
            return;
        }

        var config = File.ReadLines(path)
            .Select(l => l.Trim())
            .SkipWhile(l => !l.Equals("[server]", StringComparison.OrdinalIgnoreCase))
            .Skip(1)
            .TakeWhile(l => !l.StartsWith('['))
            .Where(l => l.Contains('=') && !l.StartsWith(';') && !l.StartsWith('#'))
            .Select(l => l.Split('=', 2))
            .GroupBy(p => p[0].Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Last()[1].Trim());

        if (config.TryGetValue("api_port", out var ap) && int.TryParse(ap, out var apiPort))
        {
            ApiPort = apiPort;
        }
        if (config.TryGetValue("download_port", out var dp) && int.TryParse(dp, out var dlPort))
        {
            DownloadPort = dlPort;
        }
        if (config.TryGetValue("upload_port", out var up) && int.TryParse(up, out var ulPort))
        {
            UploadPort = ulPort;
        }
    }
}
