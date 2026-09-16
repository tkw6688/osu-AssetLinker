namespace OsuAssetLinker;

/// <summary>控制台输出辅助，统一配色。</summary>
public static class Ui
{
    public static void Banner()
    {
        Accent("╔══════════════════════════════════════════╗");
        Accent("║   osu! Lazer → Stable 数据同步工具       ║");
        Accent("║   利用硬链接实现零磁盘占用同步           ║");
        Accent("╚══════════════════════════════════════════╝");
        Console.WriteLine();
    }

    public static void Separator() => Info("═══════════════════════════════════════");

    public static void Info(string text) => Write(ConsoleColor.Cyan, text);
    public static void Ok(string text) => Write(ConsoleColor.Green, text);
    public static void Warn(string text) => Write(ConsoleColor.Yellow, text);
    public static void Error(string text) => Write(ConsoleColor.Red, text);
    public static void Accent(string text) => Write(ConsoleColor.Magenta, text);
    public static void Dim(string text) => Write(ConsoleColor.DarkGray, text);

    /// <summary>不换行输出。</summary>
    public static void Inline(string text) => Console.Write(text);

    public static void InlineDim(string text) => WriteInline(ConsoleColor.DarkGray, text);
    public static void InlineOk(string text) => WriteInline(ConsoleColor.Green, text);
    public static void InlineError(string text) => WriteInline(ConsoleColor.Red, text);
    public static void InlineAccent(string text) => WriteInline(ConsoleColor.Magenta, text);

    private static void Write(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void WriteInline(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ResetColor();
    }
}
