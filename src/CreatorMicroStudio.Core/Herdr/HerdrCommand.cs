namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;

using System.Diagnostics;
using System.Text;

/// <summary>Parses a Herdr CLI line and resolves placeholders without invoking a shell.</summary>
public sealed class HerdrCommand
{
    public abstract record Segment;
    public sealed record Literal(string Text) : Segment;
    public sealed record Value(string Name) : Segment;
    public sealed record Ask(string Prompt, IReadOnlyList<Segment> Default) : Segment;

    public sealed record Argument(IReadOnlyList<Segment> Segments);

    private static readonly HashSet<string> KnownValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "pane", "tab", "workspace", "tab_label", "workspace_label", "agent",
    };

    private HerdrCommand(string source, IReadOnlyList<Argument> arguments)
    {
        Source = source;
        Arguments = arguments;
    }

    public string Source { get; }
    public IReadOnlyList<Argument> Arguments { get; }

    public IReadOnlySet<string> Placeholders => Arguments
        .SelectMany(argument => Visit(argument.Segments))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> Visit(IEnumerable<Segment> segments)
    {
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case Value value: yield return value.Name; break;
                case Ask ask:
                    foreach (var value in Visit(ask.Default)) yield return value;
                    break;
            }
        }
    }

    public static HerdrCommand Parse(string text)
    {
        var source = text ?? "";
        if (LegacyIssue(source) is { } issue) throw new FormatException(issue);
        var parser = new Parser(source);
        var args = parser.ParseArguments();
        if (args.Count == 0) throw new FormatException("No Herdr command is configured.");
        if (args[0].Segments is [Literal { Text: "herdr" }]) args.RemoveAt(0);
        if (args.Count == 0) throw new FormatException("No command follows 'herdr'.");
        return new HerdrCommand(source, args);
    }

    private static string? LegacyIssue(string text)
    {
        var value = text.Trim();
        if (value.StartsWith("herdr", StringComparison.OrdinalIgnoreCase) &&
            value.Length > 5 && char.IsWhiteSpace(value[5]))
            value = value[5..].Trim();
        if (value.Length == 0) return null;
        if (value.Any(char.IsWhiteSpace) || value.Any(character => "{}'\"\\".Contains(character))) return null;
        if (value.Contains('+')) return $"'{value}' looks like a Herdr TUI keybinding, not a CLI command.";
        if (value is "status" or "help" or "version" || value.StartsWith('-')) return null;
        return $"'{value}' is not a Herdr command line. Herdr's internal command IDs cannot be run by another app.";
    }

    public async Task<string[]?> ResolveAsync(
        IReadOnlyDictionary<string, string?> context,
        Func<string, string, Task<string?>>? ask,
        CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        var answers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var argument in Arguments)
        {
            var value = new StringBuilder();
            foreach (var segment in argument.Segments)
            {
                switch (segment)
                {
                    case Literal literal:
                        value.Append(literal.Text);
                        break;
                    case Value placeholder:
                        value.Append(ResolveValue(placeholder.Name, context));
                        break;
                    case Ask request:
                        if (!answers.TryGetValue(request.Prompt, out var answer))
                        {
                            if (ask is null) throw new InvalidOperationException("This command needs an input prompt, but no prompt UI is available.");
                            var defaultValue = ResolveSegments(request.Default, context);
                            answer = await ask(request.Prompt, defaultValue).ConfigureAwait(false) ?? "\0CANCEL\0";
                            answers[request.Prompt] = answer;
                        }
                        if (answer == "\0CANCEL\0") return null;
                        value.Append(answer);
                        break;
                }
            }
            result.Add(value.ToString());
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result.ToArray();
    }

    private static string ResolveSegments(IEnumerable<Segment> segments, IReadOnlyDictionary<string, string?> context) =>
        string.Concat(segments.Select(segment => segment switch
        {
            Literal literal => literal.Text,
            Value value => ResolveValue(value.Name, context),
            _ => "",
        }));

    private static string ResolveValue(string name, IReadOnlyDictionary<string, string?> context)
    {
        if (!context.TryGetValue(name, out var value) || string.IsNullOrEmpty(value))
            throw new InvalidOperationException($"Herdr has no value for {{{name}}} right now.");
        return value;
    }

    private sealed class Parser(string source)
    {
        private readonly string _source = source.Trim();
        private int _index;

        public List<Argument> ParseArguments()
        {
            var arguments = new List<Argument>();
            var segments = new List<Segment>();
            var literal = new StringBuilder();
            var inWord = false;

            void FlushLiteral()
            {
                if (literal.Length == 0) return;
                segments.Add(new Literal(literal.ToString()));
                literal.Clear();
            }
            void EndWord()
            {
                FlushLiteral();
                if (inWord) arguments.Add(new Argument(segments.Count == 0 ? [new Literal("")] : segments));
                segments = new List<Segment>();
                inWord = false;
            }

            while (_index < _source.Length)
            {
                var c = _source[_index];
                if (char.IsWhiteSpace(c)) { EndWord(); _index++; continue; }
                inWord = true;
                if (c == '\'')
                {
                    _index++;
                    var close = _source.IndexOf('\'', _index);
                    if (close < 0) throw new FormatException("A single-quote is not closed.");
                    literal.Append(_source.AsSpan(_index, close - _index));
                    _index = close + 1;
                    continue;
                }
                if (c == '"')
                {
                    _index++;
                    var closed = false;
                    while (_index < _source.Length)
                    {
                        c = _source[_index];
                        if (c == '"') { closed = true; _index++; break; }
                        if (c == '\\' && _index + 1 < _source.Length && "\\\"{}".Contains(_source[_index + 1]))
                        {
                            literal.Append(_source[_index + 1]); _index += 2; continue;
                        }
                        if (c == '{') { FlushLiteral(); segments.Add(ReadPlaceholder()); continue; }
                        literal.Append(c); _index++;
                    }
                    if (!closed) throw new FormatException("A double-quote is not closed.");
                    continue;
                }
                if (c == '\\')
                {
                    if (_index + 1 < _source.Length) { literal.Append(_source[_index + 1]); _index += 2; }
                    else { literal.Append('\\'); _index++; }
                    continue;
                }
                if (c == '{') { FlushLiteral(); segments.Add(ReadPlaceholder()); continue; }
                if (c == '}') throw new FormatException("A closing brace has no matching opening brace. Quote literal braces with single quotes.");
                literal.Append(c); _index++;
            }
            EndWord();
            return arguments;
        }

        private Segment ReadPlaceholder()
        {
            var start = ++_index;
            var depth = 1;
            while (_index < _source.Length && depth > 0)
            {
                if (_source[_index] == '{') depth++;
                else if (_source[_index] == '}') depth--;
                if (depth > 0) _index++;
            }
            if (depth != 0) throw new FormatException("A placeholder is missing its closing brace.");
            var body = _source[start.._index];
            _index++;

            if (body.StartsWith("ask:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = body[4..];
                var equal = TopLevelEquals(rest);
                var prompt = (equal < 0 ? rest : rest[..equal]).Trim();
                if (prompt.Length == 0) throw new FormatException("An ask placeholder needs a prompt.");
                var defaultSegments = equal < 0 ? Array.Empty<Segment>() : ParseDefault(rest[(equal + 1)..]);
                if (defaultSegments.Any(segment => segment is Ask)) throw new FormatException("An ask default cannot contain another ask.");
                return new Ask(prompt, defaultSegments);
            }

            var name = body.Trim();
            if (!KnownValues.Contains(name)) throw new FormatException($"Unknown Herdr placeholder '{{{name}}}'.");
            return new Value(name.ToLowerInvariant());
        }

        private static int TopLevelEquals(string text)
        {
            var depth = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}') depth--;
                else if (text[i] == '=' && depth == 0) return i;
            }
            return -1;
        }

        private static IReadOnlyList<Segment> ParseDefault(string text)
        {
            var output = new List<Segment>();
            var literal = new StringBuilder();
            for (var i = 0; i < text.Length;)
            {
                if (text[i] == '\\' && i + 1 < text.Length) { literal.Append(text[i + 1]); i += 2; continue; }
                if (text[i] != '{') { literal.Append(text[i++]); continue; }
                var close = text.IndexOf('}', i + 1);
                if (close < 0) throw new FormatException("A placeholder in an ask default is not closed.");
                if (literal.Length > 0) { output.Add(new Literal(literal.ToString())); literal.Clear(); }
                var name = text[(i + 1)..close].Trim();
                if (!KnownValues.Contains(name)) throw new FormatException($"Unknown Herdr placeholder '{{{name}}}'.");
                output.Add(new Value(name.ToLowerInvariant()));
                i = close + 1;
            }
            if (literal.Length > 0) output.Add(new Literal(literal.ToString()));
            return output;
        }
    }
}

