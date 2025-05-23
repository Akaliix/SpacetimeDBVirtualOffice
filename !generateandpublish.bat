@echo off
cd /d %~dp0
echo Publishing Spacetime project to local server...
spacetime generate --lang csharp --out-dir ..\..\VirtualOffice\Assets\Assets\Scripts\Autogen\
spacetime publish --server local stdboffice -c
pause