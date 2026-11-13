#!/usr/bin/env bash
# Génère windows/src/ClaudeBar/app.ico (jauge blanche sur fond orange) avec ImageMagick.
# Usage : ./windows/tools/make-icon.sh   (brew install imagemagick)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/src/ClaudeBar/app.ico"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

magick -size 256x256 gradient:'#E8956F'-'#BF5B3A' "$TMP/grad.png"
magick -size 256x256 xc:none -fill white -draw "roundrectangle 8,8 247,247 56,56" "$TMP/mask.png"
magick "$TMP/grad.png" "$TMP/mask.png" -alpha off -compose CopyOpacity -composite "$TMP/base.png"

magick "$TMP/base.png" \
  -fill none -stroke '#EDB29A' -strokewidth 26 \
  -draw "stroke-linecap round ellipse 128,140 74,74 310,405" \
  -stroke white \
  -draw "stroke-linecap round ellipse 128,140 74,74 135,310" \
  -strokewidth 16 \
  -draw "stroke-linecap round line 128,140 161,101" \
  -stroke none -fill white \
  -draw "circle 128,140 128,154" \
  "$TMP/icon-256.png"

magick "$TMP/icon-256.png" -define icon:auto-resize=256,128,64,48,40,32,24,20,16 "$OUT"
echo "Icône générée : $OUT"
