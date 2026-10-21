// Finding the WoW: Forever AddOns folder.
//
// A WoW install has one folder per game version ("flavor"): _retail_, _classic_, _classic_era_,
// and _classic_beta_ for WoW: Forever. Each has Interface\AddOns. The player can pick any level
// (the install folder, the flavor folder, Interface or AddOns itself); it is turned into AddOns here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EldoUI.Updater.Core
{
	public static class WowFolders
	{
		/// <summary>Flavor folders that run WoW: Forever, best first.</summary>
		public static readonly string[] ForeverFlavors = { "_classic_beta_" };

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
				return dir;
			if (name.Equals("Interface", StringComparison.OrdinalIgnoreCase))
				return Path.Combine(dir, "AddOns");
			if (IsFlavorFolder(dir))
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
		/// The first Forever AddOns folder found under the given install folders (from the registry and the
		/// usual places on every drive), or null.
		/// </summary>
		public static string Find(IEnumerable<string> installDirs)
		{
			foreach (string root in installDirs.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				string dir;
				try { dir = Path.GetFullPath(root).TrimEnd('\\', '/'); }
				catch { continue; }
				// The registry often points at a flavor folder (…\World of Warcraft\_retail_\): go up one.
				if (IsFlavorFolder(dir)) dir = Path.GetDirectoryName(dir);
				string flavor = ForeverFlavorIn(dir);
				if (flavor != null) return Path.Combine(flavor, "Interface", "AddOns");
			}
			return null;
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
