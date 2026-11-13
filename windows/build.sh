#!/usr/bin/env bash
# Construit ClaudeBar-Setup.exe depuis macOS ou Linux.
# Prérequis : .NET SDK 10 (brew install --cask dotnet-sdk) et NSIS (brew install makensis / apt install nsis).
# Usage : ./windows/build.sh [version]     ex. ./windows/build.sh 1.0.0
set -euo pipefail

VERSION="${1:-1.0.0}"
ROOT="$(cd "$(dirname "$0")" && pwd)"
PUBLISH="$ROOT/publish"
DIST="$ROOT/dist"

rm -rf "$PUBLISH" "$DIST"
mkdir -p "$DIST"

echo "==> dotnet publish (win-x64, autonome, fichier unique) — version $VERSION"
dotnet publish "$ROOT/src/ClaudeBar/ClaudeBar.csproj" \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:Version="$VERSION" \
  -o "$PUBLISH"

if ! command -v makensis >/dev/null 2>&1; then
  echo "makensis introuvable. Installe NSIS : brew install makensis (macOS) ou sudo apt install nsis (Linux)." >&2
  echo "L'exécutable seul est disponible dans : $PUBLISH" >&2
  exit 1
fi

echo "==> makensis"
# makensis a besoin d'une locale UTF-8 valide pour lire les accents du script.
LC_ALL=en_US.UTF-8 LANG=en_US.UTF-8 makensis -INPUTCHARSET UTF8 -DVERSION="$VERSION" -DPUBLISH_DIR="$PUBLISH" -DOUT_DIR="$DIST" "$ROOT/installer/ClaudeBar.nsi"

echo
echo "Installateur prêt : $DIST/ClaudeBar-Setup.exe"
