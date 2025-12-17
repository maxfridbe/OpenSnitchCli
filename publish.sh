#!/bin/bash

# Configuration
PROJECT_DIR="OpenSnitchCli"
PROJECT_FILE="OpenSnitchCli.csproj"
OUTPUT_DIR="./publish"
RUNTIME="linux-x64"

echo "🚀 Starting publish process for OpenSnitch CLI..."

# Ensure we are in the project directory or provide the path
if [ ! -d "$PROJECT_DIR" ]; then
    echo "❌ Error: Project directory '$PROJECT_DIR' not found."
    exit 1
fi

# Create output directory
mkdir -p "$OUTPUT_DIR"

# Run dotnet publish
# Flags:
# -c Release: Build in Release mode
# -r $RUNTIME: Target Linux x64
# --self-contained true: Include .NET runtime in the executable
# -p:PublishSingleFile=true: Package everything into one file
# -p:PublishReadyToRun=true: Ahead-of-time (AOT) compilation for faster startup
# -p:IncludeNativeLibrariesForSelfExtract=true: Ensure native dependencies are handled correctly
dotnet publish "$PROJECT_DIR/$PROJECT_FILE" \
    -c Release \
    -r "$RUNTIME" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUTPUT_DIR"

if [ $? -eq 0 ]; then
    echo "----------------------------------------------------"
    echo "✅ Success! Single-file executable created."
    echo "📍 Location: $OUTPUT_DIR/OpenSnitchCli"
    echo "💻 To run: chmod +x $OUTPUT_DIR/OpenSnitchCli && sudo $OUTPUT_DIR/OpenSnitchCli"
    echo "----------------------------------------------------"
else
    echo "❌ Error: Publish failed."
    exit 1
fi
