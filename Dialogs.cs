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

    // Touch editor for where the screens are in the room. Each touch on a square steps it
    // through: empty -> outside view -> instrument panel -> empty.
    public class LayoutDialog : Form
    {
        public ScreenLayout Result;
        private const int GW = 8, GH = 4;
        private int[,] grid = new int[GW, GH];   // 0 empty, 1 outside view, 2 instrument panel
        private Label message;
        private Panel board;
        private const string Help = "Touch the squares to match where the screens are in the room.\r\nOne touch = outside view.  Two = instrument panel.  Three = empty.";

        public LayoutDialog(ScreenLayout current, Rectangle area)
        {
            Ui.Prepare(this, "Screen layout", area, 1.15, 0.78);
            float px = area.Height / 26f;
            LoadLayout(current);

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill; t.Padding = new Padding(8);
            t.ColumnCount = 3; t.RowCount = 3;
            for (int c = 0; c < 3; c++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 18));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 66));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 16));

            message = Ui.Label(Help, px * 0.62f);
            t.Controls.Add(message, 0, 0); t.SetColumnSpan(message, 3);

            board = new Panel();
            board.Dock = DockStyle.Fill;
            board.Paint += PaintBoard;
            board.MouseDown += BoardTouched;
            t.Controls.Add(board, 0, 1); t.SetColumnSpan(board, 3);

            Button save = Ui.Button("Save", px, Ui.Go), clear = Ui.Button("Clear", px, Ui.Plain), cancel = Ui.Button("Cancel", px, Ui.Plain);
            save.Click += delegate { Save(); };
            clear.Click += delegate { grid = new int[GW, GH]; message.Text = Help; board.Invalidate(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            t.Controls.Add(save, 0, 2); t.Controls.Add(clear, 1, 2); t.Controls.Add(cancel, 2, 2);
            Controls.Add(t);
        }

        // Places the current layout in the middle of the board.
        private void LoadLayout(ScreenLayout l)
        {
            int oc = Math.Max(0, (GW - l.Cols) / 2), or = Math.Max(0, (GH - l.Rows) / 2);
            for (int i = 0; i <= l.ViewCount; i++)
            {
                int[] c = l.Cell(i);
                int x = c[0] + oc, y = c[1] + or;
                if (x < GW && y < GH) grid[x, y] = i < l.ViewCount ? 1 : 2;
            }
        }

        private ScreenLayout Build()
        {
            ScreenLayout l = new ScreenLayout();
            l.Panel = null;
            for (int y = 0; y < GH; y++)
                for (int x = 0; x < GW; x++)
                {
                    if (grid[x, y] == 1) l.Views.Add(new int[] { x, y });
                    else if (grid[x, y] == 2) l.Panel = new int[] { x, y };
                }
            return l;
        }

        private void Save()
        {
            ScreenLayout l = Build();
            if (l.Views.Count < 1) { message.Text = "Add at least one outside view screen (touch a square once)."; return; }
            if (l.Views.Count > 8) { message.Text = "Condor PreFlight handles up to 8 outside view screens."; return; }
            if (l.Panel == null) { message.Text = "Add the instrument panel screen (touch a square twice)."; return; }
            l.Normalize();
            Result = l;
            DialogResult = DialogResult.OK;
        }

        private Rectangle CellRect(int x, int y)
        {
            int gap = 6;
            int cw = (board.ClientSize.Width - gap * (GW + 1)) / GW, ch = (board.ClientSize.Height - gap * (GH + 1)) / GH;
            return new Rectangle(gap + x * (cw + gap), gap + y * (ch + gap), cw, ch);
        }

        private void BoardTouched(object sender, MouseEventArgs e)
        {
            for (int y = 0; y < GH; y++)
                for (int x = 0; x < GW; x++)
                    if (CellRect(x, y).Contains(e.Location))
                    {
                        int next = (grid[x, y] + 1) % 3;
                        if (next == 2)   // only one instrument panel: it moves here
                            for (int yy = 0; yy < GH; yy++)
                                for (int xx = 0; xx < GW; xx++)
                                    if (grid[xx, yy] == 2) grid[xx, yy] = 0;
                        grid[x, y] = next;
                        message.Text = Help;
                        board.Invalidate();
                        return;
                    }
        }

        private void PaintBoard(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            Rectangle one = CellRect(0, 0);
            using (Pen empty = new Pen(Color.FromArgb(70, 85, 100), 2))
            using (Brush view = new SolidBrush(Ui.Go))
            using (Brush panel = new SolidBrush(Color.FromArgb(190, 120, 20)))
            using (Brush white = new SolidBrush(Color.White))
            using (Font big = new Font("Segoe UI", Math.Max(10f, one.Height * 0.42f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font small = new Font("Segoe UI", Math.Max(9f, one.Height * 0.20f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                int viewNo = 0;
                for (int y = 0; y < GH; y++)
                    for (int x = 0; x < GW; x++)
                    {
                        Rectangle r = CellRect(x, y);
                        if (grid[x, y] == 1) { g.FillRectangle(view, r); g.DrawString((++viewNo).ToString(), big, white, r, sf); }
                        else if (grid[x, y] == 2) { g.FillRectangle(panel, r); g.DrawString("panel", small, white, r, sf); }
                        else g.DrawRectangle(empty, r);
                    }
            }
        }
    }
}
