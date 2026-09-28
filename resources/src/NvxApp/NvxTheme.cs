using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

static class NvxTheme
{
    public static readonly Color Ink = Color.FromArgb(38, 35, 35);
    public static readonly Color Panel = Color.FromArgb(48, 45, 45);
    public static readonly Color Card = Color.FromArgb(53, 49, 49);
    public static readonly Color Line = Color.FromArgb(75, 70, 70);
    public static readonly Color Text = Color.FromArgb(247, 245, 245);
    public static readonly Color Muted = Color.FromArgb(184, 178, 178);
    public static readonly Color Green = Color.FromArgb(124, 224, 143);
    public static readonly Color GreenInk = Color.FromArgb(23, 35, 26);
    public static readonly Color Red = Color.FromArgb(224, 122, 104);

    public static Font UiFont = new Font("Segoe UI", 10f);
    public static Font TitleFont = new Font("Segoe UI", 20f, FontStyle.Bold);
    public static Font MonoFont = new Font("Consolas", 10f);

    public static Button Button(string text)
    {
        Button button = new Button();
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(12, 6, 12, 6);
        button.FlatStyle = FlatStyle.Flat;
        button.Font = UiFont;
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 8, 0);
        button.BackColor = Panel;
        button.ForeColor = Text;
        button.FlatAppearance.BorderColor = Line;
        return button;
    }

    public static void MarkActive(PowerButton button, bool active)
    {
        button.Active = active;
        button.AccessibleName = active ? "On" : "Off";
        button.Invalidate();
    }

    public static TextBox Box()
    {
        TextBox box = new TextBox();
        box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = Ink;
        box.ForeColor = Text;
        box.Font = UiFont;
        return box;
    }

    public static Label Mute(string text)
    {
        Label label = new Label();
        label.Text = text;
        label.ForeColor = Muted;
        label.AutoSize = true;
        label.Font = UiFont;
        return label;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Panel;
        grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.GridColor = Line;
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Ink;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.Font = UiFont;
        grid.DefaultCellStyle.BackColor = Panel;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(58, 64, 40);
        grid.DefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.Font = UiFont;
    }
}

sealed class PowerButton : Control
{
    public bool Active;

    public PowerButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(92, 36);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleName = "Off";
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Active ? NvxTheme.Green : NvxTheme.Card);
        int diameter = 22;
        int x = 10;
        int y = (Height - diameter) / 2;
        if (Active)
        {
            using (Pen pen = new Pen(Color.White, 2.8f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new Point[] {
                    new Point(x + 3, y + 11),
                    new Point(x + 9, y + 17),
                    new Point(x + 20, y + 4)
                });
            }
        }
        else
        {
            using (Pen power = new Pen(NvxTheme.Muted, 2.4f))
            {
                power.StartCap = LineCap.Round;
                power.EndCap = LineCap.Round;
                g.DrawArc(power, x, y, diameter - 1, diameter - 1, 45, 270);
                g.DrawLine(power, x + diameter / 2, y - 1, x + diameter / 2, y + diameter / 2 + 1);
            }
        }
        using (SolidBrush text = new SolidBrush(Active ? NvxTheme.GreenInk : NvxTheme.Muted))
        using (Font font = new Font("Segoe UI", 10f, FontStyle.Bold))
        {
            g.DrawString(Active ? "On" : "Off", font, text, 40, 8);
        }
    }
}

