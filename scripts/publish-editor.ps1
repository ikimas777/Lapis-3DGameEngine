<#
.SYNOPSIS
    Builds the Lapis editor as a single self-contained Windows executable.

.DESCRIPTION
    The result is publish\Lapis.Editor\Lapis.Editor.exe, plus Player\Lapis.Player.exe that File > Export Game
    copies into every exported game. Both include the .NET runtime, the native
    libraries and the built-in shaders, so it runs on any 64-bit Windows 10/11 PC without installing .NET.
    It is a windowed app (no console window); messages go to %LOCALAPPDATA%\Lapis\editor.log.

.PARAMETER Output
    Folder to write the executable to.

.PARAMETER Shortcut
    Also create a "Lapis Editor" shortcut on the desktop.

.EXAMPLE
    .\scripts\publish-editor.ps1 -Shortcut
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot "..\publish\Lapis.Editor"),
    [switch]$Shortcut
)

$ErrorActionPreference = "Stop"
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
    -p:DebugType=none

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# The player runtime, copied into every exported game (File > Export Game).
$player = Join-Path $PSScriptRoot "..\src\Lapis.Player\Lapis.Player.csproj"
dotnet publish $player `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output (Join-Path $Output "Player") `
    -p:LapisWindowedApp=true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish of the player failed with exit code $LASTEXITCODE."
}

$exe = Resolve-Path (Join-Path $Output "Lapis.Editor.exe")
Write-Host "Editor published to $exe"

if ($Shortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $desktop "Lapis Editor.lnk"))
    $link.TargetPath = $exe.Path
    $link.WorkingDirectory = Split-Path $exe.Path
    $link.Description = "Lapis Editor"
    $link.Save()
    Write-Host "Shortcut created on the desktop."
}
