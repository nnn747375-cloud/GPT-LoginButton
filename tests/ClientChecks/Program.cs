using GptLoginButton;

// No packages, no credentials, no network in the default checks.
// --live explicitly opts into requests using an existing ChatGPT/Codex account.
var workspace = Path.Combine(Path.GetTempPath(), "GptLoginButton-Checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workspace);
try
{
    await using (var client = new GptCodexClient(workspace))
    {
        Check(!client.IsConnected && !client.ToolsEnabled, "Starts disconnected with tools disabled");
        await ThrowsAsync<ArgumentException>(() => client.SendAsync("  "), "Rejects empty input");
        await ThrowsAsync<ArgumentException>(() => client.SendAsync(new string('x', 32_001)), "Bounds prompt size");
        await ThrowsAsync<GptCodexClientException>(() => client.SendAsync("Hello"), "Requires connection");
        client.ResetConversation();
        client.Disconnect();
    }
    var disposed = new GptCodexClient(workspace);
    await disposed.DisposeAsync();
    await disposed.DisposeAsync();
    await ThrowsAsync<ObjectDisposedException>(() => disposed.ConnectAsync(), "Rejects use after dispose");

    if (!args.Contains("--live")) return;

    await using (var chat = new GptCodexClient(workspace))
    {
        var connection = await chat.ConnectAsync();
        Check(connection.ReusedSession, "Reuses existing official ChatGPT sign-in");
        Console.WriteLine(connection.CliVersion);
        var marker = "test-" + Guid.NewGuid().ToString("N");
        var events = new List<GptCodexProgress>();
        var progress = new InlineProgress(events.Add);
        var first = await chat.SendAsync("Remember this test marker: " + marker + ". Reply with exactly OK.", progress);
        Check(first.Trim() == "OK", "Real ChatGPT inference");
        Check(events.All(item => item.Kind != "tool"), "Chat-only request emits no tool activity");
        var second = await chat.SendAsync("What test marker did I ask you to remember? Reply only with the marker.");
        Check(second.Contains(marker, StringComparison.Ordinal), "Conversation history reaches the next request");
        chat.ResetConversation();
    }

    var secret = Guid.NewGuid().ToString("N");
    await File.WriteAllTextAsync(Path.Combine(workspace, "read-only-check.txt"), secret);
    await using (var reader = new GptCodexClient(workspace, enableTools: true))
    {
        await reader.ConnectAsync();
        var events = new List<GptCodexProgress>();
        var answer = await reader.SendAsync(
            "Read read-only-check.txt using a read-only shell command and return only its contents. Do not inspect anything else.",
            new InlineProgress(events.Add));
        Check(answer.Contains(secret, StringComparison.Ordinal), "Real tool reads fresh file content not supplied in the prompt");
        Check(events.Any(item => item.Kind == "tool" && item.Text.EndsWith("completed", StringComparison.Ordinal)),
            "Real command completion is surfaced to the UI");
        Check(await File.ReadAllTextAsync(Path.Combine(workspace, "read-only-check.txt")) == secret, "Read preserves the original file");

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await ThrowsAsync<OperationCanceledException>(() => reader.SendAsync(
            "Explain the purpose of this demo in a short sentence.", cancellationToken: cancellation.Token),
            "Cancellation interrupts a real CLI request");
        Check(timer.Elapsed < TimeSpan.FromSeconds(10), "Cancellation returns promptly");
    }
}
finally
{
    // Delete only the uniquely created test workspace, never a user-selected folder.
    Directory.Delete(workspace, recursive: true);
}

static void Check(bool passed, string message)
{
    if (!passed) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

static async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); }
    catch (T) { Console.WriteLine("PASS: " + message); return; }
    throw new Exception("FAIL: " + message);
}

sealed class InlineProgress(Action<GptCodexProgress> action) : IProgress<GptCodexProgress>
{
    public void Report(GptCodexProgress value) => action(value);
}
