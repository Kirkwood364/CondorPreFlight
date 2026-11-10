using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CondorPreFlight
{
    public class MainForm : Form
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private Settings settings;
        private TextBox status;
        private Button autoButton;
        private bool screensOk;

        public MainForm()
        {
            settings = Settings.Load();
            Text = "Condor PreFlight";
            BackColor = Color.FromArgb(16, 24, 32);
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.WorkingArea;
            BuildUi(Screen.PrimaryScreen.Bounds.Height);
            Shown += delegate { FirstRunPaths(); CheckScreens(true); MoveToPanel(); MaybeAutoStart(); };
        }

        private void BuildUi(int screenH)
        {
            float big = Math.Max(16f, screenH / 22f), normal = Math.Max(12f, screenH / 40f);
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.Padding = new Padding(screenH / 40);
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            t.RowCount = 5;
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

            Button start = Make("START SIMULATOR", big, Color.FromArgb(40, 150, 90), delegate { StartSim(true); });
            t.Controls.Add(start, 0, 0); t.SetColumnSpan(start, 2);
            t.Controls.Add(Make("Instruments only", normal, Color.FromArgb(50, 62, 75), delegate { StartSim(false); }), 0, 1);
            t.Controls.Add(Make("Calibrate screens", normal, Color.FromArgb(190, 120, 20), delegate { Calibrate(); }), 1, 1);
            t.Controls.Add(Make("Undo last screen change", normal, Color.FromArgb(50, 62, 75), delegate { Undo(); }), 0, 2);
            autoButton = Make("", normal, Color.FromArgb(50, 62, 75), delegate { ToggleAutoStart(); });
            t.Controls.Add(autoButton, 1, 2);
            t.Controls.Add(Make("Settings file", normal, Color.FromArgb(50, 62, 75), delegate { OpenSettings(); }), 0, 3);
            t.Controls.Add(Make("Close panel", normal, Color.FromArgb(50, 62, 75), delegate { Close(); }), 1, 3);

            status = new TextBox();
            status.Multiline = true; status.ReadOnly = true; status.Dock = DockStyle.Fill;
            status.BorderStyle = BorderStyle.None; status.TabStop = false;
            status.BackColor = BackColor; status.ForeColor = Color.White;
            status.Font = new Font("Segoe UI", Math.Max(11f, screenH / 50f), FontStyle.Regular, GraphicsUnit.Pixel);
            t.Controls.Add(status, 0, 4); t.SetColumnSpan(status, 2);
            Controls.Add(t);
            MigrateAutoStart();
            UpdateAutoButton();
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

        private static Button Make(string text, float px, Color back, EventHandler click)
        {
            Button b = new Button();
            b.Text = text; b.Dock = DockStyle.Fill; b.Margin = new Padding(8);
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.BackColor = back; b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
            b.Click += click;
            return b;
        }

        private void Say(string text) { status.Text = text; }

        // ---- screens -------------------------------------------------------

        private void CheckScreens(bool quiet)
        {
            screensOk = false;
            try
            {
                DisplayInfo[] surround; DisplayInfo panel; string reason;
                if (!Calibration.TryResolve(settings, Displays.GetAll(), out surround, out panel, out reason))
                {
                    Say(reason + "\r\nPress \"Calibrate screens\".");
                    return;
                }
                if (CondorRunning())
                {
                    screensOk = true;
                    Say("Condor is running, so screen settings were not checked.");
                    return;
                }
                ApplyResult r = Configurator.Apply(settings, surround, panel);
                screensOk = true;
                Say((r.Changed ? "Windows had renumbered the screens - fixed automatically.\r\n" : "Screens OK.\r\n") + r.Summary());
            }
            catch (Exception ex) { Say("Could not check the screens: " + ex.Message); }
        }

        private void MoveToPanel()
        {
            try
            {
                DisplayInfo[] surround; DisplayInfo panel; string reason;
                if (!Calibration.TryResolve(settings, Displays.GetAll(), out surround, out panel, out reason)) return;
                WindowState = FormWindowState.Normal;
                Bounds = new Rectangle(panel.X, panel.Y, panel.W, panel.H);
                WindowState = FormWindowState.Maximized;
            }
            catch { }
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
            CalibrationWizard w = new CalibrationWizard(displays, settings.SurroundCount);
            if (!w.Run()) { Say("Calibration cancelled. Nothing was changed."); return; }
            try
            {
                Calibration.Save(settings, w.Surround, w.Panel);
                ApplyResult r = Configurator.Apply(settings, w.Surround, w.Panel);
                screensOk = true;
                Say("Calibration saved.\r\n" + r.Summary());
                MoveToPanel();
            }
            catch (Exception ex) { Say("Calibration could not be saved: " + ex.Message); }
        }

        private void Undo()
        {
            if (CondorRunning()) { Say("Close Condor first."); return; }
            try { Say(Configurator.RestoreLast()); }
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
                    if (condor.Length > 0 && File.Exists(condor)) { if (!Running(condor)) { Launch(condor); done.Add("Condor"); } }
                    else { Say("Condor.exe is not set. Press \"Settings file\"."); return; }
                }
                Say(done.Count > 0 ? "Started " + string.Join(" and ", done.ToArray()) + "." : "Already running.");
            }
            catch (Exception ex) { Say("Could not start: " + ex.Message); }
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
            if (MessageBox.Show(this, "First-time setup: show the panel where Condor and the instruments are installed?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
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
                Say("Save and close Notepad, then press \"Calibrate screens\" or restart the panel to use new settings.");
                p.EnableRaisingEvents = true;
                p.Exited += delegate { try { BeginInvoke((MethodInvoker)delegate { settings = Settings.Load(); CheckScreens(true); }); } catch { } };
            }
            catch (Exception ex) { Say("Could not open settings: " + ex.Message); }
        }

        private bool AutoStartOn()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) { return k != null && k.GetValue("CondorPreFlight") != null; }
        }

        private void UpdateAutoButton()
        {
            bool on = false;
            try { on = AutoStartOn(); } catch { }
            autoButton.Text = "Open at Windows start: " + (on ? "ON" : "OFF");
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
            UpdateAutoButton();
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--demo", StringComparison.OrdinalIgnoreCase))
            {
                int n = 3;
                if (args.Length > 1) int.TryParse(args[1], out n);
                Displays.MakeDpiAware();
                Application.EnableVisualStyles();
                Demo.Run(n);
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
