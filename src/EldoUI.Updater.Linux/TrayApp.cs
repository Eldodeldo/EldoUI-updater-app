// The updater as it lives in the panel on Linux: the tray icon (StatusNotifierItem), its menu, the timer
// that checks for a new EldoUI, the app's own quiet self-update, and the window. The same behaviour as
// the Windows TrayApp: checks every 3 hours, installs by itself, never a pop-up for the app's own update.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EldoUI.Updater.Core;

namespace EldoUI.Updater
{
	sealed class TrayApp
	{
		static readonly TimeSpan CheckEvery = TimeSpan.FromHours(3);
		const string SiteUrl = "https://eldodeldo.dk/eldoui/";

		readonly Settings _settings;
		readonly Web _web;
		readonly UpdateEngine _engine;
		readonly TrayIcon _tray;
		readonly MainWindow _form;
		readonly DispatcherTimer _timer;
		readonly NativeMenuItem _miStatus, _miOpen, _miCheck, _miSelf, _miNotes, _miFolder, _miAddOns, _miAuto, _miStartup;
		readonly NativeMenuItem _miLang, _miLangAuto, _miLangEn, _miLangDa, _miSite, _miExit;
		readonly NativeMenu _menu;
		readonly CancellationTokenSource _quit = new CancellationTokenSource();
		readonly Action _shutdown;
		ViewState _view = new ViewState();
		CheckResult _last;
		// A newer updater: shown in the window and menu, installed by itself when _appAt has passed.
		Release _app;
		DateTime _appAt;
		bool _appBusy;
		// After a failed check: wait 15 minutes, then twice as long each time, at most the normal interval.
		DateTime _retryAt = DateTime.MinValue;
		TimeSpan _backoff = TimeSpan.FromMinutes(15);
		// The check at start installs what it finds at once and quietly.
		readonly BootCheck _boot = new BootCheck(DateTime.UtcNow);
		// A window asked for at start opens once that check is done, so a self-update never closes it again.
		bool _showPending;

		public TrayApp(bool showWindow, bool justUpdated, Action shutdown)
		{
			_shutdown = shutdown;
			_settings = Settings.Load(Setup.SettingsPath);
			LinuxTexts.Use(_settings.Language);
			_web = new Web("EldoUI-Updater/" + Setup.Version + " (Linux; +https://github.com/Eldodeldo/EldoUI-updater-app)");
			_engine = new UpdateEngine(_web, Setup.TempDir, Log.Write);

			_form = new MainWindow(Assets.Logo());
			_form.CheckClicked += () => Check(true, false);
			_form.UpdateClicked += () => Check(true, true);
			_form.ChooseFolderClicked += () => _ = ChooseFolder();
			_form.OpenAddOnsClicked += OpenAddOns;
			_form.AutostartChanged += SetAutostart;
			_form.AutoInstallChanged += SetAutoInstall;
			_form.AppUpdateClicked += () => InstallApp();

			_miStatus = new NativeMenuItem { IsEnabled = false };
			_miOpen = Item(() => ShowWindow());
			_miCheck = Item(() => Check(true, false));
			_miSelf = Item(() => InstallApp());
			_miNotes = Item(() => Setup.Open(_last != null && _last.Release != null ? _last.Release.PageUrl : SiteUrl));
			_miFolder = Item(() => _ = ChooseFolder());
			_miAddOns = Item(OpenAddOns);
			_miAuto = Item(() => SetAutoInstall(!_settings.AutoInstall));
			_miAuto.ToggleType = NativeMenuItemToggleType.CheckBox;
			_miStartup = Item(() => SetAutostart(!Setup.Autostart));
			_miStartup.ToggleType = NativeMenuItemToggleType.CheckBox;
			_miLangAuto = Item(() => SetLanguage("auto"));
			_miLangEn = Item(() => SetLanguage("en"));
			_miLangEn.Header = "English";
			_miLangDa = Item(() => SetLanguage("da"));
			_miLangDa.Header = "Dansk";
			foreach (var m in new[] { _miLangAuto, _miLangEn, _miLangDa }) m.ToggleType = NativeMenuItemToggleType.Radio;
			_miLang = new NativeMenuItem { Menu = new NativeMenu { _miLangAuto, _miLangEn, _miLangDa } };
			_miSite = Item(() => Setup.Open(SiteUrl));
			_miExit = Item(Exit);
			_menu = new NativeMenu
			{
				_miStatus, new NativeMenuItemSeparator(),
				_miOpen, _miCheck, _miSelf, _miNotes, new NativeMenuItemSeparator(),
				_miFolder, _miAddOns, new NativeMenuItemSeparator(),
				_miAuto, _miStartup, _miLang, new NativeMenuItemSeparator(),
				_miSite, _miExit,
			};
			_tray = new TrayIcon { Icon = new WindowIcon(Assets.Logo()), Menu = _menu, ToolTipText = T.S("app"), IsVisible = true };
			_tray.Clicked += (s, e) => ShowWindow();

			_timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
			_timer.Tick += (s, e) =>
			{
				if (_app != null && SelfUpdatePolicy.Due(_appAt, DateTime.UtcNow)) InstallApp();
				else if (Due()) Check(false, false);
			};
			_timer.Start();

			ApplyTexts();
			// The app's own updates are never announced with a notice: the window and menu show them.
			if (justUpdated && SelfUpdatePolicy.Clear(_settings)) Save();
			FindFolder();
			UpdateView(null);
			_showPending = showWindow;
			// The first check right at start: what it finds installs before the window opens.
			DispatcherTimer.RunOnce(() => Check(showWindow, false), TimeSpan.FromMilliseconds(300));
			// A slow network does not keep the window waiting long.
			DispatcherTimer.RunOnce(ShowPending, TimeSpan.FromSeconds(8));
		}

