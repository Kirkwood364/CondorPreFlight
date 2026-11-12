using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CondorPreFlight
{
    // A small always-on-top button in the bottom-right corner of the instrument panel
    // screen. Touching it slides the menu out to the left; touching it again tucks it away.
    public class MainForm : Form
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private static readonly Color Back = Color.FromArgb(16, 24, 32);
        private static readonly Color Plain = Color.FromArgb(50, 62, 75);
        private static readonly Color Go = Color.FromArgb(40, 150, 90);
        private static readonly Color Warn = Color.FromArgb(190, 120, 20);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private class Item
        {
            public string Label; public Color Color; public MethodInvoker Action;
            public Item(string label, Color color, MethodInvoker action) { Label = label; Color = color; Action = action; }
        }

        private Settings settings;
        private List<Item> items;                      // the row currently shown
        private List<Item> mainItems = new List<Item>();
        private List<Item> adminItems = new List<Item>();
        private Item autoItem;
        private DateTime restartArmed = DateTime.MinValue;
        private bool expanded;
        private bool screensOk;
        private string statusText = "";
        private int S = 64, buttonW = 120, statusH;
        private Timer topTimer = new Timer();
        private Timer frontTimer = new Timer();
        private Dictionary<IntPtr, bool> fronted = new Dictionary<IntPtr, bool>();
        private DateTime frontStarted, frontLastNew;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        public MainForm()
        {
            settings = Settings.Load();
            Text = "Condor PreFlight";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Back;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            // Everyday row
            mainItems.Add(new Item("START\nSIMULATOR", Go, delegate { StartSim(true); }));
            mainItems.Add(new Item("Instruments\nonly", Plain, delegate { StartSim(false); }));
            mainItems.Add(new Item("Restart\nsimulator", Plain, delegate { RestartSim(); }));
            mainItems.Add(new Item("Calibrate\nscreens", Warn, delegate { Calibrate(); }));
            mainItems.Add(new Item("Admin", Plain, delegate { OpenAdmin(); }));
            // Admin row (behind the optional PIN)
            adminItems.Add(new Item("< Back", Plain, delegate { ShowRow(mainItems); }));
            adminItems.Add(new Item("Screen\nlayout", Plain, delegate { EditLayout(); }));
            adminItems.Add(new Item("Undo screen\nchange", Plain, delegate { Undo(); }));
            autoItem = new Item("", Plain, delegate { ToggleAutoStart(); });
            adminItems.Add(autoItem);
            adminItems.Add(new Item("Screen\nreport", Plain, delegate { Report(); }));
            adminItems.Add(new Item("Set PIN", Plain, delegate { SetPin(); }));
            adminItems.Add(new Item("Settings\nfile", Plain, delegate { OpenSettings(); }));
            adminItems.Add(new Item("Exit", Plain, delegate { Close(); }));
            items = mainItems;

            MigrateAutoStart();
            UpdateAutoLabel();
            Place();

            // Other always-on-top windows (the instruments) can slip above us; climb back up.
            topTimer.Interval = 2000;
            topTimer.Tick += delegate { Raise(); };
            topTimer.Start();
            frontTimer.Interval = 500;
            frontTimer.Tick += delegate { FrontTick(); };
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            FormClosed += delegate { SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; };
            Shown += delegate { FirstRunPaths(); CheckScreens(true); Place(); MaybeAutoStart(); };
        }

        // Never take the keyboard away from Condor when touched, and stay out of Alt+Tab.
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        private void Raise()
        {
            if (IsHandleCreated && Visible) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        private void OnDisplayChanged(object sender, EventArgs e)
        {
            try { BeginInvoke((MethodInvoker)delegate { Place(); }); } catch { }
        }

        // ---- overlay layout and drawing ------------------------------------

        private Rectangle AnchorArea()
        {
            try
            {
                DisplayInfo[] surround; DisplayInfo panel; string reason;
                if (Calibration.TryResolve(settings, Displays.GetAll(), out surround, out panel, out reason))
                    foreach (Screen sc in Screen.AllScreens)
                        if (sc.DeviceName.StartsWith(panel.DeviceName, StringComparison.OrdinalIgnoreCase)) return sc.WorkingArea;
            }
            catch { }
            return Screen.PrimaryScreen.WorkingArea;
        }

        private void Place()
        {
            Rectangle a = AnchorArea();
            S = Math.Max(56, a.Height / 11);
            int margin = S / 6;
            if (!expanded)
            {
                statusH = 0;
                Bounds = new Rectangle(a.Right - margin - S, a.Bottom - margin - S, S, S);
            }
            else
            {
                buttonW = Math.Min((int)(S * 2.2), (a.Width - 2 * margin - S) / items.Count);
                int w = S + items.Count * buttonW;
                statusH = statusText.Length == 0 ? 0 : (int)(S * 0.95);
                Bounds = new Rectangle(a.Right - margin - w, a.Bottom - margin - S - statusH, w, S + statusH);
            }
            Invalidate();
            Raise();
        }

        private Rectangle IconRect() { return new Rectangle(ClientSize.Width - S, statusH, S, S); }
        private Rectangle ItemRect(int i) { return new Rectangle(i * buttonW + 2, statusH + 2, buttonW - 4, S - 4); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using (StringFormat centre = new StringFormat())
            using (Brush white = new SolidBrush(Color.White))
            {
                centre.Alignment = StringAlignment.Center; centre.LineAlignment = StringAlignment.Center;
                if (expanded)
                {
                    if (statusH > 0)
                        using (Font f = new Font("Segoe UI", S * 0.21f, FontStyle.Regular, GraphicsUnit.Pixel))
                            g.DrawString(statusText, f, white, new Rectangle(8, 4, ClientSize.Width - 16, statusH - 6));
                    using (Font f = new Font("Segoe UI", S * 0.22f, FontStyle.Bold, GraphicsUnit.Pixel))
                        for (int i = 0; i < items.Count; i++)
                        {
                            Rectangle r = ItemRect(i);
                            using (Brush b = new SolidBrush(items[i].Color)) g.FillRectangle(b, r);
                            g.DrawString(items[i].Label, f, white, r, centre);
                        }
                }
                // The button itself: green when the screens check out, amber when they need attention.
                Rectangle ic = IconRect();
                ic.Inflate(-S / 10, -S / 10);
                using (Brush b = new SolidBrush(screensOk ? Go : Warn)) g.FillEllipse(b, ic);
                int bar = Math.Max(3, S / 14), bw = ic.Width / 2, bx = ic.X + (ic.Width - bw) / 2, cy = ic.Y + ic.Height / 2;
                for (int k = -1; k <= 1; k++) g.FillRectangle(white, bx, cy + k * bar * 2 - bar / 2, bw, bar);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (IconRect().Contains(e.Location))
            {
                expanded = !expanded;
                if (!expanded) { statusText = ""; items = mainItems; }   // admin locks again when tucked away
                Place();
                return;
            }
            if (!expanded) return;
            for (int i = 0; i < items.Count; i++)
                if (ItemRect(i).Contains(e.Location)) { items[i].Action(); return; }
        }

        // Shows a message and opens the menu so it can be read.
        private void Say(string text) { statusText = text; expanded = true; Place(); }
        // Keeps the message for when the menu is next opened, without popping it open.
        private void Note(string text) { statusText = text; Place(); }

        // ---- menus ---------------------------------------------------------

        private void ShowRow(List<Item> row) { items = row; statusText = ""; Place(); }

        private void OpenAdmin()
        {
            string pin = settings.Get("Admin", "Pin", "");
            if (pin.Length > 0)
            {
                using (PinDialog d = new PinDialog("Admin PIN", AnchorArea()))
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    if (d.Pin != pin) { Say("Wrong PIN."); return; }
                }
            }
            ShowRow(adminItems);
        }

        private void SetPin()
        {
            using (PinDialog d = new PinDialog("New admin PIN (OK with nothing entered = no PIN)", AnchorArea()))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                settings.Ini.Set("Admin", "Pin", d.Pin);
                settings.Ini.Save();
                Say(d.Pin.Length > 0 ? "Admin PIN set." : "Admin PIN removed.");
            }
        }

        private void EditLayout()
        {
            string before = settings.Layout.ToString(), after;
            using (LayoutDialog d = new LayoutDialog(settings.Layout, AnchorArea()))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                after = d.Result.ToString();
            }
            if (after == before) return;
            ScreenLayout chosen = ScreenLayout.Parse(after, 3);
            settings.Ini.Set("Layout", "Cells", after);
            settings.Ini.Set("Layout", "SurroundCount", chosen.ViewCount.ToString());
            settings.Ini.Set("Calibration", "Count", "0");   // the old calibration no longer matches the picture
            settings.Ini.Save();
            CheckScreens(false);
        }

        // Writes everything known about the screens and settings files to a text file,
        // for working out numbering problems. Changes nothing.
        private void Report()
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("Condor PreFlight screen report  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                sb.AppendLine();
                List<DisplayInfo> all = Displays.GetAll();
                sb.AppendLine("SCREENS AS WINDOWS REPORTS THEM");
                foreach (DisplayInfo d in all)
                    sb.AppendLine("  wizard number " + d.Number + " | device " + d.DeviceName + " | list position " + d.ListPosition + " | card position " + d.AdapterRank
                        + " | " + (d.Primary ? "MAIN DISPLAY" : "not main") + " | at " + d.X + "," + d.Y + " size " + d.W + "x" + d.H + " | id " + d.HardwareId + " | card " + d.Adapter);
                sb.AppendLine();
                sb.AppendLine("LAYOUT  " + settings.Layout + "   (calibrated " + settings.Get("Calibration", "Saved", "never") + ")");
                DisplayInfo[] surround; DisplayInfo panel; string reason;
                if (Calibration.TryResolve(settings, all, out surround, out panel, out reason))
                {
                    for (int i = 0; i < surround.Length; i++)
                        sb.AppendLine("  outside view " + (i + 1) + " = wizard number " + surround[i].Number + " -> Condor Monitor" + i + "ID should be " + Configurator.NumberFor(surround[i], settings, "Condor"));
                    sb.AppendLine("  instrument panel = wizard number " + panel.Number + " -> instruments Monitor should be " + Configurator.NumberFor(panel, settings, "Instruments"));
                }
                else sb.AppendLine("  Not resolved: " + reason);
                sb.AppendLine();
                sb.AppendLine("NUMBERING  Condor: " + settings.Get("Condor", "Numbering", "WindowsNumber") + " offset " + settings.GetInt("Condor", "NumberOffset", -1)
                    + "   Instruments: " + settings.Get("Instruments", "Numbering", "WindowsNumber") + " offset " + settings.GetInt("Instruments", "NumberOffset", -1));
                sb.AppendLine();
                sb.AppendLine("CONDOR  exe: " + settings.CondorExe + "   running: " + CondorRunning());
                sb.AppendLine("  pilots folder: " + settings.CondorPilotsDir);
                foreach (string f in Configurator.CondorSetupFiles(settings))
                {
                    IniFile ini = IniFile.Load(f);
                    int count; int.TryParse(ini.Get("Graphics", "MonitorCount", "0"), out count);
                    string line = "  " + f + "  MonitorCount=" + count;
                    for (int i = 0; i < count && i < 9; i++)
                        line += "  [" + i + "] ID=" + ini.Get("Graphics", "Monitor" + i + "ID", "?") + " offset=" + ini.Get("Graphics", "Monitor" + i + "FiOffset", "?");
                    sb.AppendLine(line);
                }
                sb.AppendLine();
                string fci = settings.InstrumentsIni, fciExe = settings.InstrumentsExe;
                sb.AppendLine("INSTRUMENTS  exe: " + fciExe + "   running: " + (fciExe.Length > 0 && Running(fciExe)));
                sb.AppendLine("  settings file: " + fci);
                if (fci.Length > 0 && File.Exists(fci))
                {
                    IniFile ini = IniFile.Load(fci);
                    sb.AppendLine("  Monitor=" + ini.Get("Window", "Monitor", "?") + "  Mode=" + ini.Get("Window", "Mode", "?") + "  Left=" + ini.Get("Window", "Left", "?") + "  Top=" + ini.Get("Window", "Top", "?"));
                }
                else sb.AppendLine("  (file not found)");
                if (fciExe.Length > 0 && File.Exists(fciExe))
                {
                    sb.AppendLine("  other .ini files beside the instruments program:");
                    foreach (string f in Directory.GetFiles(Path.GetDirectoryName(fciExe), "*.ini"))
                        sb.AppendLine("    " + Path.GetFileName(f) + "  Monitor=" + IniFile.Load(f).Get("Window", "Monitor", "-"));
                }
                string path = Path.Combine(Settings.BaseDir, "CondorPreFlight-report.txt");
                File.WriteAllText(path, sb.ToString());
                Process.Start("notepad.exe", "\"" + path + "\"");
                Say("Report saved as CondorPreFlight-report.txt next to the program.");
            }
            catch (Exception ex) { Say("Could not write the report: " + ex.Message); }
        }

        // ---- screens -------------------------------------------------------

        private void CheckScreens(bool quiet)
        {
            screensOk = false;
            try
            {
                DisplayInfo[] surround; DisplayInfo panel; string reason;
                List<DisplayInfo> all = Displays.GetAll();
                if (!Calibration.TryResolve(settings, all, out surround, out panel, out reason))
                {
                    Say(reason + "\r\nPress \"Calibrate screens\".");
                    return;
                }
                if (CondorRunning())
                {
                    screensOk = true;
                    Note("Condor is running, so screen settings were not checked.");
                    return;
                }
                string primaryNote = EnsurePanelPrimary(panel, all);
                ApplyResult r = Configurator.Apply(settings, surround, panel);
                screensOk = true;
                string restartNote = r.Changed ? RestartInstrumentsIfRunning() : "";
                string msg = primaryNote + (r.Changed ? "Windows had renumbered the screens - fixed automatically.\r\n" : "Screens OK.\r\n") + restartNote + r.Summary();
                if (quiet && !r.Changed && primaryNote.Length == 0) Note(msg); else Say(msg);
            }
            catch (Exception ex) { Say("Could not check the screens: " + ex.Message); }
        }

        // Makes the instrument panel the Windows main display if it is not already.
        // Returns a line for the status box ("" when nothing needed doing).
        private string EnsurePanelPrimary(DisplayInfo panel, List<DisplayInfo> all)
        {
            if (settings.GetInt("Layout", "MakePanelPrimary", 1) != 1 || panel.Primary) return "";
            string err = Displays.SetPrimary(panel, all);
            return err == null ? "Instrument panel set as the Windows main display.\r\n" : "Could not set the main display: " + err + "\r\n";
        }

        private void Calibrate()
        {
            if (CondorRunning()) { Say("Close Condor first, then calibrate."); return; }
            List<DisplayInfo> displays = Displays.GetAll();
            int need = settings.SurroundCount + 1;
            if (displays.Count != need)
            {
                Say("Expected " + need + " screens but Windows can see " + displays.Count + ".\r\nCheck every projector is switched on, then try again.");
                return;
            }
            CalibrationWizard w = new CalibrationWizard(displays, settings.Layout);
            Hide();   // the wizard covers every screen; keep the button out of its way
            bool ok;
            try { ok = w.Run(); } finally { Show(); }
            if (!ok) { Say("Calibration cancelled. Nothing was changed."); return; }
            try
            {
                Calibration.Save(settings, w.Surround, w.Panel);
                string primaryNote = EnsurePanelPrimary(w.Panel, displays);
                ApplyResult r = Configurator.Apply(settings, w.Surround, w.Panel);
                screensOk = true;
                Say("Calibration saved.\r\n" + primaryNote + RestartInstrumentsIfRunning() + r.Summary());
            }
            catch (Exception ex) { Say("Calibration could not be saved: " + ex.Message); }
        }

        private void Undo()
        {
            if (CondorRunning()) { Say("Close Condor first."); return; }
            try { Say(Configurator.RestoreLast() + "\r\n" + RestartInstrumentsIfRunning()); }
            catch (Exception ex) { Say("Could not undo: " + ex.Message); }
        }

        // ---- launching -----------------------------------------------------

        private bool CondorRunning()
        {
            string exe = settings.CondorExe;
            string name = exe.Length > 0 ? Path.GetFileNameWithoutExtension(exe) : "Condor";
            try { return Process.GetProcessesByName(name).Length > 0; } catch { return false; }
        }

        private static bool Running(string exe)
        {
            try { return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).Length > 0; } catch { return false; }
        }

        private static void Launch(string exe)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe);
            psi.WorkingDirectory = Path.GetDirectoryName(exe);
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        // The instruments only read their settings when they start, so a running copy
        // has to be closed and reopened to move to the newly chosen screen.
        private string RestartInstrumentsIfRunning()
        {
            string exe = settings.InstrumentsExe;
            if (exe.Length == 0 || !File.Exists(exe) || !Running(exe)) return "";
            try
            {
                CloseProgram(exe);
                Launch(exe);
                return "Instruments restarted.\r\n";
            }
            catch (Exception ex) { return "Could not restart the instruments: " + ex.Message + "\r\n"; }
        }

        // Asks the program to close; if it has not gone after a few seconds, ends it.
        private static void CloseProgram(string exe)
        {
            if (exe.Length == 0) return;
            foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
            {
                try
                {
                    if (!p.CloseMainWindow() || !p.WaitForExit(4000)) { p.Kill(); p.WaitForExit(4000); }
                }
                catch { }
            }
        }

        // Two touches needed, so a stray tap cannot end someone's flight.
        private void RestartSim()
        {
            if ((DateTime.Now - restartArmed).TotalSeconds > 8)
            {
                restartArmed = DateTime.Now;
                Say("This closes Condor and the instruments, then starts them again.\r\nTouch \"Restart simulator\" again to go ahead.");
                return;
            }
            restartArmed = DateTime.MinValue;
            CloseProgram(settings.CondorExe);
            CloseProgram(settings.InstrumentsExe);
            StartSim(true);
        }

        private void StartSim(bool withCondor)
        {
            CheckScreens(true);
            if (!screensOk) return;
            try
            {
                string fci = settings.InstrumentsExe, condor = settings.CondorExe;
                List<string> done = new List<string>();
                if (fci.Length > 0 && File.Exists(fci)) { if (!Running(fci)) { Launch(fci); done.Add("instruments"); } }
                else { Say("The instruments program is not set. Press \"Settings file\"."); return; }
                if (withCondor)
                {
                    if (condor.Length > 0 && File.Exists(condor)) { if (!Running(condor)) { Launch(condor); done.Add("Condor"); } BringCondorForward(); }
                    else { Say("Condor.exe is not set. Press \"Settings file\"."); return; }
                }
                // Tuck the menu away so it is not sitting over the instruments.
                statusText = done.Count > 0 ? "Started " + string.Join(" and ", done.ToArray()) + "." : "Already running.";
                expanded = false;
                Place();
            }
            catch (Exception ex) { Say("Could not start: " + ex.Message); }
        }

        // Condor's menu opens on the main display, underneath the always-on-top instruments.
        // Watch for Condor's windows as they appear and lift each one to the front.
        private void BringCondorForward()
        {
            fronted.Clear();
            frontStarted = frontLastNew = DateTime.Now;
            frontTimer.Start();
        }

        private void FrontTick()
        {
            DateTime now = DateTime.Now;
            // Stop once the menu has been up for a while, so flight windows are left alone.
            if ((now - frontStarted).TotalSeconds > 180 || (fronted.Count > 0 && (now - frontLastNew).TotalSeconds > 20)) { frontTimer.Stop(); return; }
            try
            {
                string exe = settings.CondorExe;
                Dictionary<uint, bool> pids = new Dictionary<uint, bool>();
                foreach (Process p in Process.GetProcessesByName(exe.Length > 0 ? Path.GetFileNameWithoutExtension(exe) : "Condor")) pids[(uint)p.Id] = true;
                if (pids.Count == 0) return;
                bool onTop = settings.GetInt("Startup", "CondorOnTop", 1) == 1;
                EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    uint pid; RECT r;
                    GetWindowThreadProcessId(h, out pid);
                    if (!pids.ContainsKey(pid) || fronted.ContainsKey(h) || !IsWindowVisible(h) || !GetWindowRect(h, out r)) return true;
                    if (r.Right - r.Left < 300 || r.Bottom - r.Top < 200) return true;   // skip hidden helper windows
                    fronted[h] = true;
                    frontLastNew = now;
                    if (IsIconic(h)) ShowWindow(h, 9);
                    if (onTop) SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002);
                    if (!SetForegroundWindow(h) || GetForegroundWindow() != h)
                    {
                        // Windows sometimes refuses a focus change; a tap of Alt unlocks it.
                        keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
                        SetForegroundWindow(h);
                    }
                    return true;
                }, IntPtr.Zero);
                Raise();   // keep our own button above Condor's menu
            }
            catch { }
        }

        private void MaybeAutoStart()
        {
            if (screensOk && settings.GetInt("Startup", "AutoStartSim", 0) == 1) StartSim(true);
        }

        // ---- setup ---------------------------------------------------------

        private void FirstRunPaths()
        {
            if (settings.CondorExe.Length == 0 && File.Exists(@"C:\Condor3\Condor.exe")) settings.Set("Paths", "CondorExe", @"C:\Condor3\Condor.exe");
            if (settings.CondorExe.Length > 0 && settings.InstrumentsExe.Length > 0 && settings.InstrumentsIni.Length > 0) return;
            if (MessageBox.Show(this, "First-time setup: show Condor PreFlight where Condor and the instruments are installed?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            if (settings.CondorExe.Length == 0) Pick("Paths", "CondorExe", "Find Condor.exe", "Programs|*.exe", null);
            if (settings.InstrumentsExe.Length == 0) Pick("Paths", "InstrumentsExe", "Find the Free Condor Instruments program", "Programs|*.exe", null);
            if (settings.InstrumentsIni.Length == 0)
            {
                string dir = settings.InstrumentsExe.Length > 0 ? Path.GetDirectoryName(settings.InstrumentsExe) : null;
                Pick("Paths", "InstrumentsIni", "Find the instruments settings file in use", "Settings files|*.ini", dir);
            }
        }

        private void Pick(string section, string key, string title, string filter, string dir)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = title; d.Filter = filter;
                if (dir != null) d.InitialDirectory = dir;
                if (d.ShowDialog(this) == DialogResult.OK) settings.Set(section, key, d.FileName);
            }
        }

        private void OpenSettings()
        {
            try
            {
                Process p = Process.Start("notepad.exe", "\"" + settings.Ini.Path + "\"");
                Say("Save and close Notepad to use the new settings.");
                p.EnableRaisingEvents = true;
                p.Exited += delegate { try { BeginInvoke((MethodInvoker)delegate { settings = Settings.Load(); CheckScreens(false); }); } catch { } };
            }
            catch (Exception ex) { Say("Could not open settings: " + ex.Message); }
        }

        // Carry over the start-with-Windows entry from the tool's earlier name.
        private void MigrateAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k.GetValue("SimPanel") == null) return;
                    k.DeleteValue("SimPanel");
                    k.SetValue("CondorPreFlight", "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch { }
        }

        private void UpdateAutoLabel()
        {
            bool on = false;
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) { on = k != null && k.GetValue("CondorPreFlight") != null; } } catch { }
            autoItem.Label = "Start with\nWindows: " + (on ? "ON" : "OFF");
        }

        private void ToggleAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k.GetValue("CondorPreFlight") != null) k.DeleteValue("CondorPreFlight");
                    else k.SetValue("CondorPreFlight", "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch (Exception ex) { Say("Could not change start-up setting: " + ex.Message); }
            UpdateAutoLabel();
            Invalidate();
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--demo", StringComparison.OrdinalIgnoreCase))
            {
                // "--demo 5" previews a plain row of 5; "--demo" alone uses the saved layout.
                int n;
                ScreenLayout layout = (args.Length > 1 && int.TryParse(args[1], out n)) ? ScreenLayout.Default(n) : Settings.Load().Layout;
                Displays.MakeDpiAware();
                Application.EnableVisualStyles();
                Demo.Run(layout);
                return;
            }
            bool first;
            using (System.Threading.Mutex m = new System.Threading.Mutex(true, "CondorPreFlight.SingleInstance", out first))
            {
                if (!first) return;
                Displays.MakeDpiAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }
}
