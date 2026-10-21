namespace AbesWorkLouderCreatorMicroStudio.Windows.Platforms.Windows;

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;

/// <summary>Windows implementations of host-level input and launch actions.</summary>
internal static class SystemActions
{
    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint KeyUp = 0x0002;
    private const uint KeyUnicode = 0x0004;
    private const uint MouseWheel = 0x0800;
    private const uint MouseHWheel = 0x1000;
    private const int WheelDelta = 120;

    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0xA2, ["control"] = 0xA2, ["alt"] = 0xA4, ["shift"] = 0xA0,
        ["win"] = 0x5B, ["windows"] = 0x5B, ["cmd"] = 0x5B, ["command"] = 0x5B,
        ["enter"] = 0x0D, ["return"] = 0x0D, ["esc"] = 0x1B, ["escape"] = 0x1B,
        ["tab"] = 0x09, ["space"] = 0x20, ["backspace"] = 0x08, ["delete"] = 0x2E,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["insert"] = 0x2D, ["spacebar"] = 0x20,
    };

    public static void Scroll(InputDirection direction, int amount)
    {
        var delta = Math.Clamp(amount, 1, 20) * WheelDelta;
        if (direction is InputDirection.Up or InputDirection.Left) delta = -delta;
        var flags = direction is InputDirection.Left or InputDirection.Right ? MouseHWheel : MouseWheel;
        Send(new Input
        {
            Type = InputMouse,
            Union = new InputUnion { Mouse = new MouseInput { Flags = flags, MouseData = unchecked((uint)delta) } },
        });
    }

    public static void Shortcut(string chord)
    {
        var names = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0) throw new InvalidOperationException("Enter a keyboard shortcut such as ctrl+shift+k.");
        var keys = names.Select(ResolveKey).ToArray();
        foreach (var key in keys) Key(key, keyUp: false);
        foreach (var key in keys.Reverse()) Key(key, keyUp: true);
    }

    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var inputs = new List<Input>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(Unicode(character, keyUp: false));
            inputs.Add(Unicode(character, keyUp: true));
        }
        Send(inputs.ToArray());
    }

    public static void Media(MediaKey key)
    {
        var virtualKey = key switch
        {
            MediaKey.PlayPause => (ushort)0xB3,
            MediaKey.Next => (ushort)0xB0,
            MediaKey.Previous => (ushort)0xB1,
            MediaKey.VolumeUp => (ushort)0xAF,
            MediaKey.VolumeDown => (ushort)0xAE,
            MediaKey.Mute => (ushort)0xAD,
            _ => throw new NotSupportedException($"Windows does not expose a standard media key for {key} on this device."),
        };
        Key(virtualKey, keyUp: false);
        Key(virtualKey, keyUp: true);
    }

    public static void VoiceTyping() => Shortcut("win+h");

    public static void Open(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("Set a URL, file path, or app name first.");
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    public static async Task RunShellAsync(string command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("Set a PowerShell command first.");
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(command);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("PowerShell could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("PowerShell action timed out after 30 seconds.");
        }

        var stderr = await error.ConfigureAwait(false);
        var stdout = await output.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? $"PowerShell exited with code {process.ExitCode}." : stderr.Trim());
        if (!string.IsNullOrWhiteSpace(stdout)) System.Diagnostics.Debug.WriteLine(stdout.Trim());
    }

    private static ushort ResolveKey(string name)
    {
        if (NamedKeys.TryGetValue(name, out var key)) return key;
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
        {
            var upper = char.ToUpperInvariant(name[0]);
            return char.IsAsciiLetter(upper) ? (ushort)upper : (ushort)(0x30 + upper - '0');
        }
        if (name.Length >= 2 && name[0] is 'f' or 'F' && int.TryParse(name.AsSpan(1), out var function) && function is >= 1 and <= 24)
            return (ushort)(0x70 + function - 1);
        throw new InvalidOperationException($"Unknown Windows keyboard key '{name}'.");
    }

    private static void Key(ushort key, bool keyUp) => Send(new Input
    {
        Type = InputKeyboard,
        Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = keyUp ? KeyUp : 0 } },
    });

    private static Input Unicode(char character, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput { ScanCode = character, Flags = KeyUnicode | (keyUp ? KeyUp : 0) },
        },
    };

    private static void Send(params Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not accept the synthesized input. Check the target window's elevation and focus.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
