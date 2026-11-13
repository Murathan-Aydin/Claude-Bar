#!/usr/bin/env bash
# Génère les images exigées par le paquet MSIX (Microsoft Store) à partir de app.ico.
# Usage : ./windows/tools/make-store-assets.sh   (brew install imagemagick)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ICO="$ROOT/src/ClaudeBar/app.ico"
OUT="$ROOT/store/Assets"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

mkdir -p "$OUT"
rm -f "$OUT"/*.png
magick "$ICO[0]" "$TMP/src.png"   # image 256×256 de l'icône

square() { # nom taille [marge %]
  local name=$1 size=$2 margin=${3:-0}
  local inner=$(( size * (100 - 2 * margin) / 100 ))
  magick "$TMP/src.png" -resize "${inner}x${inner}" -background none -gravity center -extent "${size}x${size}" "$OUT/$name"
}

wide() { # nom largeur hauteur
  local name=$1 w=$2 h=$3
  local inner=$(( h * 70 / 100 ))
  magick "$TMP/src.png" -resize "${inner}x${inner}" -background none -gravity center -extent "${w}x${h}" "$OUT/$name"
}

for scale in 100 200; do
  f=$(( scale ))
  square "StoreLogo.scale-$scale.png"          $(( 50 * f / 100 ))
  square "Square44x44Logo.scale-$scale.png"    $(( 44 * f / 100 ))
  square "Square150x150Logo.scale-$scale.png"  $(( 150 * f / 100 )) 18
  wide   "Wide310x150Logo.scale-$scale.png"    $(( 310 * f / 100 )) $(( 150 * f / 100 ))
done

# Icônes de la barre des tâches / menu Démarrer sans fond (« unplated »)
for size in 16 24 32 48 256; do
  square "Square44x44Logo.targetsize-$size.png" $size
  square "Square44x44Logo.targetsize-${size}_altform-unplated.png" $size
done

echo "Images générées dans $OUT :"
ls "$OUT"
