# Recorded verification

Verified on Windows on 2026-09-24 using the official Codex CLI **0.141.0**, an existing ChatGPT session, and .NET SDK 10.0.401 targeting .NET 9 Windows.

The library built with zero errors and warnings. The package-free harness passed **14 assertions**:

- Five offline checks: disconnected/tools-disabled defaults; empty input rejection; prompt length bound; connection required before send; use-after-dispose rejected.
- Existing official ChatGPT authentication reused successfully.
- A real request returned the requested answer.
- Chat-only inference emitted no tool activity.
- A subsequent request recalled a randomly generated conversation marker.
- A real shell tool read fresh random file contents that were absent from the prompt.
- The actual command-completed event reached the progress handler.
- The read left the original file unchanged.
- Cancellation interrupted a real CLI request and returned within ten seconds.

Reproduce offline checks:

```powershell
dotnet run --project tests/ClientChecks --configuration Release
```

Opt into live checks, which require an account and consume usage:

```powershell
dotnet run --project tests/ClientChecks --configuration Release -- --live
```

The fresh browser OAuth consent flow was **not exercised** because the existing authenticated session was reused. These client checks do not establish visual UI quality or prove behavior on every Windows installation. No legacy-proxy image-generation result was verified by these checks. CI deliberately runs only the offline checks and builds.
