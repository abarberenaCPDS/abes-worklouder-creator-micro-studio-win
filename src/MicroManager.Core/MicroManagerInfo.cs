namespace MicroManager.Core;

using System.Reflection;

/// What the port calls itself.
///
/// The version is read from the assembly's informational version, which the SDK
/// fills in from `Version` in `windows/Directory.Build.props` — the single place
/// a release bumps. Anything from a `+` onwards (build metadata the SDK may add,
/// such as a commit sha) is dropped, so this is the plain SemVer string.
public static class MicroManagerInfo
{
    public const string Name = "Micro Manager";

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(MicroManagerInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrEmpty(informational))
        {
            var metadata = informational.IndexOf('+');
            return metadata >= 0 ? informational[..metadata] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
