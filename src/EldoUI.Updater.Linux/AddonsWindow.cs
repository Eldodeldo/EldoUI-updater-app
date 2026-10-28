// The Extra addons window on Linux: the same rows, words and choices as the Windows window (AddonRows),
// drawn with Avalonia in EldoUI's colours. Every change is the player's own click.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using EldoUI.Updater.Core;

namespace EldoUI.Updater
{
	sealed class AddonsWindow : Window
	{
		public event Action<AddonAction, string> ActionRequested;
		public event Action<bool> InstallClicked;
		public event Action<string> ImportChosen, ExportChosen;
		/// <summary>The player confirmed uninstalling these Installed rows (their keys); true: delete their saved settings too.</summary>
		public event Action<List<string>, bool> UninstallRequested;
		public bool AllowClose;

		readonly TextBlock _heading, _intro, _message, _note, _empty;
		readonly Button _tabFind, _tabInstalled, _install, _recommended, _import, _export, _close, _uninstall;
		readonly TextBox _search;
		readonly ComboBox _category;
		readonly CheckBox _showHidden;
		readonly StackPanel _list = new StackPanel();
		readonly ScrollViewer _scroll;
		readonly Grid _header = new Grid { Background = Palette.B(Palette.Panel2), Height = 30 };
		readonly Border _frame;
		readonly List<Grid> _grids = new List<Grid>();
		readonly Avalonia.Threading.DispatcherTimer _saveSize = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
		AddonsView _view = new AddonsView();
		/// <summary>The Installed rows ticked to uninstall together.</summary>
		readonly HashSet<string> _marked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		WindowLayout _layout = new WindowLayout();
		double[] _shares = ColumnLayout.Default("find");
		int _sortColumn = -1;
		bool _sortDescending;
		bool _installedTab;
		bool _rendering;
		bool _boxes, _buttons, _menus;

		/// <summary>The window's inner size at first start, and the smallest it can be made.</summary>
		const int DefaultWidth = 920, DefaultHeight = 680, MinimumWidth = 720, MinimumHeight = 540;
		const double Gap = 8;

		/// <summary>The player resized the window or a column; the layout object holds the new sizes.</summary>
		public event Action LayoutChanged;

		/// <summary>The rows shown now, for the self-test.</summary>
		public List<AddonRow> Rows { get; private set; } = new List<AddonRow>();

		/// <summary>The column widths now, for the self-test.</summary>
		public int[] ColumnWidths { get; private set; } = new int[0];

