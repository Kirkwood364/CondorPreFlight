using System;
using System.Drawing;
using System.Windows.Forms;

namespace CondorPreFlight
{
    internal static class Ui
    {
        public static readonly Color Back = Color.FromArgb(16, 24, 32);
        public static readonly Color Plain = Color.FromArgb(50, 62, 75);
        public static readonly Color Go = Color.FromArgb(40, 150, 90);

        public static Button Button(string text, float px, Color back)
        {
            Button b = new Button();
            b.Text = text; b.Dock = DockStyle.Fill; b.Margin = new Padding(4);
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.BackColor = back; b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
            return b;
        }

        public static Label Label(string text, float px)
        {
            Label l = new Label();
            l.Text = text; l.Dock = DockStyle.Fill; l.ForeColor = Color.White;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
            return l;
        }

        public static void Prepare(Form f, string title, Rectangle area, double wFrac, double hFrac)
        {
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false;
            f.TopMost = true;
            f.BackColor = Back;
            f.StartPosition = FormStartPosition.Manual;
            int h = (int)(area.Height * hFrac), w = (int)(area.Height * wFrac);
            f.Bounds = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        }
    }

    // On-screen number pad, so a PIN can be typed on the touch screen.
    public class PinDialog : Form
    {
        public string Pin = "";
        private Label shown;

        public PinDialog(string title, Rectangle area)
        {
            Ui.Prepare(this, title, area, 0.45, 0.62);
            float px = area.Height / 22f;
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill; t.Padding = new Padding(8);
            t.ColumnCount = 3; t.RowCount = 6;
            for (int c = 0; c < 3; c++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            for (int r = 0; r < 6; r++) t.RowStyles.Add(new RowStyle(SizeType.Percent, 16.66f));

            shown = Ui.Label("", px * 1.2f);
            t.Controls.Add(shown, 0, 0); t.SetColumnSpan(shown, 3);
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "Clear", "0", "OK" };
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                Button b = Ui.Button(k, px, k == "OK" ? Ui.Go : Ui.Plain);
                b.Click += delegate { Press(k); };
                t.Controls.Add(b, i % 3, 1 + i / 3);
            }
            Button cancel = Ui.Button("Cancel", px * 0.8f, Ui.Plain);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            t.Controls.Add(cancel, 0, 5); t.SetColumnSpan(cancel, 3);
            Controls.Add(t);
            KeyPreview = true;
            KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                if (char.IsDigit(e.KeyChar)) Press(e.KeyChar.ToString());
                else if (e.KeyChar == (char)Keys.Enter) Press("OK");
                else if (e.KeyChar == (char)Keys.Back) Press("Clear");
                else if (e.KeyChar == (char)Keys.Escape) DialogResult = DialogResult.Cancel;
            };
        }

        private void Press(string k)
        {
            if (k == "OK") { DialogResult = DialogResult.OK; return; }
            if (k == "Clear") Pin = "";
            else if (Pin.Length < 8) Pin += k;
            shown.Text = new string('*', Pin.Length);
        }
    }

    // Lets a club with a different number of outside-view screens set that up by touch.
    public class LayoutDialog : Form
    {
        public int Count;
        private Label number;
        private Panel preview;

        public LayoutDialog(int current, Rectangle area)
        {
            Count = current;
            Ui.Prepare(this, "Screen layout", area, 0.80, 0.62);
            float px = area.Height / 24f;
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill; t.Padding = new Padding(8);
            t.ColumnCount = 3; t.RowCount = 4;
            for (int c = 0; c < 3; c++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 14));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 20));

            Label q = Ui.Label("How many screens show the outside view?", px * 0.8f);
            t.Controls.Add(q, 0, 0); t.SetColumnSpan(q, 3);
            Button minus = Ui.Button("-", px * 1.6f, Ui.Plain), plus = Ui.Button("+", px * 1.6f, Ui.Plain);
            number = Ui.Label("", px * 1.8f);
            minus.Click += delegate { Change(-1); };
            plus.Click += delegate { Change(1); };
            t.Controls.Add(minus, 0, 1); t.Controls.Add(number, 1, 1); t.Controls.Add(plus, 2, 1);

            preview = new Panel();
            preview.Dock = DockStyle.Fill;
            preview.Paint += PaintPreview;
            t.Controls.Add(preview, 0, 2); t.SetColumnSpan(preview, 3);

            Button save = Ui.Button("Save", px, Ui.Go), cancel = Ui.Button("Cancel", px, Ui.Plain);
            save.Click += delegate { DialogResult = DialogResult.OK; };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            t.Controls.Add(save, 0, 3); t.SetColumnSpan(save, 2);
            t.Controls.Add(cancel, 2, 3);
            Controls.Add(t);
            Change(0);
        }

        private void Change(int by)
        {
            Count = Math.Max(1, Math.Min(8, Count + by));
            number.Text = Count.ToString();
            preview.Invalidate();
        }

        // Same picture the calibration wizard will show.
        private void PaintPreview(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int W = preview.ClientSize.Width, H = preview.ClientSize.Height, gap = 6;
            int bw = Math.Min((W - gap * (Count + 1)) / Count, W / 4), bh = (H - 3 * gap) / 2;
            int left = (W - (Count * bw + (Count - 1) * gap)) / 2;
            using (Pen p = new Pen(Color.FromArgb(150, 165, 180), 2))
            using (Font f = new Font("Segoe UI", Math.Max(9f, bh * 0.22f), FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush ink = new SolidBrush(Color.FromArgb(150, 165, 180)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                for (int i = 0; i < Count; i++) g.DrawRectangle(p, left + i * (bw + gap), gap, bw, bh);
                Rectangle panel = new Rectangle((W - bw) / 2, 2 * gap + bh, bw, bh);
                g.DrawRectangle(p, panel);
                g.DrawString("panel", f, ink, panel, sf);
            }
        }
    }
}
