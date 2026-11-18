using System.Runtime.InteropServices;

namespace OsuAssetLinker;

/// <summary>控制台交互：判断是否双击运行、y/N 提问、退出前暂停。</summary>
public static class ConsoleSession
{
    /// <summary>没有父控制台进程（通常意味着双击运行）时为 true。</summary>
    public static bool IsInteractive
    {
        get
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;
            uint[] list = new uint[1];
            return GetConsoleProcessList(list, 1) <= 1;
        }
    }

    /// <summary>y/N 提问，输入为空时返回 <paramref name="defaultValue"/>。</summary>
    public static bool Ask(string prompt, bool defaultValue)
    {
        Console.Write($"{prompt} [{(defaultValue ? "Y/n" : "y/N")}]: ");
        string input = Console.ReadLine()?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrEmpty(input))
            return defaultValue;

        return input is "y" or "yes";
    }

    /// <summary>双击运行时暂停，让用户能看到结果。输入被重定向时 ReadKey 不可用，直接跳过。</summary>
    public static void PauseBeforeExit()
    {
        if (Console.IsInputRedirected)
            return;

        Console.Write("\n按任意键退出...");

        try
        {
            Console.ReadKey(true);
        }
        catch (InvalidOperationException)
        {
            // 没有控制台（例如被计划任务拉起），忽略即可
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
}
