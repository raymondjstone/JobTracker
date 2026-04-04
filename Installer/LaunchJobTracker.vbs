Set WshShell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

' Resolve the folder this script lives in
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)

' Build environment so Kestrel listens on HTTP (no dev cert needed)
Set env = WshShell.Environment("Process")
env("ASPNETCORE_URLS") = "http://localhost:7046"

' Start JobTracker.exe hidden (0 = hidden window, False = don't wait)
WshShell.CurrentDirectory = scriptDir
WshShell.Run """" & scriptDir & "\JobTracker.exe""", 0, False

' Wait for the server to start
WScript.Sleep 4000

' Open browser on HTTP
WshShell.Run "http://localhost:7046", 1, False
