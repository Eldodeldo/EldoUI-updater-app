// Installing the updater for the current Windows user, without admin rights:
//   %LOCALAPPDATA%\Programs\EldoUI Updater\EldoUI Updater.exe   the program
//   %LOCALAPPDATA%\EldoUI Updater\                              settings, log and downloads
//   Start menu shortcut, an entry in Settings → Apps (for removing it) and, when chosen, autostart.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace EldoUI.Updater
{
	static class Setup
	{
		public const string ExeName = "EldoUI Updater.exe";
		const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
		const string RunValue = "EldoUI Updater";
		const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\EldoUI Updater";

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

		static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
		public static string InstallDir { get { return Path.Combine(LocalAppData, "Programs", "EldoUI Updater"); } }
		public static string InstalledExe { get { return Path.Combine(InstallDir, ExeName); } }
		public static string DataDir { get { return Path.Combine(LocalAppData, "EldoUI Updater"); } }
		public static string SettingsPath { get { return Path.Combine(DataDir, "settings.json"); } }
		public static string TempDir { get { return Path.Combine(DataDir, "downloads"); } }
		public static string ShortcutPath
		{
			get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "EldoUI Updater.lnk"); }
		}

		public static string CurrentExe { get { return System.Windows.Forms.Application.ExecutablePath; } }

		public static bool RunningInstalled
		{
			get { return string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase); }
		}

		/// <summary>Copies this exe into place and registers it. Returns the installed exe.</summary>
		public static string Install()
		{
			Directory.CreateDirectory(InstallDir);
			Directory.CreateDirectory(DataDir);
			if (!RunningInstalled)
			{
				// An older copy may be running from the tray: it was told to exit before this.
				string tmp = InstalledExe + ".new";
				File.Copy(CurrentExe, tmp, true);
				if (File.Exists(InstalledExe)) File.Replace(tmp, InstalledExe, null);
				else File.Move(tmp, InstalledExe);
			}
			WriteConfig();
			CreateShortcut();
			RegisterUninstall();
			return InstalledExe;
		}

		/// <summary>The .exe.config (sharp text on high-DPI screens, long paths) is built into the exe and written beside it.</summary>
		static void WriteConfig()
		{
			try
			{
				using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("EldoUI.Updater.exe.config"))
				using (var dst = File.Create(InstalledExe + ".config"))
					src.CopyTo(dst);
			}
			catch (Exception ex)
			{
				Log.Write("Config not written: " + ex.Message);
			}
		}

		static void CreateShortcut()
		{
			try
			{
				Type shellType = Type.GetTypeFromProgID("WScript.Shell");
				if (shellType == null) return;
				dynamic shell = Activator.CreateInstance(shellType);
				dynamic link = shell.CreateShortcut(ShortcutPath);
				link.TargetPath = InstalledExe;
				link.WorkingDirectory = InstallDir;
				link.Description = T.S("tagline");
				link.IconLocation = InstalledExe + ",0";
				link.Save();
			}
			catch (Exception ex)
			{
				Log.Write("Shortcut not created: " + ex.Message);
			}
		}

		static void RegisterUninstall()
		{
			using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
			{
				key.SetValue("DisplayName", "EldoUI Updater");
				key.SetValue("DisplayVersion", Version);
				key.SetValue("Publisher", "Eldodeldo");
				key.SetValue("DisplayIcon", InstalledExe + ",0");
				key.SetValue("InstallLocation", InstallDir);
				key.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
				key.SetValue("QuietUninstallString", "\"" + InstalledExe + "\" --uninstall --quiet");
				key.SetValue("URLInfoAbout", "https://github.com/Eldodeldo/EldoUI-updater-app");
				key.SetValue("NoModify", 1, RegistryValueKind.DWord);
				key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
				try { key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord); } catch { }
			}
		}

		public static bool Autostart
		{
			get
			{
				using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
					return key != null && key.GetValue(RunValue) != null;
			}
			set
			{
				using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
				{
					if (value) key.SetValue(RunValue, "\"" + InstalledExe + "\" --tray");
					else key.DeleteValue(RunValue, false);
				}
			}
		}

		/// <summary>Removes everything the updater added. EldoUI itself stays in the game.</summary>
		public static void Uninstall()
		{
			Autostart = false;
			try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
			try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); } catch { }
			try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); } catch { }
			// A running exe cannot delete itself: a hidden command waits a moment, then removes the folder.
			var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + InstallDir + "\"")
			{
				CreateNoWindow = true,
				UseShellExecute = false,
				WindowStyle = ProcessWindowStyle.Hidden,
			};
			Process.Start(psi);
		}

		/// <summary>
		/// Puts a downloaded new version in place of the running exe and starts it. Windows lets a running
		/// exe be renamed, so the old one moves aside and is deleted by the new one when it starts.
		/// </summary>
		public static void ReplaceAndRestart(string newExe)
		{
			string old = InstalledExe + ".old";
			try { if (File.Exists(old)) File.Delete(old); } catch { }
			File.Move(InstalledExe, old);
			try
			{
				File.Move(newExe, InstalledExe);
			}
			catch
			{
				File.Move(old, InstalledExe);
				throw;
			}
			WriteConfig();
			RegisterUninstall();
			Process.Start(new ProcessStartInfo(InstalledExe, "--tray --updated") { UseShellExecute = false, WorkingDirectory = InstallDir });
		}

		/// <summary>Removes the previous exe left by a self-update.</summary>
		public static void CleanUpOld()
		{
			try { File.Delete(InstalledExe + ".old"); } catch { }
		}
	}
}
