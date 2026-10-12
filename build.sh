#!/bin/bash
# RamClock 빌드 (mono 필요). 사용법: ./build.sh [버전]
set -e
cd "$(dirname "$0")/src"
if [ -n "$1" ]; then sed -i "s/public const string Version = \"[^\"]*\"/public const string Version = \"$1\"/" Updater.cs; fi
R=""; for f in fontres/*.gz; do R="$R -resource:$f,$(basename $f)"; done
mcs -nologo -target:winexe -sdk:4.5 -optimize+ -codepage:utf8 -win32icon:app.ico \
  -r:System.Windows.Forms -r:System.Drawing -r:System.Web.Extensions -r:System.IO.Compression -r:System.Xml \
  $R -out:../RamClock.exe *.cs
echo "built RamClock.exe"