		public TrayIcon Tray { get { return _tray; } }

		static NativeMenuItem Item(Action click)
		{
			var m = new NativeMenuItem();
			m.Click += (s, e) => click();
			return m;
		}

		bool Due()
		{
			if (DateTime.UtcNow < _retryAt) return false;
			// Until the check at start has reached GitHub, every minute counts, however recent the last check.
			if (_boot.Active(DateTime.UtcNow)) return true;
			return !_settings.LastCheck.HasValue || DateTime.UtcNow - _settings.LastCheck.Value.ToUniversalTime() >= CheckEvery;
		}

		public void ShowWindow()
		{
			_form.Show();
			if (_form.WindowState == WindowState.Minimized) _form.WindowState = WindowState.Normal;
			_form.Activate();
		}

		void ApplyTexts()
		{
			_miOpen.Header = T.S("menu.open");
			_miCheck.Header = T.S("menu.check");
			_miNotes.Header = T.S("menu.notes");
			_miFolder.Header = T.S("menu.folder");
			_miAddOns.Header = T.S("menu.addons");
			_miAuto.Header = T.S("menu.auto");
			_miStartup.Header = T.S("menu.startup");
			_miLang.Header = T.S("menu.language");
			_miLangAuto.Header = T.S("menu.lang.auto");
			_miSite.Header = T.S("menu.site");
			_miExit.Header = T.S("menu.exit");
			_form.ApplyTexts();
			RefreshMenu();
		}

		void RefreshMenu()
		{
			_miStatus.Header = _view.Installed != null ? "EldoUI " + _view.Installed : T.S("app");
			_miCheck.IsEnabled = !_engine.Busy;
			_miSelf.IsVisible = _app != null;
			_miSelf.IsEnabled = !_appBusy;
			if (_app != null) _miSelf.Header = T.F("menu.self", _app.Version);
			_miAddOns.IsEnabled = !string.IsNullOrEmpty(_settings.AddOnsDir);
			_miAuto.IsChecked = _settings.AutoInstall;
			_miStartup.IsChecked = Setup.Autostart;
			_miLangAuto.IsChecked = _settings.Language != "en" && _settings.Language != "da";
			_miLangEn.IsChecked = _settings.Language == "en";
			_miLangDa.IsChecked = _settings.Language == "da";
		}

