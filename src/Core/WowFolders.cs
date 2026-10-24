// Finding the WoW: Forever AddOns folder.
//
// A WoW install has one folder per game version ("flavor"): _retail_, _classic_, _classic_era_,
// and for WoW: Forever _forever_ (the released game) or _classic_beta_ (its beta). Each has Interface\AddOns. The player can pick any level
// (the install folder, the flavor folder, Interface or AddOns itself); it is turned into AddOns here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EldoUI.Updater.Core
{
	public static class WowFolders
	{
		/// <summary>Flavor folders that run WoW: Forever, best first: the released game, then the beta.</summary>
		public static readonly string[] ForeverFlavors = { "_forever_", "_classic_beta_" };

		/// <summary>Folder names a WoW install usually has on a drive.</summary>
		public static readonly string[] CommonInstallPaths =
		{
			@"World of Warcraft",
			@"Program Files (x86)\World of Warcraft",
			@"Program Files\World of Warcraft",
			@"Games\World of Warcraft",
			@"Program Files (x86)\Battle.net\World of Warcraft",
			@"Blizzard\World of Warcraft",
			@"Battle.net\World of Warcraft",
		};

		public static bool IsFlavorFolder(string dir)
		{
			string name = Path.GetFileName(dir.TrimEnd('\\', '/'));
			return name.Length > 2 && name.StartsWith("_") && name.EndsWith("_") && Directory.Exists(dir);
		}

		static bool IsForeverFolder(string dir)
		{
			return !string.IsNullOrEmpty(dir) && Directory.Exists(dir)
				&& ForeverFlavors.Any(f => string.Equals(Path.GetFileName(dir.TrimEnd('\\', '/')), f, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>Turns whatever folder the player picked into an AddOns folder, or null when it is not part of a WoW install.</summary>
		public static string ResolveAddOns(string picked)
		{
			if (string.IsNullOrWhiteSpace(picked)) return null;
			string dir;
			try { dir = Path.GetFullPath(picked.Trim()).TrimEnd('\\', '/'); }
			catch { return null; }
			if (!Directory.Exists(dir)) return null;
			string name = Path.GetFileName(dir);

			if (name.Equals("AddOns", StringComparison.OrdinalIgnoreCase)
				&& Path.GetFileName(Path.GetDirectoryName(dir) ?? "").Equals("Interface", StringComparison.OrdinalIgnoreCase))
				return IsForeverFolder(Path.GetDirectoryName(Path.GetDirectoryName(dir))) ? dir : null;
			if (name.Equals("Interface", StringComparison.OrdinalIgnoreCase))
				return IsForeverFolder(Path.GetDirectoryName(dir)) ? Path.Combine(dir, "AddOns") : null;
			if (IsForeverFolder(dir))
				return Path.Combine(dir, "Interface", "AddOns");
			// The install folder: pick the Forever flavor in it.
			string flavor = ForeverFlavorIn(dir);
			return flavor == null ? null : Path.Combine(flavor, "Interface", "AddOns");
		}

		/// <summary>The Forever flavor folder inside a WoW install folder, or null.</summary>
		public static string ForeverFlavorIn(string installDir)
		{
			if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir)) return null;
			foreach (string f in ForeverFlavors)
			{
				string p = Path.Combine(installDir, f);
				if (Directory.Exists(p)) return p;
			}
			return null;
		}

		/// <summary>
		/// The best Forever AddOns folder found under the given install folders (from Battle.net, the registry
		/// and the usual places on every drive), or null. The released game wins over the beta wherever it is;
		/// among equals the first folder given wins.
		/// </summary>
		public static string Find(IEnumerable<string> installDirs)
		{
			var dirs = new List<string>();
			foreach (string root in installDirs.Where(d => !string.IsNullOrWhiteSpace(d)))
			{
				string dir;
				try { dir = Path.GetFullPath(root.Trim()).TrimEnd('\\', '/'); }
				catch { continue; }
				// The registry often points at a flavor folder (…\World of Warcraft\_retail_\): go up one.
				if (IsFlavorFolder(dir)) dir = Path.GetDirectoryName(dir);
				if (!dirs.Contains(dir, StringComparer.OrdinalIgnoreCase)) dirs.Add(dir);
			}
			foreach (string f in ForeverFlavors)
				foreach (string dir in dirs)
				{
					string p = Path.Combine(dir, f);
					if (Directory.Exists(p)) return Path.Combine(p, "Interface", "AddOns");
				}
			return null;
		}

		/// <summary>How good an AddOns folder's flavor is: 0 for the released game, higher for the beta, int.MaxValue otherwise.</summary>
		public static int Rank(string addOnsDir)
		{
			if (string.IsNullOrEmpty(addOnsDir)) return int.MaxValue;
			int i = Array.FindIndex(ForeverFlavors, f => string.Equals(f, FlavorOf(addOnsDir), StringComparison.OrdinalIgnoreCase));
			return i < 0 ? int.MaxValue : i;
		}

		/// <summary>
		/// A better Forever folder next to the chosen one (the released game's _forever_ beside the beta), or null.
		/// Used when Forever launches into the same WoW folder the beta was in.
		/// </summary>
		public static string Better(string addOnsDir)
		{
			int rank = Rank(addOnsDir);
			if (rank == 0 || rank == int.MaxValue) return null;
			try
			{
				string install = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(addOnsDir.TrimEnd('\\', '/'))));
				for (int i = 0; i < rank; i++)
				{
					string p = Path.Combine(install, ForeverFlavors[i]);
					if (Directory.Exists(p)) return Path.Combine(p, "Interface", "AddOns");
				}
			}
			catch { }
			return null;
		}

		/// <summary>
		/// WoW folders from Battle.net's own settings (%APPDATA%\Battle.net\Battle.net.config): the folder new
		/// games install into, with "World of Warcraft" added the way Battle.net does.
		/// </summary>
		public static IEnumerable<string> FromBattleNetConfig(string json)
		{
			var found = new List<string>();
			try
			{
				var client = Json.ParseObject(json).Obj("Client");
				var install = client != null ? client.Obj("Install") : null;
				string dir = install != null ? install.Str("DefaultInstallPath") : null;
				if (!string.IsNullOrWhiteSpace(dir))
				{
					dir = dir.Trim();
					found.Add(Path.Combine(dir, "World of Warcraft"));
					found.Add(dir);
				}
			}
			catch { }
			return found;
		}

		/// <summary>
		/// Install folders from Battle.net's list of installed games (%ProgramData%\Battle.net\Agent\product.db,
		/// a binary file with the paths as text). Every path is returned; only one with a Forever folder is used.
		/// The text after a path can run into the file's next bytes, so shorter versions are tried as well.
		/// </summary>
		public static IEnumerable<string> FromProductDb(byte[] data)
		{
			var found = new List<string>();
			if (data == null) return found;
			string text = System.Text.Encoding.UTF8.GetString(data);
			foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"[A-Za-z]:[\\/][^\x00-\x1f""*?<>|\uFFFD]{0,240}"))
			{
				string p = m.Value.Replace('/', '\\').TrimEnd('\\', ' ');
				for (int cut = 0; cut <= 3 && p.Length - cut > 3; cut++)
				{
					string c = p.Substring(0, p.Length - cut).TrimEnd('\\', ' ');
					if (!found.Contains(c, StringComparer.OrdinalIgnoreCase)) found.Add(c);
				}
			}
			return found;
		}

		/// <summary>The usual install places on each drive root given.</summary>
		public static IEnumerable<string> CommonPlaces(IEnumerable<string> driveRoots)
		{
			foreach (string drive in driveRoots)
				foreach (string p in CommonInstallPaths)
					yield return Path.Combine(drive, p);
		}

		/// <summary>The flavor folder of an AddOns folder ("…\_classic_beta_"), for display.</summary>
		public static string FlavorOf(string addOnsDir)
		{
			try
			{
				var interfaceDir = Path.GetDirectoryName(addOnsDir.TrimEnd('\\', '/'));
				return Path.GetFileName(Path.GetDirectoryName(interfaceDir));
			}
			catch { return null; }
		}
	}
}
