// A short log in the data folder, for when a player asks why an update did not happen.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using EldoUI.Updater.Core;

namespace EldoUI.Updater
{
	static class Log
	{
		static readonly object Lock = new object();
		const long MaxBytes = 256 * 1024;
		public static string FilePath { get { return Path.Combine(Setup.DataDir, "updater.log"); } }

		public static void Write(string line)
		{
			try
			{
				lock (Lock)
				{
					Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
					var info = new FileInfo(FilePath);
					if (info.Exists && info.Length > MaxBytes)
					{
						string old = FilePath + ".1";
						if (File.Exists(old)) File.Delete(old);
						File.Move(FilePath, old);
					}
					File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
				}
			}
			catch
			{
				// Logging never stops the app.
			}
		}
	}

	/// <summary>Where WoW is installed, from Blizzard's registry entries and the usual places.</summary>
	static class WowSearch
	{
		static readonly string[] RegistryKeys =
		{
			@"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft",
			@"SOFTWARE\Blizzard Entertainment\World of Warcraft",
			@"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft\Beta",
			@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\World of Warcraft",
		};

		public static string Find()
		{
			var roots = new List<string>();
			foreach (string k in RegistryKeys)
			{
				foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
				{
					try
					{
						using (var key = hive.OpenSubKey(k))
						{
							if (key == null) continue;
							foreach (string name in new[] { "InstallPath", "InstallLocation", "GamePath" })
							{
								var v = key.GetValue(name) as string;
								if (!string.IsNullOrWhiteSpace(v)) roots.Add(v.Trim().Trim('"'));
							}
						}
					}
					catch { }
				}
			}
			var drives = DriveInfo.GetDrives().Where(d => { try { return d.DriveType == DriveType.Fixed && d.IsReady; } catch { return false; } })
				.Select(d => d.RootDirectory.FullName);
			roots.AddRange(WowFolders.CommonPlaces(drives));
			return WowFolders.Find(roots);
		}
	}
}
