using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptLoginButton;

public sealed record GptModel(
    [property: JsonPropertyName("id")] string Id,
    bool IsImage);

public sealed record GptConnection(IReadOnlyList<GptModel> Models);

public sealed record GptMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

/// <summary>
/// Talks to the user's local openai-oauth proxy. The proxy owns the ChatGPT
/// OAuth session; this class never reads or logs tokens.
/// </summary>
public sealed class GptLocalClient : IAsyncDisposable
{
    private const string Package = "openai-oauth@latest";
    private const string LoopbackHost = "127.0.0.1";
    private const int DefaultPort = 10531;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly int _port;
    private bool _ownsProxy;
    private bool _disposed;

    public GptLocalClient(int port = DefaultPort)
    {
        _port = port;
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://{LoopbackHost}:{port}/v1/"),
            Timeout = TimeSpan.FromMinutes(5),
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Uri ProxyUri => _http.BaseAddress!;

    public async Task<GptConnection?> TryReuseAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsHealthyAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new GptConnection(await LoadModelsAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<GptConnection> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var existing = await TryReuseAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var start = await RunNpxAsync(
            ["--yes", Package, "--host", LoopbackHost, "--port", _port.ToString(), "--detach"],
            visible: false,
            cancellationToken).ConfigureAwait(false);

        if (start.ExitCode != 0 && LooksLikeMissingAuth(start.Output))
        {
            var login = await RunNpxAsync(
                ["--yes", Package, "login"],
                visible: true,
                cancellationToken).ConfigureAwait(false);

            if (login.ExitCode != 0)
            {
                throw new GptLocalClientException(
                    "ChatGPT sign-in was not completed. Close the login window and try again.");
            }

            start = await RunNpxAsync(
                ["--yes", Package, "--host", LoopbackHost, "--port", _port.ToString(), "--detach"],
                visible: false,
                cancellationToken).ConfigureAwait(false);
        }

        if (start.ExitCode != 0 && !LooksLikeAlreadyRunning(start.Output))
        {
            throw new GptLocalClientException(ToCommandError(start.Output));
        }

        _ownsProxy = start.Output.Contains("endpoint ready", StringComparison.OrdinalIgnoreCase);
        var connected = await WaitForConnectionAsync(TimeSpan.FromMinutes(2), cancellationToken)
            .ConfigureAwait(false);

        if (connected is null)
        {
            if (_ownsProxy)
            {
                await StopProxyAsync().ConfigureAwait(false);
            }

            throw new GptLocalClientException(
                "The local ChatGPT proxy did not become ready. Check Node.js, npx and the openai-oauth output.");
        }

        return connected;
    }

    public async Task<string> SendAsync(
        IReadOnlyList<GptMessage> messages,
        string model,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (messages.Count == 0)
        {
            throw new ArgumentException("At least one message is required.", nameof(messages));
        }

        var payload = JsonSerializer.Serialize(
            new { model, messages, stream = false },
            JsonOptions);
        using var response = await _http.PostAsync(
            "chat/completions",
            new StringContent(payload, Encoding.UTF8, "application/json"),
            cancellationToken).ConfigureAwait(false);
        using var document = await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            throw new GptLocalClientException("ChatGPT returned no answer.");
        }

        var message = choices[0].GetProperty("message");
        var content = ReadTextValue(message.GetProperty("content"));
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new GptLocalClientException("ChatGPT returned an empty answer.");
        }

