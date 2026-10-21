#!/usr/bin/env bash
# Builds the Lapis editor for Linux as a single self-contained executable.
#
# The result is publish/Lapis.Editor-linux/Lapis.Editor, plus Runtime/Lapis.Runtime (the game runtime that
# File > Export Game turns into every exported game), Docs/, README.txt and LICENSE.txt. Both programs include
# the .NET runtime, the native libraries and the built-in shaders, so they run on 64-bit Linux without
# installing .NET.
#
# Usage: scripts/publish-editor.sh [output-folder]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${1:-$root/publish/Lapis.Editor-linux}"

# LAPIS_VERSION overrides the version from Directory.Build.props, e.g. 0.7.0-beta.1 (the release workflow passes the tag).
version_args=()
if [ -n "${LAPIS_VERSION:-}" ]; then version_args=("-p:Version=$LAPIS_VERSION"); fi

publish() {
    dotnet publish "$1" "${version_args[@]}" \
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
# The game runtime, copied into every exported game (File > Export Game).
publish "$root/src/Lapis.Runtime/Lapis.Runtime.csproj" "$output/Runtime"

chmod +x "$output/Lapis.Editor" "$output/Runtime/Lapis.Runtime"

# Documentation, a short read-me and the licence next to the programs.
mkdir -p "$output/Docs"
cp -r "$root/docs/." "$output/Docs/"
cp "$root/scripts/package/README.txt" "$output/README.txt"
cp "$root/LICENSE" "$output/LICENSE.txt"

echo "Editor published to $output/Lapis.Editor"
