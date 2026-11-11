using System.Windows.Threading;

namespace PCL
{

    /// <summary>
/// 指示页面可以通过按下 F5 或以其他方式刷新。
/// </summary>
    public interface IRefreshable
    {
        void Refresh();
    }

    /// <summary>
/// 指示页面可以处理未捕获的异常。
/// </summary>
    public interface IDispatcherUnhandledException
    {
        void DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e);
    }

    /// <summary>
/// 指示该控件是可勾选的。
/// </summary>
    public interface IMyRadio
    {
        event CheckEventHandler Check;

        delegate void CheckEventHandler(object sender, ModBase.RouteEventArgs e);
        event ChangedEventHandler Changed;

        delegate void ChangedEventHandler(object sender, ModBase.RouteEventArgs e);
    }
}