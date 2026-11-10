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
                d.Number = TrailingNumber(name, d.ListPosition);
                d.X = sc.Bounds.X; d.Y = sc.Bounds.Y; d.W = sc.Bounds.Width; d.H = sc.Bounds.Height;
                d.Primary = sc.Primary;
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
            list.Sort(delegate(DisplayInfo a, DisplayInfo b) { return a.Number.CompareTo(b.Number); });
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
