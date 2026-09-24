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

## Native demo verification

The Windows Forms demo was also inspected and exercised directly:

- Default 1120 x 800 client size and minimum 920 x 720 outer window size showed no overlaps or clipping in the inspected layouts.
- An 8,109-character answer remained readable in the conversation layout.
- Keyboard activation, accessibility behavior and invalid settings handling passed.
- The actual form's `ConnectAsync` -> composer -> `SendAsync` path returned `UI_PIPELINE_OK` from ChatGPT in 5.4 seconds.
- Busy, cancellation and ready-to-send states passed their UI checks.

The README preview is a capture of this native demo. These results apply to the tested Windows environment and do not prove behavior on every display scale or Windows installation.

The fresh browser OAuth consent flow was **not exercised** because the existing authenticated session was reused. No legacy-proxy image-generation result was verified by these checks. CI deliberately runs only the offline checks and builds.
