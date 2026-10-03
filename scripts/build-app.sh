#!/bin/zsh
# Compila en release y empaqueta "dist/Al Día.app" (firma ad-hoc, uso local).
# Builds a release and packages "dist/Al Día.app" (ad-hoc signed, local use).
# Uso / usage: scripts/build-app.sh [--install]   (--install copia la app a /Applications)
set -euo pipefail
cd "${0:A:h}/.."

EXEC=AlDia
NOMBRE="Al Día"
swift build -c release
BIN="$(swift build -c release --show-bin-path)/$EXEC"

OUT="dist/$NOMBRE.app"
rm -rf "$OUT"
mkdir -p "$OUT/Contents/MacOS" "$OUT/Contents/Resources"
cp "$BIN" "$OUT/Contents/MacOS/$EXEC"
cp packaging/Info.plist "$OUT/Contents/Info.plist"
cp packaging/AppIcon.icns "$OUT/Contents/Resources/"
# Traducciones compiladas desde Localizable.xcstrings / compiled translations
xcrun xcstringstool compile packaging/Localizable.xcstrings --output-directory "$OUT/Contents/Resources" >/dev/null
codesign --force --sign - "$OUT"
echo "✓ $OUT"

if [[ "${1:-}" == "--install" ]]; then
  rm -rf "/Applications/$NOMBRE.app"
  cp -R "$OUT" /Applications/
  echo "✓ /Applications/$NOMBRE.app"
fi
