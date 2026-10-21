// Installing the updater for the current Linux user, without root, the freedesktop way:
//   ~/.local/share/eldoui-updater/EldoUI-Updater.AppImage    the program (and its icon)
//   ~/.config/eldoui-updater/settings.json                    settings
//   ~/.local/state/eldoui-updater/updater.log                 log
//   ~/.cache/eldoui-updater/                                  downloads
//   ~/.local/share/applications/eldoui-updater.desktop        the entry in the app menu
//   ~/.config/autostart/eldoui-updater.desktop                autostart, when chosen
// Every folder follows the XDG_* variables when they are set.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace EldoUI.Updater
{
	static class Setup
	{
		public const string AppFile = "EldoUI-Updater.AppImage";
		const string DesktopName = "eldoui-updater.desktop";

		public static string Version
		{
			get
			{
				var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
				string v = info != null ? info.InformationalVersion : Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
				int plus = v.IndexOf('+');
				return plus >= 0 ? v.Substring(0, plus) : v;
			}
		}

		public static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }

		static string Xdg(string variable, string fallback)
		{
			string v = Environment.GetEnvironmentVariable(variable);
			return !string.IsNullOrWhiteSpace(v) && Path.IsPathRooted(v) ? v : Path.Combine(Home, fallback);
		}

		static string DataHome { get { return Xdg("XDG_DATA_HOME", ".local/share"); } }
		static string ConfigHome { get { return Xdg("XDG_CONFIG_HOME", ".config"); } }

		public static string InstallDir { get { return Path.Combine(DataHome, "eldoui-updater"); } }
		public static string InstalledApp { get { return Path.Combine(InstallDir, AppFile); } }
		public static string IconPath { get { return Path.Combine(InstallDir, "eldoui-updater.png"); } }
		public static string SettingsPath { get { return Path.Combine(ConfigHome, "eldoui-updater", "settings.json"); } }
		public static string LogDir { get { return Path.Combine(Xdg("XDG_STATE_HOME", ".local/state"), "eldoui-updater"); } }
		public static string TempDir { get { return Path.Combine(Xdg("XDG_CACHE_HOME", ".cache"), "eldoui-updater"); } }
		static string MenuEntry { get { return Path.Combine(DataHome, "applications", DesktopName); } }
		static string AutostartEntry { get { return Path.Combine(ConfigHome, "autostart", DesktopName); } }

		/// <summary>The AppImage that is running (the AppImage runtime sets APPIMAGE), else the program file itself.</summary>
		public static string CurrentApp
		{
			get
			{
				string a = Environment.GetEnvironmentVariable("APPIMAGE");
				return !string.IsNullOrEmpty(a) && File.Exists(a) ? a : Environment.ProcessPath;
			}
		}

		/// <summary>True when started from an AppImage; a build started from its folder runs where it is.</summary>
		public static bool IsAppImage { get { return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE")); } }

		public static bool RunningInstalled
		{
			get { return string.Equals(Path.GetFullPath(CurrentApp), Path.GetFullPath(InstalledApp), StringComparison.Ordinal); }
		}

		/// <summary>Copies this AppImage into place and adds it to the app menu. Returns the installed file.</summary>
		public static string Install()
		{
			Directory.CreateDirectory(InstallDir);
			if (!RunningInstalled)
			{
				string tmp = InstalledApp + ".new";
				File.Copy(CurrentApp, tmp, true);
				MakeRunnable(tmp);
				File.Move(tmp, InstalledApp, true);
			}
			WriteIcon();
			WriteDesktop(MenuEntry, "");
			return InstalledApp;
		}

		static void WriteIcon()
		{
			try
			{
				using (var src = Assets.Open("logo.png"))
				using (var dst = File.Create(IconPath))
					src.CopyTo(dst);
			}
			catch (Exception ex) { Log.Write("Icon not written: " + ex.Message); }
		}

		static void WriteDesktop(string path, string args)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			string exec = "\"" + InstalledApp.Replace("\"", "\\\"") + "\"" + (args.Length > 0 ? " " + args : "");
			File.WriteAllText(path, string.Join("\n", new[]
			{
				"[Desktop Entry]",
				"Type=Application",
				"Name=EldoUI Updater",
				"Comment=" + T.S("tagline"),
				"Exec=" + exec,
				"Icon=" + IconPath,
				"Terminal=false",
				"Categories=Game;Utility;",
				"StartupNotify=false",
				"X-GNOME-Autostart-enabled=true",
				"",
			}));
		}

		public static bool Autostart
		{
			get { return File.Exists(AutostartEntry); }
			set
			{
				if (value) WriteDesktop(AutostartEntry, "--tray");
				else if (File.Exists(AutostartEntry)) File.Delete(AutostartEntry);
			}
		}

		/// <summary>Removes everything the updater added. EldoUI itself stays in the game.</summary>
		public static void Uninstall()
		{
			Autostart = false;
			foreach (string f in new[] { MenuEntry })
				try { if (File.Exists(f)) File.Delete(f); } catch { }
			foreach (string d in new[] { InstallDir, Path.GetDirectoryName(SettingsPath), LogDir, TempDir })
				try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
		}

		/// <summary>
		/// Puts a downloaded new version in place of the running AppImage and starts it. Linux lets a running file
		/// be replaced by a rename: the running copy keeps its old file until it exits.
		/// </summary>
		public static void ReplaceAndRestart(string newApp)
		{
			MakeRunnable(newApp);
			File.Move(newApp, InstalledApp, true);
			Process.Start(new ProcessStartInfo(InstalledApp, "--tray --updated") { UseShellExecute = false, WorkingDirectory = InstallDir });
		}

		static void MakeRunnable(string path)
		{
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
				| UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
		}

		/// <summary>Opens a folder or link with the desktop's own handler.</summary>
		public static void Open(string target)
		{
			try { Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { target }, UseShellExecute = false }); }
			catch (Exception ex) { Log.Write("Could not open " + target + ": " + ex.Message); }
		}
	}
}