        return content.Trim();
    }

    public async Task<byte[]> GenerateImageAsync(
        string prompt,
        string model = "gpt-image-2",
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var payload = JsonSerializer.Serialize(new { model, prompt }, JsonOptions);
        using var response = await _http.PostAsync(
            "images/generations",
            new StringContent(payload, Encoding.UTF8, "application/json"),
            cancellationToken).ConfigureAwait(false);
        using var document = await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
        {
            throw new GptLocalClientException("The image model returned no image.");
        }

        var item = data[0];
        if (item.TryGetProperty("b64_json", out var base64))
        {
            try
            {
                return Convert.FromBase64String(base64.GetString() ?? string.Empty);
            }
            catch (FormatException ex)
            {
                throw new GptLocalClientException("The image response was invalid.", ex);
            }
        }

        if (item.TryGetProperty("url", out var urlElement) &&
            Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var url) &&
            (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps))
        {
            return await _http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        }

        throw new GptLocalClientException("The image response did not contain an image.");
    }

    public async Task DisconnectAsync()
    {
        if (!_ownsProxy)
        {
            return;
        }

        await StopProxyAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            _http.Dispose();
        }
    }

    private async Task<GptConnection?> WaitForConnectionAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var connection = await TryReuseAsync(cancellationToken).ConfigureAwait(false);
                if (connection is not null)
                {
                    return connection;
                }
            }
            catch (HttpRequestException)
            {
                // The proxy may still be starting or discovering models.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A single slow local request is retryable.
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(
                new Uri($"http://{LoopbackHost}:{_port}/health"),
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<GptModel>> LoadModelsAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("models", cancellationToken).ConfigureAwait(false);
        using var document = await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new GptLocalClientException("The local proxy returned an invalid model list.");
        }

        var models = data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new GptModel(id, id.Contains("image", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (models.Length == 0)
        {
            throw new GptLocalClientException("No ChatGPT models are available for this account.");
        }

        return models;
    }

    private async Task StopProxyAsync()
    {
        _ownsProxy = false;
        try
        {
            await RunNpxAsync(["--yes", Package, "stop"], visible: false, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // The app is closing; the proxy can be stopped manually with the
            // documented command if npm itself is unavailable.
        }
    }

    private static async Task<JsonDocument> ReadResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new GptLocalClientException(ReadError(text, response.StatusCode));
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new GptLocalClientException("The local ChatGPT proxy returned invalid JSON.", ex);
        }
    }

    private static string ReadError(string body, HttpStatusCode statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var message))
                {
                    return $"ChatGPT request failed ({(int)statusCode}): {message.GetString()}";
                }

                if (error.ValueKind == JsonValueKind.String)
                {
                    return $"ChatGPT request failed ({(int)statusCode}): {error.GetString()}";
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to a stable, non-sensitive error.
        }

        return $"ChatGPT request failed ({(int)statusCode}).";
    }

    private static string ReadTextValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var part in value.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                parts.Add(part.GetString() ?? string.Empty);
            }
            else if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                parts.Add(text.GetString() ?? string.Empty);
            }
        }

        return string.Join(string.Empty, parts);
    }

    private static bool LooksLikeMissingAuth(string output)
        => output.Contains("No OpenAI OAuth credentials", StringComparison.OrdinalIgnoreCase) ||
           output.Contains("No auth file", StringComparison.OrdinalIgnoreCase) ||
           output.Contains("ChatGPT access token not found", StringComparison.OrdinalIgnoreCase) ||
           output.Contains("Run `npx openai-oauth login`", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeAlreadyRunning(string output)
        => output.Contains("already running", StringComparison.OrdinalIgnoreCase);

    private static string ToCommandError(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "Could not start openai-oauth. Install Node.js 20+ and try again.";
        }

        var clean = output.Trim();
        if (clean.Length > 700)
        {
            clean = clean[^700..];
        }

        return $"Could not start openai-oauth: {clean}";
    }

    private static async Task<CommandResult> RunNpxAsync(
        IReadOnlyList<string> arguments,
        bool visible,
        CancellationToken cancellationToken)
    {
        var node = ResolveNodeRuntime();
        var info = new ProcessStartInfo
        {
            FileName = node.NodePath,
            UseShellExecute = visible,
            CreateNoWindow = !visible,
            WindowStyle = visible ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
            RedirectStandardOutput = !visible,
            RedirectStandardError = !visible,
        };

        info.ArgumentList.Add(node.NpxCliPath);
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new GptLocalClientException(
                "Node.js with npx is required. Install Node.js 20 or newer, then restart this app.", ex);
        }

        try
        {
            Task<string> stdout = Task.FromResult(string.Empty);
            Task<string> stderr = Task.FromResult(string.Empty);
            if (!visible)
            {
                stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
                stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = $"{await stdout.ConfigureAwait(false)}\n{await stderr.ConfigureAwait(false)}";
            return new CommandResult(process.ExitCode, output);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The process may already have exited between the checks.
            }

            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GptLocalClient));
        }
    }

    private static NodeRuntime ResolveNodeRuntime()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length == 0)
            {
                continue;
            }

            var nodePath = Path.Combine(trimmed, "node.exe");
            var npxCliPath = Path.Combine(trimmed, "node_modules", "npm", "bin", "npx-cli.js");
            if (File.Exists(nodePath) && File.Exists(npxCliPath))
            {
                return new NodeRuntime(nodePath, npxCliPath);
            }
        }

        throw new GptLocalClientException(
            "Node.js 20+ with npx is required. Install Node.js, then restart this app.");
    }

    private sealed record CommandResult(int ExitCode, string Output);
    private sealed record NodeRuntime(string NodePath, string NpxCliPath);
}

public sealed class GptLocalClientException : Exception
{
    public GptLocalClientException(string message)
        : base(message)
    {
    }

    public GptLocalClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
