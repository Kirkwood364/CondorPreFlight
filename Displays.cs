using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CondorPreFlight
{
    public static class Displays
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);
        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY;
            public int dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsEx")]
        private static extern int ApplyDisplaySettings(string deviceName, IntPtr devMode, IntPtr hwnd, uint flags, IntPtr lParam);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int DM_POSITION = 0x20;
        private const uint CDS_UPDATEREGISTRY = 0x1, CDS_SET_PRIMARY = 0x10, CDS_NORESET = 0x10000000;

        // Makes one display the Windows main display. Windows requires the main display
        // to sit at position 0,0, so every display is shifted by the same amount and the
        // arrangement stays as it was. Returns null on success, or what went wrong.
        public static string SetPrimary(DisplayInfo target, List<DisplayInfo> all)
        {
            foreach (DisplayInfo d in all)
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (!EnumDisplaySettings(d.DeviceName, ENUM_CURRENT_SETTINGS, ref dm)) return "Could not read the settings of screen " + d.Number + ".";
                dm.dmFields = DM_POSITION;
                dm.dmPositionX = d.X - target.X;
                dm.dmPositionY = d.Y - target.Y;
                uint flags = CDS_UPDATEREGISTRY | CDS_NORESET;
                if (d == target) flags |= CDS_SET_PRIMARY;
                int rc = ChangeDisplaySettingsEx(d.DeviceName, ref dm, IntPtr.Zero, flags, IntPtr.Zero);
                if (rc != 0) return "Windows refused to move screen " + d.Number + " (code " + rc + ").";
            }
            int applied = ApplyDisplaySettings(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            return applied == 0 ? null : "Windows refused the new main display (code " + applied + ").";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        // The Windows "Scale" setting of the display, in percent. 0 if it cannot be read.
        private static int ScaleOf(DisplayInfo d)
        {
            try
            {
                POINT p; p.X = d.X + d.W / 2; p.Y = d.Y + d.H / 2;
                uint dx, dy;
                if (GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out dx, out dy) == 0) return (int)Math.Round(dx * 100.0 / 96.0);
            }
            catch { }
            return 0;
        }

        // Must run before any window exists, so screen sizes are real pixels.
        public static void MakeDpiAware()
        {
            try { SetProcessDpiAwareness(2); }
            catch { try { SetProcessDPIAware(); } catch { } }
        }

        public static List<DisplayInfo> GetAll()
        {
            List<DisplayInfo> list = new List<DisplayInfo>();
            int pos = 1;
            foreach (Screen sc in Screen.AllScreens)
            {
                DisplayInfo d = new DisplayInfo();
                string name = sc.DeviceName;
                int nul = name.IndexOf('\0');
                if (nul >= 0) name = name.Substring(0, nul);
                d.DeviceName = name;
                d.ListPosition = pos++;
                d.DeviceNumber = TrailingNumber(name, d.ListPosition);
                d.X = sc.Bounds.X; d.Y = sc.Bounds.Y; d.W = sc.Bounds.Width; d.H = sc.Bounds.Height;
                d.Primary = sc.Primary;
                d.Scale = ScaleOf(d);
                try
                {
                    DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
                    dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    // flag 1 = return the monitor's device interface path (model + connection)
                    if (EnumDisplayDevices(name, 0, ref dd, 1) && dd.DeviceID != null) d.HardwareId = dd.DeviceID;
                }
                catch { }
                list.Add(d);
            }
            // Device numbers can skip (1, 4, 5, 6) when the graphics card has unused outputs.
            // Display Settings, Condor and the instruments count 1, 2, 3, 4, so do the same.
            list.Sort(delegate(DisplayInfo a, DisplayInfo b) { return a.DeviceNumber.CompareTo(b.DeviceNumber); });
            for (int i = 0; i < list.Count; i++) list[i].Number = i + 1;

            // Which graphics card each display is on, and its place among that card's displays.
            Dictionary<string, string> adapters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (uint i = 0; i < 64; i++)
                {
                    DISPLAY_DEVICE ad = new DISPLAY_DEVICE();
                    ad.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    if (!EnumDisplayDevices(null, i, ref ad, 0)) break;
                    if (ad.DeviceName != null) adapters[ad.DeviceName] = ad.DeviceString + " | " + ad.DeviceID;
                }
            }
            catch { }
            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DisplayInfo d in list)
            {
                string a;
                if (adapters.TryGetValue(d.DeviceName, out a)) d.Adapter = a;
                int n;
                seen.TryGetValue(d.Adapter, out n);
                seen[d.Adapter] = d.AdapterRank = n + 1;
            }
            return list;
        }

        private static int TrailingNumber(string s, int fallback)
        {
            int i = s.Length;
            while (i > 0 && char.IsDigit(s[i - 1])) i--;
            int n;
            return (i < s.Length && int.TryParse(s.Substring(i), out n)) ? n : fallback;
        }
    }

    public static class Calibration
    {
        public static void Save(Settings s, DisplayInfo[] surround, DisplayInfo panel)
        {
            s.Ini.Set("Calibration", "Count", surround.Length.ToString());
            for (int i = 0; i < surround.Length; i++) s.Ini.Set("Calibration", "Surround" + i, surround[i].HardwareId);
            s.Ini.Set("Calibration", "Panel", panel.HardwareId);
            s.Ini.Set("Calibration", "Saved", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            s.Ini.Save();
        }

        // Works out which display is which from the saved hardware IDs.
        // Fails (with a plain-English reason) whenever it cannot be certain.
        public static bool TryResolve(Settings s, List<DisplayInfo> displays, out DisplayInfo[] surround, out DisplayInfo panel, out string reason)
        {
            surround = null; panel = null; reason = "";
            int n = s.SurroundCount;
            if (s.GetInt("Calibration", "Count", 0) != n) { reason = "The screens have not been calibrated yet."; return false; }
            if (displays.Count != n + 1) { reason = "Expected " + (n + 1) + " screens but Windows can see " + displays.Count + ". Check every projector is switched on."; return false; }

            Dictionary<string, DisplayInfo> byId = new Dictionary<string, DisplayInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (DisplayInfo d in displays)
            {
                if (d.HardwareId.Length == 0 || byId.ContainsKey(d.HardwareId)) { reason = "Windows cannot tell the screens apart reliably. Please calibrate."; return false; }
                byId[d.HardwareId] = d;
            }
            DisplayInfo[] sur = new DisplayInfo[n];
            for (int i = 0; i < n; i++)
            {
                string id = s.Get("Calibration", "Surround" + i, "");
                if (!byId.TryGetValue(id, out sur[i])) { reason = "A screen has changed since the last calibration. Please calibrate."; return false; }
            }
            DisplayInfo p;
            if (!byId.TryGetValue(s.Get("Calibration", "Panel", ""), out p)) { reason = "The instrument panel screen has changed since the last calibration. Please calibrate."; return false; }
            surround = sur; panel = p;
            return true;
        }
    }
}
