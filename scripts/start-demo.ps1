param([switch]$CheckOnly)

$ErrorActionPreference = 'Stop'
$demoRoot = Split-Path -Parent $PSScriptRoot
$demoProject = Join-Path $demoRoot 'examples\WinFormsDemo\WinFormsDemo.csproj'

try {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        throw 'Install the .NET 9 SDK for Windows from https://dotnet.microsoft.com/download/dotnet/9.0, then reopen Start-Demo.cmd.'
    }
    $demoSdks = @(& $dotnetCommand.Source --list-sdks)
    $demoRuntimes = @(& $dotnetCommand.Source --list-runtimes)
    if (-not ($demoSdks | Where-Object { $_ -match '^([0-9]+)\.' -and [int]$Matches[1] -ge 9 })) {
        throw 'A .NET SDK 9 or newer is required to build this source demo. Install the .NET 9 SDK for Windows, then reopen Start-Demo.cmd.'
    }
    if (-not ($demoRuntimes | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App 9\.' })) {
        throw 'Install the .NET 9 Windows Desktop Runtime from https://dotnet.microsoft.com/download/dotnet/9.0, then reopen Start-Demo.cmd.'
    }
    $codexCommand = Get-Command codex -ErrorAction SilentlyContinue
    if (-not $codexCommand) {
        throw 'Install the official Codex CLI: install Node.js from https://nodejs.org, run npm install -g @openai/codex@0.141.0, then reopen Start-Demo.cmd.'
    }
    if (-not (Test-Path -LiteralPath $demoProject -PathType Leaf)) {
        throw 'The demo source is missing. Extract the complete repository before opening Start-Demo.cmd.'
    }

    Write-Host 'ChatGPT Studio prerequisites are ready.' -ForegroundColor Green
    if ($CheckOnly) { exit 0 }
    Write-Host 'Starting the demo. Click Continue with ChatGPT in the app.'
    & $dotnetCommand.Source run --project $demoProject --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "The demo exited with code $LASTEXITCODE. Review the build output above." }
}
catch {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Yellow
    if (-not $CheckOnly) { [void](Read-Host 'Press Enter to close') }
    exit 1
}
