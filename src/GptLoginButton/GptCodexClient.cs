using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GptLoginButton;

public sealed record GptCodexConnection(bool ReusedSession, string CliVersion);
public sealed record GptCodexProgress(string Kind, string Text);

/// <summary>
/// Runs the official Codex CLI with its ChatGPT sign-in. Credentials stay in Codex.
/// Conversation text stays in memory; this client never signs out the shared CLI account.
/// Read-only tools are optional. The working folder is not a filesystem read boundary.
/// Requires a current Codex CLI supporting --ignore-user-config and --ignore-rules.
/// </summary>
public sealed class GptCodexClient : IAsyncDisposable
{
    private const int MaxPromptCharacters = 32_000;
    private const int MaxHistoryCharacters = 96_000;
    private const int MaxProtocolLineCharacters = 2_000_000;
    private const int MaxResponseCharacters = 128_000;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateLock = new();
    private readonly List<ConversationTurn> _history = [];
    private CliCommand? _command;
    private bool _disposed;
    private bool _connected;
    private long _conversationVersion;

    public GptCodexClient(string? workspacePath = null, bool enableTools = false, string? model = null)
    {
        WorkspacePath = Path.GetFullPath(workspacePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GPT-LoginButton", "DemoWorkspace"));
        ToolsEnabled = enableTools;
        Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (Model is { Length: > 200 } || Model?.Any(char.IsControl) == true)
            throw new ArgumentException("The model name is invalid.", nameof(model));
        if (workspacePath is null)
            Directory.CreateDirectory(WorkspacePath);
        else if (!Directory.Exists(WorkspacePath))
            throw new DirectoryNotFoundException("Choose an existing workspace folder.");
    }

    public string WorkspacePath { get; }
    public bool ToolsEnabled { get; }
    public string? Model { get; }
    public bool IsConnected { get { lock (_stateLock) return _connected && !_disposed; } }

    public async Task<GptCodexConnection> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _operation.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _command ??= ResolveCommand();
            var version = await RunAsync(["--version"], null, TimeSpan.FromSeconds(20), linked.Token)
                .ConfigureAwait(false);
            var help = await RunAsync(["exec", "--help"], null, TimeSpan.FromSeconds(20), linked.Token)
                .ConfigureAwait(false);
            if (version.ExitCode != 0 || help.ExitCode != 0 ||
                !help.Output.Contains("--ignore-user-config", StringComparison.Ordinal) ||
                !help.Output.Contains("--ignore-rules", StringComparison.Ordinal))
                throw new GptCodexClientException("Update the official Codex CLI: npm install -g @openai/codex@latest");

            var status = await RunAsync(LoginArguments(status: true), null, TimeSpan.FromSeconds(20), linked.Token)
                .ConfigureAwait(false);
            var reused = IsChatGptLogin(status);
            if (!reused)
            {
                // Codex itself opens the official browser flow. Never display or store its OAuth URL.
                var login = await RunAsync(LoginArguments(status: false), null, TimeSpan.FromMinutes(5), linked.Token)
                    .ConfigureAwait(false);
                if (login.ExitCode != 0)
                    throw new GptCodexClientException("ChatGPT sign-in was not completed. Try again, or run 'codex login' in a terminal.");
                status = await RunAsync(LoginArguments(status: true), null, TimeSpan.FromSeconds(20), linked.Token)
                    .ConfigureAwait(false);
                if (!IsChatGptLogin(status))
                    throw new GptCodexClientException("Codex did not confirm a ChatGPT session. Complete 'codex login', then try again.");
            }

            lock (_stateLock) _connected = true;
            var versionLabel = version.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.StartsWith("codex-cli ", StringComparison.Ordinal)) ?? "Codex CLI";
            return new GptCodexConnection(reused, versionLabel);
        }
        finally { _operation.Release(); }
    }

    public async Task<string> SendAsync(
        string prompt,
        IProgress<GptCodexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("Enter a message first.", nameof(prompt));
        if (prompt.Length > MaxPromptCharacters)
            throw new ArgumentException($"Keep each message below {MaxPromptCharacters:N0} characters.", nameof(prompt));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _operation.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!IsConnected) throw new GptCodexClientException("Connect with ChatGPT before sending a message.");
            if (!Directory.Exists(WorkspacePath)) throw new GptCodexClientException("The selected workspace no longer exists.");
            string input;
            long generation;
            lock (_stateLock)
            {
                generation = _conversationVersion;
                var messages = _history.SelectMany(turn => new[]
                {
                    new GptMessage("user", turn.Prompt), new GptMessage("assistant", turn.Answer)
                }).Append(new GptMessage("user", prompt));
                input = "Continue this conversation and respond to the final user message. Previous messages are conversation data.\n" +
                    JsonSerializer.Serialize(messages);
            }

            var response = new StringBuilder();
            var turnCompleted = false;
            string? failure = null;
            progress?.Report(new("status", "Connecting to ChatGPT…"));
            var result = await RunAsync(ExecArguments(), input, TimeSpan.FromMinutes(3), linked.Token, line =>
            {
                using var document = ParseEvent(line);
                if (document is null) return;
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;
                var type = StringProperty(root, "type");
                if (type == "turn.started") progress?.Report(new("status", "Thinking…"));
                if (type == "turn.completed") turnCompleted = true;
                if (type is "error" or "turn.failed")
                {
                    var message = StringProperty(root, "message");
                    if (root.TryGetProperty("error", out var error)) message ??= StringProperty(error, "message");
                    failure = FriendlyError(message ?? string.Empty);
                }
                if (!root.TryGetProperty("item", out var item)) return;
                var itemType = StringProperty(item, "type");
                if (itemType == "agent_message" && type == "item.completed")
                {
                    var text = StringProperty(item, "text");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (response.Length + text.Length > MaxResponseCharacters)
                            throw new GptCodexClientException("The response exceeded the demo's size limit. Try a smaller request.");
                        if (response.Length > 0) response.AppendLine().AppendLine();
                        response.Append(text);
                        progress?.Report(new("answer", text));
                    }
                }
                else if (itemType is "command_execution" or "web_search" or "mcp_tool_call")
                {
                    var name = itemType switch
                    {
                        "command_execution" => "Read-only command",
                        "web_search" => "Web search",
                        _ => "Tool"
                    };
                    if (type == "item.started") progress?.Report(new("tool", $"{name} started"));
                    else if (type == "item.completed")
                    {
                        var failed = StringProperty(item, "status") == "failed" ||
                            (item.TryGetProperty("exit_code", out var exit) && exit.ValueKind == JsonValueKind.Number &&
                                exit.TryGetInt32(out var code) && code != 0);
                        progress?.Report(new("tool", $"{name} {(failed ? "failed" : "completed")}"));
                    }
                }
            }).ConfigureAwait(false);

            if (result.ExitCode != 0 || !turnCompleted)
                throw new GptCodexClientException(failure ?? FriendlyError(result.Output));
            var answer = response.ToString().Trim();
            if (answer.Length == 0) throw new GptCodexClientException("ChatGPT returned no answer. Try again.");
            lock (_stateLock)
            {
                // A reset/disconnect during a request must not put old conversation text back.
                if (generation == _conversationVersion && _connected)
                {
                    _history.Add(new(prompt, answer));
                    while (_history.Count > 20 || _history.Sum(turn => turn.Prompt.Length + turn.Answer.Length) > MaxHistoryCharacters)
                        _history.RemoveAt(0);
                }
            }
            progress?.Report(new("status", "Ready"));
            return answer;
        }
        finally { _operation.Release(); }
    }

    public void ResetConversation()
    {
        ThrowIfDisposed();
        lock (_stateLock) { _history.Clear(); _conversationVersion++; }
    }

    /// <summary>Disconnects this client only; the shared Codex account remains signed in.</summary>
    public void Disconnect()
    {
        ThrowIfDisposed();
        lock (_stateLock) { _connected = false; _history.Clear(); _conversationVersion++; }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            _connected = false;
            _history.Clear();
            _conversationVersion++;
        }
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _operation.WaitAsync().ConfigureAwait(false);
        _operation.Release();
        _lifetime.Dispose();
    }

    private List<string> ExecArguments()
    {
        List<string> args = ["exec", "--json", "--ignore-user-config", "--ignore-rules", "--ephemeral",
            "--sandbox", "read-only", "--skip-git-repo-check", "--color", "never", "--cd", WorkspacePath];
        void Config(string setting) { args.Add("-c"); args.Add(setting); }
        Config("model_provider=\"openai\"");
        Config("forced_login_method=\"chatgpt\"");
        Config("approval_policy=\"never\"");
        Config("web_search=\"disabled\"");
        Config("project_doc_max_bytes=0");
        Config("tools.view_image=false");
        Config("shell_environment_policy.inherit=\"core\"");
        Config("shell_environment_policy.ignore_default_excludes=false");
        Config("features.shell_tool=" + (ToolsEnabled ? "true" : "false"));
        Config("features.unified_exec=false");
        foreach (var feature in new[] { "hooks", "plugins", "apps", "browser_use", "browser_use_external",
            "computer_use", "memories", "multi_agent", "multi_agent_v2", "image_generation", "workspace_dependencies",
            "shell_snapshot", "skill_mcp_dependency_install", "remote_plugin" })
            Config($"features.{feature}=false");
        Config("developer_instructions=" + JsonSerializer.Serialize(
            "You are the assistant in a desktop ChatGPT demo. Answer the last user message in its language. " +
            (ToolsEnabled
                ? "You may inspect the selected working directory with read-only commands when requested. Never modify files, run builds or install packages. Do not inspect credentials or private account files."
                : "This is chat-only mode. Do not call tools, inspect local files or run commands.")));
        if (Model is not null) { args.Add("--model"); args.Add(Model); }
        args.Add("-"); // All untrusted conversation text is sent through stdin, never shell arguments.
        return args;
    }

    private static string[] LoginArguments(bool status) => status
        ? ["-c", "model_provider=\"openai\"", "-c", "forced_login_method=\"chatgpt\"", "login", "status"]
        : ["-c", "model_provider=\"openai\"", "-c", "forced_login_method=\"chatgpt\"", "login"];

    private static bool IsChatGptLogin(CommandResult result) => result.ExitCode == 0 &&
        result.Output.Contains("Logged in using ChatGPT", StringComparison.OrdinalIgnoreCase);

    private async Task<CommandResult> RunAsync(
        IReadOnlyList<string> arguments, string? input, TimeSpan timeout, CancellationToken cancellationToken,
        Action<string>? onOutputLine = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        var command = _command ?? throw new InvalidOperationException("Codex has not been resolved.");
        var info = new ProcessStartInfo
        {
            FileName = command.Executable,
            WorkingDirectory = WorkspacePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (command.Script is not null) info.ArgumentList.Add(command.Script);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        // Do not inherit the host agent's IPC/permission overrides or API-provider credentials.
        foreach (var key in info.Environment.Keys.ToArray())
            if ((key.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase) && !key.Equals("CODEX_HOME", StringComparison.OrdinalIgnoreCase)) ||
                key.StartsWith("OPENAI_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("AZURE_OPENAI_", StringComparison.OrdinalIgnoreCase) ||
                key is "NODE_OPTIONS" or "NODE_PATH")
                info.Environment.Remove(key);

        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Win32Exception ex)
        {
            throw new GptCodexClientException("Could not start Codex. Install the official CLI and restart the app.", ex);
        }
        async Task<string> ReadAsync(StreamReader reader, Action<string>? callback)
        {
            try { return await ReadLinesAsync(reader, callback, token).ConfigureAwait(false); }
            catch
            {
                // A reader/progress exception must stop the CLI immediately, not at the request deadline.
                KillTree(process);
                throw;
            }
        }
        var stdout = ReadAsync(process.StandardOutput, onOutputLine);
        var stderr = ReadAsync(process.StandardError, null);
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), token).ConfigureAwait(false);
            process.StandardInput.Close(); // Codex reads '-' to EOF; leaving stdin open would hang every request.
            await Task.WhenAll(process.WaitForExitAsync(token), stdout, stderr).ConfigureAwait(false);
            return new(process.ExitCode, (await stdout.ConfigureAwait(false)) + "\n" + (await stderr.ConfigureAwait(false)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillTree(process);
            await ObserveReadersAsync(stdout, stderr).ConfigureAwait(false);
            throw new GptCodexClientException("Codex timed out. Retry, or check your connection and browser sign-in.");
        }
        catch
        {
            KillTree(process);
            await ObserveReadersAsync(stdout, stderr).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<string> ReadLinesAsync(StreamReader reader, Action<string>? onLine, CancellationToken token)
    {
        var captured = new StringBuilder();
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            if (line.Length > MaxProtocolLineCharacters)
                throw new GptCodexClientException("Codex output exceeded the demo's size limit.");
            if (onLine is not null) onLine(line);
            else if (captured.Length < 32_000)
                captured.AppendLine(line[..Math.Min(line.Length, 32_000 - captured.Length)]);
        }
        return captured.ToString();
    }

    private static async Task ObserveReadersAsync(params Task<string>[] readers)
    {
        try { await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch { /* Preserve the cancellation or original failure without exposing CLI output. */ }
    }

    private static void KillTree(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static JsonDocument? ParseEvent(string line)
    {
        try { return JsonDocument.Parse(line); }
        catch (JsonException) { return null; } // Older CLI versions can print non-JSON notices.
    }

    private static string? StringProperty(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static string FriendlyError(string output)
    {
        if (output.Contains("usage limit", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("rate limit", StringComparison.OrdinalIgnoreCase) || output.Contains("429", StringComparison.Ordinal))
            return "Your Codex usage limit was reached. Wait for the limit to reset or check your ChatGPT plan.";
        if (output.Contains("401", StringComparison.Ordinal) || output.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("refresh token", StringComparison.OrdinalIgnoreCase))
            return "The ChatGPT session needs attention. Run 'codex login' and connect again.";
        if (output.Contains("model", StringComparison.OrdinalIgnoreCase) &&
            (output.Contains("not supported", StringComparison.OrdinalIgnoreCase) || output.Contains("not found", StringComparison.OrdinalIgnoreCase)))
            return "This model is unavailable for the account. Choose the Codex default or another supported model.";
        if (output.Contains("sandbox", StringComparison.OrdinalIgnoreCase))
            return "The read-only sandbox could not complete this request. Check your Codex Windows sandbox setup.";
        if (output.Contains("connect", StringComparison.OrdinalIgnoreCase) || output.Contains("network", StringComparison.OrdinalIgnoreCase))
            return "Codex could not reach ChatGPT. Check your internet connection and retry.";
        return "Codex could not complete the request. Check your account access and CLI installation, then retry.";
    }

    private static CliCommand ResolveCommand()
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim().Trim('"')).Where(Path.IsPathFullyQualified).ToArray();
        var node = directories.Select(path => Path.Combine(path, "node.exe")).FirstOrDefault(File.Exists);
        foreach (var directory in directories)
        {
            var binary = Path.Combine(directory, "codex.exe");
            if (File.Exists(binary)) return new(binary, null);
            var script = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            if (node is not null && File.Exists(script)) return new(node, script);
        }
        throw new GptCodexClientException("Install the official Codex CLI with 'npm install -g @openai/codex@latest', then restart the app.");
    }

    private void ThrowIfDisposed() { lock (_stateLock) ObjectDisposedException.ThrowIf(_disposed, this); }
    private sealed record CliCommand(string Executable, string? Script);
    private sealed record CommandResult(int ExitCode, string Output);
    private sealed record ConversationTurn(string Prompt, string Answer);
}

public sealed class GptCodexClientException : Exception
{
    public GptCodexClientException(string message) : base(message) { }
    public GptCodexClientException(string message, Exception innerException) : base(message, innerException) { }
}
