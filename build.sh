#!/bin/sh
# Compila con Mono (mcs). Salida en ./dist  -> copiar la carpeta a Windows.
set -e
cd "$(dirname "$0")"
[ -f Mono.Cecil.dll ] || cp dist/Mono.Cecil.dll .
mkdir -p dist
mcs -target:library -out:dist/Esp2JpnHook.dll Hook.cs
mcs -target:winexe -win32icon:Esp2Jpn.ico -win32manifest:app.manifest -out:dist/Esp2JpnPatcher.exe -r:Mono.Cecil.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll Main.cs Patcher.cs Hook.cs Help.cs
cp Mono.Cecil.dll dist/
cp LEEME.txt dist/LEEME.txt
echo "OK -> dist/   (prueba: Esp2JpnPatcher.exe --selftest)"
