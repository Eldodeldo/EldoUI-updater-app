// The Linux setup assistant: checks the system, installs Lightforge (a separate program by its own authors
// that installs Battle.net and WoW: Forever) from its pinned, checksum-checked release, and shows what
// Lightforge reports. Every step that changes something is a button the player presses; the assistant never
// asks for a password, never installs system packages and never starts Battle.net or the game.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EldoUI.Updater.Core;

namespace EldoUI.Updater
{
	sealed class SetupWindow : Window
	{
		/// <summary>The player chose Lightforge's WoW: Forever as the updater's AddOns folder.</summary>
		public event Action<string> UseFolder;
		public bool AllowClose;

		readonly IWeb _web;
		readonly IProcessRunner _runner;
		readonly LightforgePlan _plan;
		readonly string _home, _work;
		readonly TextBlock _heading, _intro, _systemHead, _lightforgeHead, _servicesHead, _services, _license, _state, _forever, _message;
		readonly StackPanel _checks = new StackPanel { Spacing = 6 };
		readonly Button _install, _open, _use, _again, _page, _source, _close;
		List<SystemCheck> _system = new List<SystemCheck>();
		LightforgeStatus _status = new LightforgeStatus();
		CancellationTokenSource _cts;
		bool _busy;

		public SetupWindow(Avalonia.Media.Imaging.Bitmap logo, IWeb web, IProcessRunner runner, string home, string xdgDataHome, string work)
		{
			_web = web;
			_runner = runner;
			_home = home;
			_work = work;
			_plan = LightforgePlan.For(home, xdgDataHome);
			Width = 600;
			Height = 720;
			CanResize = false;
			WindowStartupLocation = WindowStartupLocation.CenterScreen;
			Background = Palette.B(Palette.Panel);
			Foreground = Palette.B(Palette.Text);
			FontSize = 13;
			if (logo != null) Icon = new WindowIcon(logo);

			TextBlock Head() { return new TextBlock { FontSize = 14.5, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 8, 0, 0) }; }
			TextBlock Body(bool muted = true) { return new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(muted ? Palette.Muted : Palette.Text), FontSize = 12.5 }; }
			_heading = new TextBlock { FontSize = 19, FontWeight = FontWeight.Bold };
			_intro = Body();
			_systemHead = Head();
			_lightforgeHead = Head();
			_state = Body(false);
			_forever = Body();
			_servicesHead = Head();
			_services = Body();
			_license = Body();
			_license.FontSize = 11.5;
			_message = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Palette.B(Palette.Accent) };

			_install = MainWindow.MakeButton(190, true);
			_open = MainWindow.MakeButton(150, false);
			_use = MainWindow.MakeButton(190, false);
			_again = MainWindow.MakeButton(130, false);
			_page = MainWindow.MakeButton(150, false);
			_source = MainWindow.MakeButton(150, false);
			_close = MainWindow.MakeButton(100, false);
			foreach (var b in new[] { _install, _open, _use, _again, _page, _source, _close }) b.Height = 34;
			// The install button names the version, so it takes the width its text needs.
			_install.Width = double.NaN;
			_install.Padding = new Thickness(18, 0);
			_install.Click += async (s, e) => await InstallAsync();
			_open.Click += (s, e) => OpenLightforge();
			_use.Click += (s, e) => { string addOns = WowFolders.ResolveAddOns(_status.ForeverDir); if (addOns != null) { UseFolder?.Invoke(addOns); _message.Text = T.F("lf.used", addOns); } };
			_again.Click += async (s, e) => await RefreshAsync();
			_page.Click += (s, e) => Setup.OpenPage(LightforgePackage.Page);
			_source.Click += (s, e) => Setup.OpenPage(LightforgePackage.Source);
			_close.Click += (s, e) => Hide();

			StackPanel Row(params Control[] items) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var i in items) p.Children.Add(i); return p; }
			var content = new StackPanel
			{
				Margin = new Thickness(22, 16, 22, 16), Spacing = 8,
				Children =
				{
					_heading, _intro,
					_systemHead, _checks,
					_lightforgeHead, _state, _forever,
					Row(_install, _open, _again),
					Row(_use),
					_message,
					_servicesHead, _services, _license,
					Row(_page, _source),
					new Border { Height = 1, Background = Palette.B(Palette.Edge), Margin = new Thickness(0, 6) },
					new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _close } },
				},
			};
			Content = new ScrollViewer { Content = content };
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
			Title = T.S("lf.title");
			_heading.Text = T.S("lf.title");
			_intro.Text = T.F("lf.intro", LightforgePackage.Version);
			_systemHead.Text = T.S("lf.section.system");
			_lightforgeHead.Text = T.S("lf.section.lightforge");
			_servicesHead.Text = T.S("lf.section.services");
			_services.Text = T.S("lf.services");
			_license.Text = T.F("lf.license", LightforgePackage.Version, LightforgePackage.License);
			_open.Content = T.S("lf.btn.open");
			_use.Content = T.S("lf.btn.use");
			_again.Content = T.S("lf.btn.check");
			_page.Content = T.S("lf.btn.page");
			_source.Content = T.S("lf.btn.source");
			_close.Content = T.S("btn.close");
			Render(_system, _status);
		}

		/// <summary>Reads the system and asks Lightforge about itself with its read-only commands.</summary>
		public async Task RefreshAsync()
		{
			if (_busy) return;
			SetBusy(true, T.S("lf.busy.read"));
			try
			{
				_system = LightforgeChecks.Evaluate(await Task.Run(() => SystemInfo.Read(_home)));
				_status = await new LightforgeProbe(_runner).ReadAsync(_plan, CancellationToken.None);
				_message.Text = "";
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
			{
				Log.Write("Lightforge state not read: " + ex.Message);
				_status = new LightforgeStatus { Installed = File.Exists(_plan.Cli), Problem = "lf.problem.status" };
				_message.Text = "";
			}
			finally { SetBusy(false, null); }
			Render(_system, _status);
		}

		async Task InstallAsync()
		{
			if (_busy || !LightforgeChecks.CanInstall(_system)) return;
			_cts = new CancellationTokenSource();
			string work = System.IO.Path.Combine(_work, "lightforge");
			try
			{
				SetBusy(true, T.F("lf.busy.download", LightforgePackage.Version));
				string tar = await LightforgeInstaller.DownloadAsync(_web, work, _cts.Token);
				SetBusy(true, T.S("lf.busy.unpack"));
				string unpacked = await Task.Run(() => LightforgeInstaller.Unpack(tar, work, _cts.Token));
				SetBusy(true, T.S("lf.busy.place"));
				await Task.Run(() => LightforgeInstaller.Place(unpacked, _plan, _cts.Token));
				Log.Write("Lightforge " + LightforgePackage.Version + " installed in " + _plan.Bin);
				SetBusy(false, null);
				await RefreshAsync();
				_message.Foreground = Palette.B(Palette.Ok);
				_message.Text = T.F("lf.done", LightforgePackage.Version);
			}
			catch (Exception ex) when (!(ex is OutOfMemoryException))
			{
				Log.Write("Lightforge not installed: " + ex.Message);
				SetBusy(false, null);
				_message.Foreground = Palette.B(Palette.Warn);
				_message.Text = T.F("lf.failed", ex.Message);
				Render(_system, _status);
			}
			finally
			{
				try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
				_cts.Dispose();
				_cts = null;
			}
		}

		void OpenLightforge()
		{
			if (!File.Exists(_plan.Gui)) return;
			try { _runner.Start(_plan.Gui, new string[0]); }
			catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) { Log.Write("Lightforge did not open: " + ex.Message); }
		}

		void SetBusy(bool busy, string text)
		{
			_busy = busy;
			if (busy) { _message.Foreground = Palette.B(Palette.Accent); _message.Text = text; }
			Render(_system, _status);
		}

		/// <summary>Shows a system check and a Lightforge state (the real ones, or made-up ones for the pictures CI takes).</summary>
		public void Render(List<SystemCheck> system, LightforgeStatus status)
		{
			_system = system ?? new List<SystemCheck>();
			_status = status ?? new LightforgeStatus();
			_checks.Children.Clear();
			foreach (var c in _system) _checks.Children.Add(CheckLine(c));
			if (_system.Count == 0) _checks.Children.Add(new TextBlock { Text = T.S("lf.busy.read"), Foreground = Palette.B(Palette.Muted) });

			var s = _status;
			string binDir = _plan.Bin.StartsWith(_home + "/", StringComparison.Ordinal) ? "~" + _plan.Bin.Substring(_home.Length) : _plan.Bin;
			if (!s.Installed) _state.Text = T.F("lf.state.none", binDir);
			else if (s.Problem == "lf.problem.unknown") _state.Text = T.F("lf.problem.unknown", s.Version, LightforgePackage.Version);
			else if (s.Problem != null) _state.Text = T.S(s.Problem);
			else if (s.Ready == true) _state.Text = T.F("lf.state.ready", s.Version);
			else _state.Text = T.F("lf.state.outstanding", s.Version, string.Join(", ", s.Outstanding));
			_state.Foreground = Palette.B(s.Problem != null ? Palette.Warn : s.Installed ? Palette.Ok : Palette.Text);
			_forever.Text = s.ForeverDir == null ? "" : s.ForeverInstalled ? T.F("lf.state.forever", s.ForeverDir) : T.S("lf.state.noforever");
			_forever.IsVisible = s.ForeverDir != null;

			bool sameVersion = s.Installed && s.Version == LightforgePackage.Version && s.Problem == null;
			_install.Content = T.F(s.Installed ? "lf.btn.reinstall" : "lf.btn.install", LightforgePackage.Version);
			_install.IsEnabled = !_busy && _system.Count > 0 && LightforgeChecks.CanInstall(_system);
			MainWindow.Paint(_install, !sameVersion);
			_open.IsEnabled = !_busy && s.Installed && File.Exists(_plan.Gui);
			_use.IsEnabled = !_busy && s.ForeverAddOns != null;
			_use.IsVisible = s.ForeverDir != null;
			_again.IsEnabled = !_busy;
		}

		Control CheckLine(SystemCheck c)
		{
			Color color = c.State == CheckState.Ok ? Palette.Ok : c.State == CheckState.Warn ? Palette.Warn : Palette.Err;
			var dot = new Ellipse { Width = 9, Height = 9, Fill = Palette.B(color), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 8, 0) };
			var name = new TextBlock { Text = T.S(c.Key), Width = 190 };
			var found = new TextBlock { Text = c.Found ?? (c.State == CheckState.Ok ? T.S("lf.found.yes") : T.S("lf.found.none")), Foreground = Palette.B(Palette.Muted), TextTrimming = TextTrimming.CharacterEllipsis, Width = 330 };
			var line = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, name, found } };
			if (c.State == CheckState.Ok) return line;
			string fix = "lf.fix." + c.Key.Substring("lf.check.".Length) + (c.Key == "lf.check.glibc" ? (c.State == CheckState.Warn ? ".warn" : ".fail") : "");
			return new StackPanel
			{
				Children =
				{
					line,
					new TextBlock { Text = T.S(fix), TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Palette.B(color), Margin = new Thickness(17, 2, 0, 0) },
				},
			};
		}
	}
}
