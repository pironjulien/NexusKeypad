@echo off
rem Marketplace hook: runs the NexusKeypad helper installed by the Logitech plugin; without it, an empty answer.
setlocal
set "HOOK=%LOCALAPPDATA%\NexusKeypad\hook\nexuskeypad-hook.exe"
if not exist "%HOOK%" (
  echo {}
  exit /b 0
)
"%HOOK%" %*
exit /b 0
