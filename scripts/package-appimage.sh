#!/bin/bash
VERSION=$1
if [ -z "$VERSION" ]; then VERSION="1.0.0"; fi

# Setup AppDir
mkdir -p OpenSnitch.AppDir/usr/bin
cp OpenSnitchCli OpenSnitch.AppDir/usr/bin/opensnitch-cli
cp opensnitch-cli.png OpenSnitch.AppDir/opensnitch-cli.png

# Create Desktop file
cat > OpenSnitch.AppDir/opensnitch-cli.desktop <<EOF
[Desktop Entry]
Name=OpenSnitch CLI
Exec=opensnitch-cli
Icon=opensnitch-cli
Type=Application
Categories=System;Security;
Terminal=true
EOF

# Create AppRun
ln -s usr/bin/opensnitch-cli OpenSnitch.AppDir/AppRun

# Run appimagetool
export ARCH=x86_64
/usr/local/bin/appimagetool --appimage-extract-and-run OpenSnitch.AppDir /dist/OpenSnitch_CLI-${VERSION}-x86_64.AppImage
