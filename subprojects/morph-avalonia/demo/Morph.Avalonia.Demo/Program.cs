using Avalonia;
using Mostlylucid.Avalonia.UITesting;

namespace Morph.Avalonia.Demo;

internal static class Program
{
    public static string[] Arguments { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        Arguments = args;
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace()
            .UseUITesting(options => options.Log = Console.WriteLine)
            .StartWithClassicDesktopLifetime(args);
    }
}
