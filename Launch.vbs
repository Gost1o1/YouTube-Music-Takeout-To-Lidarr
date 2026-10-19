Option Explicit
Dim sh, fso, folder, command
Set sh = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
folder = fso.GetParentFolderName(WScript.ScriptFullName)
sh.CurrentDirectory = folder
command = "powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File " & Chr(34) & fso.BuildPath(folder,"Start.ps1") & Chr(34)
sh.Run command, 0, True
