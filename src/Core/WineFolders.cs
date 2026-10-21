// Finding WoW: Forever on Linux, where the game runs in a Wine prefix: plain Wine, Lutris, Bottles,
// Steam (Proton) or winetricks. Each prefix is a small Windows: drive_c is C:, and dosdevices links the
// other drive letters. Battle.net inside the prefix keeps its own install list with Windows paths,
// which are turned into Linux paths here. Nothing here touches Windows-only APIs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EldoUI.Updater.Core
{
	public static class WineFolders
	{
		/// <summary>Where WoW usually lands inside a prefix's C: drive.</summary>
		static readonly string[] CommonInPrefix =
		{
			"Program Files (x86)/World of Warcraft",
			"Program Files/World of Warcraft",
			"World of Warcraft",
			"Games/World of Warcraft",
			"Program Files (x86)/Battle.net/World of Warcraft",
		};

		/// <summary>
		/// Every Wine prefix worth looking in for this user: $WINEPREFIX, ~/.wine, winetricks, Lutris (its
		/// game folders and the prefix lines in its game files), Bottles (native and Flatpak) and Steam's Proton
		/// prefixes in every Steam library. Folders that do not exist are left out.
		/// </summary>
		public static List<string> Prefixes(string home, string winePrefixEnv = null)
		{
			var found = new List<string>();
			Action<string> add = p =>
			{
				if (string.IsNullOrWhiteSpace(p)) return;
				try
				{
					p = Path.GetFullPath(p.Trim());
					if (Directory.Exists(Path.Combine(p, "drive_c")) && !found.Contains(p)) found.Add(p);
				}
				catch { }
			};
			Action<string> addChildren = dir =>
			{
				try
				{
					if (!Directory.Exists(dir)) return;
					foreach (string d in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.Ordinal)) add(d);
				}
				catch { }
			};

			add(winePrefixEnv);
			add(Path.Combine(home, ".wine"));
			addChildren(Path.Combine(home, ".local/share/wineprefixes"));

			// Lutris: games usually live in ~/Games/<name>; its game files name the prefix exactly.
			addChildren(Path.Combine(home, "Games"));
			foreach (string dir in new[] { Path.Combine(home, ".config/lutris/games"), Path.Combine(home, ".local/share/lutris/games"),
				Path.Combine(home, ".var/app/net.lutris.Lutris/config/lutris/games"), Path.Combine(home, ".var/app/net.lutris.Lutris/data/lutris/games") })
			{
				try
				{
					if (!Directory.Exists(dir)) continue;
					foreach (string yml in Directory.GetFiles(dir, "*.yml").OrderBy(x => x, StringComparer.Ordinal))
						foreach (string p in LutrisPrefixes(File.ReadAllText(yml), home)) add(p);
				}
				catch { }
			}

			// Bottles, native and Flatpak.
			addChildren(Path.Combine(home, ".local/share/bottles/bottles"));
			addChildren(Path.Combine(home, ".var/app/com.usebottles.bottles/data/bottles/bottles"));

			// Steam: every library's compatdata/<app id>/pfx.
			foreach (string steamapps in SteamLibraries(home))
			{
				try
				{
					string compat = Path.Combine(steamapps, "compatdata");
					if (!Directory.Exists(compat)) continue;
					foreach (string app in Directory.GetDirectories(compat).OrderBy(x => x, StringComparer.Ordinal)) add(Path.Combine(app, "pfx"));
				}
				catch { }
			}
			return found;
		}

		/// <summary>The "prefix:" lines of a Lutris game file, with ~ expanded.</summary>
		public static IEnumerable<string> LutrisPrefixes(string yml, string home)
		{
			var list = new List<string>();
			if (string.IsNullOrEmpty(yml)) return list;
			foreach (Match m in Regex.Matches(yml, @"^\s*prefix:\s*(.+?)\s*$", RegexOptions.Multiline))
			{
				string p = m.Groups[1].Value.Trim().Trim('"', '\'');
				if (p.StartsWith("~/", StringComparison.Ordinal)) p = Path.Combine(home, p.Substring(2));
				if (p.StartsWith("/", StringComparison.Ordinal)) list.Add(p);
			}
			return list;
		}

		/// <summary>Steam's steamapps folders: the usual places, Flatpak, and every library in libraryfolders.vdf.</summary>
		public static List<string> SteamLibraries(string home)
		{
			var roots = new[]
			{
				Path.Combine(home, ".steam/steam"),
				Path.Combine(home, ".local/share/Steam"),
				Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"),
			};
			var libs = new List<string>();
			Action<string> add = p =>
			{
				try
				{
					p = Path.GetFullPath(p);
					if (Directory.Exists(p) && !libs.Contains(p)) libs.Add(p);
				}
				catch { }
			};
			foreach (string root in roots)
			{
				string steamapps = Path.Combine(root, "steamapps");
				add(steamapps);
				try
				{
					string vdf = Path.Combine(steamapps, "libraryfolders.vdf");
					if (File.Exists(vdf))
						foreach (string lib in VdfLibraryPaths(File.ReadAllText(vdf))) add(Path.Combine(lib, "steamapps"));
				}
				catch { }
			}
			return libs;
		}

		/// <summary>The "path" entries of Steam's libraryfolders.vdf.</summary>
		public static IEnumerable<string> VdfLibraryPaths(string vdf)
		{
			var list = new List<string>();
			if (string.IsNullOrEmpty(vdf)) return list;
			foreach (Match m in Regex.Matches(vdf, "\"path\"\\s+\"([^\"]+)\""))
				list.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
			return list;
		}

		/// <summary>
		/// A Windows path inside a prefix as a Linux path: C:\ is drive_c, any other letter goes through
		/// dosdevices. Returns null for a path that is not a drive path.
		/// </summary>
		public static string ToUnix(string prefix, string windowsPath)
		{
			if (string.IsNullOrWhiteSpace(windowsPath)) return null;
			string p = windowsPath.Trim().Replace('\\', '/');
			if (p.Length < 2 || p[1] != ':' || !char.IsLetter(p[0])) return null;
			char drive = char.ToLowerInvariant(p[0]);
			string rest = Regex.Replace(p.Substring(2), "/{2,}", "/").Trim('/');
			string root = drive == 'c' ? Path.Combine(prefix, "drive_c") : Path.Combine(prefix, "dosdevices", drive + ":");
			return rest.Length == 0 ? root : Path.Combine(root, rest);
		}

		/// <summary>
		/// WoW install folders inside one prefix: what Battle.net in the prefix says (its default install folder
		/// and its list of installed games), then the usual places on its C: drive.
		/// </summary>
		public static List<string> InstallDirs(string prefix)
		{
			var dirs = new List<string>();
			Action<string> add = p => { if (p != null && !dirs.Contains(p)) dirs.Add(p); };
			string driveC = Path.Combine(prefix, "drive_c");
			try
			{
				string users = Path.Combine(driveC, "users");
				if (Directory.Exists(users))
				{
					foreach (string user in Directory.GetDirectories(users).OrderBy(x => x, StringComparer.Ordinal))
					{
						foreach (string roaming in new[] { "AppData/Roaming", "Application Data" })
						{
							string config = Path.Combine(user, roaming, "Battle.net", "Battle.net.config");
							if (!File.Exists(config)) continue;
							foreach (string w in WowFolders.FromBattleNetConfig(File.ReadAllText(config))) add(ToUnix(prefix, w));
						}
					}
				}
			}
			catch { }
			try
			{
				string db = Path.Combine(driveC, "ProgramData", "Battle.net", "Agent", "product.db");
				if (File.Exists(db) && new FileInfo(db).Length < 4 * 1024 * 1024)
					foreach (string w in WowFolders.FromProductDb(File.ReadAllBytes(db))) add(ToUnix(prefix, w));
			}
			catch { }
			foreach (string c in CommonInPrefix) add(Path.Combine(driveC, c));
			return dirs;
		}

		/// <summary>The best Forever AddOns folder in any prefix for this user, or null.</summary>
		public static string Find(string home, string winePrefixEnv = null)
		{
			var dirs = new List<string>();
			foreach (string prefix in Prefixes(home, winePrefixEnv)) dirs.AddRange(InstallDirs(prefix));
			return WowFolders.Find(dirs);
		}
	}
}
