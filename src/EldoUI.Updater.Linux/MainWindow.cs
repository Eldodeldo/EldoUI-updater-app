// The updater's one window on Linux, laid out like the Windows window: what is installed, what is
// newest, where WoW is, the app's own version, and the main actions. Built in code, no XAML.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace EldoUI.Updater
{
	enum StatusKind { Busy, Ok, Info, Warn, Error }
	enum MainAction { Check, Update, Install, ChooseFolder }

	sealed class ViewState
	{
		public string Installed;
		public string Latest;
		public string Folder;
		public DateTime? LastCheck;
		public StatusKind Kind = StatusKind.Info;
		public string Status = "";
		public string ErrorDetail;
		public MainAction Action = MainAction.Check;
		public bool Busy;
		public bool Autostart;
		public bool AutoInstall;
		public bool ExtraAddons;
		public string ExtraStatus;
		public string AppNew;
		public DateTime? AppAt;
		public bool AppBusy;
	}

	/// <summary>EldoUI's colours, the same as the website and the Windows app.</summary>
	static class Palette
	{
		public static readonly Color Bg = Color.Parse("#060b16");
		public static readonly Color Glow = Color.Parse("#0e1d3a");
		public static readonly Color Panel = Color.Parse("#0a1426");
		public static readonly Color Panel2 = Color.Parse("#0e1b33");
		public static readonly Color Edge = Color.Parse("#1b2d4d");
		public static readonly Color EdgeStrong = Color.Parse("#2a4470");
		public static readonly Color Text = Color.Parse("#e6ecf6");
		public static readonly Color Muted = Color.Parse("#93a3c0");
		public static readonly Color Accent = Color.Parse("#4fa8ee");
		public static readonly Color Gold = Color.Parse("#f0c46c");
		public static readonly Color Gold2 = Color.Parse("#d9a443");
		public static readonly Color GoldInk = Color.Parse("#2a1a02");
		public static readonly Color Ornament = Color.Parse("#b8925a");
		public static readonly Color Ok = Color.Parse("#4cc38a");
		public static readonly Color Warn = Color.Parse("#e5a54b");
		public static readonly Color Err = Color.Parse("#ec6a6a");

		public static IBrush B(Color c) { return new SolidColorBrush(c); }
	}

	sealed class MainWindow : Window
	{
		public event Action CheckClicked, UpdateClicked, ChooseFolderClicked, OpenAddOnsClicked, AppUpdateClicked;
		public event Action<bool> AutostartChanged, AutoInstallChanged, ExtraAddonsChanged;
		public bool AllowClose;

		readonly Canvas _canvas = new Canvas { Width = 470, Height = 482 };
		readonly TextBlock _title, _tagline, _status, _footer, _appUpdate;
		readonly TextBlock[] _labels = new TextBlock[5];
		readonly TextBlock _installed, _latest, _folder, _checked, _app;
		readonly Ellipse _dot, _halo;
		readonly CheckBox _autostart, _autoInstall, _extras;
		readonly Button _primary, _chooseFolder, _openAddOns;
		ViewState _state = new ViewState();
		bool _rendering;

		public MainWindow(Bitmap logo)
		{
			Title = "EldoUI Updater";
			Width = 470;
			Height = 482;
			CanResize = false;
			WindowStartupLocation = WindowStartupLocation.CenterScreen;
			Background = Palette.B(Palette.Panel);
			Foreground = Palette.B(Palette.Text);
			FontSize = 13;
			if (logo != null) Icon = new WindowIcon(logo);

			// Header band: the same deep blue gradient as the website's hero, a gold-edged troll and the name.
			var band = new Border
			{
				Width = 470, Height = 96,
				Background = new LinearGradientBrush
				{
					StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
					EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
					GradientStops = { new GradientStop(Palette.Glow, 0), new GradientStop(Palette.Panel, 1) },
				},
				BorderBrush = Palette.B(Palette.Edge),
				BorderThickness = new Thickness(0, 0, 0, 1),
			};
			Place(band, 0, 0);
			var logoCircle = new Ellipse
			{
				Width = 60, Height = 60,
				Fill = logo != null ? new ImageBrush(logo) { Stretch = Stretch.UniformToFill } : Palette.B(Palette.Panel2),
				Stroke = Palette.B(Palette.Ornament), StrokeThickness = 1.5,
			};
			Place(logoCircle, 22, 18);
			_title = new TextBlock { FontSize = 21, FontWeight = FontWeight.Bold, Foreground = Palette.B(Palette.Text) };
			Place(_title, 98, 22);
			_tagline = new TextBlock { FontSize = 12.5, Foreground = Palette.B(Palette.Muted) };
			Place(_tagline, 99, 56);

			// Status line with a coloured dot.
			_halo = new Ellipse { Width = 16, Height = 16, StrokeThickness = 3, IsVisible = false };
			Place(_halo, 21, 111);
			_dot = new Ellipse { Width = 10, Height = 10 };
			Place(_dot, 24, 114);
			_status = new TextBlock { Width = 408, TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Text) };
			Place(_status, 42, 109);
			Rule(154);

			// Details.
			string[] keys = { "label.installed", "label.latest", "label.folder", "label.checked", "label.app" };
			var values = new TextBlock[5];
			for (int i = 0; i < 5; i++)
			{
				double y = 162 + i * 26;
				_labels[i] = new TextBlock { Width = 118, Foreground = Palette.B(Palette.Muted), Tag = keys[i] };
				values[i] = new TextBlock { Width = 310, Foreground = Palette.B(Palette.Text), TextTrimming = TextTrimming.CharacterEllipsis };
				Place(_labels[i], 22, y + 2);
				Place(values[i], 140, y + 2);
			}
			_installed = values[0];
			_latest = values[1];
			_folder = values[2];
			_checked = values[3];
			_app = values[4];
			// "Update now" beside the app's own version, shown only when a newer one is out.
			_appUpdate = new TextBlock
			{
				Width = 110, TextAlignment = TextAlignment.Right, Foreground = Palette.B(Palette.Accent),
				TextDecorations = TextDecorations.Underline, Cursor = new Cursor(StandardCursorType.Hand), IsVisible = false,
			};
			_appUpdate.PointerPressed += (s, e) => { if (AppUpdateClicked != null && !_state.AppBusy) AppUpdateClicked(); };
			Place(_appUpdate, 340, 268);
			Rule(296);

			// Choices.
			_autostart = Check();
			_autoInstall = Check();
			_autostart.IsCheckedChanged += (s, e) => { if (!_rendering && AutostartChanged != null) AutostartChanged(_autostart.IsChecked == true); };
			_autoInstall.IsCheckedChanged += (s, e) => { if (!_rendering && AutoInstallChanged != null) AutoInstallChanged(_autoInstall.IsChecked == true); };
			Place(_autostart, 18, 302);
			Place(_autoInstall, 18, 330);
			_extras = Check();
			_extras.IsCheckedChanged += (sender, args) => { if (!_rendering) ExtraAddonsChanged?.Invoke(_extras.IsChecked == true); };
			Place(_extras, 18, 358);

			// Actions: one gold main button, two quiet ones.
			_primary = MakeButton(156, true);
			_chooseFolder = MakeButton(136, false);
			_openAddOns = MakeButton(116, false);
			_primary.Click += (s, e) => OnPrimary();
			_chooseFolder.Click += (s, e) => ChooseFolderClicked?.Invoke();
			_openAddOns.Click += (s, e) => OpenAddOnsClicked?.Invoke();
			Place(_primary, 22, 404);
			Place(_chooseFolder, 188, 404);
			Place(_openAddOns, 334, 404);

			_footer = new TextBlock { Width = 390, FontSize = 11, TextAlignment = TextAlignment.Center, Foreground = Palette.B(Palette.Muted) };
			Place(_footer, 40, 454);
			Ornaments();

			Content = _canvas;
			KeyDown += (s, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
			ApplyTexts();
		}

		void Place(Control c, double x, double y)
		{
			Canvas.SetLeft(c, x);
			Canvas.SetTop(c, y);
			_canvas.Children.Add(c);
		}

		void Rule(double y)
		{
			Place(new Rectangle { Width = 426, Height = 1, Fill = Palette.B(Palette.Edge) }, 22, y);
		}

		/// <summary>The gold corner ornaments at the bottom, as on the website's cards.</summary>
		void Ornaments()
		{
			var pen = Palette.B(Color.FromArgb(200, Palette.Ornament.R, Palette.Ornament.G, Palette.Ornament.B));
			Place(new Path { Data = Geometry.Parse("M 0,0 L 0,13 A 5,5 0 0 0 5,18 L 18,18"), Stroke = pen, StrokeThickness = 1 }, 8, 456);
			Place(new Path { Data = Geometry.Parse("M 18,0 L 18,13 A 5,5 0 0 1 13,18 L 0,18"), Stroke = pen, StrokeThickness = 1 }, 444, 456);
		}

		static CheckBox Check()
		{
			return new CheckBox { Foreground = Palette.B(Palette.Text), Width = 430 };
		}

		static Button MakeButton(double width, bool primary)
		{
			var b = new Button
			{
				Width = width, Height = 36,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center,
				CornerRadius = new CornerRadius(8),
				BorderThickness = new Thickness(1),
				FontWeight = primary ? FontWeight.Bold : FontWeight.SemiBold,
			};
			IBrush bg, hover, fg, edge;
			if (primary)
			{
				bg = new LinearGradientBrush
				{
					StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
					EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
					GradientStops = { new GradientStop(Palette.Gold, 0), new GradientStop(Palette.Gold2, 1) },
				};
				hover = Palette.B(Palette.Gold);
				fg = Palette.B(Palette.GoldInk);
				edge = Palette.B(Palette.Gold2);
			}
			else
			{
				bg = Palette.B(Palette.Panel2);
				hover = Palette.B(Palette.Edge);
				fg = Palette.B(Palette.Text);
				edge = Palette.B(Palette.EdgeStrong);
			}
			b.Background = bg;
			b.Foreground = fg;
			b.BorderBrush = edge;
			// Fluent swaps these in on hover and press; keep them in EldoUI's colours.
			b.Resources["ButtonBackgroundPointerOver"] = hover;
			b.Resources["ButtonBackgroundPressed"] = hover;
			b.Resources["ButtonForegroundPointerOver"] = fg;
			b.Resources["ButtonForegroundPressed"] = fg;
			b.Resources["ButtonBorderBrushPointerOver"] = edge;
			b.Resources["ButtonBorderBrushPressed"] = edge;
			b.Resources["ButtonBackgroundDisabled"] = Palette.B(Palette.Panel2);
			b.Resources["ButtonForegroundDisabled"] = Palette.B(Palette.Muted);
			return b;
		}

		protected override void OnClosing(WindowClosingEventArgs e)
		{
			// Closing the window keeps the updater running in the tray.
			if (!AllowClose)
			{
				e.Cancel = true;
				Hide();
			}
			base.OnClosing(e);
		}

		void OnPrimary()
		{
			switch (_state.Action)
			{
				case MainAction.ChooseFolder: ChooseFolderClicked?.Invoke(); break;
				case MainAction.Update:
				case MainAction.Install: UpdateClicked?.Invoke(); break;
				default: CheckClicked?.Invoke(); break;
			}
		}

		public void ApplyTexts()
		{
			Title = T.S("app");
			_title.Text = T.S("app");
			_tagline.Text = T.S("tagline");
			foreach (var l in _labels) l.Text = T.S((string)l.Tag);
			_autostart.Content = T.S("check.autostart");
			_autoInstall.Content = T.S("check.auto");
			_extras.Content = T.S("check.extras");
			_chooseFolder.Content = T.S("btn.folder");
			_openAddOns.Content = T.S("btn.addons");
			_appUpdate.Text = T.S("btn.update");
			_footer.Text = "v" + Setup.Version + " · " + T.S("footer");
			Render(_state);
		}

		public void Render(ViewState s)
		{
			_state = s;
			_rendering = true;
			try
			{
				_status.Text = s.Status;
				ToolTip.SetTip(_status, s.ErrorDetail);
				_installed.Text = s.Installed ?? T.S("value.none");
				_latest.Text = s.Latest ?? T.S("value.unknown");
				_folder.Text = string.IsNullOrEmpty(s.Folder) ? T.S("value.nofolder") : s.Folder;
				_folder.Foreground = Palette.B(string.IsNullOrEmpty(s.Folder) ? Palette.Warn : Palette.Text);
				ToolTip.SetTip(_folder, s.Folder);
				_checked.Text = Ago(s.LastCheck);
				_app.Text = AppText(s);
				_app.Foreground = Palette.B(s.AppNew != null ? Palette.Accent : Palette.Text);
				_app.Width = s.AppNew != null && !s.AppBusy ? 200 : 310;
				_appUpdate.IsVisible = s.AppNew != null && !s.AppBusy;
				_autostart.IsChecked = s.Autostart;
				_autoInstall.IsChecked = s.AutoInstall;
				_extras.IsChecked = s.ExtraAddons;
				_extras.IsEnabled = !s.Busy;
				ToolTip.SetTip(_extras, s.ExtraStatus ?? T.S("extras.hint"));
				_primary.Content = T.S(s.Action == MainAction.ChooseFolder ? "btn.folder"
					: s.Action == MainAction.Update ? "btn.update"
					: s.Action == MainAction.Install ? "btn.install" : "btn.check");
				_primary.IsEnabled = !s.Busy;
				_chooseFolder.IsVisible = s.Action != MainAction.ChooseFolder;
				_chooseFolder.IsEnabled = !s.Busy;
				_openAddOns.IsEnabled = !string.IsNullOrEmpty(s.Folder);
				Color dot = s.Kind == StatusKind.Ok ? Palette.Ok : s.Kind == StatusKind.Warn ? Palette.Warn
					: s.Kind == StatusKind.Error ? Palette.Err : Palette.Accent;
				_dot.Fill = Palette.B(dot);
				_halo.Stroke = Palette.B(Color.FromArgb(90, dot.R, dot.G, dot.B));
				_halo.IsVisible = s.Busy;
			}
			finally
			{
				_rendering = false;
			}
		}

		static string AppText(ViewState s)
		{
			if (s.AppNew == null) return T.F("value.app.current", Setup.Version);
			if (s.AppBusy) return T.F("value.app.updating", s.AppNew);
			double hours = s.AppAt.HasValue ? (s.AppAt.Value.ToUniversalTime() - DateTime.UtcNow).TotalHours : 0;
			return hours >= 1 ? T.F("value.app.new", s.AppNew, (int)Math.Ceiling(hours)) : T.F("value.app.soon", s.AppNew);
		}

		static string Ago(DateTime? when)
		{
			if (!when.HasValue) return T.S("value.never");
			var span = DateTime.UtcNow - when.Value.ToUniversalTime();
			if (span.TotalMinutes < 1) return T.S("value.justnow");
			if (span.TotalHours < 1) return T.F("value.minutes", (int)span.TotalMinutes);
			if (span.TotalHours < 48) return T.F("value.hours", (int)span.TotalHours);
			return when.Value.ToLocalTime().ToString("g");
		}
	}
}
