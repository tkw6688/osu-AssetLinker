namespace OsuAssetLinker;

/// <summary>--help 输出。</summary>
public static class HelpText
{
    public static void Print()
    {
        Console.WriteLine("用法:");
        Console.WriteLine("  osu-AssetLinker [--lazer-dir <目录>] [--stable-dir <目录>] [功能开关] [其他选项]");
        Console.WriteLine();
        Console.WriteLine("路径选项（省略时自动检测）:");
        Console.WriteLine("  --lazer-dir <目录>   osu!lazer 数据目录（含 client.realm）");
        Console.WriteLine("                       会自动跟随 storage.ini 里的自定义数据目录");
        Console.WriteLine("  --stable-dir <目录>  osu!stable 根目录（含 osu!.exe / Songs / Skins / osu!*.cfg）");
        Console.WriteLine("                       也接受 Songs 或 Skins 目录，会自动改用其父目录");
        Console.WriteLine();
        Console.WriteLine("功能开关（可组合，都不写时等同 --beats）:");
        Console.WriteLine("  --beats        同步铺面到 stable 的 Songs 目录");
        Console.WriteLine("  --skins        同步皮肤到 <stable-dir>\\Skins");
        Console.WriteLine("  --all          同步以上全部");
        Console.WriteLine();
        Console.WriteLine("其他选项:");
        Console.WriteLine("  --relink       将 stable 中已有的普通文件替换为硬链接，释放磁盘空间");
        Console.WriteLine("                 （要求目标目录与 lazer 在同一磁盘分区）");
        Console.WriteLine("  --version, -v  显示版本信息");
        Console.WriteLine("  --help, -h     显示此帮助信息");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  osu-AssetLinker");
        Console.WriteLine("      自动检测路径，只同步铺面");
        Console.WriteLine();
        Console.WriteLine("  osu-AssetLinker --all --relink");
        Console.WriteLine("      同步全部内容，并把已有的普通文件替换为硬链接");
        Console.WriteLine();
        Console.WriteLine("  osu-AssetLinker --lazer-dir \"C:\\Users\\你\\AppData\\Roaming\\osu\" --stable-dir \"C:\\osu!\" --skins");
        Console.WriteLine("      手动指定路径，只同步皮肤");
        Console.WriteLine();
        Console.WriteLine("说明:");
        Console.WriteLine("  · 铺面目标优先使用 stable 配置里的 BeatmapDirectory；没有配置时使用 <stable-dir>\\Songs");
        Console.WriteLine("  · 同一分区时使用硬链接，文件不会被复制，磁盘占用为零");
        Console.WriteLine("  · 不同分区时回退为复制，占用额外磁盘空间");
        Console.WriteLine("  · 运行前请关闭 osu! lazer（Realm 数据库需要独占访问）");
        Console.WriteLine("  · 同步完成后在 stable 中按 F5 刷新铺面列表");
    }
}
