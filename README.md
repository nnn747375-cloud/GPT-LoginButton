# GPT-LoginButton

**A clean Windows chat demo with official ChatGPT sign-in and real, optional read-only tools.**

ChatGPT Studio pairs a reusable WinForms login button with the official Codex CLI. Connect once, chat in your own desktop interface, and see actual tool activity as it happens.

Independent community project; not affiliated with or endorsed by OpenAI.

## Start in one click

Download a Windows build from [Releases](https://github.com/nnn747375-cloud/GPT-LoginButton/releases), or clone the source and double-click **`Start-Demo.cmd`**.

The source launcher checks prerequisites and starts the demo. It does not install system software or change your account settings.

Source prerequisites:

- Windows 10 or 11.
- [.NET 9 SDK for Windows](https://dotnet.microsoft.com/download/dotnet/9.0), or a newer SDK with the .NET 9 Windows Desktop Runtime.
- The [official Codex CLI](https://developers.openai.com/codex/cli), tested with **0.141.0**. With [Node.js](https://nodejs.org/) installed, run `npm install -g @openai/codex@0.141.0` and reopen the launcher.
- A ChatGPT account with Codex access and available usage. No API key is required.

Click **Continue with ChatGPT**. An existing Codex ChatGPT session is reused; otherwise Codex opens its official browser sign-in. Return to the demo after completing authentication. A connected label confirms authentication; sending a message verifies that your account can actually make a request.

Alternatively:

```powershell
dotnet run --project examples/WinFormsDemo/WinFormsDemo.csproj --configuration Release
```

## The demo

- A quiet sidebar, readable conversation, message composer and live activity panel.
- **Ctrl+Enter** sends; **Stop** or **Escape** cancels an active operation.
- Leave the model field empty to use the Codex default, or enter a model supported by your account.
- Start a **New chat** to clear this demo's in-memory conversation.
- Enable **Read-only tools** to let the model inspect files with real shell commands. Choose a folder or use the isolated demo workspace.
- Disconnect clears this app's connection and conversation; it does **not** sign you out of Codex globally.

Tools start disabled. The default workspace is `%LOCALAPPDATA%\GPT-LoginButton\DemoWorkspace`. The selected folder is the command working directory, **not a strict boundary on which files can be read**. Only select folders you intend to discuss. The CLI runs with its read-only sandbox and without approval escalation. Plugins, browser actions, image generation, web search and user hooks are disabled in this demo.

## Use the client

Reference `src/GptLoginButton/GptLoginButton.csproj` from a Windows Forms project:

```csharp
using GptLoginButton;

await using var client = new GptCodexClient(); // Chat-only by default.
var connection = await client.ConnectAsync(cancellationToken);
var activity = new Progress<GptCodexProgress>(item =>
    statusLabel.Text = item.Text);

string answer = await client.SendAsync("Hello!", activity, cancellationToken);
client.ResetConversation();
client.Disconnect(); // Keeps the shared Codex account signed in.
```

To opt into tools, explicitly supply a working directory:

```csharp
await using var client = new GptCodexClient(
    workspacePath: selectedFolder,
    enableTools: true,
    model: null);
```

`GptCodexProgress.Kind` is `status`, `tool` or `answer`. Tool events originate from the CLI's real JSONL events; the activity panel does not invent tool execution. Conversation history is bounded and kept in memory. Requests use ephemeral CLI sessions. Login has a five-minute timeout; requests have a three-minute timeout. Cancellation terminates the child process tree.

The `GptLoginButton` control remains independent of authentication. Handle `LoginRequested` and `LogoutRequested`, and update it through `SetSigningIn()`, `SetConnected()`, `SetSignedOut()` or `SetError()`. The host application owns the client and cancellation.

## Legacy API compatibility

`GptLocalClient`, `GptConnection`, `GptModel` and `GptMessage` remain available for existing integrations. `GptLocalClient` uses the unofficial `openai-oauth@2.0.0` loopback proxy. The new demo uses **`GptCodexClient` exclusively** and does not start that proxy.

`GptLocalClient.GenerateImageAsync()` remains a legacy proxy feature. Image generation is **not included in ChatGPT Studio or the new Codex client**. Legacy availability depends on that third-party package and upstream account support.

## Build and verify

```powershell
dotnet build src/GptLoginButton/GptLoginButton.csproj --configuration Release
dotnet build examples/WinFormsDemo/WinFormsDemo.csproj --configuration Release
dotnet run --project tests/ClientChecks --configuration Release
```

The default checks require no account, network or test packages. CI runs these checks and builds both projects. To explicitly run account-backed checks with an existing ChatGPT session:

```powershell
dotnet run --project tests/ClientChecks --configuration Release -- --live
```

Live checks consume account usage. They verify real inference, conversation history, reading fresh file contents through a real tool, progress events and cancellation. See [TESTING.md](TESTING.md) for the recorded results and remaining verification limits.

See [SECURITY.md](SECURITY.md) for credential handling and the tool boundary. Official references: [Codex authentication](https://developers.openai.com/codex/auth), [non-interactive mode](https://developers.openai.com/codex/noninteractive), [configuration](https://developers.openai.com/codex/config-reference).

## License

MIT. GPT and OpenAI are trademarks of OpenAI.
