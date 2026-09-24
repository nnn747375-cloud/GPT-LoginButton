# Security policy

The current demo uses the official Codex CLI. Codex owns browser authentication and credential storage; this library never reads or logs credential files. The demo reuses the existing ChatGPT session and never runs `codex logout`. Account access and usage limits still apply.

The client runs without a command shell, passes arguments through `ProcessStartInfo.ArgumentList`, and sends conversation text through standard input. It closes stdin, bounds requests and retained conversation history, and kills the child process tree on cancellation. Raw authentication output, OAuth URLs and tool output are not displayed in the activity log.

## Tool boundary

Tools are disabled by default. Opting in enables shell commands under the Codex read-only sandbox with `approval_policy=never`. There is no automatic escalation to an unrestricted process. A working directory is **not read confinement**: read-only commands may be able to read other files accessible to the user. Use the default isolated demo directory for experiments and select a project deliberately.

The demo ignores user configuration and local execution rules, and explicitly disables plugins, apps, hooks, browser/computer actions, image generation, web search and multi-agent features. It removes inherited host-agent IPC/permission variables and OpenAI API/provider environment overrides from the child process. Organization-managed policy and the installed CLI remain part of the trust boundary. Keep the official CLI current; the implementation was verified with version 0.141.0.

Conversation text is retained only in this app's memory and passed to the authenticated service. CLI requests use `--ephemeral`; this is not a claim that OpenAI retains no service-side data. New chat, disconnect and app closure clear the client history. No local chat export is performed automatically.

## Legacy proxy

`GptLocalClient` is preserved for compatibility but is not used by the demo. It depends on the unofficial `openai-oauth@2.0.0` package. Integrators who still use it must keep its endpoint on `127.0.0.1`, review the third-party dependency and its authentication behavior, and avoid exposing the proxy to a network. Its image-generation API is separate from the new demo.

## Report a problem

Report security issues privately through a GitHub security advisory when available. Do not post API keys, OAuth codes, tokens, cookies, `auth.json`, account identifiers, browser profiles or private conversations in issues or pull requests. The repository and release archives must not contain account files or generated user data.
