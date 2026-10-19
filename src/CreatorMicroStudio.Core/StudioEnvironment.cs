namespace AbesWorkLouderCreatorMicroStudio.Windows.Core;

/// The environment variables the Studio apps own.
///
/// They share the `WL_` prefix with the macOS reference (`WL_EMULATE`), so one
/// name means the same thing on either side. Herdr's own `HERDR_SOCKET_PATH` and
/// `XDG_CONFIG_HOME` keep their upstream names and are read directly. The old
/// `MICROMANAGER_*` names retired in 0.5.0 - do not reintroduce them.
public static class StudioEnvironment
{
    /// `1` runs against the in-process pad emulator, ignoring hardware.
    public const string Emulate = "WL_EMULATE";

    /// The distro that holds Herdr, when it is not WSL's default.
    public const string WslDistro = "WL_WSL_DISTRO";

    /// Optional path to the `herdr` CLI used by Herdr command actions.
    public const string HerdrPath = "WL_HERDR_PATH";

    /// The terminal executable to raise when an agent key is pressed.
    public const string TerminalProcess = "WL_TERMINAL_PROCESS";

    /// True when <see cref="Emulate"/> asks for the in-process pad.
    public static bool IsEmulated() =>
        Environment.GetEnvironmentVariable(Emulate) is "1" or "true" or "True" or "TRUE";
}
