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
		public bool AllowClose;

		readonly TextBlock _heading, _intro, _message, _note, _empty;
		readonly Button _tabFind, _tabInstalled, _install, _recommended, _import, _export, _close;
		readonly TextBox _search;
		readonly ComboBox _category;
		readonly CheckBox _showHidden;
		readonly StackPanel _list = new StackPanel();
		readonly ScrollViewer _scroll;
		AddonsView _view = new AddonsView();
		bool _installedTab;
		bool _rendering;

		/// <summary>The rows shown now, for the self-test.</summary>
		public List<AddonRow> Rows { get; private set; } = new List<AddonRow>();

		public AddonsWindow(Avalonia.Media.Imaging.Bitmap logo)
		{
			Width = 680;
			Height = 620;
			CanResize = false;
			WindowStartupLocation = WindowStartupLocation.CenterScreen;
			Background = Palette.B(Palette.Panel);
			Foreground = Palette.B(Palette.Text);
			FontSize = 13;
			if (logo != null) Icon = new WindowIcon(logo);

			var canvas = new Canvas { Width = 680, Height = 620 };
			void Place(Control c, double x, double y) { Canvas.SetLeft(c, x); Canvas.SetTop(c, y); canvas.Children.Add(c); }

			_heading = new TextBlock { FontSize = 19, FontWeight = FontWeight.Bold };
			Place(_heading, 22, 14);
			_intro = new TextBlock { Width = 636, TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Muted), FontSize = 12.5 };
			Place(_intro, 22, 48);

			_tabFind = MainWindow.MakeButton(150, true);
			_tabInstalled = MainWindow.MakeButton(150, false);
			_tabFind.Height = _tabInstalled.Height = 32;
			_tabFind.Click += (s, e) => ShowInstalled(false);
			_tabInstalled.Click += (s, e) => ShowInstalled(true);
			Place(_tabFind, 22, 96);
			Place(_tabInstalled, 178, 96);
			_search = new TextBox { Width = 170, Height = 30, Background = Palette.B(Palette.Bg), Foreground = Palette.B(Palette.Text), BorderBrush = Palette.B(Palette.EdgeStrong) };
			_search.TextChanged += (s, e) => { if (!_rendering) Fill(); };
			Place(_search, 344, 97);
			_category = new ComboBox { Width = 136, Height = 30, Background = Palette.B(Palette.Bg), Foreground = Palette.B(Palette.Text), BorderBrush = Palette.B(Palette.EdgeStrong) };
			_category.SelectionChanged += (s, e) => { if (!_rendering) Fill(); };
			Place(_category, 522, 97);
			_showHidden = new CheckBox { Width = 314, Foreground = Palette.B(Palette.Text), IsVisible = false };
			_showHidden.IsCheckedChanged += (s, e) => { if (!_rendering) Fill(); };
			Place(_showHidden, 344, 96);

			var frame = new Border { Width = 636, Height = 370, Background = Palette.B(Palette.Bg), BorderBrush = Palette.B(Palette.Edge), BorderThickness = new Thickness(1) };
			_scroll = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
			_empty = new TextBlock { Foreground = Palette.B(Palette.Muted), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20, 28), TextAlignment = TextAlignment.Center };
			frame.Child = new Panel { Children = { _scroll, _empty } };
			Place(frame, 22, 138);

			_message = new TextBlock { Width = 636, FontSize = 11.5, Foreground = Palette.B(Palette.Accent), TextTrimming = TextTrimming.CharacterEllipsis };
			Place(_message, 22, 514);
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
			Place(_install, 22, 538);
			Place(_recommended, 198, 538);
			Place(_import, 374, 538);
			Place(_export, 476, 538);
			Place(_close, 578, 538);
			_note = new TextBlock { Width = 600, FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Palette.B(Palette.Muted) };
			Place(_note, 40, 580);

			Content = canvas;
			KeyDown += (s, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
			ApplyTexts();
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
			_installedTab = installed;
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
			_showHidden.IsVisible = _installedTab;
			int hidden = AddonRows.HiddenCount(_view);
			_rendering = true;
			_showHidden.Content = T.F("addons.showhidden", hidden);
			_showHidden.IsEnabled = hidden > 0;
			if (hidden == 0) _showHidden.IsChecked = false;
			_rendering = false;
			string empty;
			if (_installedTab)
			{
				Rows = AddonRows.Installed(_view, _showHidden.IsChecked == true);
				empty = !_view.HasFolder ? T.S("addons.nofolder") : T.S("addons.empty");
			}
			else
			{
				string category = _category.SelectedIndex > 0 ? ((Tuple<string, string>)_category.SelectedItem).Item1 : null;
				Rows = AddonRows.Find(_view, _search.Text, category);
				empty = T.S("addons.none");
			}
			_list.Children.Clear();
			foreach (var row in Rows) _list.Children.Add(RowControl(row));
			_empty.Text = empty;
			_empty.IsVisible = Rows.Count == 0;
		}

		Control RowControl(AddonRow row)
		{
			bool enabled = !_view.Busy;
			var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(10, 8, 6, 8) };
			if (row.Checked != null)
			{
				var box = new CheckBox { IsChecked = row.Checked == true, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, 4, 0) };
				box.IsCheckedChanged += (s, e) => Raise(row.Checked == true ? AddonAction.Deselect : AddonAction.Select, row.Key);
				grid.Children.Add(box);
			}
			var title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
			title.Inlines.Add(new Avalonia.Controls.Documents.Run(row.Title) { FontWeight = FontWeight.Bold, Foreground = Palette.B(row.Tone == Tone.Info && row.Checked == null ? Palette.Accent : Palette.Text) });
			if (!string.IsNullOrEmpty(row.Meta)) title.Inlines.Add(new Avalonia.Controls.Documents.Run("  " + row.Meta) { FontSize = 11.5, Foreground = Palette.B(Palette.Muted) });
			var detail = new TextBlock { Text = row.Detail ?? "", FontSize = 11.5, Foreground = Palette.B(Palette.Muted), TextTrimming = TextTrimming.CharacterEllipsis };
			var text = new StackPanel { Spacing = 4, Children = { title, detail } };
			if (!string.IsNullOrEmpty(row.Status))
			{
				Color tone = row.Tone == Tone.Ok ? Palette.Ok : row.Tone == Tone.Warn ? Palette.Warn : row.Tone == Tone.Error ? Palette.Err : row.Tone == Tone.Info ? Palette.Accent : Palette.Muted;
				text.Children.Add(new TextBlock { Text = row.Status, FontSize = 11.5, Foreground = Palette.B(tone), TextTrimming = TextTrimming.CharacterEllipsis });
			}
			if (row.Tip != null) ToolTip.SetTip(text, row.Tip);
			Grid.SetColumn(text, 1);
			grid.Children.Add(text);
			if (row.Button != null)
			{
				var b = MainWindow.MakeButton(104, false);
				b.Height = 28;
				b.FontSize = 12;
				b.Content = row.ButtonText;
				b.IsEnabled = enabled;
				b.VerticalAlignment = VerticalAlignment.Center;
				b.Margin = new Thickness(8, 0, 0, 0);
				b.Click += (s, e) => Choose(new MenuChoice { Action = row.Button.Value, Arg = row.Button == AddonAction.Add ? row.Key.Substring("suggest:".Length) : MenuArg(row, row.Button.Value) });
				Grid.SetColumn(b, 2);
				grid.Children.Add(b);
			}
			if (row.Menu.Count > 0)
			{
				var more = MainWindow.MakeButton(34, false);
				more.Height = 28;
				more.Content = "⋯";
				more.VerticalAlignment = VerticalAlignment.Center;
				more.Margin = new Thickness(8, 0, 0, 0);
				ToolTip.SetTip(more, T.S("addons.more"));
				more.Click += (s, e) => ShowMenu(row, more);
				Grid.SetColumn(more, 3);
				grid.Children.Add(more);
			}
			return new Border { Child = grid, BorderBrush = Palette.B(Palette.Edge), BorderThickness = new Thickness(0, 0, 0, 1) };
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
				var item = new MenuItem { Header = c.Text, IsEnabled = !_view.Busy || c.Action == AddonAction.OpenPage };
				if (c.Checked != null) { item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = c.Checked == true; }
				item.Click += (s, e) => Choose(c);
				items.Add(item);
			}
			new ContextMenu { ItemsSource = items }.Open(anchor);
		}

		async void Choose(MenuChoice c)
		{
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
			if (_rendering || (_view.Busy && action != AddonAction.OpenPage)) return;
			ActionRequested?.Invoke(action, arg);
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
	}
}
