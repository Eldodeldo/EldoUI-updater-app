// The Extra addons window: find addons in the catalog, see what is installed, and choose what the updater
// keeps up to date. Every change is the player's own click; installing runs through the tray app's checks.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using EldoUI.Updater.Core;

namespace EldoUI.Updater
{
	sealed class AddonsForm : Form
	{
		public event Action<AddonAction, string> ActionRequested;
		public event Action<bool> InstallClicked;
		public event Action<string> ImportChosen;
		public event Action<string> ExportChosen;

		readonly Font _head, _body, _small;
		readonly Label _intro, _note, _message;
		readonly EButton _tabFind, _tabInstalled, _install, _recommended, _import, _export, _close;
		readonly TextBox _search;
		readonly ComboBox _category;
		readonly ECheck _showHidden;
		readonly RowList _list;
		readonly ContextMenuStrip _menu = new ContextMenuStrip { Renderer = new MenuRenderer(), ShowCheckMargin = true };
		AddonsView _view = new AddonsView();
		bool _installedTab;
		bool _rendering;

		public AddonsForm(Icon icon)
		{
			_head = Theme.Head(14f);
			_body = Theme.Body(9.75f);
			_small = Theme.Body(8.5f);
			SuspendLayout();
			AutoScaleDimensions = new SizeF(96f, 96f);
			AutoScaleMode = AutoScaleMode.Dpi;
			Icon = icon;
			FormBorderStyle = FormBorderStyle.FixedSingle;
			MaximizeBox = false;
			MinimizeBox = false;
			StartPosition = FormStartPosition.CenterScreen;
			ClientSize = new Size(680, 620);
			BackColor = Theme.Panel;
			ForeColor = Theme.Text;
			Font = _body;
			KeyPreview = true;
			DoubleBuffered = true;

			_intro = new Label { Location = new Point(22, 50), Size = new Size(636, 38), ForeColor = Theme.Muted, Font = _body };
			Controls.Add(_intro);
			_tabFind = new EButton { Location = new Point(22, 96), Size = new Size(150, 32) };
			_tabInstalled = new EButton { Location = new Point(178, 96), Size = new Size(150, 32) };
			_tabFind.Click += (s, e) => { _installedTab = false; Fill(); };
			_tabInstalled.Click += (s, e) => { _installedTab = true; Fill(); };
			Controls.Add(_tabFind);
			Controls.Add(_tabInstalled);

			_search = new TextBox { Location = new Point(344, 101), Size = new Size(170, 24), BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = _body };
			_search.TextChanged += (s, e) => { if (!_rendering) Fill(); };
			_search.HandleCreated += (s, e) => SearchHint();
			Controls.Add(_search);
			_category = new ComboBox { Location = new Point(522, 100), Size = new Size(136, 26), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Bg, ForeColor = Theme.Text, Font = _body };
			_category.SelectedIndexChanged += (s, e) => { if (!_rendering) Fill(); };
			Controls.Add(_category);
			_showHidden = new ECheck { Location = new Point(344, 100), Size = new Size(314, 26) };
			_showHidden.CheckedChanged += (s, e) => { if (!_rendering) Fill(); };
			Controls.Add(_showHidden);

			_list = new RowList { Location = new Point(22, 138), Size = new Size(636, 370) };
			_list.Toggled += row => Raise(row.Checked == true ? AddonAction.Deselect : AddonAction.Select, row.Key);
			_list.ButtonClicked += row => Choose(new MenuChoice { Action = row.Button.Value, Arg = row.Button == AddonAction.Add ? row.Key.Substring("suggest:".Length) : MenuArg(row, row.Button.Value) });
			_list.MenuRequested += (row, at) => ShowMenu(row, at);
			Controls.Add(_list);

			_message = new Label { Location = new Point(22, 514), Size = new Size(636, 20), ForeColor = Theme.Accent, Font = _small, AutoEllipsis = true };
			Controls.Add(_message);
			_install = new EButton { Primary = true, Location = new Point(22, 538), Size = new Size(170, 34) };
			_recommended = new EButton { Location = new Point(198, 538), Size = new Size(170, 34) };
			_import = new EButton { Location = new Point(374, 538), Size = new Size(96, 34) };
			_export = new EButton { Location = new Point(476, 538), Size = new Size(96, 34) };
			_close = new EButton { Location = new Point(578, 538), Size = new Size(80, 34) };
			_install.Click += (s, e) => InstallClicked?.Invoke(false);
			_recommended.Click += (s, e) => InstallClicked?.Invoke(true);
			_import.Click += (s, e) => Import();
			_export.Click += (s, e) => Export();
			_close.Click += (s, e) => Hide();
			foreach (var b in new[] { _install, _recommended, _import, _export, _close }) Controls.Add(b);
			_note = new Label { Location = new Point(40, 578), Size = new Size(600, 34), ForeColor = Theme.Muted, Font = _small, TextAlign = ContentAlignment.TopCenter };
			Controls.Add(_note);
			CancelButton = _close;
			ResumeLayout(false);
			ApplyTexts();
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			Theme.DarkTitleBar(Handle);
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
			base.OnFormClosing(e);
		}

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);

