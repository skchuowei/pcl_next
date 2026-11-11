using System;
using System.Windows;

namespace PCL
{
    /// <summary>
    /// Explicit WPF entry point.  The VB build relied on MyApplication to
    /// synthesise Main; a C# SDK project needs a real one.  InitializeComponent
    /// is generated from Application.xaml and applies StartupUri / resources.
    /// </summary>
    internal static class PclEntryPoint
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var app = new Application();
            app.InitializeComponent();
            app.Run();
        }
    }
}
