using System.Windows;

namespace DingPanMao.Interop;

/// <summary>定位 Windows 任务栏与系统托盘，用于把长条贴到任务栏空白处。</summary>
internal static class TaskbarLocator
{
    /// <summary>主任务栏的物理像素矩形。</summary>
    internal static Rect? GetTaskbarRect() => GetRectByClass("Shell_TrayWnd");

    /// <summary>
    /// 系统托盘区域的物理像素矩形，它的左边界即任务栏可用空白区的右界。
    /// 托盘是任务栏的子窗口，必须用 FindWindowEx 从任务栏下面找。
    /// </summary>
    internal static Rect? GetTrayNotifyRect()
    {
        var shell = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (shell == IntPtr.Zero)
        {
            return null;
        }

        var tray = NativeMethods.FindWindowEx(shell, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray == IntPtr.Zero)
        {
            return null;
        }

        return ReadRect(tray);
    }

    private static Rect? GetRectByClass(string className)
    {
        var handle = NativeMethods.FindWindow(className, null);
        return handle == IntPtr.Zero ? null : ReadRect(handle);
    }

    private static Rect? ReadRect(IntPtr handle)
    {
        if (!NativeMethods.GetWindowRect(handle, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        return width > 0 && height > 0 ? new Rect(rect.Left, rect.Top, width, height) : null;
    }
}