		/// <summary>The grey hint text in the empty search box.</summary>
		void SearchHint()
		{
			const int EM_SETCUEBANNER = 0x1501;
			if (_search.IsHandleCreated) SendMessage(_search.Handle, EM_SETCUEBANNER, (IntPtr)1, T.S("addons.search"));
		}

		public void ApplyTexts()
		{
			Text = T.S("addons.title");
			SearchHint();
			_intro.Text = T.S("addons.intro");
			_tabFind.Text = T.S("addons.tab.find");
			_tabInstalled.Text = T.S("addons.tab.installed");
			_install.Text = T.S("extras.install_selected");
			_recommended.Text = T.S("extras.install_all");
			_import.Text = T.S("addons.import");
			_export.Text = T.S("addons.export");
			_close.Text = T.S("btn.close");
			_note.Text = T.S("addons.sources");
			Render(_view);
		}

		/// <summary>Shows a fresh view (after a check, a choice or a folder change), keeping the tab, search and category.</summary>
		public void Render(AddonsView view)
		{
			_view = view ?? new AddonsView();
			_rendering = true;
			try
			{
				string chosen = _category.SelectedIndex > 0 ? (string)((Tuple<string, string>)_category.SelectedItem).Item1 : null;
				_category.Items.Clear();
				_category.Items.Add(Tuple.Create<string, string>(null, T.S("addons.category.all")));
				foreach (string c in AddonCatalog.Categories) _category.Items.Add(Tuple.Create(c, AddonRows.Category(c)));
				_category.DisplayMember = "Item2";
				int index = chosen == null ? 0 : _category.Items.Cast<Tuple<string, string>>().ToList().FindIndex(t => t.Item1 == chosen);
				_category.SelectedIndex = Math.Max(0, index);
				_install.Enabled = !_view.Busy && _view.HasFolder && _view.Catalog.Any(r => r.Selected);
				_recommended.Enabled = !_view.Busy && _view.HasFolder;
				_import.Enabled = !_view.Busy;
			}
			finally { _rendering = false; }
			Fill();
		}

		/// <summary>Switches between the catalog and the installed addons.</summary>
		public void ShowInstalled(bool installed)
		{
			_installedTab = installed;
			Fill();
		}

		public void ShowMessage(string text, bool problem = false)
		{
			_message.ForeColor = problem ? Theme.Warn : Theme.Accent;
			_message.Text = text ?? "";
		}

		void Fill()
		{
			_tabFind.Primary = !_installedTab;
			_tabInstalled.Primary = _installedTab;
			_tabFind.Invalidate();
			_tabInstalled.Invalidate();
			_search.Visible = _category.Visible = !_installedTab;
			_showHidden.Visible = _installedTab;
			int hidden = AddonRows.HiddenCount(_view);
			_showHidden.Text = T.F("addons.showhidden", hidden);
			_showHidden.Enabled = hidden > 0;
			List<AddonRow> rows;
			string empty;
			if (_installedTab)
			{
				rows = AddonRows.Installed(_view, _showHidden.Checked);
				empty = !_view.HasFolder ? T.S("addons.nofolder") : T.S("addons.empty");
			}
			else
			{
				string category = _category.SelectedIndex > 0 ? ((Tuple<string, string>)_category.SelectedItem).Item1 : null;
				rows = AddonRows.Find(_view, _search.Text, category);
				empty = T.S("addons.none");
			}
			_list.SetRows(rows, empty, !_view.Busy);
		}

