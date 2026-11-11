using System.Runtime.CompilerServices;
using System.Text;

namespace MeloongCore.Tests
{
    /// <summary>
    /// 测试进程的全局初始化。
    /// </summary>
    /// <remarks>
    /// .NET Core / .NET 5+ 不再内置 GBK / GB18030 等代码页编码，必须显式注册
    /// <see cref="CodePagesEncodingProvider"/> 才能 <c>Encoding.GetEncoding("GB18030")</c>。
    /// 产品侧在 <c>pcl-next/Application.xaml.cs</c> 启动时注册过一次；
    /// 测试进程不经过 WPF 启动流程，所以这里要自己注册，
    /// 否则 FileUtils 解压 GBK 编码的压缩包会抛 ArgumentException（2026-11-11 修正）。
    /// </remarks>
    internal static class TestSetup
    {
        [ModuleInitializer]
        internal static void Init()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }
}
