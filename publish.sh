#!/bin/bash

# Configuration
VERSION=$(grep "<Version>" OpenSnitchCli/OpenSnitchCli.csproj | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/')
if [ -z "$VERSION" ]; then
  VERSION="1.0.0"
  echo "⚠️ Warning: Could not detect version from csproj, defaulting to $VERSION"
fi
OUTPUT_DIR="publish"
RUNTIME="linux-x64"

echo "🚀 Phase 2: Starting Multi-distro build for OpenSnitch CLI v$VERSION..."

# Detect container engine
if [ -x "$(command -v docker)" ]; then
  ENGINE="docker"
elif [ -x "$(command -v podman)" ]; then
  ENGINE="podman"
else
  echo "❌ Error: Neither docker nor podman is installed." >&2
  exit 1
fi

echo "🐳 Using container engine: $ENGINE"

# 0. Clean up
echo "🧹 Cleaning up previous artifacts..."
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# 1. Build on host
echo "🛠️ Publishing single-file executable on host..."
dotnet publish OpenSnitchCli/OpenSnitchCli.csproj \
    -c Release \
    -r "$RUNTIME" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUTPUT_DIR"

if [ $? -ne 0 ]; then
    echo "❌ Error: dotnet publish failed."
    exit 1
fi

# 2. Package as RPM (Fedora/RedHat)
echo "📦 Building RPM packer image..."
$ENGINE build -t opensnitch-cli-rpm-packer -f Dockerfile.rpm .
echo "🎁 Creating RPM package..."
$ENGINE run --rm -v "$(pwd)/$OUTPUT_DIR:/dist:Z" opensnitch-cli-rpm-packer

# 3. Package as DEB (Debian/Ubuntu)
echo "📦 Building DEB packer image..."
$ENGINE build -t opensnitch-cli-deb-packer -f Dockerfile.deb .
echo "🎁 Creating DEB package..."
$ENGINE run --rm -v "$(pwd)/$OUTPUT_DIR:/dist:Z" opensnitch-cli-deb-packer

if [ $? -eq 0 ]; then
    echo "----------------------------------------------------"
    echo "✅ Success! Linux packages created."
    
    RPM_FILE=$(ls $OUTPUT_DIR/*.rpm 2>/dev/null | tail -n 1)
    DEB_FILE=$(ls $OUTPUT_DIR/*.deb 2>/dev/null | tail -n 1)
    
    [ -n "$RPM_FILE" ] && echo "📍 RPM: $RPM_FILE"
    [ -n "$DEB_FILE" ] && echo "📍 DEB: $DEB_FILE"
    
    echo "🚀 Command: OpenSnitchCli"
    echo "----------------------------------------------------"
else
    echo "❌ Error: Packaging failed."
    exit 1
fi