		static string MenuArg(AddonRow row, AddonAction action)
		{
			var m = row.Menu.FirstOrDefault(c => c.Action == action);
			return m != null ? m.Arg : row.Key;
		}

		void ShowMenu(AddonRow row, Point at)
		{
			foreach (ToolStripItem old in _menu.Items.Cast<ToolStripItem>().ToList()) old.Dispose();
			_menu.Items.Clear();
			foreach (var choice in row.Menu)
			{
				var c = choice;
				var item = new ToolStripMenuItem(c.Text) { Checked = c.Checked == true, Enabled = !_view.Busy || c.Action == AddonAction.OpenPage };
				item.Click += (s, e) => Choose(c);
				_menu.Items.Add(item);
			}
			if (_menu.Items.Count > 0) _menu.Show(_list, at);
		}

		void Choose(MenuChoice c)
		{
			if (c.Action == AddonAction.TakeOver)
			{
				var entry = AddonCatalog.Find(c.Arg);
				if (entry == null) return;
				var installed = _view.Installed.FirstOrDefault(i => i.CatalogId == c.Arg);
				string folders = installed != null ? string.Join(", ", installed.Folders) : string.Join(", ", entry.Folders.Take(3));
				string name = entry.Legacy != null ? entry.Legacy.Name : entry.Name;
				if (MessageBox.Show(this, T.F("addons.takeover.body", name, folders), T.F("addons.takeover.title", name), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
			}
			Raise(c.Action, c.Arg);
		}

		void Raise(AddonAction action, string arg)
		{
			if (_view.Busy && action != AddonAction.OpenPage) return;
			ActionRequested?.Invoke(action, arg);
		}

		void Import()
		{
			using (var dlg = new OpenFileDialog { Filter = T.S("addons.file") + " (*.json)|*.json", Title = T.S("addons.import").TrimEnd('…') })
				if (dlg.ShowDialog(this) == DialogResult.OK) ImportChosen?.Invoke(dlg.FileName);
		}

		void Export()
		{
			using (var dlg = new SaveFileDialog { Filter = T.S("addons.file") + " (*.json)|*.json", FileName = "eldoui-addons.json", Title = T.S("addons.export").TrimEnd('…') })
				if (dlg.ShowDialog(this) == DialogResult.OK) ExportChosen?.Invoke(dlg.FileName);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
			float k = DeviceDpi / 96f;
			TextRenderer.DrawText(g, T.S("addons.title"), _head, new Point((int)(22 * k), (int)(16 * k)), Theme.Text, TextFormatFlags.NoPadding);
			Theme.Ornaments(g, ClientRectangle, k);
			base.OnPaint(e);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_menu.Dispose();
				_head.Dispose();
				_body.Dispose();
				_small.Dispose();
			}
			base.Dispose(disposing);
		}
	}

	/// <summary>A scrolling list of addon rows, drawn in EldoUI's look: tick box, name, details, state and a menu button.</summary>
	sealed class RowList : Control
	{
		public event Action<AddonRow> Toggled, ButtonClicked;
		public event Action<AddonRow, Point> MenuRequested;

		const int RowHeight = 62;
		readonly VScrollBar _bar = new VScrollBar { Dock = DockStyle.Right };
		readonly ToolTip _tips = new ToolTip();
		readonly Font _name = Theme.Body(9.75f, FontStyle.Bold), _text = Theme.Body(8.75f), _button = Theme.Body(8.75f, FontStyle.Bold);
		List<AddonRow> _rows = new List<AddonRow>();
		string _empty = "";
		bool _enabled = true;
		int _hover = -1;
		string _tip;

		public RowList()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
			BackColor = Theme.Bg;
			_bar.Scroll += (s, e) => Invalidate();
			Controls.Add(_bar);
		}

