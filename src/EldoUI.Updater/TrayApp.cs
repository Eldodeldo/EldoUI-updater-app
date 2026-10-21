// The updater as it lives in the notification area: the icon, its menu, the timer that checks
// for a new EldoUI, and the window.
using System;
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
		static readonly TimeSpan SelfCheckEvery = TimeSpan.FromHours(24);
		const string SiteUrl = "https://eldodeldo.dk/eldoui/";

		readonly Settings _settings;
		readonly Web _web;
		readonly UpdateEngine _engine;
		readonly NotifyIcon _tray;
		readonly ContextMenuStrip _menu;
		readonly MainForm _form;
		readonly System.Windows.Forms.Timer _timer;
		readonly SynchronizationContext _ui;
		readonly ToolStripMenuItem _miStatus, _miAuto, _miStartup, _miLangAuto, _miLangEn, _miLangDa, _miNotes;
		readonly ToolStripMenuItem _miOpen, _miCheck, _miFolder, _miAddOns, _miLang, _miSite, _miExit;
		readonly CancellationTokenSource _quit = new CancellationTokenSource();
		ViewState _view = new ViewState();
		CheckResult _last;
		string _toastAction;
		DateTime _lastSelfCheck = DateTime.MinValue;
		// After a failed check: wait 15 minutes, then twice as long each time, at most the normal interval.
		DateTime _retryAt = DateTime.MinValue;
		TimeSpan _backoff = TimeSpan.FromMinutes(15);

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

			_menu = new ContextMenuStrip { Renderer = new MenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, Font = Theme.Body(9.5f), ForeColor = Theme.Text, BackColor = Theme.Panel };
			_miStatus = new ToolStripMenuItem { Enabled = false, Tag = "head" };
			_miOpen = new ToolStripMenuItem("", null, (s, e) => ShowWindow()) { Font = Theme.Body(9.5f, FontStyle.Bold) };
			_miCheck = new ToolStripMenuItem("", null, (s, e) => Check(true, false));
			_miNotes = new ToolStripMenuItem("", null, (s, e) => OpenUrl(_last != null && _last.Release != null ? _last.Release.PageUrl : SiteUrl));
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
				_miOpen, _miCheck, _miNotes, new ToolStripSeparator(),
				_miFolder, _miAddOns, new ToolStripSeparator(),
				_miAuto, _miStartup, _miLang, new ToolStripSeparator(),
				_miSite, _miExit,
			});
			_menu.Opening += (s, e) => RefreshMenu();

			_tray = new NotifyIcon { Icon = icon, ContextMenuStrip = _menu, Visible = true };
			_tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
			_tray.BalloonTipClicked += (s, e) => OnToastClicked();

			_timer = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
			_timer.Tick += (s, e) => { if (Due()) Check(false, false); };
			_timer.Start();
			SystemEvents.PowerModeChanged += OnPower;

			ApplyTexts();
			if (string.IsNullOrEmpty(_settings.AddOnsDir) || WowFolders.ResolveAddOns(_settings.AddOnsDir) == null)
			{
				string found = WowSearch.Find();
				if (found != null)
				{
					_settings.AddOnsDir = found;
					Log.Write("Found WoW: Forever AddOns at " + found);
					Save();
				}
			}
			UpdateView(null);
			if (justUpdated) Toast(T.S("toast.self.title"), T.F("toast.self.body", Setup.Version), ToolTipIcon.Info, null);
			if (showWindow) ShowWindow();
			// The first check soon after start, so a login does not wait three hours.
			var first = new System.Windows.Forms.Timer { Interval = showWindow ? 800 : 20 * 1000 };
			first.Tick += (s, e) => { first.Stop(); first.Dispose(); Check(showWindow, false); };
			first.Start();
		}

		bool Due()
		{
			if (DateTime.UtcNow < _retryAt) return false;
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
		}

		void ApplyTexts()
		{
			_miOpen.Text = T.S("menu.open");
			_miCheck.Text = T.S("menu.check");
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
		}

		void RefreshMenu()
		{
			_miStatus.Text = _view.Installed != null ? "EldoUI " + _view.Installed : T.S("app");
			_miCheck.Enabled = !_engine.Busy;
			_miAddOns.Enabled = !string.IsNullOrEmpty(_settings.AddOnsDir);
			_miAuto.Checked = _settings.AutoInstall;
			_miStartup.Checked = Setup.Autostart;
			_miLangAuto.Checked = _settings.Language == "auto";
			_miLangEn.Checked = _settings.Language == "en";
			_miLangDa.Checked = _settings.Language == "da";
		}

		/// <summary>Checks for a new EldoUI. user: the player asked (show the result in the window). install: install even if automatic install is off.</summary>
		async void Check(bool user, bool install)
		{
			if (_engine.Busy) return;
			if (string.IsNullOrEmpty(_settings.AddOnsDir))
			{
				UpdateView(null);
				if (user) ShowWindow();
				else if (!_settings.Welcomed) Toast(T.S("toast.nofolder.title"), T.S("toast.nofolder.body"), ToolTipIcon.Info, "folder");
				_settings.Welcomed = true;
				Save();
				// Still look up the newest version, so the window can show it.
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
			if (r.Outcome == CheckOutcome.Offline || r.Outcome == CheckOutcome.Failed)
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
			Announce(r, user);
			await SelfUpdate(user);
		}

		void Announce(CheckResult r, bool user)
		{
			switch (r.Outcome)
			{
				case CheckOutcome.Installed:
					_settings.AnnouncedVersion = r.Installed;
					Save();
					if (!user || !_form.Visible)
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
					if (!user && r.Latest != null && r.Latest != _settings.AnnouncedVersion)
					{
						_settings.AnnouncedVersion = r.Latest;
						Save();
						Toast(T.S("toast.failed.title"), T.S("state.failed"), ToolTipIcon.Warning, "open");
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
			string tip = v.Installed != null ? T.F("tip.idle", v.Installed) : T.S("tip.idle.none");
			_tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
		}

		async Task SelfUpdate(bool user)
		{
			if (!_settings.SelfUpdate || !Setup.RunningInstalled) return;
			if (!user && DateTime.UtcNow - _lastSelfCheck < SelfCheckEvery) return;
			_lastSelfCheck = DateTime.UtcNow;
			var r = await _engine.NewerUpdaterAsync(Setup.Version, _quit.Token);
			if (r == null) return;
			string target = Setup.InstalledExe + ".download";
			try
			{
				await _engine.DownloadUpdaterAsync(r, target, _quit.Token);
				Log.Write("Updating the updater to " + r.Version);
				Setup.ReplaceAndRestart(target);
				Exit();
			}
			catch (Exception ex)
			{
				Log.Write("Updater self-update failed: " + ex.Message);
				try { File.Delete(target); } catch { }
			}
		}

		void ChooseFolder()
		{
			using (var dlg = new FolderBrowserDialog { Description = T.S("folder.prompt"), ShowNewFolderButton = false })
			{
				if (!string.IsNullOrEmpty(_settings.AddOnsDir))
				{
					try { dlg.SelectedPath = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(_settings.AddOnsDir))); } catch { }
				}
				if (dlg.ShowDialog(_form.Visible ? _form : null) != DialogResult.OK) return;
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
			if (!Release.IsGitHubDownload(url) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
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
			ExitThread();
		}
	}
}
