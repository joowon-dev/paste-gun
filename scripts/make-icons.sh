#!/bin/bash
# build/icon.png(1024), build/tray.png(44, 템플릿), windows/icon.ico 를 그린다.
# 그림은 src/render/draw.js 그대로다. 만든 것은 저장소에 넣어 둔다 — CI 는 다시 그리지 않는다.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
chrome="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
port=8771
python3 -m http.server $port --directory "$root" >/dev/null 2>&1 &
server=$!
trap 'kill $server' EXIT
sleep 1
shoot() { # 크기 파일 쿼리
  "$chrome" --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 \
    --force-device-scale-factor=1 --window-size=$1,$1 --virtual-time-budget=2000 --screenshot="$2" \
    "http://localhost:$port/scripts/icon.html$3" >/dev/null 2>&1
}
mkdir -p "$root/build"
shoot 1024 "$root/build/icon.png" ""
shoot 44 "$root/build/tray.png" "?tray&size=44"

# ico 는 PNG 를 그대로 담을 수 있다(Vista 이후). 256 하나면 윈도우가 알아서 줄여 쓴다.
sips -z 256 256 "$root/build/icon.png" --out "$root/build/.icon256.png" >/dev/null
python3 - "$root/build/.icon256.png" "$root/windows/icon.ico" <<'PY'
import struct, sys
png = open(sys.argv[1], 'rb').read()
header = struct.pack('<HHH', 0, 1, 1)
entry = struct.pack('<BBBBHHII', 0, 0, 0, 0, 1, 32, len(png), 22)
open(sys.argv[2], 'wb').write(header + entry + png)
PY
rm -f "$root/build/.icon256.png"
echo "› build/icon.png, build/tray.png, windows/icon.ico"
