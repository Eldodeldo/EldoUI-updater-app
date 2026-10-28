// Uninstalls addons from the AddOns folder, only after the player confirmed it in the Extra addons window.
//
// EldoUI's own folders are never removed from here. The folders are first moved aside into a hidden work
// folder inside AddOns (the same drive, even when AddOns is a link to another one), all of them or none: if
// one cannot be moved, the ones already moved are put back and nothing changes. Only then are they deleted.
// The addon's saved settings in the game's WTF folder stay unless the player ticked the box for them; then
// only the files the game names after the removed folders are deleted.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EldoUI.Updater.Core
{
	public sealed class RemovalPlan
	{
		/// <summary>The addons as the Installed list names them.</summary>
		public List<string> Names = new List<string>();
		/// <summary>The catalog addons among them, so the updater also stops updating them.</summary>
		public List<string> CatalogIds = new List<string>();
		public List<string> Folders = new List<string>();
		/// <summary>Installed addons that are not removed but need one of the folders, so they stop loading.</summary>
		public List<string> NeededBy = new List<string>();
		/// <summary>The saved settings files the game keeps for these folders (full paths).</summary>
		public List<string> SavedSettings = new List<string>();
		/// <summary>One of them is an addon the updater keeps up to date.</summary>
		public bool Managed;
	}

	public sealed class RemovalResult
	{
		public List<string> Removed = new List<string>();
		public int SettingsDeleted;
		/// <summary>Files that could not be deleted right away; they are out of AddOns and go at the next cleanup.</summary>
		public bool LeftOver;
	}

	public static class AddonRemover
	{
		const string WorkPrefix = ".eldoui-remove-";

		/// <summary>True for an installed addon the player may uninstall: anything but EldoUI and its guide data.</summary>
		public static bool CanRemove(InstalledAddon addon)
		{
			return addon != null && addon.State != AddonState.EldoUI && addon.State != AddonState.GuideData
				&& addon.Folders.Count > 0 && addon.Folders.All(IsRemovableName);
		}

		/// <summary>A plain folder name directly in AddOns that is not one of EldoUI's.</summary>
		public static bool IsRemovableName(string folder)
		{
			return !string.IsNullOrWhiteSpace(folder) && folder == folder.Trim() && !folder.StartsWith(".")
				&& folder.IndexOfAny(new[] { '/', '\\', ':', '\0' }) < 0 && folder.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
				&& !AddonInstaller.IsEldoUIFolder(folder);
		}

		/// <summary>The row key the Installed list uses for an addon: its folders joined with "/".</summary>
		public static string Key(InstalledAddon addon) { return string.Join("/", addon.Folders); }

		/// <summary>
		/// What uninstalling the rows with these keys would remove, or null when one of them is not in the list or
		/// may not be removed (EldoUI's own folders).
		/// </summary>
		public static RemovalPlan Plan(string addOnsDir, IEnumerable<InstalledAddon> installed, IEnumerable<string> keys)
		{
			var all = (installed ?? Enumerable.Empty<InstalledAddon>()).ToList();
			var wanted = new HashSet<string>(keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			if (wanted.Count == 0 || string.IsNullOrEmpty(addOnsDir)) return null;
			var rows = all.Where(a => wanted.Contains(Key(a))).ToList();
			if (rows.Count != wanted.Count || !rows.All(CanRemove)) return null;
			var plan = new RemovalPlan();
			foreach (var row in rows)
			{
				plan.Names.Add(row.Name);
				if (row.CatalogId != null && !plan.CatalogIds.Contains(row.CatalogId)) plan.CatalogIds.Add(row.CatalogId);
				plan.Managed |= row.State == AddonState.Managed;
				plan.Folders.AddRange(row.Folders);
			}
			plan.Folders = plan.Folders.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
			var removed = new HashSet<string>(plan.Folders, StringComparer.OrdinalIgnoreCase);
			plan.NeededBy = all.Where(a => !rows.Contains(a) && a.Dependencies.Any(removed.Contains))
				.Select(a => a.State == AddonState.EldoUI ? "EldoUI" : a.Name).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
			plan.SavedSettings = SavedSettings(addOnsDir, plan.Folders);
			return plan;
		}

		/// <summary>
		/// The saved settings files the game keeps for these addon folders: Folder.lua and Folder.lua.bak in
		/// WTF\Account\ACCOUNT\SavedVariables and WTF\Account\ACCOUNT\Realm\Character\SavedVariables.
		/// </summary>
		public static List<string> SavedSettings(string addOnsDir, IEnumerable<string> folders)
		{
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string f in folders ?? Enumerable.Empty<string>())
			{
				if (!IsRemovableName(f)) continue;
				names.Add(f + ".lua");
				names.Add(f + ".lua.bak");
			}
			var files = new List<string>();
			string accounts = AccountsDir(addOnsDir);
			if (names.Count == 0 || accounts == null) return files;
			try
			{
				foreach (string account in Directories(accounts))
				{
					files.AddRange(Matching(Path.Combine(account, "SavedVariables"), names));
					foreach (string realm in Directories(account))
					{
						if (string.Equals(Path.GetFileName(realm), "SavedVariables", StringComparison.OrdinalIgnoreCase)) continue;
						foreach (string character in Directories(realm))
							files.AddRange(Matching(Path.Combine(character, "SavedVariables"), names));
					}
				}
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
			return files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>WTF\Account next to Interface\AddOns, or null when the game has none yet.</summary>
		static string AccountsDir(string addOnsDir)
		{
			if (string.IsNullOrEmpty(addOnsDir)) return null;
			string flavor = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(addOnsDir).TrimEnd('\\', '/')));
			if (flavor == null) return null;
			string dir = Path.Combine(flavor, "WTF", "Account");
			return Directory.Exists(dir) ? dir : null;
		}

		/// <summary>The folders in a folder, never through a link.</summary>
		static IEnumerable<string> Directories(string dir)
		{
			if (!Directory.Exists(dir)) return Enumerable.Empty<string>();
			return Directory.GetDirectories(dir).Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0);
		}

		static IEnumerable<string> Matching(string dir, HashSet<string> names)
		{
			if (!Directory.Exists(dir) || (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) return Enumerable.Empty<string>();
			return Directory.GetFiles(dir).Where(f => names.Contains(Path.GetFileName(f)) && (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0);
		}

		/// <summary>
		/// Moves the plan's folders out of AddOns (all or none) and deletes them; with deleteSettings, also deletes
		/// the plan's saved settings files. Throws IOException (nothing changed) when a folder cannot be moved,
		/// and InvalidDataException for a plan that names a folder it may not remove.
		/// </summary>
		public static RemovalResult Remove(string addOnsDir, RemovalPlan plan, bool deleteSettings)
		{
			if (plan == null || plan.Folders.Count == 0) throw new InvalidDataException("Nothing to uninstall.");
			string addOns = Path.GetFullPath(addOnsDir).TrimEnd(Path.DirectorySeparatorChar);
			foreach (string f in plan.Folders)
			{
				if (!IsRemovableName(f)) throw new InvalidDataException("The folder " + f + " cannot be uninstalled here.");
				string path = Path.Combine(addOns, f);
				if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), addOns, StringComparison.Ordinal))
					throw new InvalidDataException("The folder " + f + " is not in the AddOns folder.");
				if (!Directory.Exists(path)) throw new IOException("The folder " + f + " is no longer in the AddOns folder.");
			}
			CleanUp(addOns);
			// The game loads only folders with a .toc at the top of AddOns, so nothing in the work folder loads.
			string work = Path.Combine(addOns, WorkPrefix + Guid.NewGuid().ToString("N").Substring(0, 8));
			var moved = new List<string>();
			Directory.CreateDirectory(work);
			try
			{
				foreach (string f in plan.Folders)
				{
					Directory.Move(Path.Combine(addOns, f), Path.Combine(work, f));
					moved.Add(f);
				}
			}
			catch
			{
				// Put every folder back exactly where it was. One that cannot go back stays in the work folder, which the
				// game does not load, and goes at the next cleanup, as the player asked.
				foreach (string f in moved.ToList())
					try { Directory.Move(Path.Combine(work, f), Path.Combine(addOns, f)); moved.Remove(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
				if (moved.Count == 0) try { Directory.Delete(work, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
				throw;
			}
			var result = new RemovalResult { Removed = moved };
			if (deleteSettings)
			{
				var allowed = new HashSet<string>(SavedSettings(addOns, plan.Folders), StringComparer.OrdinalIgnoreCase);
				foreach (string file in plan.SavedSettings.Where(allowed.Contains))
				{
					try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); result.SettingsDeleted++; }
					catch (IOException) { result.LeftOver = true; }
					catch (UnauthorizedAccessException) { result.LeftOver = true; }
				}
			}
			if (!DeleteTree(work)) result.LeftOver = true;
			return result;
		}

		/// <summary>Deletes what an earlier uninstall could not delete right away (a file that was in use).</summary>
		public static void CleanUp(string addOnsDir)
		{
			if (string.IsNullOrEmpty(addOnsDir)) return;
			try
			{
				string addOns = Path.GetFullPath(addOnsDir);
				if (!Directory.Exists(addOns)) return;
				foreach (string dir in Directory.GetDirectories(addOns, WorkPrefix + "*")) DeleteTree(dir);
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
			catch (ArgumentException) { }
		}

		/// <summary>Deletes a folder and everything in it. A link is removed itself; what it points to is never touched.</summary>
		static bool DeleteTree(string dir)
		{
			try
			{
				if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) { DeleteLink(dir); return true; }
				bool ok = true;
				foreach (string file in Directory.GetFiles(dir))
				{
					try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
					catch (IOException) { ok = false; }
					catch (UnauthorizedAccessException) { ok = false; }
				}
				foreach (string sub in Directory.GetDirectories(dir)) ok &= DeleteTree(sub);
				if (ok) Directory.Delete(dir, false);
				return ok;
			}
			catch (IOException) { return false; }
			catch (UnauthorizedAccessException) { return false; }
		}

		static void DeleteLink(string path)
		{
			// Windows removes a folder link as a folder, Linux and macOS as a file.
			try { Directory.Delete(path, false); }
			catch (IOException) { File.Delete(path); }
		}

		/// <summary>
		/// After an uninstall: the updater stops updating the removed catalog addons, so the next check does not
		/// install them again, and forgets the removed folders it was told to hide.
		/// </summary>
		public static void Forget(Settings settings, RemovalPlan plan)
		{
			foreach (string id in plan.CatalogIds)
			{
				settings.ExtraFiles.Remove(id);
				settings.ExtraVersions.Remove(id);
				settings.Reinstall.Remove(id);
				if (settings.ExtraAddons && Settings.Selectable(id) && settings.SelectedCompanions.Contains(id)) settings.SelectCompanion(id, false);
			}
			foreach (string f in plan.Folders) settings.IgnoredFolders.Remove(f);
		}

		/// <summary>The full path of an addon folder in AddOns, for opening it; null when the name is not a plain folder there.</summary>
		public static string FolderPath(string addOnsDir, string folder)
		{
			if (string.IsNullOrEmpty(addOnsDir) || string.IsNullOrWhiteSpace(folder) || folder.StartsWith(".")
				|| folder.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0) return null;
			string path = Path.Combine(addOnsDir, folder);
			return Directory.Exists(path) ? path : null;
		}
	}
}
