// EldoUI's look, as on the website: one dark flat panel with a thin edge, the accent only where
// something is selected or a link, gold for the one main action, and small-caps style headings.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EldoUI.Updater
{
	static class Theme
	{
		public static readonly Color Bg = Hex("#060b16");
		public static readonly Color Glow = Hex("#0e1d3a");
		public static readonly Color Panel = Hex("#0a1426");
		public static readonly Color Panel2 = Hex("#0e1b33");
		public static readonly Color Edge = Hex("#1b2d4d");
		public static readonly Color EdgeStrong = Hex("#2a4470");
		public static readonly Color Text = Hex("#e6ecf6");
		public static readonly Color Muted = Hex("#93a3c0");
		public static readonly Color Accent = Hex("#4fa8ee");
		public static readonly Color AccentSoft = Color.FromArgb(36, 79, 168, 238);
		public static readonly Color Gold = Hex("#f0c46c");
		public static readonly Color Gold2 = Hex("#d9a443");
		public static readonly Color GoldInk = Hex("#2a1a02");
		public static readonly Color Ornament = Hex("#b8925a");
		public static readonly Color Ok = Hex("#4cc38a");
		public static readonly Color Warn = Hex("#e5a54b");
		public static readonly Color Err = Hex("#ec6a6a");

		public static Font Body(float size = 9.75f, FontStyle style = FontStyle.Regular) { return Pick(new[] { "Segoe UI Variable Text", "Segoe UI" }, size, style); }
		public static Font Head(float size = 15f) { return Pick(new[] { "Alegreya SC", "Cinzel", "Georgia" }, size, FontStyle.Bold); }

		static Font Pick(string[] families, float size, FontStyle style)
		{
			foreach (string f in families)
			{
				using (var probe = new Font(f, size, style))
					if (string.Equals(probe.Name, f, StringComparison.OrdinalIgnoreCase)) return new Font(f, size, style);
			}
			return new Font(FontFamily.GenericSansSerif, size, style);
		}

		static Color Hex(string hex)
		{
			return ColorTranslator.FromHtml(hex);
		}

		public static GraphicsPath Round(RectangleF r, float radius)
		{
			var p = new GraphicsPath();
			float d = radius * 2;
			p.AddArc(r.X, r.Y, d, d, 180, 90);
			p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
			p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
			p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
			p.CloseFigure();
			return p;
		}

		/// <summary>The two corner ornaments at the bottom of an EldoUI panel.</summary>
		public static void Ornaments(Graphics g, Rectangle r, float scale)
		{
			float s = 18 * scale, inset = 8 * scale, rad = 5 * scale;
			using (var pen = new Pen(Color.FromArgb(200, Ornament), Math.Max(1f, scale)))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				float l = r.Left + inset, b = r.Bottom - inset, rt = r.Right - inset;
				g.DrawLine(pen, l, b - s, l, b - rad);
				g.DrawArc(pen, l, b - 2 * rad, 2 * rad, 2 * rad, 90, 90);
				g.DrawLine(pen, l + rad, b, l + s, b);
				g.DrawLine(pen, rt, b - s, rt, b - rad);
				g.DrawArc(pen, rt - 2 * rad, b - 2 * rad, 2 * rad, 2 * rad, 0, 90);
				g.DrawLine(pen, rt - rad, b, rt - s, b);
			}
		}

		/// <summary>Dark title bar, and on Windows 11 a title bar and border in EldoUI's colors.</summary>
		public static void DarkTitleBar(IntPtr hwnd)
		{
			try
			{
				int on = 1;
				DwmSetWindowAttribute(hwnd, 20, ref on, 4);           // DWMWA_USE_IMMERSIVE_DARK_MODE
				int caption = ToColorRef(Panel), border = ToColorRef(EdgeStrong), text = ToColorRef(Text);
				DwmSetWindowAttribute(hwnd, 35, ref caption, 4);      // DWMWA_CAPTION_COLOR
				DwmSetWindowAttribute(hwnd, 34, ref border, 4);       // DWMWA_BORDER_COLOR
				DwmSetWindowAttribute(hwnd, 36, ref text, 4);         // DWMWA_TEXT_COLOR
			}
			catch
			{
				// Older Windows: the normal title bar is fine.
			}
		}

		static int ToColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

		[DllImport("dwmapi.dll")]
		static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
	}

	/// <summary>The tray menu in EldoUI's colors.</summary>
	sealed class MenuRenderer : ToolStripProfessionalRenderer
	{
		public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

		protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
		{
			e.TextColor = !e.Item.Enabled ? Theme.Muted
				: e.Item.Tag as string == "head" ? Theme.Gold
				: e.Item.Selected ? Theme.Text : Theme.Text;
			base.OnRenderItemText(e);
		}

		protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
		{
			if (!e.Item.Selected || !e.Item.Enabled) return;
			var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using (var path = Theme.Round(r, 4))
			using (var fill = new SolidBrush(Theme.AccentSoft))
			using (var pen = new Pen(Color.FromArgb(90, Theme.Accent)))
			{
				e.Graphics.FillPath(fill, path);
				e.Graphics.DrawPath(pen, path);
			}
		}

		protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
		{
			var r = e.ImageRectangle;
			r.Inflate(-1, -1);
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using (var pen = new Pen(Theme.Accent, 2f))
			{
				e.Graphics.DrawLines(pen, new[]
				{
					new PointF(r.Left + r.Width * 0.18f, r.Top + r.Height * 0.52f),
					new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.76f),
					new PointF(r.Left + r.Width * 0.84f, r.Top + r.Height * 0.26f),
				});
			}
		}

		protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
		{
			e.ArrowColor = Theme.Muted;
			base.OnRenderArrow(e);
		}

		sealed class MenuColors : ProfessionalColorTable
		{
			public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
			public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
			public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
			public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
			public override Color MenuBorder { get { return Theme.EdgeStrong; } }
			public override Color MenuItemBorder { get { return Theme.Accent; } }
			public override Color MenuItemSelected { get { return Theme.AccentSoft; } }
			public override Color SeparatorDark { get { return Theme.Edge; } }
			public override Color SeparatorLight { get { return Theme.Panel; } }
			public override Color CheckBackground { get { return Theme.Panel; } }
			public override Color CheckSelectedBackground { get { return Theme.Panel2; } }
			public override Color CheckPressedBackground { get { return Theme.Panel2; } }
		}
	}

	/// <summary>A flat EldoUI button: gold for the one main action, a quiet edge for the rest.</summary>
	sealed class EButton : Button
	{
		public bool Primary;
		bool _hover, _down;

		public EButton()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
			FlatStyle = FlatStyle.Flat;
			Cursor = Cursors.Hand;
			Font = Theme.Body(9.75f, FontStyle.Bold);
			Height = 34;
		}

		protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
		protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
		protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
			var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
			using (var path = Theme.Round(r, 7))
			{
				if (Primary)
				{
					Color top = !Enabled ? Color.FromArgb(120, Theme.Gold2) : _down ? Theme.Gold2 : _hover ? Color.FromArgb(255, 247, 214, 140) : Theme.Gold;
					using (var fill = new LinearGradientBrush(r, top, Enabled ? Theme.Gold2 : Color.FromArgb(120, Theme.Gold2), LinearGradientMode.Vertical))
						g.FillPath(fill, path);
				}
				else
				{
					using (var fill = new SolidBrush(_down ? Theme.Panel : _hover ? Theme.Panel2 : Theme.Panel))
						g.FillPath(fill, path);
					using (var pen = new Pen(_hover && Enabled ? Theme.EdgeStrong : Theme.Edge))
						g.DrawPath(pen, path);
				}
				if (Focused && ShowFocusCues)
					using (var pen = new Pen(Theme.Accent, 1.5f))
						g.DrawPath(pen, path);
			}
			Color ink = Primary ? Theme.GoldInk : Enabled ? Theme.Text : Theme.Muted;
			TextRenderer.DrawText(g, Text, Font, ClientRectangle, ink,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
		}
	}

	/// <summary>An EldoUI check box: a small rounded box, the accent tick when on.</summary>
	sealed class ECheck : CheckBox
	{
		public ECheck()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
			AutoSize = false;
			Height = 26;
			Cursor = Cursors.Hand;
			ForeColor = Theme.Text;
			Font = Theme.Body();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
			g.SmoothingMode = SmoothingMode.AntiAlias;
			float box = Height * 0.62f, y = (Height - box) / 2f;
			var r = new RectangleF(1, y, box, box);
			using (var path = Theme.Round(r, 4))
			{
				using (var fill = new SolidBrush(Checked ? Theme.AccentSoft : Theme.Bg))
					g.FillPath(fill, path);
				using (var pen = new Pen(Checked ? Theme.Accent : Theme.EdgeStrong, 1.2f))
					g.DrawPath(pen, path);
			}
			if (Checked)
				using (var pen = new Pen(Theme.Accent, 2f))
					g.DrawLines(pen, new[]
					{
						new PointF(r.Left + box * 0.22f, r.Top + box * 0.52f),
						new PointF(r.Left + box * 0.43f, r.Top + box * 0.74f),
						new PointF(r.Left + box * 0.80f, r.Top + box * 0.28f),
					});
			var text = new Rectangle((int)(box + 9), 0, Width - (int)(box + 9), Height);
			TextRenderer.DrawText(g, Text, Font, text, Enabled ? Theme.Text : Theme.Muted,
				TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
			if (Focused && ShowFocusCues)
				using (var pen = new Pen(Theme.Accent))
					g.DrawRectangle(pen, text.X - 2, 2, Math.Min(text.Width, TextRenderer.MeasureText(Text, Font).Width + 4), Height - 5);
		}
	}
}
