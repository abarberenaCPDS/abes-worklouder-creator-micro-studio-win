using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Platforms.Windows;

/// Brings the terminal that hosts Herdr's panes to the foreground.
///
/// The macOS app looks up the terminal by bundle id; Windows has no equivalent,
/// so this matches the executable name of the common terminals instead. Override
/// with `WL_TERMINAL_PROCESS` if your panes live somewhere else.
///
/// Nothing is ever launched: the agent whose key was pressed is running in a
/// pane of a terminal that is by definition already up, so a terminal that is
/// not running means the name is wrong, and opening a fresh window would not be
/// what the key meant.
internal static class TerminalFocus
{
    private static readonly string[] KnownTerminals =
    {
        "WindowsTerminal", "wt", "wezterm-gui", "alacritty", "kitty", "mintty",
        "ghostty", "ConEmu64", "ConEmu",
    };

    private const int SW_RESTORE = 9;

    /// True when a terminal window was found and asked to come forward.
    public static bool TryRaiseTerminal()
    {
        var handle = FindTerminalWindow();
        if (handle == IntPtr.Zero) return false;

        if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);

        // Windows only lets the foreground process hand focus on, so a
        // background app's SetForegroundWindow is a no-op. Attaching to the
        // current foreground thread's input queue first is the standard way
        // around that.
        var foreground = GetForegroundWindow();
        var target = GetWindowThreadProcessId(foreground, out _);
        var current = GetCurrentThreadId();
        var attached = target != 0 && target != current && AttachThreadInput(current, target, true);

        try
        {
            return SetForegroundWindow(handle);
        }
        finally
        {
            if (attached) AttachThreadInput(current, target, false);
        }
    }

    private static IntPtr FindTerminalWindow()
    {
        var overrideName = Environment.GetEnvironmentVariable("WL_TERMINAL_PROCESS");
        var names = string.IsNullOrEmpty(overrideName) ? KnownTerminals : new[] { overrideName };

        foreach (var name in names)
        {
            Process[] candidates;
            try
            {
                candidates = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in candidates)
            {
                using (process)
                {
                    if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                }
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
