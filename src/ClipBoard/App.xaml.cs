using System;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace ClipBoard;

public partial class App : Application
{
    private Mutex? instanceMutex;
    private bool ownsMutex;

    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        var user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        instanceMutex = new Mutex(true, @"Local\ClipBoard." + user, out ownsMutex);
        if (!ownsMutex)
        {
            NativeClipboardMonitor.ShowExistingInstance();
            Shutdown();
            return;
        }

        var window = new MainWindow(startPaused: args.Args.Contains("--paused"));
        MainWindow = window;
        window.ShowFromTray();
    }

    protected override void OnExit(ExitEventArgs args)
    {
        if (ownsMutex)
            instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(args);
    }
}