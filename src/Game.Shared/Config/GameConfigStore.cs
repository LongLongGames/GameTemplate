namespace Game.Shared.Config;

/// <summary>
/// 配置目录加载骨架。
/// clone 后按 ECC 生成的表类型扩展 LoadXxx；模板本身不内置玩法表。
/// </summary>
public sealed class GameConfigStore
{
    public string ConfigRoot { get; }

    GameConfigStore(string configRoot)
    {
        ConfigRoot = configRoot;
    }

    /// <summary>
    /// 解析配置根目录：优先 Config:Root / Config__Root，否则探测常见相对路径。
    /// </summary>
    public static string ResolveConfigRoot(string? fromConfiguration)
    {
        if (!string.IsNullOrWhiteSpace(fromConfiguration))
            return Path.GetFullPath(fromConfiguration);

        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "config"),
            Path.Combine(AppContext.BaseDirectory, "config"),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "config")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "config")),
        };

        foreach (var c in candidates)
        {
            if (Directory.Exists(c))
                return Path.GetFullPath(c);
        }

        var fallback = Path.Combine(Directory.GetCurrentDirectory(), "config");
        Directory.CreateDirectory(fallback);
        return Path.GetFullPath(fallback);
    }

    /// <summary>
    /// 仅解析根目录并返回空 Store。各游戏在此加载 .bytes 并填入字典。
    /// </summary>
    public static GameConfigStore Load(string configRoot)
    {
        if (string.IsNullOrWhiteSpace(configRoot))
            throw new ArgumentException("configRoot required", nameof(configRoot));

        configRoot = Path.GetFullPath(configRoot);
        Directory.CreateDirectory(configRoot);
        Console.WriteLine($"[GameConfig] root={configRoot} (template skeleton; extend with ECC tables)");
        return new GameConfigStore(configRoot);
    }
}
