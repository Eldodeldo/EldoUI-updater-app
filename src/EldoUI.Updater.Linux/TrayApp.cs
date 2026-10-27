// The updater as it lives in the panel on Linux: the tray icon (StatusNotifierItem), its menu, the timer
// that checks for a new EldoUI, the app's own quiet self-update, and the window. The same behaviour as
// the Windows TrayApp: checks every 3 hours, installs by itself, never a pop-up for the app's own update.
using System;
using System.Linq;
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
		const string SiteUrl = "https://github.com/Eldodeldo/EldoUI";
		const string ReleasesUrl = "https://github.com/Eldodeldo/EldoUI/releases/latest";

		readonly Settings _settings;
		readonly Web _web;
		readonly UpdateEngine _engine;
		readonly TrayIcon _tray;
		readonly MainWindow _form;
		readonly AddonsWindow _addons;
		SetupWindow _setup;
		readonly DispatcherTimer _timer;
		readonly NativeMenuItem _miStatus, _miOpen, _miCheck, _miSelf, _miAppCheck, _miNotes, _miFolder, _miAddOns, _miSetup, _miAuto, _miStartup;
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
		bool _appChecking;
		// After a failed check: wait 15 minutes, then twice as long each time, at most the normal interval.
		DateTime _retryAt = DateTime.MinValue;
		TimeSpan _backoff = TimeSpan.FromMinutes(15);
		// The check at start installs what it finds at once and quietly.
		readonly BootCheck _boot = new BootCheck(DateTime.UtcNow);
		// A window asked for at start opens once that check is done, so a self-update never closes it again.
		bool _showPending;
		int _searchTicks;
		bool _lightforgeAsked;

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

			_form.ExtrasClicked += ShowAddons;
			_form.LinuxSetupClicked += ShowSetup;
			_addons = new AddonsWindow(Assets.Logo());
			_addons.ActionRequested += OnAddonAction;
			_addons.InstallClicked += all => {
				if (_engine.Busy) return;
				if (all) { foreach (var a in Companion.All) _settings.SelectCompanion(a.Id, true); }
				Save(); Check(true, false, true, false);
			};
			_addons.ImportChosen += ImportAddons;
			_addons.ExportChosen += ExportAddons;
			_addons.UseLayout(_settings.AddonsWindow);
			_addons.LayoutChanged += Save;

			_form.AppUpdateClicked += () => InstallApp();
			_form.AppCheckClicked += CheckAppNow;

			_miStatus = new NativeMenuItem { IsEnabled = false };
			_miOpen = Item(() => ShowWindow());
			_miCheck = Item(() => Check(true, false));
			_miSelf = Item(() => InstallApp());
			_miAppCheck = Item(() => CheckAppNow());
			_miNotes = Item(() => Setup.OpenPage(_last != null && _last.Release != null ? _last.Release.PageUrl : ReleasesUrl));
			_miFolder = Item(() => _ = ChooseFolder());
			_miAddOns = Item(OpenAddOns);
			_miSetup = Item(ShowSetup);
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
			_miSite = Item(() => Setup.OpenPage(SiteUrl));
			_miExit = Item(Exit);
			_menu = new NativeMenu
			{
				_miStatus, new NativeMenuItemSeparator(),
				_miOpen, _miCheck, _miSelf, _miAppCheck, _miNotes, new NativeMenuItemSeparator(),
				_miFolder, _miAddOns, _miSetup, new NativeMenuItemSeparator(),
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
				else if (string.IsNullOrEmpty(_settings.AddOnsDir)) WatchForGame();
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
			Introduce();
		}

		/// <summary>Once, the first time the window opens: what the updater does by itself, and where to turn it off.</summary>
		void Introduce()
		{
			if (_settings.Introduced) return;
			_settings.Introduced = true;
			Save();
			Notify(T.S("intro.title"), T.S("intro.body"));
		}

		void ApplyTexts()
		{
			_miOpen.Header = T.S("menu.open");
			_miCheck.Header = T.S("menu.check");
			_miAppCheck.Header = T.S("menu.appcheck");
			_miNotes.Header = T.S("menu.notes");
			_miFolder.Header = T.S("menu.folder");
			_miAddOns.Header = T.S("menu.addons");
			_miSetup.Header = T.S("menu.linuxsetup");
			_miAuto.Header = T.S("menu.auto");
			_miStartup.Header = T.S("menu.startup");
			_miLang.Header = T.S("menu.language");
			_miLangAuto.Header = T.S("menu.lang.auto");
			_miSite.Header = T.S("menu.site");
			_miExit.Header = T.S("menu.exit");
			_form.ApplyTexts();
			_addons.ApplyTexts();
			_setup?.ApplyTexts();
			RefreshMenu();
		}

		void RefreshMenu()
		{
			_miStatus.Header = _view.Installed != null ? "EldoUI " + _view.Installed : T.S("app");
			_miCheck.IsEnabled = !_engine.Busy;
			_miSelf.IsVisible = _app != null;
			_miSelf.IsEnabled = !_appBusy;
			_miAppCheck.IsEnabled = !_appBusy && !_appChecking;
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
			_settings.InstalledFolders.Clear();
			_settings.ExtraVersions.Clear();
			_settings.ExtraFiles.Clear();
			_settings.AddOnsDir = better;
			Log.Write((valid ? "Moved to WoW: Forever AddOns at " : "Found WoW: Forever AddOns at ") + better);
			Save();
		}

		/// <summary>
		/// While no WoW folder is known (a new PC, the game not installed yet): looks again every minute on disk, and
		/// every five minutes asks Lightforge where its WoW: Forever is. When the game turns up, EldoUI is installed
		/// right away instead of at the next three-hourly check. Nothing goes to the network until the game is found.
		/// </summary>
		async void WatchForGame()
		{
			if (_engine.Busy) return;
			_searchTicks++;
			FindFolder();
			if (string.IsNullOrEmpty(_settings.AddOnsDir) && _searchTicks % 5 == 0) await FindLightforgeFolder();
			if (!string.IsNullOrEmpty(_settings.AddOnsDir)) Check(false, false);
		}

		/// <summary>
		/// Lightforge's WoW: Forever, read with its read-only status command, when Lightforge is installed and nothing
		/// else was found (it may keep the game in a library on another drive). Used only once the game is installed.
		/// </summary>
		async Task FindLightforgeFolder()
		{
			if (!string.IsNullOrEmpty(_settings.AddOnsDir)) return;
			try
			{
				var plan = LightforgePlan.For(Setup.Home, Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
				if (!File.Exists(plan.Cli)) return;
				var status = await new LightforgeProbe(new ProcessRunner()).ReadAsync(plan, CancellationToken.None);
				string addOns = status.ForeverAddOns;
				if (addOns == null || !string.IsNullOrEmpty(_settings.AddOnsDir)) return;
				_settings.InstalledFolders.Clear();
				_settings.ExtraVersions.Clear();
				_settings.ExtraFiles.Clear();
				_settings.AddOnsDir = addOns;
				Log.Write("Found WoW: Forever AddOns through Lightforge at " + addOns);
				Save();
			}
			catch (Exception ex) { Log.Write("Lightforge not asked for its game folder: " + ex.Message); }
		}

		/// <summary>Checks for a new EldoUI. user: the player asked. install: install even if automatic install is off.</summary>
		async void Check(bool user, bool install, bool installExtras = false, bool showWindow = true)
		{
			if (_engine.Busy)
			{
				if (user && showWindow) ShowWindow();
				return;
			}
			FindFolder();
			// Once per run, Lightforge is asked too when nothing was found on disk (the timer asks again later).
			if (string.IsNullOrEmpty(_settings.AddOnsDir) && !_lightforgeAsked)
			{
				_lightforgeAsked = true;
				await FindLightforgeFolder();
			}
			if (string.IsNullOrEmpty(_settings.AddOnsDir))
			{
				UpdateView(null);
				if (user && showWindow) ShowWindow();
				else if (!user && !_settings.Welcomed) Notify(T.S("toast.nofolder.title"), T.S("toast.nofolder.body"));
				_settings.Welcomed = true;
				Save();
			}
			_view.Busy = true;
			_view.Kind = StatusKind.Busy;
			_view.Status = T.S("state.checking");
			_form.Render(_view);
			RefreshAddons(true);
			if (user && showWindow && !_showPending) ShowWindow();
			CheckResult r;
			try
			{
				r = await _engine.CheckAsync(_settings, install, _quit.Token, installExtras);
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
			string installed = r != null ? r.Installed : null;
			if (r == null)
			{
				try { installed = AddonInstaller.InstalledVersion(_settings.AddOnsDir); }
				catch (IOException ex) { Log.Write("Could not read installed version: " + ex.Message); }
				catch (UnauthorizedAccessException ex) { Log.Write("Could not read installed version: " + ex.Message); }
			}
			var v = new ViewState
			{
				Installed = installed,
				ErrorDetail = r != null ? r.Error : null,
				Latest = r != null ? r.Latest : _view.Latest,
				Folder = _settings.AddOnsDir,
				LastCheck = _settings.LastCheck,
				Autostart = Setup.Autostart,
				AutoInstall = _settings.AutoInstall,
				AppNew = _app != null ? _app.Version : null,
				AppAt = _app != null ? (DateTime?)_appAt : null,
				AppBusy = _appBusy,
				AppChecking = _appChecking,
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
			if (r != null && r.Companions != null && _settings.ExtraAddons)
			{
				v.ExtraStatus = string.Join("\n", r.Companions.Select(c => c.Name + ": " + T.S("extra." + c.Outcome) + (c.Error != null ? " (" + c.Error + ")" : "")));
				if (r.Companions.Any(c => c.Outcome == CompanionOutcome.Available)) v.Action = MainAction.Update;
				if (r.Companions.Any(c => c.Outcome == CompanionOutcome.Failed || c.Outcome == CompanionOutcome.InUse))
				{
					v.Kind = StatusKind.Warn;
					v.Status = T.S("extras.partial");
				}
			}
			_view = v;
			_form.Render(v);
			RefreshAddons(false);
			_tray.ToolTipText = v.Installed != null ? T.F("tip.idle", v.Installed) : T.S("tip.idle.none");
			RefreshMenu();
		}

		/// <summary>
		/// Looks for a newer updater at every check. Found at start, it installs at once; found later, it is only shown
		/// and installs itself 24 hours after it was first seen.
		/// </summary>
		async Task CheckApp(bool booting)
		{
			if (!_settings.SelfUpdate || _appBusy || _appChecking) return;
			var result = await _engine.CheckUpdaterAsync(Setup.Version, _quit.Token, UpdateEngine.LinuxUpdaterAssetPattern);
			if (result.Outcome == AppCheckOutcome.Unreachable) return;
			var r = result.Outcome == AppCheckOutcome.Newer ? result.Release : null;
			if (r == null)
			{
				if (_app == null && SelfUpdatePolicy.Clear(_settings)) Save();
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

		/// <summary>
		/// "Check for an app update" in the menu: always says what it found, with a desktop notice. A newer version
		/// installs at once and restarts the updater; otherwise the notice says it is up to date, or that GitHub could
		/// not be reached.
		/// </summary>
		async void CheckAppNow()
		{
			if (_appBusy || _appChecking) return;
			_appChecking = true;
			UpdateView(_last);
			AppCheckResult r;
			try
			{
				r = await _engine.CheckUpdaterAsync(Setup.Version, _quit.Token, UpdateEngine.LinuxUpdaterAssetPattern);
			}
			finally
			{
				_appChecking = false;
				UpdateView(_last);
			}
			if (_quit.IsCancellationRequested) return;
			switch (r.Outcome)
			{
				case AppCheckOutcome.Newer:
					_app = r.Release;
					_appAt = SelfUpdatePolicy.Note(_settings, r.Release.Version, DateTime.UtcNow);
					Save();
					Log.Write("Updater " + r.Release.Version + " is out; installing it now, as asked from the menu");
					Notify(T.F("app.newer.title", r.Release.Version), T.S("app.newer.body"));
					await InstallAppAsync();
					break;
				case AppCheckOutcome.UpToDate:
					Notify(T.S("app.uptodate.title"), T.F("app.uptodate.body", Setup.Version));
					break;
				default:
					Notify(T.S("app.unreachable.title"), T.S("app.unreachable.body"));
					break;
			}
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
			if (_engine.Busy) { ShowWindow(); return; }
			ShowWindow();
			var picked = await _form.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
			{
				Title = T.S("folder.prompt"),
				AllowMultiple = false,
			});
			if (_engine.Busy || picked == null || picked.Count == 0) return;
			string path = picked[0].TryGetLocalPath();
			string addOns = path != null ? WowSearch.Resolve(path) : null;
			if (addOns == null)
			{
				_view.Kind = StatusKind.Warn;
				_view.Status = T.S("folder.bad.title") + ". " + T.S("folder.bad.body");
				_form.Render(_view);
				return;
			}
			if (addOns != _settings.AddOnsDir)
			{
				_settings.InstalledFolders.Clear();
				_settings.ExtraVersions.Clear();
				_settings.ExtraFiles.Clear();
			}
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

		void ShowAddons()
		{
			RefreshAddons(_engine.Busy, true);
			_addons.Show();
			if (_addons.WindowState == WindowState.Minimized) _addons.WindowState = WindowState.Normal;
			_addons.Activate();
		}

		/// <summary>Shows the settings, the last results and what is in the AddOns folder now in the Extra addons window.</summary>
		void RefreshAddons(bool busy, bool force = false)
		{
			if (_addons == null || (!force && !_addons.IsVisible)) return;
			var installed = new System.Collections.Generic.List<InstalledAddon>();
			try { installed = AddonScanner.Scan(_settings.AddOnsDir, _settings); }
			catch (IOException ex) { Log.Write("Could not read the AddOns folder: " + ex.Message); }
			catch (UnauthorizedAccessException ex) { Log.Write("Could not read the AddOns folder: " + ex.Message); }
			_addons.Render(AddonsView.Build(_settings, _last != null ? _last.Companions : null, installed, busy));
		}

		void OnAddonAction(AddonAction action, string arg)
		{
			if (action == AddonAction.OpenPage)
			{
				var entry = AddonCatalog.Find(arg);
				if (entry != null && entry.PageUrl != null) Setup.OpenPage(entry.PageUrl);
				return;
			}
			if (_engine.Busy || !AddonRows.Apply(_settings, action, arg)) return;
			Save();
			_addons.ShowMessage("");
			// The player confirmed the take-over in the window; the check installs it over the hand-installed copy.
			if (action == AddonAction.TakeOver || action == AddonAction.Add) Check(true, false, true, false);
			else UpdateView(_last);
			RefreshAddons(_engine.Busy);
		}

		void ImportAddons(string path)
		{
			try
			{
				if (new FileInfo(path).Length > AddonList.MaxBytes) throw new FormatException("too large");
				var choices = AddonList.Import(File.ReadAllText(path), out var unknown);
				int added = AddonList.Apply(_settings, choices);
				Save();
				UpdateView(_last);
				string text = T.F("addons.import.done", added);
				if (unknown.Count > 0) text += " " + T.F("addons.import.unknown", string.Join(", ", unknown));
				_addons.ShowMessage(text, unknown.Count > 0);
			}
			catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
			{
				Log.Write("Addon list not imported: " + ex.Message);
				_addons.ShowMessage(T.S("addons.import.bad"), true);
			}
		}

		void ExportAddons(string path)
		{
			try
			{
				File.WriteAllText(path, AddonList.Export(_settings));
				_addons.ShowMessage(T.S("addons.export.done"));
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				Log.Write("Addon list not saved: " + ex.Message);
				_addons.ShowMessage(ex.Message, true);
			}
		}

		/// <summary>Opens the Linux setup assistant; it reads the system and Lightforge's state each time it opens.</summary>
		void ShowSetup()
		{
			if (_setup == null)
			{
				_setup = new SetupWindow(Assets.Logo(), _web, new ProcessRunner(), Setup.Home, Environment.GetEnvironmentVariable("XDG_DATA_HOME"), Setup.TempDir);
				_setup.UseFolder += UseFolder;
			}
			_setup.Show();
			if (_setup.WindowState == WindowState.Minimized) _setup.WindowState = WindowState.Normal;
			_setup.Activate();
			_ = _setup.RefreshAsync();
		}

		/// <summary>The player chose Lightforge's WoW: Forever in the setup assistant.</summary>
		void UseFolder(string addOns)
		{
			if (_engine.Busy || string.IsNullOrEmpty(addOns)) return;
			if (addOns != _settings.AddOnsDir)
			{
				_settings.InstalledFolders.Clear();
				_settings.ExtraVersions.Clear();
				_settings.ExtraFiles.Clear();
			}
			_settings.AddOnsDir = addOns;
			Save();
			Log.Write("AddOns folder chosen in the setup assistant: " + addOns);
			UpdateView(_last);
			Check(true, false, false, false);
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

		/// <summary>A desktop notice through notify-send, when the desktop has it. The app's own updates get one only when asked for from the menu.</summary>
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
			_addons.AllowClose = true;
			_addons.Close();
			if (_setup != null) { _setup.AllowClose = true; _setup.Close(); }
			_shutdown();
		}
	}
}
