using Avalonia;

namespace PCL.Desktop.Linux;

/// <summary>
/// Linux 桌面宿主入口（LU1 骨架）。
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Avalonia 的预览器 / 设计器通过此方法定位 AppBuilder，签名不可改。
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