/// <summary>Runs the Herdr CLI with an argument array, never through a command shell.</summary>
public static class HerdrCommandRunner
{
    public static string? FindBinary(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return explicitPath;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), OperatingSystem.IsWindows() ? "herdr.exe" : "herdr");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static async Task RunAsync(IReadOnlyList<string> arguments, CancellationToken ct = default)
    {
        var socketPath = HerdrClient.SocketPath();
        var wsl = OperatingSystem.IsWindows() && HerdrClient.IsWslSocketPath(socketPath);
        var explicitPath = Environment.GetEnvironmentVariable(StudioEnvironment.HerdrPath);
        ProcessStartInfo info;
        if (wsl)
        {
            var distro = HerdrClient.WslDistro();
            var binary = await ResolveWslBinaryPathAsync(explicitPath, distro, ct).ConfigureAwait(false);
            info = WslStartInfo(socketPath, distro, binary);
        }
        else
        {
            info = NativeStartInfo(socketPath, explicitPath);
        }
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the herdr executable.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("herdr did not finish within 10 seconds and was stopped.");
        }
        var error = await stderr.ConfigureAwait(false);
        var output = await stdout.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"herdr exited with status {process.ExitCode}." : error.Trim());
        if (!string.IsNullOrWhiteSpace(output)) System.Diagnostics.Debug.WriteLine(output.Trim());
    }

    private static ProcessStartInfo NativeStartInfo(string socketPath, string? explicitPath)
    {
        var binary = FindBinary(explicitPath);
        if (binary is null) throw new FileNotFoundException("Could not find herdr. Install it or set WL_HERDR_PATH to the executable.");
        var info = BaseStartInfo(binary);
        info.Environment["HERDR_SOCKET_PATH"] = socketPath;
        return info;
    }

    private static ProcessStartInfo WslStartInfo(string socketPath, string? distro, string binaryPath)
    {
        var info = BaseStartInfo("wsl.exe");
        if (!string.IsNullOrWhiteSpace(distro))
        {
            info.ArgumentList.Add("-d");
            info.ArgumentList.Add(distro);
        }
        info.ArgumentList.Add("--");
        info.ArgumentList.Add("/usr/bin/env");
        info.ArgumentList.Add($"HERDR_SOCKET_PATH={socketPath}");
        info.ArgumentList.Add(binaryPath);
        return info;
    }

    /// <summary>Resolves common WSL executable forms without a shell; `~` uses the distro's HOME.</summary>
    public static async Task<string> ResolveWslBinaryPathAsync(
        string? explicitPath, string? distro, CancellationToken ct = default)
    {
        var home = await RunWslQueryAsync("printenv", new[] { "HOME" }, distro, ct).ConfigureAwait(false);
        home = CleanWslOutput(home);
        if (string.IsNullOrWhiteSpace(home) || !home.StartsWith('/'))
            throw new InvalidOperationException("Could not determine HOME inside WSL.");

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var requested = explicitPath.Trim();
            if (requested == "~") requested = home;
            else if (requested.StartsWith("~/", StringComparison.Ordinal) || requested.StartsWith("~\\", StringComparison.Ordinal))
                requested = ExpandWslPath(requested, home);
            else if (!requested.StartsWith('/'))
            {
                var located = await RunWslQueryAsync("which", new[] { requested }, distro, ct).ConfigureAwait(false);
                located = CleanWslOutput(located);
                if (!string.IsNullOrWhiteSpace(located) && located.StartsWith('/')) requested = located;
            }
            if (requested.StartsWith('/') && await WslFileExistsAsync(requested, distro, ct).ConfigureAwait(false))
                return requested;
            throw new FileNotFoundException($"WL_HERDR_PATH '{explicitPath}' was not found inside WSL. Use an absolute Linux path or `~/.local/bin/herdr`.");
        }

        foreach (var candidate in new[]
                 {
                     JoinLinuxPath(home, ".local/bin/herdr"),
                     "/usr/local/bin/herdr", "/usr/bin/herdr", "/bin/herdr",
                 })
        {
            if (await WslFileExistsAsync(candidate, distro, ct).ConfigureAwait(false)) return candidate;
        }

        var fromPath = CleanWslOutput(await RunWslQueryAsync("which", new[] { "herdr" }, distro, ct).ConfigureAwait(false));
        if (!string.IsNullOrWhiteSpace(fromPath) && fromPath.StartsWith('/') &&
            await WslFileExistsAsync(fromPath, distro, ct).ConfigureAwait(false))
            return fromPath;

        throw new FileNotFoundException("Could not find the herdr executable inside WSL. Set WL_HERDR_PATH to an absolute Linux path, for example `~/.local/bin/herdr`.");
    }

    private static async Task<bool> WslFileExistsAsync(string path, string? distro, CancellationToken ct)
    {
        try
        {
            await RunWslQueryAsync("test", new[] { "-x", path }, distro, ct).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }

    private static async Task<string> RunWslQueryAsync(
        string command, IReadOnlyList<string> arguments, string? distro, CancellationToken ct)
    {
        var info = BaseStartInfo("wsl.exe");
        if (!string.IsNullOrWhiteSpace(distro))
        {
            info.ArgumentList.Add("-d");
            info.ArgumentList.Add(distro);
        }
        info.ArgumentList.Add("--");
        info.ArgumentList.Add(command);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start wsl.exe to locate Herdr.");
        var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(CleanWslOutput(error));
        return output;
    }

    private static string CleanWslOutput(string text) =>
        new string(text.Where(character => character != '\0' && !char.IsControl(character)).ToArray()).Trim();

    private static string JoinLinuxPath(string home, string relative) =>
        $"{home.TrimEnd('/')}/{relative.Replace('\\', '/').TrimStart('/')}";

    public static string ExpandWslPath(string path, string home)
    {
        if (path == "~") return home;
        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            return JoinLinuxPath(home, path[2..]);
        return path;
    }

    private static ProcessStartInfo BaseStartInfo(string executable) => new(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
}