		public AddonsWindow(Avalonia.Media.Imaging.Bitmap logo)
		{
			Width = DefaultWidth;
			Height = DefaultHeight;
			MinWidth = MinimumWidth;
			MinHeight = MinimumHeight;
			CanResize = true;
			WindowStartupLocation = WindowStartupLocation.CenterScreen;
			Background = Palette.B(Palette.Panel);
			Foreground = Palette.B(Palette.Text);
			FontSize = 13;
			if (logo != null) Icon = new WindowIcon(logo);

			_heading = new TextBlock { FontSize = 19, FontWeight = FontWeight.Bold };
			_intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Muted), FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) };

			_tabFind = MainWindow.MakeButton(150, true);
			_tabInstalled = MainWindow.MakeButton(150, false);
			_tabFind.Height = _tabInstalled.Height = 32;
			_tabInstalled.Margin = new Thickness(6, 0, 0, 0);
			_tabFind.Click += (s, e) => ShowInstalled(false);
			_tabInstalled.Click += (s, e) => ShowInstalled(true);
			_search = new TextBox { MinWidth = 120, MaxWidth = 260, HorizontalAlignment = HorizontalAlignment.Right, Height = 30, Background = Palette.B(Palette.Bg), Foreground = Palette.B(Palette.Text), BorderBrush = Palette.B(Palette.EdgeStrong), Margin = new Thickness(16, 0, 0, 0) };
			_search.TextChanged += (s, e) => { if (!_rendering) Fill(); };
			_category = new ComboBox { Width = 170, Height = 30, Background = Palette.B(Palette.Bg), Foreground = Palette.B(Palette.Text), BorderBrush = Palette.B(Palette.EdgeStrong), Margin = new Thickness(8, 0, 0, 0) };
			_category.SelectionChanged += (s, e) => { if (!_rendering) Fill(); };
			_showHidden = new CheckBox { Foreground = Palette.B(Palette.Text), IsVisible = false, Margin = new Thickness(16, 0, 0, 0) };
			_showHidden.IsCheckedChanged += (s, e) => { if (!_rendering) Fill(); };
			_uninstall = MainWindow.MakeButton(170, false);
			_uninstall.Height = 32;
			_uninstall.IsVisible = false;
			_uninstall.HorizontalAlignment = HorizontalAlignment.Right;
			_uninstall.Click += (s, e) => ConfirmUninstall(_marked.ToList());
			var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(0, 10, 0, 10) };
			Place(tools, _tabFind, 0);
			Place(tools, _tabInstalled, 1);
			Place(tools, _showHidden, 2);
			Place(tools, _search, 2);
			Place(tools, _category, 3);
			Place(tools, _uninstall, 3);
			_search.VerticalAlignment = _category.VerticalAlignment = _showHidden.VerticalAlignment = _uninstall.VerticalAlignment = VerticalAlignment.Center;

			_scroll = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
			_empty = new TextBlock { Foreground = Palette.B(Palette.Muted), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20, 28), TextAlignment = TextAlignment.Center };
			var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
			body.Children.Add(new Border { Child = _header, BorderBrush = Palette.B(Palette.EdgeStrong), BorderThickness = new Thickness(0, 0, 0, 1) });
			var rows = new Panel { Children = { _scroll, _empty } };
			Grid.SetRow(rows, 1);
			body.Children.Add(rows);
			_frame = new Border { Background = Palette.B(Palette.Bg), BorderBrush = Palette.B(Palette.Edge), BorderThickness = new Thickness(1), Child = body };
			_frame.SizeChanged += (s, e) => Arrange();

			_message = new TextBlock { FontSize = 11.5, Foreground = Palette.B(Palette.Accent), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 4) };
			_install = MainWindow.MakeButton(170, true);
			_recommended = MainWindow.MakeButton(170, false);
			_import = MainWindow.MakeButton(96, false);
			_export = MainWindow.MakeButton(96, false);
			_close = MainWindow.MakeButton(80, false);
			_install.Click += (s, e) => InstallClicked?.Invoke(false);
			_recommended.Click += (s, e) => InstallClicked?.Invoke(true);
			_import.Click += async (s, e) => await Import();
			_export.Click += async (s, e) => await Export();
			_close.Click += (s, e) => Hide();
			var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto") };
			Place(buttons, _install, 0);
			Place(buttons, _recommended, 1);
			Place(buttons, _import, 2);
			Place(buttons, _export, 3);
			Place(buttons, _close, 5);
			_recommended.Margin = _import.Margin = _export.Margin = new Thickness(6, 0, 0, 0);
			_note = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Palette.B(Palette.Muted), Margin = new Thickness(0, 8, 0, 0) };

			var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto"), Margin = new Thickness(22, 14, 22, 12) };
			int r = 0;
			foreach (Control c in new Control[] { _heading, _intro, tools, _frame, _message, buttons, _note })
			{
				Grid.SetRow(c, r++);
				root.Children.Add(c);
			}
			Content = root;
			KeyDown += (s, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
			Resized += (s, e) => { if (e.Reason == WindowResizeReason.User) { _saveSize.Stop(); _saveSize.Start(); } };
			_saveSize.Tick += (s, e) =>
			{
				_saveSize.Stop();
				if (WindowState != WindowState.Normal) return;
				_layout.Width = (int)Math.Round(Width);
				_layout.Height = (int)Math.Round(Height);
				LayoutChanged?.Invoke();
			};
			ApplyTexts();
		}

		static void Place(Grid grid, Control c, int column)
		{
			Grid.SetColumn(c, column);
			grid.Children.Add(c);
		}

		string ListKey { get { return _installedTab ? "installed" : "find"; } }

		/// <summary>Uses the size and column widths the player left the window at (kept in the settings).</summary>
		public void UseLayout(WindowLayout layout)
		{
			_layout = layout ?? new WindowLayout();
			if (_layout.Width > 0 && _layout.Height > 0)
			{
				Width = Math.Max(MinimumWidth, _layout.Width);
				Height = Math.Max(MinimumHeight, _layout.Height);
			}
			Fill();
		}

		protected override void OnClosing(WindowClosingEventArgs e)
		{
			if (!AllowClose) { e.Cancel = true; Hide(); }
			base.OnClosing(e);
		}

		public void ApplyTexts()
		{
			Title = T.S("addons.title");
			_heading.Text = T.S("addons.title");
			_intro.Text = T.S("addons.intro");
			_tabFind.Content = T.S("addons.tab.find");
			_tabInstalled.Content = T.S("addons.tab.installed");
			_search.Watermark = T.S("addons.search");
			_install.Content = T.S("extras.install_selected");
			_recommended.Content = T.S("extras.install_all");
			_import.Content = T.S("addons.import");
			_export.Content = T.S("addons.export");
			_close.Content = T.S("btn.close");
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
				string chosen = _category.SelectedIndex > 0 ? ((Tuple<string, string>)_category.SelectedItem).Item1 : null;
				var items = new List<Tuple<string, string>> { Tuple.Create<string, string>(null, T.S("addons.category.all")) };
				items.AddRange(AddonCatalog.Categories.Select(c => Tuple.Create(c, AddonRows.Category(c))));
				_category.ItemsSource = items;
				_category.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Tuple<string, string>>((t, _) => new TextBlock { Text = t?.Item2 });
				_category.SelectedIndex = Math.Max(0, chosen == null ? 0 : items.FindIndex(t => t.Item1 == chosen));
				_install.IsEnabled = !_view.Busy && _view.HasFolder && _view.Catalog.Any(r => r.Selected);
				_recommended.IsEnabled = !_view.Busy && _view.HasFolder;
				_import.IsEnabled = !_view.Busy;
			}
			finally { _rendering = false; }
			Fill();
		}

		public void ShowInstalled(bool installed)
		{
			if (_installedTab != installed) { _sortColumn = -1; _sortDescending = false; }
			_installedTab = installed;
			Fill();
		}

		/// <summary>Ticks Installed rows to uninstall together (the window pictures use it).</summary>
		public void Mark(params string[] keys)
		{
			foreach (string k in keys) _marked.Add(k);
			Fill();
		}

		public void ShowMessage(string text, bool problem = false)
		{
			_message.Foreground = Palette.B(problem ? Palette.Warn : Palette.Accent);
			_message.Text = text ?? "";
		}

		void Fill()
		{
			MainWindow.Paint(_tabFind, !_installedTab);
			MainWindow.Paint(_tabInstalled, _installedTab);
			_search.IsVisible = _category.IsVisible = !_installedTab;
			_showHidden.IsVisible = _uninstall.IsVisible = _installedTab;
			int hidden = AddonRows.HiddenCount(_view);
			_rendering = true;
			_showHidden.Content = T.F("addons.showhidden", hidden);
			_showHidden.IsEnabled = hidden > 0;
			if (hidden == 0) _showHidden.IsChecked = false;
			_rendering = false;
			string empty;
			if (_installedTab)
			{
				Rows = AddonRows.Installed(_view, _showHidden.IsChecked == true, _marked);
				// A ticked row that is gone (uninstalled, hidden or moved) is no longer ticked.
				_marked.IntersectWith(Rows.Where(x => x.Checked != null).Select(x => x.Key));
				empty = !_view.HasFolder ? T.S("addons.nofolder") : T.S("addons.empty");
			}
			else
			{
				string category = _category.SelectedIndex > 0 ? ((Tuple<string, string>)_category.SelectedItem).Item1 : null;
				Rows = AddonRows.Find(_view, _search.Text, category);
				empty = T.S("addons.none");
			}
			_uninstall.Content = T.F("addons.uninstall.selected", _marked.Count);
			_uninstall.IsEnabled = !_view.Busy && _marked.Count > 0;
			if (_sortColumn >= 0) Rows = AddonRows.Sort(Rows, _sortColumn, _sortDescending);
			_shares = ColumnLayout.Shares(_layout, ListKey);
			_boxes = Rows.Any(x => x.Checked != null);
			_buttons = Rows.Any(x => x.Button != null);
			_menus = Rows.Any(x => x.Menu.Count > 0);
			_grids.Clear();
			BuildHeader();
			_list.Children.Clear();
			foreach (var row in Rows) _list.Children.Add(RowControl(row));
			_empty.Text = empty;
			_empty.IsVisible = Rows.Count == 0;
			Arrange();
		}

		/// <summary>The fixed columns around the four text columns: tick box, then the row's button and its menu.</summary>
		double Gutter { get { return _boxes ? 40 : 12; } }
		double ButtonSpace { get { return _buttons ? 118 : 0; } }
		double MenuSpace { get { return _menus ? 46 : 8; } }

		/// <summary>Sets every row's column widths from the shares and the list's width now.</summary>
		void Arrange()
		{
			double width = _frame.Bounds.Width - 2 - 14;
			if (width <= 0) width = Width - 44 - 2 - 14;
			int total = (int)Math.Max(0, width - Gutter - ButtonSpace - MenuSpace);
			ColumnWidths = ColumnLayout.Widths(_shares, total, ColumnLayout.MinWidth);
			foreach (var grid in _grids)
			{
				grid.ColumnDefinitions[0].Width = new GridLength(Gutter);
				for (int c = 0; c < ColumnWidths.Length; c++) grid.ColumnDefinitions[c + 1].Width = new GridLength(ColumnWidths[c]);
				grid.ColumnDefinitions[5].Width = new GridLength(ButtonSpace);
				grid.ColumnDefinitions[6].Width = new GridLength(MenuSpace);
			}
		}

		static Grid Columns()
		{
			return new Grid { ColumnDefinitions = new ColumnDefinitions("12,100,100,100,100,0,8") };
		}

		/// <summary>The heading row: click a heading to sort, drag the edge between two headings to resize them.</summary>
		void BuildHeader()
		{
			_header.Children.Clear();
			_header.ColumnDefinitions = Columns().ColumnDefinitions;
			_grids.Add(_header);
			ToolTip.SetTip(_header, T.S("addons.col.tip"));
			string[] headings = AddonRows.Headers(_installedTab);
			for (int c = 0; c < headings.Length; c++)
			{
				int column = c;
				var label = new TextBlock
				{
					Text = headings[c] + (c == _sortColumn ? (_sortDescending ? "  ▼" : "  ▲") : ""),
					FontSize = 11.5, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center,
					Foreground = Palette.B(c == _sortColumn ? Palette.Text : Palette.Muted), TextTrimming = TextTrimming.CharacterEllipsis,
				};
				var cell = new Border { Child = label, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Padding = new Thickness(0, 0, 10, 0) };
				cell.PointerReleased += (s, e) =>
				{
					if (e.InitialPressMouseButton != MouseButton.Left) return;
					if (_sortColumn == column) _sortDescending = !_sortDescending;
					else { _sortColumn = column; _sortDescending = false; }
					Fill();
				};
				Place(_header, cell, c + 1);
				if (c < headings.Length - 1) Place(_header, Edge(c), c + 1);
			}
		}

		/// <summary>The drag handle on the right edge of heading <paramref name="edge"/>.</summary>
		Control Edge(int edge)
		{
			var line = new Border { Width = 1, Margin = new Thickness(0, 7), Background = Palette.B(Palette.EdgeStrong), HorizontalAlignment = HorizontalAlignment.Center };
			var handle = new Border { Width = 10, Margin = new Thickness(0, 0, -1, 0), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, Cursor = new Cursor(StandardCursorType.SizeWestEast), Child = line, ZIndex = 1 };
			double start = 0;
			double[] from = null;
			handle.PointerPressed += (s, e) =>
			{
				if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
				start = e.GetPosition(_header).X;
				from = _shares;
				e.Pointer.Capture(handle);
				line.Background = Palette.B(Palette.Accent);
				e.Handled = true;
			};
			handle.PointerMoved += (s, e) =>
			{
				if (from == null) return;
				int total = ColumnWidths.Sum();
				_shares = ColumnLayout.Drag(from, edge, (int)Math.Round(e.GetPosition(_header).X - start), total, ColumnLayout.MinWidth);
				Arrange();
			};
			handle.PointerReleased += (s, e) =>
			{
				if (from == null) return;
				bool moved = !_shares.SequenceEqual(from);
				from = null;
				e.Pointer.Capture(null);
				line.Background = Palette.B(Palette.EdgeStrong);
				e.Handled = true;
				if (!moved) return;
				_layout.Columns[ListKey] = _shares.ToList();
				LayoutChanged?.Invoke();
			};
			return handle;
		}

		Control RowControl(AddonRow row)
		{
			bool enabled = !_view.Busy;
			var grid = Columns();
			grid.RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
			grid.Margin = new Thickness(0, 9, 0, 9);
			_grids.Add(grid);
			if (row.Checked != null)
			{
				var box = new CheckBox { IsChecked = row.Checked == true, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -6, 0, 0) };
				box.IsCheckedChanged += (s, e) =>
				{
					if (!_installedTab) { Raise(row.Checked == true ? AddonAction.Deselect : AddonAction.Select, row.Key); return; }
					if (_rendering || _view.Busy) return;
					if (!_marked.Remove(row.Key)) _marked.Add(row.Key);
					Fill();
				};
				Place(grid, box, 0);
				Grid.SetRowSpan(box, 3);
			}
			string[] cells = { row.Title, row.Kind, row.Extra, row.Status };
			Color tone = row.Tone == Tone.Ok ? Palette.Ok : row.Tone == Tone.Warn ? Palette.Warn : row.Tone == Tone.Error ? Palette.Err : row.Tone == Tone.Info ? Palette.Accent : Palette.Muted;
			for (int c = 0; c < cells.Length; c++)
			{
				if (string.IsNullOrEmpty(cells[c])) continue;
				var text = new TextBlock { Text = cells[c], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, c == 0 ? 0 : 1, Gap, 0) };
				if (c == 0) { text.FontWeight = FontWeight.Bold; text.Foreground = Palette.B(row.Tone == Tone.Info && row.Checked == null ? Palette.Accent : Palette.Text); }
				else { text.FontSize = 11.5; text.Foreground = Palette.B(c == 3 ? tone : Palette.Muted); }
				if (c == 3 && row.Tip != null) ToolTip.SetTip(text, row.Tip);
				Place(grid, text, c + 1);
			}
			if (!string.IsNullOrEmpty(row.Note)) Wide(grid, new TextBlock { Text = row.Note, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Gold), Margin = new Thickness(0, 4, Gap, 0) }, 1);
			if (!string.IsNullOrEmpty(row.Detail)) Wide(grid, new TextBlock { Text = row.Detail, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Muted), Margin = new Thickness(0, 4, Gap, 0) }, 2);
			if (row.Button != null)
			{
				var b = MainWindow.MakeButton(106, false);
				b.Height = 28;
				b.FontSize = 12;
				b.Content = row.ButtonText;
				b.IsEnabled = enabled;
				b.VerticalAlignment = VerticalAlignment.Top;
				b.Margin = new Thickness(8, 0, 4, 0);
				b.Click += (s, e) => Choose(new MenuChoice { Action = row.Button.Value, Arg = row.Button == AddonAction.Add ? row.Key.Substring("suggest:".Length) : MenuArg(row, row.Button.Value) });
				Place(grid, b, 5);
				Grid.SetRowSpan(b, 3);
			}
			if (row.Menu.Count > 0)
			{
				var more = MainWindow.MakeButton(34, false);
				more.Height = 28;
				more.Content = "⋯";
				more.VerticalAlignment = VerticalAlignment.Top;
				more.Margin = new Thickness(4, 0, 8, 0);
				ToolTip.SetTip(more, T.S("addons.more"));
				more.Click += (s, e) => ShowMenu(row, more);
				Place(grid, more, 6);
				Grid.SetRowSpan(more, 3);
			}
			return new Border { Child = grid, BorderBrush = Palette.B(Palette.Edge), BorderThickness = new Thickness(0, 0, 0, 1) };
		}

		/// <summary>A line under the row's cells that runs across all four text columns.</summary>
		static void Wide(Grid grid, Control c, int row)
		{
			Grid.SetRow(c, row);
			Grid.SetColumn(c, 1);
			Grid.SetColumnSpan(c, 4);
			grid.Children.Add(c);
		}

		static string MenuArg(AddonRow row, AddonAction action)
		{
			var m = row.Menu.FirstOrDefault(c => c.Action == action);
			return m != null ? m.Arg : row.Key;
		}

		void ShowMenu(AddonRow row, Control anchor)
		{
			var items = new List<object>();
			foreach (var choice in row.Menu)
			{
				var c = choice;
				var item = new MenuItem { Header = c.Text, IsEnabled = !_view.Busy || c.Action == AddonAction.OpenPage || c.Action == AddonAction.OpenFolder };
				if (c.Checked != null) { item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = c.Checked == true; }
				item.Click += (s, e) => Choose(c);
				items.Add(item);
			}
			new ContextMenu { ItemsSource = items }.Open(anchor);
		}

		async void Choose(MenuChoice c)
		{
			if (c.Action == AddonAction.Uninstall) { ConfirmUninstall(new List<string> { c.Arg }); return; }
			if (c.Action == AddonAction.TakeOver)
			{
				var entry = AddonCatalog.Find(c.Arg);
				if (entry == null) return;
				var installed = _view.Installed.FirstOrDefault(i => i.CatalogId == c.Arg);
				string folders = installed != null ? string.Join(", ", installed.Folders) : string.Join(", ", entry.Folders.Take(3));
				string name = entry.Legacy != null ? entry.Legacy.Name : entry.Name;
				if (!await Dialogs.Confirm(this, T.F("addons.takeover.title", name), T.F("addons.takeover.body", name, folders), T.S("addons.takeover.short"))) return;
			}
			Raise(c.Action, c.Arg);
		}

		void Raise(AddonAction action, string arg)
		{
			if (_rendering || (_view.Busy && action != AddonAction.OpenPage && action != AddonAction.OpenFolder)) return;
			ActionRequested?.Invoke(action, arg);
		}

		/// <summary>Asks before anything is uninstalled; nothing happens unless the player says yes.</summary>
		async void ConfirmUninstall(List<string> keys)
		{
			if (_view.Busy || keys.Count == 0) return;
			var plan = AddonRemover.Plan(_view.AddOnsDir, _view.Installed, keys);
			if (plan == null) return;
			bool? settings = await Dialogs.ConfirmUninstall(this, plan);
			if (settings == null) return;
			_marked.ExceptWith(keys);
			UninstallRequested?.Invoke(keys, settings == true);
		}

		static readonly FilePickerFileType ListFile = new FilePickerFileType("EldoUI addon list") { Patterns = new[] { "*.json" } };

		async Task Import()
		{
			var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = T.S("addons.import").TrimEnd('…'), AllowMultiple = false, FileTypeFilter = new[] { ListFile } });
			string path = picked != null && picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
			if (path != null) ImportChosen?.Invoke(path);
		}

		async Task Export()
		{
			var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = T.S("addons.export").TrimEnd('…'), SuggestedFileName = "eldoui-addons.json", DefaultExtension = "json", FileTypeChoices = new[] { ListFile } });
			string path = file != null ? file.TryGetLocalPath() : null;
			if (path != null) ExportChosen?.Invoke(path);
		}
	}

	/// <summary>A small question window in EldoUI's look, for the choices that need a yes first.</summary>
	static class Dialogs
	{
		public static Task<bool> Confirm(Window owner, string title, string body, string yes)
		{
			var dialog = new Window
			{
				Title = title, Width = 420, SizeToContent = SizeToContent.Height, CanResize = false,
				WindowStartupLocation = WindowStartupLocation.CenterOwner,
				Background = Palette.B(Palette.Panel), Foreground = Palette.B(Palette.Text), FontSize = 13,
			};
			var ok = MainWindow.MakeButton(130, true);
			var cancel = MainWindow.MakeButton(110, false);
			ok.Content = yes;
			cancel.Content = T.S("btn.cancel");
			ok.Click += (s, e) => dialog.Close(true);
			cancel.Click += (s, e) => dialog.Close(false);
			dialog.Content = new StackPanel
			{
				Margin = new Thickness(22), Spacing = 16,
				Children =
				{
					new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
					new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Muted) },
					new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
				},
			};
			return dialog.ShowDialog<bool>(owner);
		}

		/// <summary>
		/// The question before an uninstall: null when the player cancels, otherwise whether the saved settings go too.
		/// Cancel has the focus, so Enter alone never deletes anything.
		/// </summary>
		public static async Task<bool?> ConfirmUninstall(Window owner, RemovalPlan plan)
		{
			CheckBox box;
			var dialog = UninstallWindow(plan, out box);
			bool yes = await dialog.ShowDialog<bool>(owner);
			if (!yes) return null;
			return box.IsVisible && box.IsChecked == true;
		}

		/// <summary>The uninstall question as a window (also shown on its own for the window pictures).</summary>
		public static Window UninstallWindow(RemovalPlan plan, out CheckBox box)
		{
			string title = AddonRows.RemovalTitle(plan), boxText = AddonRows.RemovalBox(plan);
			var dialog = new Window
			{
				Title = title, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false,
				WindowStartupLocation = WindowStartupLocation.CenterOwner,
				Background = Palette.B(Palette.Panel), Foreground = Palette.B(Palette.Text), FontSize = 13,
			};
			var ok = MainWindow.MakeButton(130, true);
			var cancel = MainWindow.MakeButton(110, false);
			ok.Content = T.S("addons.uninstall.short");
			cancel.Content = T.S("btn.cancel");
			ok.Click += (s, e) => dialog.Close(true);
			cancel.Click += (s, e) => dialog.Close(false);
			box = new CheckBox { Content = boxText ?? "", IsChecked = false, IsVisible = boxText != null, Foreground = Palette.B(Palette.Text) };
			var hint = new TextBlock { Text = T.S("addons.uninstall.hint"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Muted), IsVisible = boxText != null, Margin = new Thickness(28, -10, 0, 0) };
			dialog.Content = new StackPanel
			{
				Margin = new Thickness(22), Spacing = 16,
				Children =
				{
					new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
					new TextBlock { Text = AddonRows.RemovalBody(plan), TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Muted) },
					box,
					hint,
					new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
				},
			};
			dialog.Opened += (s, e) => cancel.Focus();
			return dialog;
		}
	}
}
