using System;
using System.IO;
using System.Windows;

namespace SysMonitor
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 常驻小工具不能“静默死掉”：未处理异常全部落盘，UI 线程的异常尽量让它继续跑。
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError(e.ExceptionObject as Exception);

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                LogError(e.Exception);
                e.Handled = true;
            };

            app.Run(new FloatWindow());
        }

        private static void LogError(Exception ex)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SysMonitorFloat");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
                // 记日志本身失败就算了
            }
        }
    }
}
