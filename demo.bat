@echo off
rem Preview the calibration wizard on a single monitor. Nothing is saved.
if not exist CondorPreFlight.exe ( echo Run build.bat first. & pause & exit /b 1 )
start "" CondorPreFlight.exe --demo %1
