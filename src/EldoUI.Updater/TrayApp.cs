// The updater as it lives in the notification area: the icon, its menu, the timer that checks
// for a new EldoUI, and the window.
using System;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using EldoUI.Updater.Core;

namespace EldoUI.Updater
{
	sealed class TrayApp : ApplicationContext
	{
		static readonly TimeSpan CheckEvery = TimeSpan.FromHours(3);
		const string SiteUrl = "https://github.com/Eldodeldo/EldoUI";
		const string ReleasesUrl = "https://github.com/Eldodeldo/EldoUI/releases/latest";

		readonly Settings _settings;
		readonly Web _web;
		readonly UpdateEngine _engine;
		readonly NotifyIcon _tray;
		readonly ContextMenuStrip _menu;
		readonly MainForm _form;
		readonly AddonsForm _addons;
		readonly System.Windows.Forms.Timer _timer;
		readonly SynchronizationContext _ui;
		readonly ToolStripMenuItem _miStatus, _miAuto, _miStartup, _miLangAuto, _miLangEn, _miLangDa, _miNotes;
		readonly ToolStripMenuItem _miOpen, _miCheck, _miSelf, _miAppCheck, _miFolder, _miAddOns, _miLang, _miSite, _miExit;
		readonly CancellationTokenSource _quit = new CancellationTokenSource();
		ViewState _view = new ViewState();
		CheckResult _last;
		string _toastAction;
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

		public TrayApp(bool showWindow, bool justUpdated)
		{
			_ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
			_settings = Settings.Load(Setup.SettingsPath);
			T.Use(_settings.Language);
			_web = new Web("EldoUI-Updater/" + Setup.Version + " (+https://github.com/Eldodeldo/EldoUI-updater-app)");
			_engine = new UpdateEngine(_web, Setup.TempDir, Log.Write);
			Setup.CleanUpOld();

			var icon = Assets.Icon();
			_form = new MainForm(Assets.Icon(false), Assets.Logo());
			_form.CheckClicked += () => Check(true, false);
			_form.UpdateClicked += () => Check(true, true);
			_form.ChooseFolderClicked += ChooseFolder;
			_form.OpenAddOnsClicked += OpenAddOns;
			_form.AutostartChanged += on => SetAutostart(on);
			_form.AutoInstallChanged += on => SetAutoInstall(on);

			_form.ExtrasClicked += ShowAddons;
			_addons = new AddonsForm(Assets.Icon(false));
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

			_menu = new ContextMenuStrip { Renderer = new MenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, Font = Theme.Body(9.5f), ForeColor = Theme.Text, BackColor = Theme.Panel };
			_miStatus = new ToolStripMenuItem { Enabled = false, Tag = "head" };
			_miOpen = new ToolStripMenuItem("", null, (s, e) => ShowWindow()) { Font = Theme.Body(9.5f, FontStyle.Bold) };
			_miCheck = new ToolStripMenuItem("", null, (s, e) => Check(true, false));
			_miSelf = new ToolStripMenuItem("", null, (s, e) => InstallApp()) { Visible = false };
			_miAppCheck = new ToolStripMenuItem("", null, (s, e) => CheckAppNow());
			_miNotes = new ToolStripMenuItem("", null, (s, e) => OpenUrl(_last != null && _last.Release != null ? _last.Release.PageUrl : ReleasesUrl));
			_miFolder = new ToolStripMenuItem("", null, (s, e) => ChooseFolder());
			_miAddOns = new ToolStripMenuItem("", null, (s, e) => OpenAddOns());
			_miAuto = new ToolStripMenuItem("", null, (s, e) => SetAutoInstall(!_settings.AutoInstall));
			_miStartup = new ToolStripMenuItem("", null, (s, e) => SetAutostart(!Setup.Autostart));
			_miLangAuto = new ToolStripMenuItem("", null, (s, e) => SetLanguage("auto"));
			_miLangEn = new ToolStripMenuItem("English", null, (s, e) => SetLanguage("en"));
			_miLangDa = new ToolStripMenuItem("Dansk", null, (s, e) => SetLanguage("da"));
			_miLang = new ToolStripMenuItem("", null, _miLangAuto, _miLangEn, _miLangDa);
			_miLang.DropDown.BackColor = Theme.Panel;
			((ToolStripDropDownMenu)_miLang.DropDown).ShowImageMargin = false;
			((ToolStripDropDownMenu)_miLang.DropDown).ShowCheckMargin = true;
			_miSite = new ToolStripMenuItem("", null, (s, e) => OpenUrl(SiteUrl));
			_miExit = new ToolStripMenuItem("", null, (s, e) => Exit());
			_menu.Items.AddRange(new ToolStripItem[]
			{
				_miStatus, new ToolStripSeparator(),
				_miOpen, _miCheck, _miSelf, _miAppCheck, _miNotes, new ToolStripSeparator(),
				_miFolder, _miAddOns, new ToolStripSeparator(),
				_miAuto, _miStartup, _miLang, new ToolStripSeparator(),
				_miSite, _miExit,
			});
			_menu.Opening += (s, e) => RefreshMenu();

			_tray = new NotifyIcon { Icon = icon, ContextMenuStrip = _menu, Visible = true };
			_tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
			_tray.BalloonTipClicked += (s, e) => OnToastClicked();

			_timer = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
			_timer.Tick += (s, e) =>
			{
				if (_app != null && SelfUpdatePolicy.Due(_appAt, DateTime.UtcNow)) InstallApp();
				else if (Due()) Check(false, false);
			};
			_timer.Start();
			SystemEvents.PowerModeChanged += OnPower;

			ApplyTexts();
			FindFolder();
			// The app's own updates are never announced with a notice: the window and menu show them.
			if (justUpdated && SelfUpdatePolicy.Clear(_settings)) Save();
			UpdateView(null);
			_showPending = showWindow;
			// The first check right at start: what it finds installs before the window opens.
			var first = new System.Windows.Forms.Timer { Interval = 300 };
			first.Tick += (s, e) => { first.Stop(); first.Dispose(); Check(showWindow, false); };
			first.Start();
			// A slow network does not keep the window waiting long.
			var late = new System.Windows.Forms.Timer { Interval = 8000 };
			late.Tick += (s, e) => { late.Stop(); late.Dispose(); ShowPending(); };
			late.Start();
		}

