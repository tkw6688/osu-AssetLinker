using System.Text;
using OsuAssetLinker;

// ============================================================
//  osu! Lazer → Stable 数据同步工具
//  用法: osu-AssetLinker [--lazer-dir <目录>] [--stable-dir <目录>] [功能开关]
// ============================================================

Console.OutputEncoding = Encoding.UTF8;
Ui.Banner();

// --- --help ---
if (args.Contains("--help") || args.Contains("-h"))
{
    HelpText.Print();
    return 0;
}

try
{
    return SyncRunner.Run(args);
}
finally
{
    // 双击运行时（包括拖拽参数导致解析失败的场景）暂停，让用户看到报错
    if (ConsoleSession.IsInteractive)
        ConsoleSession.PauseBeforeExit();
}
