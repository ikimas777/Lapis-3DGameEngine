#!/usr/bin/env bash
# Builds the Lapis editor for Linux as a single self-contained executable.
#
# The result is publish/Lapis.Editor-linux/Lapis.Editor, plus Player/Lapis.Player that File > Export Game
# copies into every exported game. Both include the .NET runtime, the native libraries and the built-in
# shaders, so they run on 64-bit Linux without installing .NET.
#
# Usage: scripts/publish-editor.sh [output-folder]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-$root/publish/Lapis.Editor-linux}"

publish() {
    dotnet publish "$1" \
        --configuration Release \
        --runtime linux-x64 \
        --self-contained true \
        --output "$2" \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:IncludeAllContentForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=none
}

publish "$root/src/Lapis.Editor/Lapis.Editor.csproj" "$output"
# The player runtime, copied into every exported game (File > Export Game).
publish "$root/src/Lapis.Player/Lapis.Player.csproj" "$output/Player"

chmod +x "$output/Lapis.Editor" "$output/Player/Lapis.Player"
echo "Editor published to $output/Lapis.Editor"