		/// <summary>
		/// Finds WoW: Forever when no folder is set, and moves to the released game's _forever_ folder when it
		/// appears (next to the beta, or wherever Battle.net installed it). Nothing changes when the folder in use
		/// is already the released game.
		/// </summary>
		void FindFolder()
		{
			string cur = _settings.AddOnsDir;
			bool valid = false;
			try { valid = !string.IsNullOrEmpty(cur) && Directory.Exists(Path.GetDirectoryName(Path.GetDirectoryName(cur.TrimEnd('\\', '/')))); }
			catch { }
			if (valid && WowFolders.Rank(cur) == 0) return;
			string better = valid ? WowFolders.Better(cur) : null;
			if (better == null)
			{
				string found = WowSearch.Find();
				if (found != null && (!valid || WowFolders.Rank(found) < WowFolders.Rank(cur))) better = found;
			}
			if (better == null || string.Equals(better, cur, StringComparison.OrdinalIgnoreCase)) return;
			// A different game folder: what the last install put elsewhere is not ours to remove here.
			_settings.InstalledFolders.Clear();
			_settings.ExtraVersions.Clear();
			_settings.ExtraFiles.Clear();
			_settings.AddOnsDir = better;
			Log.Write((valid ? "Moved to WoW: Forever AddOns at " : "Found WoW: Forever AddOns at ") + better);
			Save();
		}

		bool Due()
		{
			if (DateTime.UtcNow < _retryAt) return false;
			// Until the check at start has reached GitHub, every minute counts, however recent the last check.
			if (_boot.Active(DateTime.UtcNow)) return true;
			return !_settings.LastCheck.HasValue || DateTime.UtcNow - _settings.LastCheck.Value.ToUniversalTime() >= CheckEvery;
		}

		void OnPower(object sender, PowerModeChangedEventArgs e)
		{
			if (e.Mode == PowerModes.Resume) _ui.Post(_ => { if (Due()) Check(false, false); }, null);
		}

		public void ShowWindow()
		{
			if (!_form.Visible) _form.Show();
			if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
			_form.Activate();
			_form.BringToFront();
			Introduce();
		}

