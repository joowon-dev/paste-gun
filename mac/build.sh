#!/bin/bash
# macOS 앱을 만든다. Xcode 프로젝트 없이 swiftc 로 바로 컴파일하고 .app 을 손으로 조립한다.
#   mac/build.sh            → dist/mac/Paste Gun.app
#   mac/build.sh --zip      → dist/PasteGun-mac.zip 까지
#
# 게임 코드(src/)는 그대로 Resources/web 에 들어간다. 빌드 단계에서 변형하지 않는다.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
name="Paste Gun"
out="$root/dist/mac"
appdir="$out/$name.app"

rm -rf "$appdir"
mkdir -p "$appdir/Contents/MacOS" "$appdir/Contents/Resources/web"

echo "› 컴파일"
swiftc -O -whole-module-optimization \
  -target arm64-apple-macos13 \
  -o "$appdir/Contents/MacOS/PasteGun" \
  "$root/mac/Sources/main.swift"

# 인텔 맥에서도 돌도록 x86_64 로도 한 번 더 만들어 합친다.
if swiftc -O -target x86_64-apple-macos13 -o "$out/.intel" "$root/mac/Sources/main.swift" 2>/dev/null; then
  lipo -create "$appdir/Contents/MacOS/PasteGun" "$out/.intel" -output "$appdir/Contents/MacOS/PasteGun.universal"
  mv "$appdir/Contents/MacOS/PasteGun.universal" "$appdir/Contents/MacOS/PasteGun"
  rm -f "$out/.intel"
  echo "› 유니버설 (arm64 + x86_64)"
fi

echo "› 리소스"
cp "$root/mac/Info.plist" "$appdir/Contents/Info.plist"
cp -R "$root/src/game" "$root/src/render" "$root/src/renderer" "$appdir/Contents/Resources/web/"

# 아이콘: build/icon.png → icns (scripts/make-icons.sh 가 그린다)
if [ -f "$root/build/icon.png" ]; then
  iconset="$out/icon.iconset"  # iconutil 은 .iconset 로 끝나는 이름만 받는다
  rm -rf "$iconset" && mkdir -p "$iconset"
  for size in 16 32 128 256 512; do
    sips -z $size $size "$root/build/icon.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    sips -z $((size * 2)) $((size * 2)) "$root/build/icon.png" \
      --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$iconset" -o "$appdir/Contents/Resources/icon.icns"
  rm -rf "$iconset"
fi
# 메뉴바 아이콘. 없으면 셸이 🔫 이모지로 대신한다.
[ -f "$root/build/tray.png" ] && cp "$root/build/tray.png" "$appdir/Contents/Resources/tray.png"

# **서명이 이 앱에서는 장식이 아니다.** 손쉬운 사용 권한은 서명에 묶여 있어서, 임시(ad-hoc)
# 서명이면 빌드할 때마다 서명이 달라져 권한이 풀린다 — 설정에는 켜져 있는데 총은 안 나간다.
# 키체인에 개발 인증서가 있으면 그걸로 서명해서 다시 빌드해도 권한이 남게 한다.
#   PASTEGUN_SIGN="인증서 이름"  으로 고를 수 있고,  PASTEGUN_SIGN=-  이면 임시 서명.
identity="${PASTEGUN_SIGN:-$(security find-identity -v -p codesigning 2>/dev/null \
  | sed -n 's/.*"\(Apple Development: [^"]*\)".*/\1/p' | head -1)}"
if [ -n "$identity" ] && [ "$identity" != "-" ] && codesign --force --deep --sign "$identity" "$appdir" 2>/dev/null; then
  echo "› 서명: $identity"
else
  codesign --force --deep --sign - "$appdir" 2>/dev/null && echo "› 임시 서명 (다시 빌드하면 손쉬운 사용 권한을 다시 줘야 한다)" || echo "› 서명 건너뜀"
fi

du -sh "$appdir" | awk '{print "› 앱 크기 " $1}'

if [ "${1:-}" = "--zip" ]; then
  mkdir -p "$root/dist"
  (cd "$out" && ditto -c -k --keepParent "$name.app" "$root/dist/PasteGun-mac.zip")
  du -h "$root/dist/PasteGun-mac.zip" | awk '{print "› zip " $1}'
fi
