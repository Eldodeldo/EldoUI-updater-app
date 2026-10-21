// A small log file for when something goes wrong (~/.local/state/eldoui-updater/updater.log), and the
// search for WoW: Forever in the player's Wine prefixes.
using System;
using System.IO;
using EldoUI.Updater.Core;

namespace EldoUI.Updater
{
	static class Log
	{
		static readonly object Gate = new object();
		const long MaxBytes = 256 * 1024;

		public static string PathName { get { return Path.Combine(Setup.LogDir, "updater.log"); } }

		public static void Write(string line)
		{
			lock (Gate)
			{
				try
				{
					Directory.CreateDirectory(Setup.LogDir);
					var fi = new FileInfo(PathName);
					if (fi.Exists && fi.Length > MaxBytes) File.Move(PathName, PathName + ".1", true);
					File.AppendAllText(PathName, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
				}
				catch { }
			}
		}
	}

	static class WowSearch
	{
		/// <summary>The best WoW: Forever AddOns folder in this user's Wine, Lutris, Bottles and Steam prefixes, or null.</summary>
		public static string Find()
		{
			try { return WineFolders.Find(Setup.Home, Environment.GetEnvironmentVariable("WINEPREFIX")); }
			catch (Exception ex)
			{
				Log.Write("WoW search failed: " + ex.Message);
				return null;
			}
		}

		/// <summary>
		/// Turns the folder a player picked into AddOns: any level of the WoW install (as on Windows), or a whole
		/// Wine prefix (the folder with drive_c in it).
		/// </summary>
		public static string Resolve(string picked)
		{
			string addOns = WowFolders.ResolveAddOns(picked);
			if (addOns != null) return addOns;
			try
			{
				if (Directory.Exists(Path.Combine(picked, "drive_c"))) return WowFolders.Find(WineFolders.InstallDirs(picked));
			}
			catch { }
			return null;
		}
	}
}
