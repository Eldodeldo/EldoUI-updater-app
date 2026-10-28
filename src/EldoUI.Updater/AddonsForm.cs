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
		/// <summary>The player confirmed uninstalling these Installed rows (their keys); true: delete their saved settings too.</summary>
		public event Action<List<string>, bool> UninstallRequested;
		/// <summary>The player resized the window or a column; the layout object holds the new sizes.</summary>
		public event Action LayoutChanged;

		readonly Font _head, _body, _small;
		readonly Label _intro, _note, _message;
		readonly EButton _tabFind, _tabInstalled, _install, _recommended, _import, _export, _close, _uninstall;
		readonly TextBox _search;
		readonly ComboBox _category;
		readonly ECheck _showHidden;
		readonly RowList _list;
		readonly ContextMenuStrip _menu = new ContextMenuStrip { Renderer = new MenuRenderer(), ShowCheckMargin = true };
		AddonsView _view = new AddonsView();
		/// <summary>The Installed rows ticked to uninstall together.</summary>
		readonly HashSet<string> _marked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		bool _installedTab;
		bool _rendering;
		WindowLayout _layout = new WindowLayout();
		int _sortColumn = -1;
		bool _sortDescending;

		public AddonsForm(Icon icon)
		{
			_head = Theme.Head(14f);
			_body = Theme.Body(9.75f);
			_small = Theme.Body(8.5f);
			SuspendLayout();
			AutoScaleDimensions = new SizeF(96f, 96f);
			AutoScaleMode = AutoScaleMode.Dpi;
			Icon = icon;
			FormBorderStyle = FormBorderStyle.Sizable;
			StartPosition = FormStartPosition.CenterScreen;
			ClientSize = new Size(DefaultWidth, DefaultHeight);
			BackColor = Theme.Panel;
			ForeColor = Theme.Text;
			Font = _body;
			KeyPreview = true;
			DoubleBuffered = true;

			_intro = new Label { Location = new Point(22, 50), Size = new Size(636, 38), ForeColor = Theme.Muted, Font = _body };
			Controls.Add(_intro);
			_tabFind = new EButton { Location = new Point(22, 96), Size = new Size(150, 32) };
			_tabInstalled = new EButton { Location = new Point(178, 96), Size = new Size(150, 32) };
			_tabFind.Click += (s, e) => ShowInstalled(false);
			_tabInstalled.Click += (s, e) => ShowInstalled(true);
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
			_uninstall = new EButton { Location = new Point(488, 96), Size = new Size(170, 32), Visible = false };
			_uninstall.Click += (s, e) => ConfirmUninstall(_marked.ToList());
			Controls.Add(_uninstall);

			_list = new RowList { Location = new Point(22, 138), Size = new Size(636, 370) };
			_list.ColumnsChanged += shares => { _layout.Columns[ListKey] = shares.ToList(); LayoutChanged?.Invoke(); };
			_list.HeaderClicked += column =>
			{
				if (_sortColumn == column) _sortDescending = !_sortDescending;
				else { _sortColumn = column; _sortDescending = false; }
				Fill();
			};
			_list.Toggled += row =>
			{
				if (!_installedTab) { Raise(row.Checked == true ? AddonAction.Deselect : AddonAction.Select, row.Key); return; }
				if (_view.Busy) return;
				if (!_marked.Remove(row.Key)) _marked.Add(row.Key);
				Fill();
			};
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
			_note = new Label { Location = new Point(22, 578), Size = new Size(636, 38), ForeColor = Theme.Muted, Font = _small, TextAlign = ContentAlignment.TopCenter };
			Controls.Add(_note);
			CancelButton = _close;
			ResumeLayout(false);
			ApplyTexts();
		}

		/// <summary>The window's inner size at first start, and the smallest it can be made, in logical pixels.</summary>
		const int DefaultWidth = 920, DefaultHeight = 680, MinWidth = 720, MinHeight = 540;

		int S(int v) { return (int)Math.Round(v * DeviceDpi / 96f); }

		string ListKey { get { return _installedTab ? "installed" : "find"; } }

		/// <summary>Uses the size and column widths the player left the window at (kept in the settings).</summary>
		public void UseLayout(WindowLayout layout)
		{
			_layout = layout ?? new WindowLayout();
			if (_layout.Width > 0 && _layout.Height > 0)
			{
				var area = Screen.FromControl(this).WorkingArea;
				ClientSize = new Size(Math.Min(S(Math.Max(MinWidth, _layout.Width)), area.Width), Math.Min(S(Math.Max(MinHeight, _layout.Height)), area.Height));
			}
			Fill();
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			Theme.DarkTitleBar(Handle);
			MinimumSize = SizeFromClientSize(new Size(S(MinWidth), S(MinHeight)));
		}

		protected override void OnDpiChanged(DpiChangedEventArgs e)
		{
			base.OnDpiChanged(e);
			MinimumSize = SizeFromClientSize(new Size(S(MinWidth), S(MinHeight)));
		}

		protected override void OnResizeEnd(EventArgs e)
		{
			base.OnResizeEnd(e);
			if (WindowState != FormWindowState.Normal) return;
			int w = (int)Math.Round(ClientSize.Width * 96f / DeviceDpi), h = (int)Math.Round(ClientSize.Height * 96f / DeviceDpi);
			if (w == _layout.Width && h == _layout.Height) return;
			_layout.Width = w;
			_layout.Height = h;
			LayoutChanged?.Invoke();
		}

		/// <summary>Places every part from the window's current size, so the list and its columns grow with it.</summary>
		protected override void OnLayout(LayoutEventArgs e)
		{
			base.OnLayout(e);
			if (_list == null) return;
			int w = ClientSize.Width, h = ClientSize.Height, m = S(22);
			_intro.SetBounds(m, S(50), w - 2 * m, S(40));
			_tabFind.SetBounds(m, S(96), S(150), S(32));
			_tabInstalled.SetBounds(m + S(156), S(96), S(150), S(32));
			int categoryWidth = S(170), searchWidth = Math.Max(S(150), Math.Min(S(260), w - 2 * m - S(322) - categoryWidth - S(8)));
			_category.SetBounds(w - m - categoryWidth, S(100), categoryWidth, S(26));
			_search.SetBounds(_category.Left - S(8) - searchWidth, S(101), searchWidth, S(24));
			_uninstall.SetBounds(w - m - categoryWidth, S(96), categoryWidth, S(32));
			_showHidden.SetBounds(m + S(322), S(100), w - 2 * m - S(322) - categoryWidth - S(8), S(26));
			_list.SetBounds(m, S(138), w - 2 * m, Math.Max(S(120), h - S(138) - S(112)));
			_message.SetBounds(m, h - S(106), w - 2 * m, S(20));
			int y = h - S(82);
			_install.SetBounds(m, y, S(170), S(34));
			_recommended.SetBounds(m + S(176), y, S(170), S(34));
			_import.SetBounds(m + S(352), y, S(96), S(34));
			_export.SetBounds(m + S(454), y, S(96), S(34));
			_close.SetBounds(w - m - S(80), y, S(80), S(34));
			_note.SetBounds(m, h - S(42), w - 2 * m, S(38));
			Invalidate();
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
			if (_installedTab != installed) { _sortColumn = -1; _sortDescending = false; }
			_installedTab = installed;
			Fill();
		}

		/// <summary>Ticks Installed rows to uninstall together (the self-test and the window pictures use it).</summary>
		public void Mark(params string[] keys)
		{
			foreach (string k in keys) _marked.Add(k);
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
			_showHidden.Visible = _uninstall.Visible = _installedTab;
			int hidden = AddonRows.HiddenCount(_view);
			_showHidden.Text = T.F("addons.showhidden", hidden);
			_showHidden.Enabled = hidden > 0;
			List<AddonRow> rows;
			string empty;
			if (_installedTab)
			{
				rows = AddonRows.Installed(_view, _showHidden.Checked, _marked);
				// A ticked row that is gone (uninstalled, hidden or moved) is no longer ticked.
				_marked.IntersectWith(rows.Where(r => r.Checked != null).Select(r => r.Key));
				empty = !_view.HasFolder ? T.S("addons.nofolder") : T.S("addons.empty");
			}
			else
			{
				string category = _category.SelectedIndex > 0 ? ((Tuple<string, string>)_category.SelectedItem).Item1 : null;
				rows = AddonRows.Find(_view, _search.Text, category);
				empty = T.S("addons.none");
			}
			_uninstall.Text = T.F("addons.uninstall.selected", _marked.Count);
			_uninstall.Enabled = !_view.Busy && _marked.Count > 0;
			_uninstall.Invalidate();
			if (_sortColumn >= 0) rows = AddonRows.Sort(rows, _sortColumn, _sortDescending);
			_list.SetColumns(AddonRows.Headers(_installedTab), ColumnLayout.Shares(_layout, ListKey), _sortColumn, _sortDescending);
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
				var item = new ToolStripMenuItem(c.Text) { Checked = c.Checked == true, Enabled = !_view.Busy || c.Action == AddonAction.OpenPage || c.Action == AddonAction.OpenFolder };
				item.Click += (s, e) => Choose(c);
				_menu.Items.Add(item);
			}
			if (_menu.Items.Count > 0) _menu.Show(_list, at);
		}

		void Choose(MenuChoice c)
		{
			if (c.Action == AddonAction.Uninstall) { ConfirmUninstall(new List<string> { c.Arg }); return; }
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
			if (_view.Busy && action != AddonAction.OpenPage && action != AddonAction.OpenFolder) return;
			ActionRequested?.Invoke(action, arg);
		}

		/// <summary>Asks before anything is uninstalled; nothing happens unless the player says yes.</summary>
		void ConfirmUninstall(List<string> keys)
		{
			if (_view.Busy || keys.Count == 0) return;
			var plan = AddonRemover.Plan(_view.AddOnsDir, _view.Installed, keys);
			if (plan == null) return;
			bool settings;
			using (var dialog = new UninstallDialog(plan, Icon))
			{
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				settings = dialog.DeleteSettings;
			}
			_marked.ExceptWith(keys);
			UninstallRequested?.Invoke(keys, settings);
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

	/// <summary>
	/// The question before an uninstall: what is deleted, what stops loading, and a box (off) for the saved
	/// settings. Cancel has the focus, so Enter alone never deletes anything.
	/// </summary>
	sealed class UninstallDialog : Form
	{
		readonly Font _head, _body, _small;
		readonly Label _title, _text, _hint;
		readonly ECheck _box;
		readonly EButton _yes, _no;
		// Kept apart from the box's Visible, which reads false while the dialog itself is hidden (before it opens and after it closes).
		readonly bool _hasSettings;

		public bool DeleteSettings { get { return _hasSettings && _box.Checked; } }

		public UninstallDialog(RemovalPlan plan, Icon icon)
		{
			_head = Theme.Body(11.5f, FontStyle.Bold);
			_body = Theme.Body(9.75f);
			_small = Theme.Body(8.5f);
			AutoScaleMode = AutoScaleMode.None;
			Icon = icon;
			Text = AddonRows.RemovalTitle(plan);
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			BackColor = Theme.Panel;
			ForeColor = Theme.Text;
			Font = _body;
			_title = new Label { Text = Text, Font = _head, ForeColor = Theme.Text, AutoSize = false };
			_text = new Label { Text = AddonRows.RemovalBody(plan), Font = _body, ForeColor = Theme.Muted, AutoSize = false };
			string box = AddonRows.RemovalBox(plan);
			_hasSettings = box != null;
			_box = new ECheck { Text = box ?? "", Visible = box != null, Checked = false };
			_hint = new Label { Text = T.S("addons.uninstall.hint"), Font = _small, ForeColor = Theme.Muted, AutoSize = false, Visible = box != null };
			_yes = new EButton { Primary = true, Text = T.S("addons.uninstall.short"), DialogResult = DialogResult.OK };
			_no = new EButton { Text = T.S("btn.cancel"), DialogResult = DialogResult.Cancel };
			Controls.AddRange(new Control[] { _title, _text, _box, _hint, _yes, _no });
			_no.TabIndex = 0;
			_box.TabIndex = 1;
			_yes.TabIndex = 2;
			CancelButton = _no;
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			ActiveControl = _no;
		}

		int S(int v) { return (int)Math.Round(v * DeviceDpi / 96f); }

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			Theme.DarkTitleBar(Handle);
			Arrange();
		}

		protected override void OnDpiChanged(DpiChangedEventArgs e)
		{
			base.OnDpiChanged(e);
			Arrange();
		}

		/// <summary>Places every part for the screen's scale; the texts wrap and the window is as tall as they need.</summary>
		void Arrange()
		{
			int m = S(22), w = S(460), inner = w - 2 * m, y = S(18);
			const TextFormatFlags wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;
			int h = TextRenderer.MeasureText(_title.Text, _head, new Size(inner, int.MaxValue), wrap).Height;
			_title.SetBounds(m, y, inner, h + S(4));
			y += h + S(12);
			h = TextRenderer.MeasureText(_text.Text, _body, new Size(inner, int.MaxValue), wrap).Height;
			_text.SetBounds(m, y, inner, h + S(4));
			y += h + S(14);
			if (_hasSettings)
			{
				_box.SetBounds(m, y, inner, S(26));
				y += S(30);
				h = TextRenderer.MeasureText(_hint.Text, _small, new Size(inner - S(26), int.MaxValue), wrap).Height;
				_hint.SetBounds(m + S(26), y, inner - S(26), h + S(4));
				y += h + S(14);
			}
			y += S(4);
			_yes.SetBounds(w - m - S(130), y, S(130), S(34));
			_no.SetBounds(w - m - S(130) - S(10) - S(110), y, S(110), S(34));
			ClientSize = new Size(w, y + S(34) + S(18));
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Theme.Ornaments(e.Graphics, ClientRectangle, DeviceDpi / 96f);
			base.OnPaint(e);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing) { _head.Dispose(); _body.Dispose(); _small.Dispose(); }
			base.Dispose(disposing);
		}
	}

	/// <summary>
	/// A scrolling list of addon rows in EldoUI's look, with column headings. Drag the edge between two headings
	/// to resize those columns; click a heading to sort. Names and descriptions wrap onto more lines instead of
	/// being cut off, so every row is as tall as its text needs.
	/// </summary>
	sealed class RowList : Control
	{
		public event Action<AddonRow> Toggled, ButtonClicked;
		public event Action<AddonRow, Point> MenuRequested;
		public event Action<double[]> ColumnsChanged;
		public event Action<int> HeaderClicked;

		const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;
		const TextFormatFlags Line = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
		readonly VScrollBar _bar = new VScrollBar();
		readonly ToolTip _tips = new ToolTip();
		readonly Font _name = Theme.Body(9.75f, FontStyle.Bold), _text = Theme.Body(8.75f), _button = Theme.Body(8.75f, FontStyle.Bold), _header = Theme.Body(8.5f, FontStyle.Bold);
		List<AddonRow> _rows = new List<AddonRow>();
		List<int> _tops = new List<int>(), _heights = new List<int>();
		string[] _headers = new string[0];
		double[] _shares = new double[0];
		int[] _widths = new int[0];
		int _sortColumn = -1;
		bool _sortDescending;
		string _empty = "";
		bool _enabled = true;
		int _hover = -1;
		string _tip;
		int _dragEdge = -1, _dragStart;
		double[] _dragShares;
		bool _dragged;

		public RowList()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
			BackColor = Theme.Bg;
			_bar.Scroll += (s, e) => Invalidate();
			Controls.Add(_bar);
		}

		public IReadOnlyList<AddonRow> Rows { get { return _rows; } }

		/// <summary>The column widths now, in pixels, for the self-test.</summary>
		public IReadOnlyList<int> ColumnWidths { get { return _widths; } }

		/// <summary>The height each row needs, in pixels, for the self-test.</summary>
		public IReadOnlyList<int> RowHeights { get { return _heights; } }

		public void SetColumns(string[] headers, double[] shares, int sortColumn, bool sortDescending)
		{
			_headers = headers ?? new string[0];
			_shares = shares ?? new double[0];
			_sortColumn = sortColumn;
			_sortDescending = sortDescending;
			Measure();
		}

		public void SetRows(List<AddonRow> rows, string empty, bool enabled)
		{
			_rows = rows ?? new List<AddonRow>();
			_empty = empty ?? "";
			_enabled = enabled;
			Measure();
		}

		int Scaled(int v) { return (int)Math.Round(v * DeviceDpi / 96f); }
		int HeaderHeight { get { return Scaled(30); } }
		bool HasBoxes { get { return _rows.Any(r => r.Checked != null); } }
		int Gutter { get { return HasBoxes ? Scaled(40) : Scaled(12); } }
		int ButtonSpace { get { return _rows.Any(r => r.Button != null) ? Scaled(118) : 0; } }
		int MenuSpace { get { return _rows.Any(r => r.Menu.Count > 0) ? Scaled(46) : Scaled(8); } }
		int ColumnsRight { get { return Width - _bar.Width - ButtonSpace - MenuSpace; } }

		/// <summary>The left edge of each column, from the tick box gutter on.</summary>
		int ColumnLeft(int column)
		{
			int x = Gutter;
			for (int i = 0; i < column && i < _widths.Length; i++) x += _widths[i];
			return x;
		}

		/// <summary>Works out column widths and the height of every row for the current width.</summary>
		void Measure()
		{
			_bar.SetBounds(Width - SystemInformation.VerticalScrollBarWidth, HeaderHeight, SystemInformation.VerticalScrollBarWidth, Math.Max(0, Height - HeaderHeight));
			_widths = ColumnLayout.Widths(_shares, Math.Max(0, ColumnsRight - Gutter), Scaled(ColumnLayout.MinWidth));
			_tops = new List<int>();
			_heights = new List<int>();
			int y = 0, pad = Scaled(8), span = Math.Max(Scaled(40), ColumnsRight - Gutter - pad);
			foreach (var row in _rows)
			{
				int line = Scaled(18);
				for (int c = 0; c < _widths.Length; c++)
				{
					string text = Cell(row, c);
					if (string.IsNullOrEmpty(text)) continue;
					line = Math.Max(line, TextRenderer.MeasureText(text, c == 0 ? _name : _text, new Size(Math.Max(1, _widths[c] - pad), int.MaxValue), Wrap).Height);
				}
				int h = Scaled(10) + line;
				if (!string.IsNullOrEmpty(row.Note)) h += Scaled(4) + TextRenderer.MeasureText(row.Note, _text, new Size(span, int.MaxValue), Wrap).Height;
				if (!string.IsNullOrEmpty(row.Detail)) h += Scaled(4) + TextRenderer.MeasureText(row.Detail, _text, new Size(span, int.MaxValue), Wrap).Height;
				h = Math.Max(Scaled(50), h + Scaled(10));
				_tops.Add(y);
				_heights.Add(h);
				y += h;
			}
			int view = Math.Max(1, Height - HeaderHeight);
			_bar.Minimum = 0;
			_bar.Maximum = Math.Max(0, y - 1);
			_bar.LargeChange = view;
			_bar.SmallChange = Scaled(30);
			_bar.Enabled = y > view;
			if (_bar.Value > Math.Max(0, y - view)) _bar.Value = Math.Max(0, y - view);
			Invalidate();
		}

		static string Cell(AddonRow row, int column)
		{
			return column == 0 ? row.Title : column == 1 ? row.Kind : column == 2 ? row.Extra : row.Status;
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			Measure();
		}

		protected override void OnDpiChangedAfterParent(EventArgs e)
		{
			base.OnDpiChangedAfterParent(e);
			Measure();
		}

		int RowAt(Point p)
		{
			if (p.Y < HeaderHeight || p.X >= Width - _bar.Width) return -1;
			int y = p.Y - HeaderHeight + _bar.Value;
			for (int i = 0; i < _rows.Count; i++)
				if (y >= _tops[i] && y < _tops[i] + _heights[i]) return i;
			return -1;
		}

		/// <summary>The column edge under the pointer in the heading row (between column i and i + 1), or -1.</summary>
		int EdgeAt(Point p)
		{
			if (p.Y >= HeaderHeight) return -1;
			for (int i = 0; i < _widths.Length - 1; i++)
				if (Math.Abs(p.X - ColumnLeft(i + 1)) <= Scaled(5)) return i;
			return -1;
		}

		int HeaderAt(Point p)
		{
			if (p.Y >= HeaderHeight) return -1;
			for (int i = 0; i < _widths.Length; i++)
				if (p.X >= ColumnLeft(i) && p.X < ColumnLeft(i) + _widths[i]) return i;
			return -1;
		}

		Rectangle RowRect(int i) { return new Rectangle(0, HeaderHeight + _tops[i] - _bar.Value, Width - _bar.Width, _heights[i]); }
		Rectangle BoxRect(Rectangle r) { return new Rectangle(r.Left + Scaled(14), r.Top + Scaled(12), Scaled(16), Scaled(16)); }
		Rectangle MenuRect(Rectangle r) { return new Rectangle(r.Right - Scaled(40), r.Top + Scaled(9), Scaled(30), Scaled(30)); }
		Rectangle ButtonRect(Rectangle r) { return new Rectangle(ColumnsRight + Scaled(8), r.Top + Scaled(11), Scaled(106), Scaled(26)); }

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			if (_bar.Enabled) _bar.Value = Math.Max(0, Math.Min(_bar.Maximum - _bar.LargeChange + 1, _bar.Value - Math.Sign(e.Delta) * Scaled(60)));
			Invalidate();
			base.OnMouseWheel(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			int edge = EdgeAt(e.Location);
			if (e.Button != MouseButtons.Left || edge < 0) return;
			_dragEdge = edge;
			_dragStart = e.X;
			_dragShares = _shares;
			_dragged = false;
			Capture = true;
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			if (_dragEdge < 0) return;
			_dragEdge = -1;
			Capture = false;
			if (_dragged) ColumnsChanged?.Invoke(_shares);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			if (_dragEdge >= 0)
			{
				_shares = ColumnLayout.Drag(_dragShares, _dragEdge, e.X - _dragStart, Math.Max(0, ColumnsRight - Gutter), Scaled(ColumnLayout.MinWidth));
				_dragged = _dragged || e.X != _dragStart;
				Measure();
				base.OnMouseMove(e);
				return;
			}
			Cursor = EdgeAt(e.Location) >= 0 ? Cursors.VSplit : HeaderAt(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
			int i = RowAt(e.Location);
			if (i != _hover) { _hover = i; Invalidate(); }
			string tip = e.Y < HeaderHeight ? T.S("addons.col.tip") : i >= 0 ? _rows[i].Tip : null;
			if (tip != _tip) { _tip = tip; _tips.SetToolTip(this, tip ?? ""); }
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e) { _hover = -1; Cursor = Cursors.Default; Invalidate(); base.OnMouseLeave(e); }

		protected override void OnMouseClick(MouseEventArgs e)
		{
			base.OnMouseClick(e);
			Focus();
			if (_dragged) { _dragged = false; return; }
			if (e.Y < HeaderHeight)
			{
				int column = HeaderAt(e.Location);
				if (e.Button == MouseButtons.Left && column >= 0 && EdgeAt(e.Location) < 0) HeaderClicked?.Invoke(column);
				return;
			}
			int i = RowAt(e.Location);
			if (i < 0) return;
			var row = _rows[i];
			var r = RowRect(i);
			if (row.Menu.Count > 0 && (MenuRect(r).Contains(e.Location) || e.Button == MouseButtons.Right)) { MenuRequested?.Invoke(row, new Point(MenuRect(r).Left, MenuRect(r).Bottom)); return; }
			if (!_enabled) return;
			if (row.Button != null && ButtonRect(r).Contains(e.Location)) { ButtonClicked?.Invoke(row); return; }
			if (row.Checked != null && e.X < ColumnLeft(1)) Toggled?.Invoke(row);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Down && _bar.Enabled) { _bar.Value = Math.Min(_bar.Maximum - _bar.LargeChange + 1, _bar.Value + _bar.SmallChange); Invalidate(); }
			if (e.KeyCode == Keys.Up && _bar.Value > 0) { _bar.Value = Math.Max(0, _bar.Value - _bar.SmallChange); Invalidate(); }
			base.OnKeyDown(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(Theme.Bg);
			g.SmoothingMode = SmoothingMode.AntiAlias;
			int head = HeaderHeight, pad = Scaled(8);
			if (_rows.Count == 0)
				TextRenderer.DrawText(g, _empty, _text, new Rectangle(0, head, Width - _bar.Width, Scaled(80)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
			g.SetClip(new Rectangle(0, head, Width, Math.Max(0, Height - head)));
			for (int i = 0; i < _rows.Count; i++)
			{
				var r = RowRect(i);
				if (r.Bottom < head) continue;
				if (r.Top > Height) break;
				var row = _rows[i];
				if (i == _hover)
					using (var hover = new SolidBrush(Theme.Panel2)) g.FillRectangle(hover, r.Left + 1, r.Top, r.Width - 2, r.Height);
				using (var line = new Pen(Theme.Edge)) g.DrawLine(line, r.Left + Scaled(10), r.Bottom - 1, r.Right - Scaled(10), r.Bottom - 1);
				if (row.Checked != null) DrawBox(g, BoxRect(r), row.Checked == true);
				int y = r.Top + Scaled(10), line1 = Scaled(18);
				for (int c = 0; c < _widths.Length; c++)
				{
					string text = Cell(row, c);
					if (string.IsNullOrEmpty(text)) continue;
					var font = c == 0 ? _name : _text;
					var size = new Size(Math.Max(1, _widths[c] - pad), int.MaxValue);
					int h = TextRenderer.MeasureText(text, font, size, Wrap).Height;
					line1 = Math.Max(line1, h);
					Color color = c == 0 ? (row.Tone == Tone.Info && row.Checked == null ? Theme.Accent : Theme.Text) : c == 3 ? ToneColor(row.Tone) : Theme.Muted;
					TextRenderer.DrawText(g, text, font, new Rectangle(ColumnLeft(c), y + (c == 0 ? 0 : Scaled(1)), size.Width, h), color, Wrap);
				}
				y += line1;
				int span = Math.Max(Scaled(40), ColumnsRight - Gutter - pad);
				if (!string.IsNullOrEmpty(row.Note))
				{
					int h = TextRenderer.MeasureText(row.Note, _text, new Size(span, int.MaxValue), Wrap).Height;
					TextRenderer.DrawText(g, row.Note, _text, new Rectangle(Gutter, y + Scaled(4), span, h), Theme.Gold, Wrap);
					y += Scaled(4) + h;
				}
				if (!string.IsNullOrEmpty(row.Detail))
				{
					int h = TextRenderer.MeasureText(row.Detail, _text, new Size(span, int.MaxValue), Wrap).Height;
					TextRenderer.DrawText(g, row.Detail, _text, new Rectangle(Gutter, y + Scaled(4), span, h), Theme.Muted, Wrap);
				}
				if (row.Button != null)
				{
					var b = ButtonRect(r);
					using (var path = Theme.Round(b, 6))
					using (var pen = new Pen(_enabled ? Theme.Accent : Theme.Edge))
						g.DrawPath(pen, path);
					TextRenderer.DrawText(g, row.ButtonText, _button, b, _enabled ? Theme.Accent : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
				}
				if (row.Menu.Count > 0)
				{
					var m = MenuRect(r);
					using (var dots = new SolidBrush(Theme.Muted))
						for (int d = -1; d <= 1; d++) g.FillEllipse(dots, m.Left + m.Width / 2 - Scaled(2) + d * Scaled(7), m.Top + m.Height / 2 - Scaled(2), Scaled(4), Scaled(4));
				}
			}
			g.ResetClip();
			DrawHeader(g);
			using (var edge = new Pen(Theme.Edge))
				g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
		}

		void DrawHeader(Graphics g)
		{
			int head = HeaderHeight;
			using (var fill = new SolidBrush(Theme.Panel2)) g.FillRectangle(fill, 0, 0, Width, head);
			using (var line = new Pen(Theme.EdgeStrong)) g.DrawLine(line, 0, head - 1, Width, head - 1);
			for (int c = 0; c < _widths.Length && c < _headers.Length; c++)
			{
				int x = ColumnLeft(c);
				string text = _headers[c] + (c == _sortColumn ? (_sortDescending ? "  ▼" : "  ▲") : "");
				TextRenderer.DrawText(g, text, _header, new Rectangle(x, 0, Math.Max(1, _widths[c] - Scaled(10)), head), c == _sortColumn ? Theme.Text : Theme.Muted, Line);
				if (c < _widths.Length - 1)
				{
					int edge = x + _widths[c];
					using (var pen = new Pen(c == _dragEdge ? Theme.Accent : Theme.EdgeStrong, Math.Max(1f, Scaled(1))))
						g.DrawLine(pen, edge, Scaled(7), edge, head - Scaled(7));
				}
			}
		}

		static Color ToneColor(Tone tone)
		{
			return tone == Tone.Ok ? Theme.Ok : tone == Tone.Warn ? Theme.Warn : tone == Tone.Error ? Theme.Err : tone == Tone.Info ? Theme.Accent : Theme.Muted;
		}

		static void DrawBox(Graphics g, Rectangle box, bool on)
		{
			using (var path = Theme.Round(box, 3))
			{
				using (var fill = new SolidBrush(on ? Theme.AccentSoft : Theme.Panel)) g.FillPath(fill, path);
				using (var pen = new Pen(on ? Theme.Accent : Theme.EdgeStrong, 1.2f)) g.DrawPath(pen, path);
			}
			if (on)
				using (var pen = new Pen(Theme.Accent, 2f))
					g.DrawLines(pen, new[] { new PointF(box.Left + box.Width * 0.22f, box.Top + box.Height * 0.52f), new PointF(box.Left + box.Width * 0.43f, box.Top + box.Height * 0.74f), new PointF(box.Left + box.Width * 0.8f, box.Top + box.Height * 0.28f) });
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing) { _tips.Dispose(); _name.Dispose(); _text.Dispose(); _button.Dispose(); _header.Dispose(); }
			base.Dispose(disposing);
		}
	}
}
