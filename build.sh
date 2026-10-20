#!/bin/bash
# RamClock 빌드 (mono 필요). 사용법: ./build.sh [버전]
set -e
cd "$(dirname "$0")/src"
if [ -n "$1" ]; then sed -i "s/public const string Version = \"[^\"]*\"/public const string Version = \"$1\"/" Updater.cs; fi
R=""; for f in fontres/*.gz; do R="$R -resource:$f,$(basename $f)"; done
# 유튜브 미니 플레이어(WebView2) DLL은 exe 안에 넣어 둔다
W="-r:wv2/Microsoft.Web.WebView2.Core.dll -r:wv2/Microsoft.Web.WebView2.WinForms.dll"
for f in wv2/*.dll; do W="$W -resource:$f,$(basename $f)"; done
mcs -nologo -target:winexe -sdk:4.5 -optimize+ -codepage:utf8 -win32icon:app.ico \
  -r:System.Windows.Forms -r:System.Drawing -r:System.Web.Extensions -r:System.IO.Compression -r:System.Xml \
  $R $W -out:../RamClock.exe *.cs
echo "built RamClock.exe"
