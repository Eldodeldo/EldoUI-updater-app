// Lists what is in the AddOns folder, grouped the way a player thinks of it: EldoUI, its guide data, the addons
// the updater manages, known addons put there by hand, and everything else, with each addon's size. Reading
// only: nothing is changed, and a folder the updater does not manage is never taken over from here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EldoUI.Updater.Core
{
	public enum AddonState
	{
		EldoUI,
		/// <summary>EldoUI's guide data package (EldoUI_GuideData): its own version and its own license.</summary>
		GuideData,
		/// <summary>Installed and kept up to date by the updater.</summary>
		Managed,
		/// <summary>In the catalog, but put there by hand: the updater leaves it alone until the player adds it.</summary>
		ByHand,
		Other,
	}

	public sealed class InstalledAddon
	{
		public string Name, Version, CatalogId;
		public AddonState State;
		public List<string> Folders = new List<string>();
		/// <summary>Addons it needs that are not in the AddOns folder.</summary>
		public List<string> MissingDependencies = new List<string>();
		/// <summary>Addons its folders need to load (other than its own folders), whether installed or not.</summary>
		public List<string> Dependencies = new List<string>();
		/// <summary>The size of its folders on disk, and when they were last put in place (installed or updated).</summary>
		public long Bytes;
		public DateTime? Changed;
		/// <summary>Its .toc names WoW: Forever.</summary>
		public bool Forever;
		/// <summary>The player chose not to see it in the list.</summary>
		public bool Ignored;
	}

	public static class AddonScanner
	{
		public const string GuideDataFolder = "EldoUI_GuideData";

		public static List<InstalledAddon> Scan(string addOnsDir, Settings settings)
		{
			var rows = new List<InstalledAddon>();
			if (string.IsNullOrEmpty(addOnsDir) || !Directory.Exists(addOnsDir)) return rows;
			var tocs = new Dictionary<string, TocInfo>(StringComparer.OrdinalIgnoreCase);
			foreach (string dir in Directory.GetDirectories(addOnsDir))
			{
				string name = Path.GetFileName(dir);
				if (name.StartsWith(".")) continue;
				tocs[name] = Toc.ReadFolder(dir);
			}
			var left = new SortedSet<string>(tocs.Keys, StringComparer.OrdinalIgnoreCase);

			var eldo = left.Where(f => AddonInstaller.IsEldoUIFolder(f) && !string.Equals(f, GuideDataFolder, StringComparison.OrdinalIgnoreCase)).ToList();
			if (eldo.Count > 0) rows.Add(Row("EldoUI", AddonState.EldoUI, null, eldo, tocs, AddonInstaller.MainFolder));
			var guide = left.FirstOrDefault(f => string.Equals(f, GuideDataFolder, StringComparison.OrdinalIgnoreCase));
			if (guide != null) rows.Add(Row(null, AddonState.GuideData, null, new List<string> { guide }, tocs, guide));
			foreach (string f in eldo.Concat(guide != null ? new[] { guide } : new string[0])) left.Remove(f);

			foreach (var kv in settings.ExtraFiles.OrderBy(k => k.Key, StringComparer.Ordinal))
			{
				var folders = kv.Value.Select(p => p.Split('/')[0]).Where(left.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				if (folders.Count == 0) continue;
				var entry = AddonCatalog.Find(kv.Key);
				var row = Row(entry != null ? (entry.Legacy != null ? entry.Legacy.Name : entry.Name) : kv.Key, AddonState.Managed, kv.Key, folders, tocs, entry != null && entry.Legacy != null ? entry.Legacy.MainFolder : null);
				string tag;
				if (settings.ExtraVersions.TryGetValue(kv.Key, out tag)) row.Version = tag;
				rows.Add(row);
				foreach (string f in folders) left.Remove(f);
			}

			foreach (var group in left.Select(f => new { Folder = f, Entry = AddonCatalog.ForFolder(f) }).Where(x => x.Entry != null).GroupBy(x => x.Entry.Id).ToList())
			{
				var entry = group.First().Entry;
				var folders = group.Select(x => x.Folder).ToList();
				rows.Add(Row(entry.Legacy != null ? entry.Legacy.Name : entry.Name, AddonState.ByHand, entry.Id, folders, tocs, entry.Folders.FirstOrDefault()));
				foreach (string f in folders) left.Remove(f);
			}

			// Everything else: a folder named after another one (Name_Options, Name-Data) sits with it.
			var parents = left.ToList();
			foreach (string f in parents)
			{
				if (!left.Contains(f)) continue;
				string parent = parents.Where(p => p.Length < f.Length && left.Contains(p) && (f.StartsWith(p + "_", StringComparison.OrdinalIgnoreCase) || f.StartsWith(p + "-", StringComparison.OrdinalIgnoreCase)))
					.OrderBy(p => p.Length).FirstOrDefault();
				if (parent != null) continue;
				var folders = left.Where(c => c == f || c.StartsWith(f + "_", StringComparison.OrdinalIgnoreCase) || c.StartsWith(f + "-", StringComparison.OrdinalIgnoreCase)).ToList();
				var row = Row(null, AddonState.Other, null, folders, tocs, f);
				row.Ignored = folders.All(settings.IgnoredFolders.Contains);
				rows.Add(row);
				foreach (string c in folders) left.Remove(c);
			}

			foreach (var row in rows)
			{
				row.Dependencies = row.Folders.SelectMany(f => tocs[f] != null ? tocs[f].Dependencies : new List<string>())
					.Where(d => !row.Folders.Contains(d, StringComparer.OrdinalIgnoreCase) && !d.StartsWith("Blizzard_", StringComparison.OrdinalIgnoreCase))
					.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
				row.MissingDependencies = row.Dependencies.Where(d => !tocs.ContainsKey(d)).ToList();
				foreach (string f in row.Folders)
				{
					var size = Measure(Path.Combine(addOnsDir, f));
					row.Bytes += size.Item1;
					if (size.Item2.HasValue && (!row.Changed.HasValue || size.Item2 > row.Changed)) row.Changed = size.Item2;
				}
				if (row.State == AddonState.ByHand || row.State == AddonState.Other)
					row.Ignored = row.Folders.All(settings.IgnoredFolders.Contains);
			}
			return rows;
		}

		// Sizes already measured, by folder and the time the folder last changed, so reopening the window stays quick.
		static readonly Dictionary<string, Tuple<DateTime, long>> Sizes = new Dictionary<string, Tuple<DateTime, long>>(StringComparer.Ordinal);
		const int MaxFilesMeasured = 200000;

		/// <summary>A folder's size in bytes (links are not followed) and when it last changed.</summary>
		static Tuple<long, DateTime?> Measure(string dir)
		{
			try
			{
				var info = new DirectoryInfo(dir);
				DateTime changed = info.LastWriteTimeUtc;
				Tuple<DateTime, long> known;
				lock (Sizes)
					if (Sizes.TryGetValue(info.FullName, out known) && known.Item1 == changed) return Tuple.Create(known.Item2, (DateTime?)changed);
				long bytes = 0;
				int files = 0;
				var stack = new Stack<DirectoryInfo>();
				if ((info.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(info);
				while (stack.Count > 0 && files < MaxFilesMeasured)
				{
					var d = stack.Pop();
					try
					{
						foreach (var f in d.GetFiles()) { bytes += f.Length; files++; }
						foreach (var sub in d.GetDirectories()) if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(sub);
					}
					catch (IOException) { }
					catch (UnauthorizedAccessException) { }
				}
				lock (Sizes) Sizes[info.FullName] = Tuple.Create(changed, bytes);
				return Tuple.Create(bytes, (DateTime?)changed);
			}
			catch (IOException) { return Tuple.Create(0L, (DateTime?)null); }
			catch (UnauthorizedAccessException) { return Tuple.Create(0L, (DateTime?)null); }
		}

		static InstalledAddon Row(string name, AddonState state, string id, List<string> folders, Dictionary<string, TocInfo> tocs, string main)
		{
			string first = folders.FirstOrDefault(f => string.Equals(f, main, StringComparison.OrdinalIgnoreCase)) ?? folders.OrderBy(f => f.Length).First();
			var toc = tocs[first] ?? folders.Select(f => tocs[f]).FirstOrDefault(t => t != null);
			return new InstalledAddon
			{
				Name = name ?? (toc != null && !string.IsNullOrEmpty(toc.Title) ? toc.Title : first),
				Version = toc != null ? toc.Version : null,
				CatalogId = id,
				State = state,
				Folders = folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(),
				Forever = folders.Any(f => tocs[f] != null && tocs[f].IsForever),
			};
		}

		/// <summary>
		/// Catalog addons that would supply what installed addons need and is missing. Only suggestions: the player
		/// picks them, nothing is added by itself.
		/// </summary>
		public static List<CatalogEntry> Suggestions(IEnumerable<InstalledAddon> rows, Settings settings)
		{
			return rows.Where(r => !r.Ignored).SelectMany(r => r.MissingDependencies).Select(AddonCatalog.ForFolder)
				.Where(e => e != null && e.Installable && !(settings.ExtraAddons && settings.SelectedCompanions.Contains(e.Id)))
				.GroupBy(e => e.Id).Select(g => g.First()).ToList();
		}
	}
}
