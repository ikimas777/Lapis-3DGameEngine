#!/usr/bin/env bash
# Builds the Lapis editor for Linux or macOS as single self-contained executables.
#
# Linux: the result is publish/Lapis.Editor-linux/Lapis.Editor, plus Runtime/Lapis.Runtime (the game runtime that
# File > Export Game turns into every exported game), Lapis.Monitor (live performance monitor), Docs/,
# README.txt and LICENSE.txt.
#
# macOS: the result is publish/Lapis.Editor-macos/Lapis.app, an app bundle with the same three programs in
# Lapis.app/Contents/MacOS, plus Docs/, README.txt and LICENSE.txt next to it. The app is signed ad hoc (Apple
# Silicon Macs only run signed programs). Set LAPIS_RID=osx-x64 to build for Intel Macs instead of the Mac it
# runs on.
#
# The programs include the .NET runtime, the native libraries and the built-in shaders, so they run without
# installing .NET.
#
# Usage: scripts/publish-editor.sh [output-folder]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [ "$(uname)" = "Darwin" ]; then
    macos=true
    if [ "$(uname -m)" = "arm64" ]; then default_rid=osx-arm64; else default_rid=osx-x64; fi
    rid="${LAPIS_RID:-$default_rid}"
    output="${1:-$root/publish/Lapis.Editor-macos}"
    programs="$output/Lapis.app/Contents/MacOS"
else
    macos=false
    rid="${LAPIS_RID:-linux-x64}"
    output="${1:-$root/publish/Lapis.Editor-linux}"
    programs="$output"
fi

# LAPIS_VERSION overrides the version from Directory.Build.props, e.g. 0.7.0-beta.1 (the release workflow passes the tag).
version_args=()
if [ -n "${LAPIS_VERSION:-}" ]; then version_args=("-p:Version=$LAPIS_VERSION"); fi

publish() {
    dotnet publish "$1" "${version_args[@]}" \
        --configuration Release \
        --runtime "$rid" \
        --self-contained true \
        --output "$2" \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:IncludeAllContentForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=none
}

publish "$root/src/Lapis.Editor/Lapis.Editor.csproj" "$programs"
# The game runtime, copied into every exported game (File > Export Game).
publish "$root/src/Lapis.Runtime/Lapis.Runtime.csproj" "$programs/Runtime"
# Lapis Monitor, next to the editor (Debug > Lapis Monitor starts it; it can also be run on its own).
publish "$root/src/Lapis.Monitor/Lapis.Monitor.csproj" "$programs"

chmod +x "$programs/Lapis.Editor" "$programs/Runtime/Lapis.Runtime" "$programs/Lapis.Monitor"

if [ "$macos" = true ]; then
    version="${LAPIS_VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")}"
    sed -e "s/@VERSION@/$version/g" "$root/scripts/package/Info.plist" > "$output/Lapis.app/Contents/Info.plist"

    # Sign each program, then the bundle (ad hoc: no Apple developer certificate).
    for program in "$programs/Lapis.Editor" "$programs/Lapis.Monitor" "$programs/Runtime/Lapis.Runtime"; do
        codesign --force --sign - "$program"
    done
    codesign --force --sign - "$output/Lapis.app"
fi

# Documentation, a short read-me and the licence next to the programs (next to Lapis.app on macOS).
mkdir -p "$output/Docs"
cp -r "$root/docs/." "$output/Docs/"
cp "$root/scripts/package/README.txt" "$output/README.txt"
cp "$root/LICENSE" "$output/LICENSE.txt"

echo "Editor published to $programs/Lapis.Editor"