		/// <summary>
		/// Finds WoW: Forever when no folder is set, and moves to the released game's _forever_ folder when it
		/// appears (next to the beta, or in another prefix). Nothing changes when the folder in use is already
		/// the released game.
		/// </summary>
		void FindFolder()
		{
			string cur = _settings.AddOnsDir;
			bool valid = false;
			try { valid = !string.IsNullOrEmpty(cur) && Directory.Exists(Path.GetDirectoryName(Path.GetDirectoryName(cur.TrimEnd('/')))); }
			catch { }
			if (valid && WowFolders.Rank(cur) == 0) return;
			string better = valid ? WowFolders.Better(cur) : null;
			if (better == null)
			{
				string found = WowSearch.Find();
				if (found != null && (!valid || WowFolders.Rank(found) < WowFolders.Rank(cur))) better = found;
			}
			if (better == null || better == cur) return;
			// A different game folder: what the last install put elsewhere is not ours to remove here.
			if (valid) _settings.InstalledFolders.Clear();
			_settings.AddOnsDir = better;
			Log.Write((valid ? "Moved to WoW: Forever AddOns at " : "Found WoW: Forever AddOns at ") + better);
			Save();
		}

		/// <summary>Checks for a new EldoUI. user: the player asked. install: install even if automatic install is off.</summary>
		async void Check(bool user, bool install)
		{
			if (_engine.Busy) return;
			FindFolder();
			if (string.IsNullOrEmpty(_settings.AddOnsDir))
			{
				UpdateView(null);
				if (user) ShowWindow();
				else if (!_settings.Welcomed) Notify(T.S("toast.nofolder.title"), T.S("toast.nofolder.body"));
				_settings.Welcomed = true;
				Save();
			}
			_view.Busy = true;
			_view.Kind = StatusKind.Busy;
			_view.Status = T.S("state.checking");
			_form.Render(_view);
			CheckResult r;
			try
			{
				r = await _engine.CheckAsync(_settings, install, _quit.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			_last = r;
			bool booting = _boot.Active(DateTime.UtcNow);
			if ((r.Outcome == CheckOutcome.Offline || r.Outcome == CheckOutcome.Failed) && booting)
			{
				// At start the network may not be up yet (a login): try again in a minute.
				_retryAt = DateTime.UtcNow + BootCheck.RetryEvery;
			}
			else if (r.Outcome == CheckOutcome.Offline || r.Outcome == CheckOutcome.Failed)
			{
				_retryAt = DateTime.UtcNow + _backoff;
				_backoff = TimeSpan.FromTicks(Math.Min(_backoff.Ticks * 2, CheckEvery.Ticks));
			}
			else
			{
				_retryAt = DateTime.MinValue;
				_backoff = TimeSpan.FromMinutes(15);
			}
			Save();
			UpdateView(r);
			Announce(r, user, booting);
			await CheckApp(booting);
			if (booting && r.Outcome != CheckOutcome.Offline && r.Outcome != CheckOutcome.Failed) _boot.Finish();
			ShowPending();
		}

		void ShowPending()
		{
			if (!_showPending) return;
			_showPending = false;
			ShowWindow();
		}

		void Announce(CheckResult r, bool user, bool booting)
		{
			switch (r.Outcome)
			{
				case CheckOutcome.Installed:
					_settings.AnnouncedVersion = r.Installed;
					Save();
					// At start the install is quiet: the window shows the new version.
					if (!booting && (!user || !_form.IsVisible)) Notify(T.F("toast.installed.title", r.Installed), T.S("toast.installed.body"));
					break;
				case CheckOutcome.UpdateAvailable:
					// Each new version is announced once, not on every check.
					if (r.Latest != _settings.AnnouncedVersion)
					{
						_settings.AnnouncedVersion = r.Latest;
						Save();
						if (!user) Notify(T.F("toast.available.title", r.Latest), T.S("toast.available.body"));
					}
					break;
				case CheckOutcome.Failed:
				case CheckOutcome.Unverified:
					if (!user && r.Latest != null && r.Latest != _settings.AnnouncedVersion)
					{
						_settings.AnnouncedVersion = r.Latest;
						Save();
						Notify(T.S("toast.failed.title"), r.Outcome == CheckOutcome.Unverified ? T.F("state.unverified", r.Latest) : T.S("state.failed"));
					}
					break;
			}
		}

		void UpdateView(CheckResult r)
		{
			var v = new ViewState
			{
				Installed = AddonInstaller.InstalledVersion(_settings.AddOnsDir),
				Latest = r != null ? r.Latest : _view.Latest,
				Folder = _settings.AddOnsDir,
				LastCheck = _settings.LastCheck,
				Autostart = Setup.Autostart,
				AutoInstall = _settings.AutoInstall,
				AppNew = _app != null ? _app.Version : null,
				AppAt = _app != null ? (DateTime?)_appAt : null,
				AppBusy = _appBusy,
			};
			if (string.IsNullOrEmpty(_settings.AddOnsDir))
			{
				v.Kind = StatusKind.Warn;
				v.Status = T.S("state.nofolder");
				v.Action = MainAction.ChooseFolder;
			}
			else if (r == null)
			{
				v.Kind = StatusKind.Info;
				v.Status = !string.IsNullOrEmpty(_view.Status) && !_view.Busy ? _view.Status : T.S("state.checking");
			}
			else
			{
				switch (r.Outcome)
				{
					case CheckOutcome.UpToDate:
						v.Kind = StatusKind.Ok; v.Status = T.S("state.uptodate"); break;
					case CheckOutcome.Installed:
						v.Kind = StatusKind.Ok; v.Status = T.F("state.installed", r.Installed); break;
					case CheckOutcome.UpdateAvailable:
						v.Kind = StatusKind.Info; v.Status = T.F("state.available", r.Latest);
						v.Action = v.Installed == null ? MainAction.Install : MainAction.Update; break;
					case CheckOutcome.Offline:
						v.Kind = StatusKind.Warn; v.Status = T.S("state.offline"); break;
					case CheckOutcome.NoDownload:
						v.Kind = StatusKind.Info; v.Status = T.S("state.nodownload"); break;
					case CheckOutcome.Unverified:
						v.Kind = StatusKind.Error; v.Status = T.F("state.unverified", r.Latest); break;
					case CheckOutcome.NoFolder:
						v.Kind = StatusKind.Warn; v.Status = T.S("state.nofolder"); v.Action = MainAction.ChooseFolder; break;
					default:
						v.Kind = StatusKind.Error;
						v.Status = r.Error != null && r.Error.Contains("wait") ? T.S("state.ratelimited") : T.S("state.failed");
						break;
				}
			}
			_view = v;
			_form.Render(v);
			_tray.ToolTipText = v.Installed != null ? T.F("tip.idle", v.Installed) : T.S("tip.idle.none");
			RefreshMenu();
		}

		/// <summary>
		/// Looks for a newer updater at every check. Found at start, it installs at once; found later, it is only shown
		/// and installs itself 24 hours after it was first seen.
		/// </summary>
		async Task CheckApp(bool booting)
		{
			if (!_settings.SelfUpdate || _appBusy) return;
			var r = await _engine.NewerUpdaterAsync(Setup.Version, _quit.Token, UpdateEngine.LinuxUpdaterAssetPattern);
			if (r == null)
			{
				// Also when GitHub could not be reached: a version already seen keeps its place.
				bool reached = _last != null && _last.Outcome != CheckOutcome.Offline && _last.Outcome != CheckOutcome.Failed;
				if (_app == null && reached && SelfUpdatePolicy.Clear(_settings)) Save();
				return;
			}
			bool first = _app == null || _app.Version != r.Version;
			_app = r;
			_appAt = SelfUpdatePolicy.Note(_settings, r.Version, DateTime.UtcNow);
			Save();
			if (booting) Log.Write("Updater " + r.Version + " is out; installing it now, at start");
			else if (first) Log.Write("Updater " + r.Version + " is out; it installs itself at " + _appAt.ToLocalTime().ToString("g"));
			UpdateView(_last);
			if (booting || SelfUpdatePolicy.Due(_appAt, DateTime.UtcNow)) await InstallAppAsync();
		}

		async void InstallApp()
		{
			await InstallAppAsync();
		}

		/// <summary>Downloads the newer AppImage, checks it against GitHub's checksum, puts it in place and restarts into it.</summary>
		async Task InstallAppAsync()
		{
			if (_app == null || _appBusy) return;
			if (!Setup.RunningInstalled)
			{
				// A copy started from elsewhere cannot replace the installed one; the installed copy does it.
				Log.Write("Updater " + _app.Version + " not installed: this copy runs from " + Setup.CurrentApp);
				return;
			}
			_appBusy = true;
			UpdateView(_last);
			string target = Setup.InstalledApp + ".download";
			try
			{
				await _engine.DownloadUpdaterAsync(_app, target, _quit.Token, UpdateEngine.LinuxUpdaterAssetPattern);
				Log.Write("Updating the updater to " + _app.Version);
				Setup.ReplaceAndRestart(target, _showPending || _form.IsVisible);
				Exit();
			}
			catch (OperationCanceledException)
			{
				try { File.Delete(target); } catch { }
			}
			catch (Exception ex)
			{
				Log.Write("Updater self-update failed: " + ex.Message);
				try { File.Delete(target); } catch { }
				// Try again at the next check, not every minute.
				_appAt = DateTime.UtcNow + CheckEvery;
				_appBusy = false;
				UpdateView(_last);
			}
		}

		async Task ChooseFolder()
		{
			ShowWindow();
			var picked = await _form.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
			{
				Title = T.S("folder.prompt"),
				AllowMultiple = false,
			});
			if (picked == null || picked.Count == 0) return;
			string path = picked[0].TryGetLocalPath();
			string addOns = path != null ? WowSearch.Resolve(path) : null;
			if (addOns == null)
			{
				_view.Kind = StatusKind.Warn;
				_view.Status = T.S("folder.bad.title") + ". " + T.S("folder.bad.body");
				_form.Render(_view);
				return;
			}
			if (addOns != _settings.AddOnsDir) _settings.InstalledFolders.Clear();
			_settings.AddOnsDir = addOns;
			Save();
			Log.Write("AddOns folder chosen: " + addOns);
			UpdateView(_last);
			Check(true, false);
		}

		void OpenAddOns()
		{
			if (string.IsNullOrEmpty(_settings.AddOnsDir)) return;
			try { Directory.CreateDirectory(_settings.AddOnsDir); } catch { }
			Setup.Open(_settings.AddOnsDir);
		}

		void SetAutostart(bool on)
		{
			try { Setup.Autostart = on; }
			catch (Exception ex) { Log.Write("Autostart not changed: " + ex.Message); }
			UpdateView(_last);
		}

		void SetAutoInstall(bool on)
		{
			_settings.AutoInstall = on;
			Save();
			UpdateView(_last);
			if (on && _last != null && _last.Outcome == CheckOutcome.UpdateAvailable) Check(true, true);
		}

		void SetLanguage(string lang)
		{
			_settings.Language = lang;
			Save();
			LinuxTexts.Use(lang);
			ApplyTexts();
			UpdateView(_last);
		}

		/// <summary>A desktop notice through notify-send, when the desktop has it. Never for the app's own updates.</summary>
		static void Notify(string title, string body)
		{
			try
			{
				var psi = new ProcessStartInfo("notify-send") { UseShellExecute = false };
				psi.ArgumentList.Add("--app-name=EldoUI Updater");
				if (File.Exists(Setup.IconPath)) psi.ArgumentList.Add("--icon=" + Setup.IconPath);
				psi.ArgumentList.Add(title);
				psi.ArgumentList.Add(body ?? "");
				Process.Start(psi);
			}
			catch (Exception ex) { Log.Write("No desktop notice (" + ex.Message + "): " + title); }
		}

		void Save()
		{
			try { _settings.Save(Setup.SettingsPath); }
			catch (Exception ex) { Log.Write("Settings not saved: " + ex.Message); }
		}

		public void Exit()
		{
			_quit.Cancel();
			_timer.Stop();
			_tray.IsVisible = false;
			_tray.Dispose();
			_form.AllowClose = true;
			_form.Close();
			_shutdown();
		}
	}
}
