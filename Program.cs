using System;
using System.Threading;
using System.Windows;

namespace FocusZoneWin;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 自检模式：不起托盘/热键，只跑一次不透明性验证后退出。
        if (args.Length > 0 && args[0] == "--selftest")
        {
            SelfTest.Run();
            return;
        }

        Logger.Clear();

        // 系统兼容性检查：录屏防捕获需 Win10 2004 (build 19041)+
        if (Environment.OSVersion.Version.Build < 19041)
        {
            Logger.Log("WARN: OS build < 19041 — 录屏防捕获功能不可用");
        }

        // 单实例：用命名 Mutex 判断是否已在运行。
        // 先用 try 创建，不直接用 using，因为杀旧进程后需要重试获取锁。
        Mutex? single = null;
        bool isNew = false;
        try { single = new Mutex(true, "FocusZoneWin.SingleInstance", out isNew); }
        catch (AbandonedMutexException)
        {
            // Mutex abandoned（旧进程被杀）= 当前线程获取了所有权，视为新实例
            isNew = true;
        }
        catch { }

        // 命名事件：重新选区 / 退出
        using var reselect = new EventWaitHandle(false, EventResetMode.AutoReset, "FocusZoneWin.Reselect");
        using var killSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "FocusZoneWin.Kill");

        if (!isNew)
        {
            Logger.Log("Old instance detected, sending kill signal");
            killSignal.Set();

            for (int i = 0; i < 60; i++)   // 最多等 3 秒
            {
                Thread.Sleep(50);
                try
                {
                    single?.Dispose();
                    single = new Mutex(true, "FocusZoneWin.SingleInstance", out isNew);
                    if (isNew)
                    {
                        Logger.Log($"Old instance killed after {(i + 1) * 50}ms, taking over");
                        break;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // 弃用 Mutex 视为所有权已转移
                    isNew = true;
                    single = null;
                    Logger.Log($"Old instance killed after {(i + 1) * 50}ms (abandoned), taking over");
                    break;
                }
                catch { }
            }

            if (!isNew)
            {
                Logger.Log("Failed to kill old instance (timeout 3s), fallback to reselect");
                reselect.Set();
                single?.Dispose();
                return;
            }
        }

        Logger.Log("Program start");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        // 全局异常捕获，防止静默闪退（必须在 Application 实例化之后）
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Logger.Log($"FATAL: {e.ExceptionObject}");
        };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Logger.Log($"DISPATCHER FATAL: {e.Exception}");
            e.Handled = true;  // true = 阻止进程终止，尽量保持运行
        };
        var controller = new AppController();

        // 后台监听外部信号：重新选区 / 退出替换
        using (single)
        {
            var listener = new Thread(() =>
            {
                var handles = new WaitHandle[] { reselect, killSignal };
                while (true)
                {
                    int idx = WaitHandle.WaitAny(handles);
                    if (idx == 0)
                    {
                        Logger.Log("Reselect signal received");
                        try { app.Dispatcher.BeginInvoke(new Action(controller.RequestReselect)); } catch { }
                    }
                    else
                    {
                        Logger.Log("Kill signal received, exiting");
                        try { app.Dispatcher.BeginInvoke(new Action(controller.ExitApp)); } catch { }
                        return;
                    }
                }
            })
            { IsBackground = true, Name = "SignalListener" };
            listener.Start();

            app.Run();
            GC.KeepAlive(controller);
        }
    }
}
