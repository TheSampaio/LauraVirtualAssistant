:: Title
ECHO OFF
TITLE Compile
CLS

:: Main
CD ../
SET /P NAME="Type the project's name: "
ECHO ===
pyinstaller Source/main.py --onefile --noconsole --noconfirm --name "%NAME%" --distpath "_Output/Bin/%NAME%" --workpath "_Output/Obj/" --specpath "_Output/Obj/%NAME%"
ECHO ===
PAUSE
