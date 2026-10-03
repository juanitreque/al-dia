#!/bin/zsh
# Extrae los textos de la interfaz y los sincroniza con packaging/Localizable.xcstrings.
# Extracts UI strings and syncs them into packaging/Localizable.xcstrings.
# Después, traduce las claves nuevas (Xcode abre el .xcstrings) / then translate new keys (open it in Xcode).
set -euo pipefail
cd "${0:A:h}/.."
TMP=$(mktemp -d)
rm -rf .build
swift build -Xswiftc -emit-localized-strings -Xswiftc -emit-localized-strings-path -Xswiftc "$TMP"
xcrun xcstringstool sync packaging/Localizable.xcstrings --stringsdata "$TMP"/*.stringsdata
rm -rf "$TMP"
echo "✓ packaging/Localizable.xcstrings"
