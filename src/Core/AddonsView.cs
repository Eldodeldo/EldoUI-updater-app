// What the Extra addons window shows, worked out once for the Windows and the Linux window alike: one row per
// catalog addon with its state, the installed addons, and what installed addons need. Texts are keys into Strings.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EldoUI.Updater.Core
{
	public sealed class CatalogRow
	{
		public CatalogEntry Entry;
		public string Id { get { return Entry.Id; } }
		public string Name { get { return Entry.Legacy != null ? Entry.Legacy.Name : Entry.Name; } }
		public bool Selected, Held, PreRelease, Managed, ByHand;
		/// <summary>It can be ticked: installed and updated (GitHub) or checked (WoWInterface, TukUI).</summary>
		public bool CanSelect;
		/// <summary>The state as a text key, its argument (a version) and the publisher's error, if any.</summary>
		public string StatusKey, StatusArg, Error;
		/// <summary>The last check found its folders already there, put in by hand.</summary>
		public bool InUse;
		public string SourceKey { get { return "addons.source." + Entry.Source.ToString().ToLowerInvariant(); } }
	}

	public sealed class Suggestion
	{
		public CatalogEntry Entry;
		/// <summary>The installed addons that need it.</summary>
		public List<string> NeededBy = new List<string>();
	}

	public sealed class AddonsView
	{
		public List<CatalogRow> Catalog = new List<CatalogRow>();
		public List<InstalledAddon> Installed = new List<InstalledAddon>();
		public List<Suggestion> Suggestions = new List<Suggestion>();
		public bool Busy;
		public bool HasFolder;
		/// <summary>The AddOns folder the list was read from.</summary>
		public string AddOnsDir;

		public static AddonsView Build(Settings settings, IEnumerable<CompanionResult> last, List<InstalledAddon> installed, bool busy)
		{
			var results = (last ?? Enumerable.Empty<CompanionResult>()).Where(r => r.Id != null).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.Last());
			var byHand = new HashSet<string>((installed ?? new List<InstalledAddon>()).Where(r => r.State == AddonState.ByHand && r.CatalogId != null).Select(r => r.CatalogId));
			var view = new AddonsView { Busy = busy, Installed = installed ?? new List<InstalledAddon>(), HasFolder = !string.IsNullOrEmpty(settings.AddOnsDir), AddOnsDir = settings.AddOnsDir };
			foreach (var e in AddonCatalog.All)
			{
				var row = new CatalogRow
				{
					Entry = e,
					CanSelect = Settings.Selectable(e.Id),
					Selected = settings.ExtraAddons && settings.SelectedCompanions.Contains(e.Id),
					Held = settings.Held.Contains(e.Id),
					PreRelease = settings.PreRelease.Contains(e.Id),
					Managed = settings.ExtraFiles.ContainsKey(e.Id) && settings.ExtraFiles[e.Id].Count > 0,
					ByHand = byHand.Contains(e.Id),
				};
				string tag;
				settings.ExtraVersions.TryGetValue(e.Id, out tag);
				CompanionResult r;
				if (!row.CanSelect) row.StatusKey = e.Forever == false ? "addons.status.noforever" : "addons.status.page";
				else if (row.Selected && results.TryGetValue(e.Id, out r))
				{
					row.StatusKey = "addons.status." + r.Outcome.ToString().ToLowerInvariant();
					row.StatusArg = r.Version;
					row.Error = r.Error;
					row.InUse = r.Outcome == CompanionOutcome.InUse;
				}
				else if (!e.Installable && e.Source == AddonSource.GitHub) row.StatusKey = "addons.status.noforever";
				else if (row.Managed) { row.StatusKey = "addons.status.installed"; row.StatusArg = tag; }
				else if (row.ByHand) row.StatusKey = "addons.status.byhand";
				else if (row.Selected) row.StatusKey = "addons.status.selected";
				view.Catalog.Add(row);
			}
			foreach (var s in AddonScanner.Suggestions(view.Installed, settings))
				view.Suggestions.Add(new Suggestion
				{
					Entry = s,
					NeededBy = view.Installed.Where(i => !i.Ignored && i.MissingDependencies.Any(d => s.Folders.Contains(d, StringComparer.OrdinalIgnoreCase) || (s.Legacy != null && s.Legacy.Owns(d + "/"))))
						.Select(i => i.Name).Distinct().ToList(),
				});
			return view;
		}

		/// <summary>The catalog rows that match a search text (name, description or folder) and a category; null or empty matches all.</summary>
		public static IEnumerable<CatalogRow> Filter(IEnumerable<CatalogRow> rows, string search, string category)
		{
			string q = (search ?? "").Trim();
			return rows.Where(r => (string.IsNullOrEmpty(category) || r.Entry.Category == category)
				&& (q.Length == 0 || r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || r.Entry.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
					|| r.Entry.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
					|| r.Entry.Folders.Any(f => f.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)))
				// The player's own addons first, then the rest by name.
				.OrderBy(r => r.Selected || r.Managed ? 0 : 1).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
		}
	}
}
