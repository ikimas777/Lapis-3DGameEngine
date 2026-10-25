<#
.SYNOPSIS
    Builds the Lapis editor as a single self-contained Windows executable.

.DESCRIPTION
    The result is publish\Lapis.Editor\Lapis.Editor.exe, plus Runtime\Lapis.Runtime.exe (the game runtime that
    File > Export Game turns into every exported game), Lapis.Monitor.exe (live performance monitor), Docs\,
    README.txt and LICENSE.txt. The programs include
    the .NET runtime, the native libraries and the built-in shaders, so they run on any 64-bit Windows 10/11 PC
    without installing .NET. The editor is a windowed app (no console window); messages go to
    %LOCALAPPDATA%\Lapis\editor.log.

.PARAMETER Output
    Folder to write the executable to.

.PARAMETER Shortcut
    Also create a "Lapis Editor" shortcut on the desktop.

.PARAMETER RepoRoot
    Also copy Lapis.Editor.exe and Runtime\Lapis.Runtime.exe into the repository root, where they are committed
    so the editor can be started right after cloning. Lapis.Monitor.exe is not copied: from a clone, the editor
    starts a build of src\Lapis.Monitor instead.

    On Linux use scripts/publish-editor.sh instead.

.EXAMPLE
    .\scripts\publish-editor.ps1 -Shortcut

.EXAMPLE
    .\scripts\publish-editor.ps1 -RepoRoot
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot "..\publish\Lapis.Editor"),
    [switch]$Shortcut,
    [switch]$RepoRoot,
    # Overrides the version from Directory.Build.props, e.g. 0.7.0-beta.1 (the release workflow passes the tag).
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$versionArgument = @(if ($Version) { "-p:Version=$Version" }) # empty array: nothing is passed
$project = Join-Path $PSScriptRoot "..\src\Lapis.Editor\Lapis.Editor.csproj"

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $Output `
    -p:LapisWindowedApp=true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    $versionArgument

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# The game runtime, copied into every exported game (File > Export Game).
$runtime = Join-Path $PSScriptRoot "..\src\Lapis.Runtime\Lapis.Runtime.csproj"
dotnet publish $runtime `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output (Join-Path $Output "Runtime") `
    -p:LapisWindowedApp=true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    $versionArgument

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish of the runtime failed with exit code $LASTEXITCODE."
}

# Lapis Monitor, next to the editor (Debug > Lapis Monitor starts it; it can also be run on its own).
$monitor = Join-Path $PSScriptRoot "..\src\Lapis.Monitor\Lapis.Monitor.csproj"
dotnet publish $monitor `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $Output `
    -p:LapisWindowedApp=true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    $versionArgument

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish of the monitor failed with exit code $LASTEXITCODE."
}

# Documentation, a short read-me and the licence next to the programs.
$repository = Resolve-Path (Join-Path $PSScriptRoot "..")
New-Item -ItemType Directory -Force (Join-Path $Output "Docs") | Out-Null
Copy-Item (Join-Path $repository "docs\*") (Join-Path $Output "Docs") -Recurse -Force
Copy-Item (Join-Path $repository "scripts\package\README.txt") (Join-Path $Output "README.txt") -Force
Copy-Item (Join-Path $repository "LICENSE") (Join-Path $Output "LICENSE.txt") -Force

$exe = Resolve-Path (Join-Path $Output "Lapis.Editor.exe")
Write-Host "Editor published to $exe"

if ($RepoRoot) {
    New-Item -ItemType Directory -Force (Join-Path $repository "Runtime") | Out-Null
    Copy-Item $exe (Join-Path $repository "Lapis.Editor.exe") -Force
    Copy-Item (Join-Path $Output "Runtime\Lapis.Runtime.exe") (Join-Path $repository "Runtime\Lapis.Runtime.exe") -Force
    Write-Host "Copied Lapis.Editor.exe and Runtime\Lapis.Runtime.exe to $repository"
}

if ($Shortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $desktop "Lapis Editor.lnk"))
    $link.TargetPath = $exe.Path
    $link.WorkingDirectory = Split-Path $exe.Path
    $link.Description = "Lapis Editor"
    $link.Save()
    Write-Host "Shortcut created on the desktop."
}
