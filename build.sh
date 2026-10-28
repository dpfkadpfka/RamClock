#!/bin/bash
# RamClock 빌드 (mono 필요). 사용법: ./build.sh [버전]
set -e
cd "$(dirname "$0")/src"
if [ -n "$1" ]; then sed -i "s/public const string Version = \"[^\"]*\"/public const string Version = \"$1\"/" Updater.cs; fi
R=""; for f in fontres/*.gz; do R="$R -resource:$f,$(basename $f)"; done
# 유튜브 미니 플레이어(WebView2) DLL은 exe 안에 넣어 둔다
W="-r:wv2/Microsoft.Web.WebView2.Core.dll -r:wv2/Microsoft.Web.WebView2.WinForms.dll"
for f in wv2/*.dll; do W="$W -resource:$f,$(basename $f)"; done
# 학생 카드 QR 그리는 스크립트
W="$W -resource:res/qrcode.js,qrcode.js"
# 뽑기 효과음
for f in sfx/*.wav; do W="$W -resource:$f,$(basename $f)"; done
mcs -nologo -target:winexe -sdk:4.5 -optimize+ -codepage:utf8 -win32icon:app.ico \
  -r:System.Windows.Forms -r:System.Drawing -r:System.Web.Extensions -r:System.IO.Compression -r:System.Xml \
  $R $W -out:../RamClock.exe *.cs
# mono가 만드는 버전 정보는 언어가 007f(중립)라 작업 관리자가 이름(RamClock)을 못 읽고 창 제목을 대신 보여 줌
# → 한국어(0412)로 바꿔 작업 관리자에 "RamClock"으로 나오게
perl -0777 -pi -e 's/0\x000\x007\x00f\x000\x004\x00b\x000\x00/0\x004\x001\x002\x000\x004\x00b\x000\x00/g; s/(T\x00r\x00a\x00n\x00s\x00l\x00a\x00t\x00i\x00o\x00n\x00\x00\x00\x00\x00)\x7f\x00\xb0\x04/$1\x12\x04\xb0\x04/g' ../RamClock.exe
echo "built RamClock.exe"