		/// <summary>Once, the first time the window opens: what the updater does by itself, and where to turn it off.</summary>
		void Introduce()
		{
			if (_settings.Introduced) return;
			_settings.Introduced = true;
			Save();
			MessageBox.Show(_form, T.S("intro.body"), T.S("intro.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		void ApplyTexts()
		{
			_miOpen.Text = T.S("menu.open");
			_miCheck.Text = T.S("menu.check");
			_miAppCheck.Text = T.S("menu.appcheck");
			_miNotes.Text = T.S("menu.notes");
			_miFolder.Text = T.S("menu.folder");
			_miAddOns.Text = T.S("menu.addons");
			_miAuto.Text = T.S("menu.auto");
			_miStartup.Text = T.S("menu.startup");
			_miLang.Text = T.S("menu.language");
			_miLangAuto.Text = T.S("menu.lang.auto");
			_miSite.Text = T.S("menu.site");
			_miExit.Text = T.S("menu.exit");
			_form.ApplyTexts();
			_addons.ApplyTexts();
		}

		void RefreshMenu()
		{
			_miStatus.Text = _view.Installed != null ? "EldoUI " + _view.Installed : T.S("app");
			_miCheck.Enabled = !_engine.Busy;
			_miSelf.Visible = _app != null;
			_miSelf.Enabled = !_appBusy;
			_miAppCheck.Enabled = !_appBusy && !_appChecking;
			if (_app != null) _miSelf.Text = T.F("menu.self", _app.Version);
			_miAddOns.Enabled = !string.IsNullOrEmpty(_settings.AddOnsDir);
			_miAuto.Checked = _settings.AutoInstall;
			_miStartup.Checked = Setup.Autostart;
			_miLangAuto.Checked = _settings.Language == "auto";
			_miLangEn.Checked = _settings.Language == "en";
			_miLangDa.Checked = _settings.Language == "da";
		}

		/// <summary>Checks for a new EldoUI. user: the player asked (show the result in the window). install: install even if automatic install is off.</summary>
		async void Check(bool user, bool install, bool installExtras = false, bool showWindow = true)
		{
			if (_engine.Busy)
			{
				if (user && showWindow) ShowWindow();
				return;
			}
			FindFolder();
			if (string.IsNullOrEmpty(_settings.AddOnsDir))
			{
				UpdateView(null);
				if (user && showWindow) ShowWindow();
				else if (!user && !_settings.Welcomed) Toast(T.S("toast.nofolder.title"), T.S("toast.nofolder.body"), ToolTipIcon.Info, "folder");
				_settings.Welcomed = true;
				Save();
				// Still look up the newest version, so the window can show it.
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
					if (!booting && (!user || !_form.Visible))
						Toast(T.F("toast.installed.title", r.Installed), T.S("toast.installed.body"), ToolTipIcon.Info, "open");
					break;
				case CheckOutcome.UpdateAvailable:
					// Each new version is announced once, not on every check.
					if (r.Latest != _settings.AnnouncedVersion)
					{
						_settings.AnnouncedVersion = r.Latest;
						Save();
						if (!user) Toast(T.F("toast.available.title", r.Latest), T.S("toast.available.body"), ToolTipIcon.Info, "install");
					}
					break;
				case CheckOutcome.Failed:
				case CheckOutcome.Unverified:
					if (!user && r.Latest != null && r.Latest != _settings.AnnouncedVersion)
					{
						_settings.AnnouncedVersion = r.Latest;
						Save();
						Toast(T.S("toast.failed.title"), r.Outcome == CheckOutcome.Unverified ? T.F("state.unverified", r.Latest) : T.S("state.failed"), ToolTipIcon.Warning, "open");
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
				v.Status = _view.Status != null && _view.Status.Length > 0 && !_view.Busy ? _view.Status : T.S("state.checking");
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
				bool pending = r.Companions.Any(c => c.Outcome == CompanionOutcome.Available);
				if (pending) v.Action = MainAction.Update;
				if (r.Companions.Any(c => c.Outcome == CompanionOutcome.Failed || c.Outcome == CompanionOutcome.InUse))
				{
					v.Kind = StatusKind.Warn;
					v.Status = T.S("extras.partial");
				}
			}
			_view = v;
			_form.Render(v);
			RefreshAddons(false);
			string tip = v.Installed != null ? T.F("tip.idle", v.Installed) : T.S("tip.idle.none");
			_tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
		}

		/// <summary>
		/// Looks for a newer updater at every check. Found at start, it installs at once; found later, it is only shown
		/// and installs itself 24 hours after it was first seen.
		/// </summary>
		async Task CheckApp(bool booting)
		{
			if (!_settings.SelfUpdate || _appBusy || _appChecking) return;
			var result = await _engine.CheckUpdaterAsync(Setup.Version, _quit.Token);
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
		/// "Check for an app update" in the menu: always says what it found. A newer version installs at once and
		/// restarts the updater; otherwise a message says it is up to date, or that GitHub could not be reached.
		/// </summary>
		async void CheckAppNow()
		{
			if (_appBusy || _appChecking) return;
			_appChecking = true;
			UpdateView(_last);
			AppCheckResult r;
			try
			{
				r = await _engine.CheckUpdaterAsync(Setup.Version, _quit.Token);
			}
			finally
			{
				_appChecking = false;
				UpdateView(_last);
			}
			if (_quit.IsCancellationRequested) return;
			IWin32Window owner = _form.Visible ? _form : null;
			switch (r.Outcome)
			{
				case AppCheckOutcome.Newer:
					_app = r.Release;
					_appAt = SelfUpdatePolicy.Note(_settings, r.Release.Version, DateTime.UtcNow);
					Save();
					Log.Write("Updater " + r.Release.Version + " is out; installing it now, as asked from the menu");
					MessageBox.Show(owner, T.S("app.newer.body"), T.F("app.newer.title", r.Release.Version), MessageBoxButtons.OK, MessageBoxIcon.Information);
					await InstallAppAsync();
					break;
				case AppCheckOutcome.UpToDate:
					MessageBox.Show(owner, T.F("app.uptodate.body", Setup.Version), T.S("app.uptodate.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
					break;
				default:
					MessageBox.Show(owner, T.S("app.unreachable.body"), T.S("app.unreachable.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
					break;
			}
		}

		async void InstallApp()
		{
			await InstallAppAsync();
		}

		/// <summary>Downloads the newer updater, checks it, puts it in place and restarts into it.</summary>
		async Task InstallAppAsync()
		{
			if (_app == null || _appBusy) return;
			if (!Setup.RunningInstalled)
			{
				// A copy started from elsewhere cannot replace the installed one; the installed copy does it.
				Log.Write("Updater " + _app.Version + " not installed: this copy runs from " + Setup.CurrentExe);
				return;
			}
			_appBusy = true;
			UpdateView(_last);
			string target = Setup.InstalledExe + ".download";
			try
			{
				await _engine.DownloadUpdaterAsync(_app, target, _quit.Token);
				Log.Write("Updating the updater to " + _app.Version);
				Setup.ReplaceAndRestart(target, _showPending || _form.Visible);
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

		void ChooseFolder()
		{
			if (_engine.Busy) { ShowWindow(); return; }
			using (var dlg = new FolderBrowserDialog { Description = T.S("folder.prompt"), ShowNewFolderButton = false })
			{
				if (!string.IsNullOrEmpty(_settings.AddOnsDir))
				{
					try { dlg.SelectedPath = Path.GetDirectoryName(Path.GetDirectoryName(_settings.AddOnsDir)); } catch { }
				}
				if (dlg.ShowDialog(_form.Visible ? _form : null) != DialogResult.OK) return;
				if (_engine.Busy) return;
				string addOns = WowFolders.ResolveAddOns(dlg.SelectedPath);
				if (addOns == null)
				{
					MessageBox.Show(_form.Visible ? _form : null, T.S("folder.bad.body"), T.S("folder.bad.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}
				if (!string.Equals(addOns, _settings.AddOnsDir, StringComparison.OrdinalIgnoreCase))
				{
					// A different game folder: what the last install put elsewhere is not ours to remove here.
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
		}

		void OpenAddOns()
		{
			if (string.IsNullOrEmpty(_settings.AddOnsDir)) return;
			try
			{
				Directory.CreateDirectory(_settings.AddOnsDir);
				Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _settings.AddOnsDir + "\"") { UseShellExecute = true });
			}
			catch (Exception ex) { Log.Write("Could not open AddOns: " + ex.Message); }
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
			if (!_addons.Visible) _addons.Show();
			if (_addons.WindowState == FormWindowState.Minimized) _addons.WindowState = FormWindowState.Normal;
			_addons.Activate();
		}

		/// <summary>Shows the settings, the last results and what is in the AddOns folder now in the Extra addons window.</summary>
		void RefreshAddons(bool busy, bool force = false)
		{
			if (_addons == null || (!force && !_addons.Visible)) return;
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
				if (entry != null && entry.PageUrl != null) OpenUrl(entry.PageUrl);
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
				var info = new FileInfo(path);
				if (info.Length > AddonList.MaxBytes) throw new FormatException("too large");
				System.Collections.Generic.List<string> unknown;
				var choices = AddonList.Import(File.ReadAllText(path), out unknown);
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
			T.Use(lang);
			ApplyTexts();
			UpdateView(_last);
		}

		void Toast(string title, string body, ToolTipIcon icon, string action)
		{
			_toastAction = action;
			_tray.ShowBalloonTip(8000, title, string.IsNullOrEmpty(body) ? " " : body, icon);
		}

		void OnToastClicked()
		{
			switch (_toastAction)
			{
				case "folder": ShowWindow(); ChooseFolder(); break;
				case "install": ShowWindow(); Check(true, true); break;
				default: ShowWindow(); break;
			}
		}

		static void OpenUrl(string url)
		{
			// Only GitHub and the addon publishers' own pages: the app never opens any other address.
			if (!Release.IsAllowedPage(url)) { Log.Write("Refused to open an unapproved page"); return; }
			try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
		}

		void Save()
		{
			try { _settings.Save(Setup.SettingsPath); }
			catch (Exception ex) { Log.Write("Settings not saved: " + ex.Message); }
		}

		public void Exit()
		{
			_quit.Cancel();
			SystemEvents.PowerModeChanged -= OnPower;
			_timer.Stop();
			_tray.Visible = false;
			_tray.Dispose();
			_form.AllowClose = true;
			_form.Close();
			_addons.Dispose();
			ExitThread();
		}
	}
}
