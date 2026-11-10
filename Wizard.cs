using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CondorPreFlight
{
    // Shows the same wizard on every display, so it works even when nobody knows
    // which screen is which yet (and even if touches land on the "wrong" screen).
    public class CalibrationWizard
    {
        public DisplayInfo[] Surround;
        public DisplayInfo Panel;

        private List<DisplayInfo> displays;
        private int n;                       // surround screens
        private DisplayInfo[] boxOwner;      // index 0..n-1 surround left->right, n = panel
        private int step;                    // index into displays of the screen being located
        private bool confirming;
        private bool accepted;
        private List<WizardForm> forms = new List<WizardForm>();

        public CalibrationWizard(List<DisplayInfo> displays, int surroundCount)
        {
            this.displays = displays;
            this.n = surroundCount;
            Reset();
        }

        public int SurroundCount { get { return n; } }
        public bool Confirming { get { return confirming; } }
        public DisplayInfo Current { get { return displays[step]; } }
        public DisplayInfo OwnerOf(int box) { return boxOwner[box]; }

        public int BoxOf(DisplayInfo d)
        {
            for (int i = 0; i <= n; i++) if (boxOwner[i] == d) return i;
            return -1;
        }

        public string RoleName(int box)
        {
            if (box == n) return "INSTRUMENT PANEL";
            if (n == 1) return "MAIN SCREEN";
            if (n == 3) return box == 0 ? "LEFT SCREEN" : (box == 1 ? "CENTRE SCREEN" : "RIGHT SCREEN");
            return "SCREEN " + (box + 1) + " FROM THE LEFT";
        }

        private void Reset()
        {
            boxOwner = new DisplayInfo[n + 1];
            step = 0;
            confirming = false;
        }

        public bool Run()
        {
            foreach (DisplayInfo d in displays) forms.Add(new WizardForm(this, d));
            for (int i = 1; i < forms.Count; i++) forms[i].Show();
            forms[0].ShowDialog();
            foreach (WizardForm f in forms) { if (!f.IsDisposed) f.Dispose(); }
            return accepted;
        }

        private void Refresh() { foreach (WizardForm f in forms) f.Invalidate(); }

        private void Finish(bool ok)
        {
            accepted = ok;
            if (ok)
            {
                Surround = new DisplayInfo[n];
                for (int i = 0; i < n; i++) Surround[i] = boxOwner[i];
                Panel = boxOwner[n];
            }
            for (int i = forms.Count - 1; i >= 0; i--) forms[i].Close();
        }

        public void ChooseBox(int box)
        {
            if (confirming || box < 0 || box > n || boxOwner[box] != null) return;
            boxOwner[box] = displays[step];
            step++;
            if (step == displays.Count - 1)
            {
                // Only one screen and one box left: no need to ask.
                for (int i = 0; i <= n; i++) if (boxOwner[i] == null) boxOwner[i] = displays[step];
                confirming = true;
            }
            Refresh();
        }

        public void Restart() { Reset(); Refresh(); }
        public void Cancel() { Finish(false); }
        public void Accept() { if (confirming) Finish(true); }
    }

    public class WizardForm : Form
    {
        private CalibrationWizard wiz;
        private DisplayInfo me;
        private static readonly Color Back = Color.FromArgb(16, 24, 32);
        private static readonly Color Ink = Color.White;
        private static readonly Color Dim = Color.FromArgb(150, 165, 180);
        private static readonly Color Accent = Color.FromArgb(255, 190, 40);
        private static readonly Color Done = Color.FromArgb(40, 150, 90);

        public WizardForm(CalibrationWizard wiz, DisplayInfo me)
        {
            this.wiz = wiz; this.me = me;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(me.X, me.Y, me.W, me.H);
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Back;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        private Rectangle[] BoxRects()
        {
            int n = wiz.SurroundCount;
            int W = ClientSize.Width, H = ClientSize.Height;
            int areaW = (int)(W * 0.56), top = (int)(H * 0.42);
            int gap = Math.Max(6, W / 160);
            int bw = (areaW - gap * (n - 1)) / n;
            int bh = (int)(H * 0.16);
            int left = (W - areaW) / 2;
            Rectangle[] r = new Rectangle[n + 1];
            for (int i = 0; i < n; i++) r[i] = new Rectangle(left + i * (bw + gap), top, bw, bh);
            int pw = Math.Min(bw, areaW / 3);
            r[n] = new Rectangle((W - pw) / 2, top + bh + gap, pw, bh);
            return r;
        }

        private Rectangle ButtonRect(int index)
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            int bw = (int)(W * 0.24), bh = (int)(H * 0.10), gap = W / 40;
            int left = (W - (2 * bw + gap)) / 2;
            return new Rectangle(left + index * (bw + gap), (int)(H * 0.86), bw, bh);
        }

        private Font F(double fractionOfHeight, bool bold)
        {
            float px = Math.Max(10f, (float)(ClientSize.Height * fractionOfHeight));
            return new Font("Segoe UI", px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private static void Centered(Graphics g, string text, Font f, Color c, Rectangle r)
        {
            using (StringFormat sf = new StringFormat())
            using (Brush b = new SolidBrush(c))
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                g.DrawString(text, f, b, r, sf);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            int W = ClientSize.Width, H = ClientSize.Height, n = wiz.SurroundCount;
            Rectangle[] boxes = BoxRects();

            using (Font small = F(0.035, false))
            using (Font mid = F(0.05, true))
            using (Font huge = F(0.20, true))
            using (Font boxFont = F(0.085, true))
            using (Font hint = F(0.026, false))
            using (Pen outline = new Pen(Dim, Math.Max(2, H / 300)))
            using (Pen strong = new Pen(Accent, Math.Max(4, H / 150)))
            {
                if (!wiz.Confirming)
                {
                    Centered(g, "This screen is number", small, Dim, new Rectangle(0, (int)(H * 0.02), W, (int)(H * 0.05)));
                    Centered(g, me.Number.ToString(), huge, Accent, new Rectangle(0, (int)(H * 0.05), W, (int)(H * 0.24)));
                    Centered(g, "Look around for the screen showing number " + wiz.Current.Number + ".  Touch where that screen is in this picture.",
                        mid, Ink, new Rectangle(W / 20, (int)(H * 0.29), W - W / 10, (int)(H * 0.12)));
                }
                else
                {
                    Centered(g, "This screen should be the", small, Dim, new Rectangle(0, (int)(H * 0.02), W, (int)(H * 0.05)));
                    using (Font role = F(0.13, true))
                        Centered(g, wiz.RoleName(wiz.BoxOf(me)), role, Accent, new Rectangle(0, (int)(H * 0.07), W, (int)(H * 0.20)));
                    Centered(g, "Does every screen show the right name?", mid, Ink, new Rectangle(W / 20, (int)(H * 0.29), W - W / 10, (int)(H * 0.12)));
                }

                for (int i = 0; i <= n; i++)
                {
                    DisplayInfo owner = wiz.OwnerOf(i);
                    bool mine = owner == me;
                    if (owner != null)
                    {
                        using (Brush b = new SolidBrush(Done)) g.FillRectangle(b, boxes[i]);
                        Centered(g, owner.Number.ToString(), boxFont, Ink, boxes[i]);
                    }
                    else
                    {
                        string key = i == n ? "P" : ((char)('A' + i)).ToString();
                        Rectangle hr = boxes[i]; hr.Inflate(-6, -6);
                        using (StringFormat sf = new StringFormat())
                        using (Brush b = new SolidBrush(Dim))
                        {
                            sf.Alignment = StringAlignment.Far; sf.LineAlignment = StringAlignment.Far;
                            g.DrawString("key " + key, hint, b, hr, sf);
                        }
                    }
                    g.DrawRectangle(mine && wiz.Confirming ? strong : outline, boxes[i]);
                }
                Centered(g, n > 1 ? "outside view" : "", hint, Dim, new Rectangle(0, boxes[0].Y - (int)(H * 0.04), W, (int)(H * 0.035)));
                Centered(g, "instrument panel", hint, Dim, new Rectangle(0, boxes[n].Bottom, W, (int)(H * 0.035)));

                string left = wiz.Confirming ? "YES - save" : "Start again";
                string right = wiz.Confirming ? "NO - start again" : "Cancel";
                Rectangle b0 = ButtonRect(0), b1 = ButtonRect(1);
                using (Brush bb = new SolidBrush(wiz.Confirming ? Done : Color.FromArgb(50, 62, 75))) g.FillRectangle(bb, b0);
                using (Brush bb = new SolidBrush(Color.FromArgb(50, 62, 75))) g.FillRectangle(bb, b1);
                Centered(g, left, mid, Ink, b0);
                Centered(g, right, mid, Ink, b1);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (ButtonRect(0).Contains(e.Location)) { if (wiz.Confirming) wiz.Accept(); else wiz.Restart(); return; }
            if (ButtonRect(1).Contains(e.Location)) { if (wiz.Confirming) wiz.Restart(); else wiz.Cancel(); return; }
            Rectangle[] boxes = BoxRects();
            for (int i = 0; i < boxes.Length; i++)
                if (boxes[i].Contains(e.Location)) { wiz.ChooseBox(i); return; }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { wiz.Cancel(); return; }
            if (wiz.Confirming)
            {
                if (e.KeyCode == Keys.Y || e.KeyCode == Keys.Enter) wiz.Accept();
                else if (e.KeyCode == Keys.N) wiz.Restart();
                return;
            }
            if (e.KeyCode == Keys.P) { wiz.ChooseBox(wiz.SurroundCount); return; }
            int idx = e.KeyCode - Keys.A;
            if (idx >= 0 && idx < wiz.SurroundCount) wiz.ChooseBox(idx);
        }
    }
}
