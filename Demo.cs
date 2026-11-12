using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CondorPreFlight
{
    // Preview of the calibration wizard on a single monitor: each simulator screen
    // is drawn as a tile, laid out as it is in the cockpit. Nothing is saved.
    public static class Demo
    {
        public static void Run(ScreenLayout layout)
        {
            int n = layout.ViewCount;
            Rectangle area = Screen.PrimaryScreen.Bounds;
            int gap = Math.Max(6, area.Width / 200);
            int across = Math.Max(layout.Cols, 3);
            int tw = (area.Width - gap * (across + 1)) / across;
            int th = tw * 9 / 16;
            int maxH = (area.Height - gap * (layout.Rows + 1)) / layout.Rows;
            if (th > maxH) { th = maxH; tw = th * 16 / 9; }
            int left = area.X + (area.Width - (layout.Cols * tw + (layout.Cols - 1) * gap)) / 2;
            int top = area.Y + (area.Height - (layout.Rows * th + (layout.Rows - 1) * gap)) / 2;

            // Deliberately scrambled numbers, as Windows might hand them out on a bad day.
            int total = n + 1;
            List<DisplayInfo> displays = new List<DisplayInfo>();
            for (int i = 0; i < total; i++)
            {
                DisplayInfo d = new DisplayInfo();
                d.Number = ((i * 2 + 2) % total) + 1;
                if (total % 2 == 0 && i >= total / 2) d.Number = ((i * 2 + 1) % total) + 1;
                d.DeviceNumber = d.Number;
                d.ListPosition = i + 1;
                d.HardwareId = "demo" + i;
                d.DeviceName = "DEMO" + d.Number;
                d.W = tw; d.H = th;
                int[] cell = layout.Cell(i);
                d.X = left + cell[0] * (tw + gap); d.Y = top + cell[1] * (th + gap);
                displays.Add(d);
            }
            displays.Sort(delegate(DisplayInfo a, DisplayInfo b) { return a.Number.CompareTo(b.Number); });

            using (Form backdrop = new Form())
            {
                backdrop.FormBorderStyle = FormBorderStyle.None;
                backdrop.StartPosition = FormStartPosition.Manual;
                backdrop.Bounds = area;
                backdrop.BackColor = Color.Black;
                backdrop.ShowInTaskbar = false;
                backdrop.Show();

                CalibrationWizard w = new CalibrationWizard(displays, layout);
                bool ok = w.Run();
                string msg = "Calibration cancelled.";
                if (ok)
                {
                    msg = "";
                    for (int i = 0; i < n; i++) msg += w.RoleName(i) + " = screen " + w.Surround[i].Number + "\r\n";
                    msg += w.RoleName(n) + " = screen " + w.Panel.Number + "\r\n";
                }
                MessageBox.Show(backdrop, msg + "\r\nPreview only - nothing was saved.", "Condor PreFlight preview");
            }
        }
    }
}
