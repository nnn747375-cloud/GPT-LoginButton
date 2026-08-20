# GPT-LoginButton

<p align="center">
  <strong>Real ChatGPT sign-in for Windows Forms — using the local account flow.</strong><br />
  <sub>Community-maintained · independent · not affiliated with OpenAI</sub>
</p>

> [!WARNING]
> ## READ THIS BEFORE YOU RUN IT
>
> This demo needs **Windows 10+, .NET 9, Node.js 20+ with `npx`, and a ChatGPT account**.
> Click **Continue with ChatGPT** and the app opens the browser for the local
> `openai-oauth` sign-in. **No OpenAI API key is required or requested.**
>
> The available models and image generation depend on the account that signs in.
> The local proxy binds to **`127.0.0.1` only**. Never upload or share the local
> Codex/OpenAI auth files, tokens, cookies or callback codes. The community
> `openai-oauth` package is unofficial and may stop working if the upstream
> ChatGPT/Codex service changes.

## What this repository actually contains

- `GptLoginButton`: reusable, keyboard-accessible WinForms control.
- A real local ChatGPT demo: browser OAuth, model discovery, chat and image mode.
- A source-only Windows Forms demo that uses the same real local flow.

The demo does not simulate a successful login or generate a fake answer. The
button becomes connected only after the local proxy is healthy and the account's
models have been loaded from `/v1/models`.

## Run the demo

Install [Node.js 20+](https://nodejs.org/) first. Then start the app:

```powershell
dotnet run --project examples/WinFormsDemo/WinFormsDemo.csproj
```

On the first connection the app runs:

```powershell
npx --yes openai-oauth@latest login
npx --yes openai-oauth@latest --host 127.0.0.1 --port 10531 --detach
```

The proxy is local at `http://127.0.0.1:10531/v1`. The app uses its account-aware
`/v1/models`, `/v1/chat/completions` and `/v1/images/generations` routes. The
local auth session is managed by the CLI in the normal Codex auth location; this
repository never reads or commits that file.

## Use the control in another WinForms app

Reference `src/GptLoginButton/GptLoginButton.csproj`:

```xml
<ProjectReference Include="path/to/GptLoginButton.csproj" />
```

The control owns only presentation and events. Your host decides what a
successful provider flow means:

```csharp
var button = new GptLoginButton
{
    Dock = DockStyle.Top,
};

button.LoginRequested += async (_, _) =>
{
    button.SetSigningIn();
    try
    {
        var session = await hostAuth.ConnectAsync();
        button.SetConnected(session.DisplayName);
    }
    catch (OperationCanceledException)
    {
        button.SetSignedOut();
    }
    catch (Exception error)
    {
        button.SetError(error.Message);
    }
};

button.LogoutRequested += (_, _) => button.SetSignedOut();
Controls.Add(button);
```

## Security boundary

- This project never asks the user to paste an API key.
- OAuth state and the local session belong to `openai-oauth`/Codex, not the UI control.
- Keep the proxy on loopback; do not bind it to `0.0.0.0` or a LAN address.
- Never commit `auth.json`, tokens, cookies, screenshots or callback URLs.
- Use your own account and follow the service's terms and usage limits.

See [SECURITY.md](SECURITY.md) for the short reporting policy.

## Build and test

```powershell
dotnet build src/GptLoginButton/GptLoginButton.csproj --configuration Release
dotnet build examples/WinFormsDemo/WinFormsDemo.csproj --configuration Release
```

The live request requires a signed-in ChatGPT account and Node.js. The source
build alone cannot prove the account path; the demo must be connected and used.

## License

MIT. GPT and OpenAI are trademarks of OpenAI. This project is independent and
is not endorsed by or affiliated with OpenAI.
