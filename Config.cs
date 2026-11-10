using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CondorPreFlight
{
    public class DisplayInfo
    {
        public string DeviceName = "";   // \\.\DISPLAY3
        public int Number;               // 3  (normally the number Windows Display Settings shows)
        public int ListPosition;         // 1-based position in Windows' own enumeration order
        public int X, Y, W, H;
        public string HardwareId = "";
        public bool Primary;
    }

    public class Settings
    {
        public IniFile Ini;
        public const string Template =
@"; CondorPreFlight settings. Edit with the simulator panel closed.

[Paths]
; Condor program, e.g. C:\Condor3\Condor.exe
CondorExe=
; Folder holding one sub-folder per pilot, each with a Setup.ini.
; Blank = Documents\Condor3\Pilots
CondorPilotsDir=
; Free Condor Instruments program and the settings file it loads
InstrumentsExe=
InstrumentsIni=

[Layout]
; Number of projectors / screens in the outside view
SurroundCount=3
; 1 = keep the instrument panel screen as the Windows main display
MakePanelPrimary=1

[Condor]
; How Condor numbers displays:
;   WindowsNumber = the number in Windows Display Settings
;   ListOrder     = the order Windows lists displays to programs
Numbering=WindowsNumber
; Added to the number above. -1 because Condor stores MONITOR 1 as 0.
NumberOffset=-1
; View direction per screen, left to right, comma separated (e.g. -1,0,1).
; keep = reuse the offsets already in each pilot's Setup.ini.
ViewOffsets=keep

[Instruments]
Numbering=WindowsNumber
NumberOffset=-1
; Window mode to force (0 windowed, 1 fullscreen, 2 fullscreen emulation). Blank = leave alone.
Mode=

[Startup]
; 1 = start Condor and the instruments automatically when the panel opens and screens check out
AutoStartSim=0

[Admin]
; PIN for the Admin menu. Blank = no PIN. Only there to stop accidents; it is stored in plain text.
Pin=

[Calibration]
";

        public static string BaseDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }

        public static Settings Load()
        {
            string path = System.IO.Path.Combine(BaseDir, "CondorPreFlight.ini");
            Settings s = new Settings();
            // Carry over settings from the tool's earlier name.
            string legacy = System.IO.Path.Combine(BaseDir, "SimPanel.ini");
            if (!File.Exists(path) && File.Exists(legacy)) File.Move(legacy, path);
            if (!File.Exists(path))
            {
                s.Ini = IniFile.FromText(path, Template);
                s.Ini.Save();
            }
            else s.Ini = IniFile.Load(path);
            return s;
        }

        public string Get(string sec, string key, string fb) { string v = Ini.Get(sec, key, fb); return (v == null || v.Length == 0) ? fb : v; }
        public void Set(string sec, string key, string val) { if (Ini.Set(sec, key, val)) Ini.Save(); }
        public int GetInt(string sec, string key, int fb) { int v; return int.TryParse(Get(sec, key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fb; }

        public int SurroundCount { get { int n = GetInt("Layout", "SurroundCount", 3); return n < 1 ? 1 : (n > 8 ? 8 : n); } }
        public string CondorExe { get { return Get("Paths", "CondorExe", ""); } }
        public string InstrumentsExe { get { return Get("Paths", "InstrumentsExe", ""); } }
        public string InstrumentsIni { get { return Get("Paths", "InstrumentsIni", ""); } }
        public string CondorPilotsDir
        {
            get
            {
                string d = Get("Paths", "CondorPilotsDir", "");
                if (d.Length > 0) return d;
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"Condor3\Pilots");
            }
        }
    }

    public class ApplyResult
    {
        public bool Changed;
        public List<string> Notes = new List<string>();
        public string Summary() { return string.Join("\r\n", Notes.ToArray()); }
    }

    public static class Configurator
    {
        public static int NumberFor(DisplayInfo d, Settings s, string app)
        {
            string scheme = s.Get(app, "Numbering", "WindowsNumber");
            int offset = s.GetInt(app, "NumberOffset", -1);
            int basis = string.Equals(scheme, "ListOrder", StringComparison.OrdinalIgnoreCase) ? d.ListPosition : d.Number;
            return basis + offset;
        }

        public static List<string> CondorSetupFiles(Settings s)
        {
            List<string> files = new List<string>();
            string dir = s.CondorPilotsDir;
            if (!Directory.Exists(dir)) return files;
            foreach (string pilot in Directory.GetDirectories(dir))
            {
                string f = System.IO.Path.Combine(pilot, "Setup.ini");
                if (File.Exists(f)) files.Add(f);
            }
            return files;
        }

        private static string[] OffsetsFor(IniFile ini, Settings s, int n)
        {
            string cfg = s.Get("Condor", "ViewOffsets", "keep");
            if (!string.Equals(cfg, "keep", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = cfg.Split(',');
                if (parts.Length == n)
                {
                    for (int i = 0; i < n; i++) parts[i] = parts[i].Trim();
                    return parts;
                }
            }
            // Reuse what the file already has, ordered most-left (lowest) to most-right.
            int have;
            int.TryParse(ini.Get("Graphics", "MonitorCount", "0"), out have);
            if (have == n)
            {
                List<string> vals = new List<string>();
                bool ok = true;
                for (int i = 0; i < n; i++)
                {
                    string v = ini.Get("Graphics", "Monitor" + i + "FiOffset", null);
                    double d;
                    if (v == null || !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) { ok = false; break; }
                    vals.Add(v);
                }
                if (ok)
                {
                    vals.Sort(delegate(string a, string b)
                    {
                        return double.Parse(a, CultureInfo.InvariantCulture).CompareTo(double.Parse(b, CultureInfo.InvariantCulture));
                    });
                    return vals.ToArray();
                }
            }
            return null; // unknown: leave offsets untouched
        }

        public static ApplyResult Apply(Settings s, DisplayInfo[] surround, DisplayInfo panel)
        {
            ApplyResult r = new ApplyResult();
            List<IniFile> dirty = new List<IniFile>();
            int n = surround.Length;

            List<string> setups = CondorSetupFiles(s);
            if (setups.Count == 0) r.Notes.Add("No Condor Setup.ini found in " + s.CondorPilotsDir);
            foreach (string f in setups)
            {
                IniFile ini = IniFile.Load(f);
                string[] offsets = OffsetsFor(ini, s, n);
                ini.Set("Graphics", "MonitorCount", n.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < n; i++)
                {
                    ini.Set("Graphics", "Monitor" + i + "ID", NumberFor(surround[i], s, "Condor").ToString(CultureInfo.InvariantCulture));
                    if (offsets != null) ini.Set("Graphics", "Monitor" + i + "FiOffset", offsets[i]);
                }
                if (offsets == null)
                    r.Notes.Add("View directions not changed for " + PilotName(f) + " (set ViewOffsets in CondorPreFlight.ini, or set them once in Condor).");
                if (ini.Dirty) dirty.Add(ini);
            }

            string fci = s.InstrumentsIni;
            if (fci.Length > 0 && File.Exists(fci))
            {
                IniFile ini = IniFile.Load(fci);
                ini.Set("Window", "Monitor", NumberFor(panel, s, "Instruments").ToString(CultureInfo.InvariantCulture));
                string mode = s.Get("Instruments", "Mode", "");
                if (mode.Length > 0) ini.Set("Window", "Mode", mode);
                if (ini.Dirty) dirty.Add(ini);
            }
            else r.Notes.Add("Instruments settings file not set or not found.");

            if (dirty.Count > 0)
            {
                string backup = System.IO.Path.Combine(System.IO.Path.Combine(Settings.BaseDir, "Backups"), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(backup);
                List<string> manifest = new List<string>();
                for (int i = 0; i < dirty.Count; i++)
                {
                    string name = i + "_" + System.IO.Path.GetFileName(dirty[i].Path);
                    File.Copy(dirty[i].Path, System.IO.Path.Combine(backup, name), true);
                    manifest.Add(name + "|" + dirty[i].Path);
                }
                File.WriteAllLines(System.IO.Path.Combine(backup, "manifest.txt"), manifest.ToArray());
                foreach (IniFile ini in dirty) ini.Save();
                r.Changed = true;
                r.Notes.Insert(0, "Updated " + dirty.Count + " settings file(s). Previous versions saved.");
            }
            else r.Notes.Insert(0, "Screen settings already correct.");
            return r;
        }

        private static string PilotName(string setupPath)
        {
            return System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(setupPath));
        }

        public static string RestoreLast()
        {
            string root = System.IO.Path.Combine(Settings.BaseDir, "Backups");
            if (!Directory.Exists(root)) return "Nothing to undo.";
            List<string> dirs = new List<string>();
            foreach (string d in Directory.GetDirectories(root))
                if (!System.IO.Path.GetFileName(d).StartsWith("undone-")) dirs.Add(d);
            if (dirs.Count == 0) return "Nothing to undo.";
            dirs.Sort(StringComparer.Ordinal);
            string last = dirs[dirs.Count - 1];
            int count = 0;
            foreach (string line in File.ReadAllLines(System.IO.Path.Combine(last, "manifest.txt")))
            {
                int bar = line.IndexOf('|');
                if (bar <= 0) continue;
                File.Copy(System.IO.Path.Combine(last, line.Substring(0, bar)), line.Substring(bar + 1), true);
                count++;
            }
            Directory.Move(last, System.IO.Path.Combine(root, "undone-" + System.IO.Path.GetFileName(last)));
            return "Put back " + count + " settings file(s) from " + System.IO.Path.GetFileName(last) + ".";
        }
    }
}
