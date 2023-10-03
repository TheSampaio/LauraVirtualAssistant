:: Title
ECHO OFF
TITLE Compile
CLS

:: Main
CD ../
SET NAME="LauraVirtualAssistant"
ECHO Building project...
ECHO ===
pyinstaller Source/main.py --onefile --noconsole --noconfirm --icon "../../../Data/icon.ico" --name "%NAME%" --distpath "_Output/Bin/%NAME%" --workpath "_Output/Obj/" --specpath "_Output/Obj/%NAME%"
ECHO ===
PAUSE