		public IReadOnlyList<AddonRow> Rows { get { return _rows; } }

		public void SetRows(List<AddonRow> rows, string empty, bool enabled)
		{
			_rows = rows ?? new List<AddonRow>();
			_empty = empty ?? "";
			_enabled = enabled;
			int visible = Math.Max(1, Height / Scaled(RowHeight));
			_bar.Maximum = Math.Max(0, _rows.Count - 1);
			_bar.LargeChange = visible;
			_bar.Enabled = _rows.Count > visible;
			if (_bar.Value > Math.Max(0, _rows.Count - visible)) _bar.Value = Math.Max(0, _rows.Count - visible);
			Invalidate();
		}

		int Scaled(int v) { return (int)(v * DeviceDpi / 96f); }

		int RowAt(Point p)
		{
			int i = _bar.Value + p.Y / Scaled(RowHeight);
			return p.X < Width - _bar.Width && i >= 0 && i < _rows.Count ? i : -1;
		}

		Rectangle RowRect(int i) { return new Rectangle(0, (i - _bar.Value) * Scaled(RowHeight), Width - _bar.Width, Scaled(RowHeight)); }
		Rectangle BoxRect(Rectangle r) { return new Rectangle(r.Left + Scaled(12), r.Top + Scaled(12), Scaled(16), Scaled(16)); }
		Rectangle MenuRect(Rectangle r) { return new Rectangle(r.Right - Scaled(40), r.Top + Scaled(16), Scaled(30), Scaled(30)); }
		Rectangle ButtonRect(Rectangle r) { return new Rectangle(r.Right - Scaled(150), r.Top + Scaled(30), Scaled(104), Scaled(24)); }

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			if (_bar.Enabled) _bar.Value = Math.Max(0, Math.Min(_bar.Maximum - _bar.LargeChange + 1, _bar.Value - Math.Sign(e.Delta) * 2));
			Invalidate();
			base.OnMouseWheel(e);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int i = RowAt(e.Location);
			if (i != _hover) { _hover = i; Invalidate(); }
			string tip = i >= 0 ? _rows[i].Tip : null;
			if (tip != _tip) { _tip = tip; _tips.SetToolTip(this, tip ?? ""); }
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

