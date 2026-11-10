CONDOR PREFLIGHT

Install
  1. Copy this folder to the sim PC, e.g. C:\CondorPreFlight
  2. Run CondorPreFlight.exe (or double-click build.bat to rebuild it from the .cs files)
  3. First run asks where Condor.exe, the instruments program and its .ini are
  4. Press "Calibrate screens" once
  5. Open Admin and press "Start with Windows" so it reads ON

Using it
  It sits as a round button in the bottom-right corner of the instrument panel screen,
  always on top. Touch it to slide the menu out; touch it again to tuck it away.
  Green = screens OK. Amber = needs attention (open the menu to read why).
  For the button to show over the instruments, set Mode=2 in the instruments .ini
  (or Mode=2 under [Instruments] in CondorPreFlight.ini).

  Everyday buttons: Start simulator, Instruments only, Restart simulator, Calibrate screens.
  Admin holds the rest: Screen layout, Undo screen change, Start with Windows, Set PIN,
  Settings file, Exit. Set a PIN there to stop it being opened by accident.

Calibration
  Every screen shows its number and a picture of the screen layout.
  Touch where the asked-for screen is in the picture. The last screen is worked out.
  Check the names shown on each screen, then press YES - save.
  Keyboard: A, B, C = top row left to right, P = panel, Y / N to confirm, Esc to cancel.

What it changes
  Condor:       [Graphics] MonitorCount, MonitorNID (and MonitorNFiOffset) in every
                pilot's Setup.ini
  Instruments:  [Window] Monitor
  Windows:      the instrument panel is made the main display (MakePanelPrimary in the
                settings file turns this off)
  Every change is backed up in the Backups folder. "Undo last screen change" puts it back.

Settings live in CondorPreFlight.ini next to the program (created on first run).

Preview without the simulator
  Double-click demo.bat to try the calibration wizard on one monitor.
  The four sim screens are drawn as tiles in a T. Nothing is saved.
  For a different number of outside-view screens: CondorPreFlight.exe --demo 5