		protected override void OnMouseClick(MouseEventArgs e)
		{
			base.OnMouseClick(e);
			Focus();
			int i = RowAt(e.Location);
			if (i < 0) return;
			var row = _rows[i];
			var r = RowRect(i);
			if (row.Menu.Count > 0 && (MenuRect(r).Contains(e.Location) || e.Button == MouseButtons.Right)) { MenuRequested?.Invoke(row, new Point(MenuRect(r).Left, MenuRect(r).Bottom)); return; }
			if (!_enabled) return;
			if (row.Button != null && ButtonRect(r).Contains(e.Location)) { ButtonClicked?.Invoke(row); return; }
			if (row.Checked != null && e.X < r.Left + Scaled(260)) Toggled?.Invoke(row);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Down && _bar.Enabled) { _bar.Value = Math.Min(_bar.Maximum - _bar.LargeChange + 1, _bar.Value + 1); Invalidate(); }
			if (e.KeyCode == Keys.Up && _bar.Value > 0) { _bar.Value--; Invalidate(); }
			base.OnKeyDown(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(Theme.Bg);
			g.SmoothingMode = SmoothingMode.AntiAlias;
			using (var edge = new Pen(Theme.Edge))
				g.DrawRectangle(edge, 0, 0, Width - _bar.Width - 1, Height - 1);
			if (_rows.Count == 0)
			{
				TextRenderer.DrawText(g, _empty, _text, new Rectangle(0, 0, Width - _bar.Width, Scaled(80)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
				return;
			}
			for (int i = _bar.Value; i < _rows.Count; i++)
			{
				var r = RowRect(i);
				if (r.Top > Height) break;
				var row = _rows[i];
				if (i == _hover)
					using (var hover = new SolidBrush(Theme.Panel2)) g.FillRectangle(hover, r.Left + 1, r.Top, r.Width - 2, r.Height);
				using (var line = new Pen(Theme.Edge)) g.DrawLine(line, r.Left + Scaled(10), r.Bottom - 1, r.Right - Scaled(10), r.Bottom - 1);
				int x = r.Left + Scaled(12);
				if (row.Checked != null)
				{
					var box = BoxRect(r);
					using (var path = Theme.Round(box, 3))
					{
						using (var fill = new SolidBrush(row.Checked == true ? Theme.AccentSoft : Theme.Panel)) g.FillPath(fill, path);
						using (var pen = new Pen(row.Checked == true ? Theme.Accent : Theme.EdgeStrong, 1.2f)) g.DrawPath(pen, path);
					}
					if (row.Checked == true)
						using (var pen = new Pen(Theme.Accent, 2f))
							g.DrawLines(pen, new[] { new PointF(box.Left + box.Width * 0.22f, box.Top + box.Height * 0.52f), new PointF(box.Left + box.Width * 0.43f, box.Top + box.Height * 0.74f), new PointF(box.Left + box.Width * 0.8f, box.Top + box.Height * 0.28f) });
					x = box.Right + Scaled(10);
				}
				int right = r.Right - Scaled(row.Menu.Count > 0 ? 48 : 12);
				var flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
				int nameWidth = Math.Min(TextRenderer.MeasureText(g, row.Title, _name, Size.Empty, flags).Width, right - x - Scaled(160));
				TextRenderer.DrawText(g, row.Title, _name, new Rectangle(x, r.Top + Scaled(10), nameWidth, Scaled(18)), row.Tone == Tone.Info && row.Checked == null ? Theme.Accent : Theme.Text, flags);
				if (!string.IsNullOrEmpty(row.Meta))
					TextRenderer.DrawText(g, "  " + row.Meta, _text, new Rectangle(x + nameWidth, r.Top + Scaled(11), Math.Max(0, right - x - nameWidth - Scaled(150)), Scaled(18)), Theme.Muted, flags);
				int detailWidth = right - x - (row.Button != null ? Scaled(150) : 0);
				int noteWidth = 0;
				if (!string.IsNullOrEmpty(row.Note))
				{
					noteWidth = Math.Min(TextRenderer.MeasureText(g, row.Note, _text, Size.Empty, flags).Width + Scaled(10), detailWidth);
					TextRenderer.DrawText(g, row.Note, _text, new Rectangle(x, r.Top + Scaled(32), noteWidth, Scaled(18)), Theme.Gold, flags);
				}
				TextRenderer.DrawText(g, row.Detail ?? "", _text, new Rectangle(x + noteWidth, r.Top + Scaled(32), Math.Max(0, detailWidth - noteWidth), Scaled(18)), Theme.Muted, flags);
				if (!string.IsNullOrEmpty(row.Status))
				{
					Color tone = row.Tone == Tone.Ok ? Theme.Ok : row.Tone == Tone.Warn ? Theme.Warn : row.Tone == Tone.Error ? Theme.Err : row.Tone == Tone.Info ? Theme.Accent : Theme.Muted;
					TextRenderer.DrawText(g, row.Status, _text, new Rectangle(right - Scaled(220), r.Top + Scaled(11), Scaled(214), Scaled(18)), tone, flags | TextFormatFlags.Right);
				}
				if (row.Button != null)
				{
					var b = ButtonRect(r);
					using (var path = Theme.Round(b, 6))
					using (var pen = new Pen(_enabled ? Theme.Accent : Theme.Edge))
						g.DrawPath(pen, path);
					TextRenderer.DrawText(g, row.ButtonText, _button, b, _enabled ? Theme.Accent : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
				}
				if (row.Menu.Count > 0)
				{
					var m = MenuRect(r);
					using (var dots = new SolidBrush(Theme.Muted))
						for (int d = -1; d <= 1; d++) g.FillEllipse(dots, m.Left + m.Width / 2 - Scaled(2) + d * Scaled(7), m.Top + m.Height / 2 - Scaled(2), Scaled(4), Scaled(4));
				}
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing) { _tips.Dispose(); _name.Dispose(); _text.Dispose(); _button.Dispose(); }
			base.Dispose(disposing);
		}
	}
}
