// The rows of the Extra addons window as plain data, so the Windows and the Linux window show the same words
// and offer the same choices, and the player's choices change the settings in one place.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EldoUI.Updater.Core;

namespace EldoUI.Updater
{
	enum Tone { Plain, Ok, Info, Warn, Error }

	enum AddonAction { Select, Deselect, Hold, PreRelease, OpenPage, TakeOver, Hide, Show, Add }

	sealed class MenuChoice
	{
		public string Text;
		/// <summary>Shown with a tick when true, without when false; null for a plain action.</summary>
		public bool? Checked;
		public AddonAction Action;
		public string Arg;
	}

	sealed class AddonRow
	{
		public string Key, Title, Detail, Status, Tip;
		/// <summary>The second and third columns: category and source in the catalog, update state and version in Installed.</summary>
		public string Kind, Extra;
		/// <summary>"EldoUI already does this: Bags", for an addon an EldoUI module replaces; null otherwise.</summary>
		public string Note;
		public Tone Tone;
		/// <summary>The tick box: null when the row has none.</summary>
		public bool? Checked;
		/// <summary>What clicking the row's main button does, and its text (Add, Show); null for none.</summary>
		public AddonAction? Button;
		public string ButtonText;
		public List<MenuChoice> Menu = new List<MenuChoice>();
	}

	static class AddonRows
	{
		public static List<AddonRow> Find(AddonsView view, string search, string category)
		{
			var rows = new List<AddonRow>();
			foreach (var r in AddonsView.Filter(view.Catalog, search, category))
			{
				var row = new AddonRow
				{
					Key = r.Id,
					Title = r.Name,
					Kind = Category(r.Entry.Category),
					Extra = T.S(r.SourceKey),
					Detail = r.Entry.Notice != null ? r.Entry.Description + " " + r.Entry.Notice : r.Entry.Description,
					Status = StatusText(r),
					Tone = ToneOf(r.StatusKey),
					Tip = r.Error,
					Checked = r.CanSelect ? r.Selected : (bool?)null,
					Note = Covered(r.Entry),
				};
				if (r.InUse) { row.Button = AddonAction.TakeOver; row.ButtonText = T.S("addons.takeover.short"); }
				Choices(row, r);
				rows.Add(row);
			}
			return rows;
		}

		static void Choices(AddonRow row, CatalogRow r)
		{
			if (r.CanSelect && r.Entry.Source == AddonSource.GitHub && r.Selected)
			{
				row.Menu.Add(new MenuChoice { Text = T.S("addons.hold"), Checked = r.Held, Action = AddonAction.Hold, Arg = r.Id });
				row.Menu.Add(new MenuChoice { Text = T.S("addons.prerelease"), Checked = r.PreRelease, Action = AddonAction.PreRelease, Arg = r.Id });
			}
			if (r.Entry.PageUrl != null) row.Menu.Add(new MenuChoice { Text = T.S("addons.page"), Action = AddonAction.OpenPage, Arg = r.Id });
			if (r.InUse) row.Menu.Add(new MenuChoice { Text = T.S("addons.takeover"), Action = AddonAction.TakeOver, Arg = r.Id });
		}

		/// <summary>The column headings of the catalog list or the Installed list.</summary>
		public static string[] Headers(bool installed)
		{
			return installed
				? new[] { T.S("addons.col.name"), T.S("addons.col.updates"), T.S("addons.col.version"), T.S("addons.col.notes") }
				: new[] { T.S("addons.col.name"), T.S("addons.col.category"), T.S("addons.col.source"), T.S("addons.col.status") };
		}

		/// <summary>The rows sorted by one column (0 name, 1 kind, 2 extra, 3 status); suggestions stay on top and empty cells go last.</summary>
		public static List<AddonRow> Sort(List<AddonRow> rows, int column, bool descending)
		{
			Func<AddonRow, string> cell = column == 1 ? (Func<AddonRow, string>)(r => r.Kind) : column == 2 ? r => r.Extra : column == 3 ? r => r.Status : (Func<AddonRow, string>)(r => r.Title);
			var top = rows.Where(r => r.Key.StartsWith("suggest:", StringComparison.Ordinal)).ToList();
			var rest = rows.Where(r => !r.Key.StartsWith("suggest:", StringComparison.Ordinal)).ToList();
			var filled = rest.Where(r => !string.IsNullOrEmpty(cell(r))).ToList();
			var sorted = descending
				? filled.OrderByDescending(cell, NaturalOrder.Instance).ThenBy(r => r.Title, NaturalOrder.Instance)
				: filled.OrderBy(cell, NaturalOrder.Instance).ThenBy(r => r.Title, NaturalOrder.Instance);
			return top.Concat(sorted).Concat(rest.Where(r => string.IsNullOrEmpty(cell(r)))).ToList();
		}

		/// <summary>Text order that ignores case and reads runs of digits as numbers, so 1.9 comes before 1.10.</summary>
		sealed class NaturalOrder : IComparer<string>
		{
			public static readonly NaturalOrder Instance = new NaturalOrder();

			public int Compare(string a, string b)
			{
				a = a ?? "";
				b = b ?? "";
				int i = 0, j = 0;
				while (i < a.Length && j < b.Length)
				{
					if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
					{
						int si = i, sj = j;
						while (i < a.Length && char.IsDigit(a[i])) i++;
						while (j < b.Length && char.IsDigit(b[j])) j++;
						string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
						int c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
						if (c != 0) return c;
					}
					else
					{
						int c = string.Compare(a[i].ToString(), b[j].ToString(), StringComparison.CurrentCultureIgnoreCase);
						if (c != 0) return c;
						i++;
						j++;
					}
				}
				return (a.Length - i).CompareTo(b.Length - j);
			}
		}

		public static List<AddonRow> Installed(AddonsView view, bool showHidden)
		{
			var rows = new List<AddonRow>();
			foreach (var s in view.Suggestions)
				rows.Add(new AddonRow
				{
					Key = "suggest:" + s.Entry.Id,
					Title = T.F("addons.suggest", string.Join(", ", s.NeededBy), s.Entry.Legacy != null ? s.Entry.Legacy.Name : s.Entry.Name),
					Detail = s.Entry.Description,
					Tone = Tone.Info,
					Button = AddonAction.Add,
					ButtonText = T.S("addons.add"),
					Menu = { new MenuChoice { Text = T.S("addons.add"), Action = AddonAction.Add, Arg = s.Entry.Id } },
				});
			foreach (var a in view.Installed.Where(a => showHidden || !a.Ignored))
			{
				var entry = a.CatalogId != null ? AddonCatalog.Find(a.CatalogId) : null;
				var row = new AddonRow
				{
					Key = string.Join("/", a.Folders),
					Title = a.State == AddonState.GuideData ? T.S("addons.state.guidedata") : a.Name,
					Kind = a.State == AddonState.Managed && entry != null && view.Catalog.Any(c => c.Id == entry.Id && c.Held) ? T.S("extra.Held") : T.S("addons.state." + a.State.ToString().ToLowerInvariant()),
					Extra = a.Version ?? "",
					Detail = string.Join(", ", a.Folders),
					Note = Covered(entry),
					Tone = a.State == AddonState.Managed || a.State == AddonState.EldoUI || a.State == AddonState.GuideData ? Tone.Ok : Tone.Plain,
				};
				var notes = new List<string>();
				if (!a.Forever) notes.Add(T.S("addons.notforever"));
				if (a.MissingDependencies.Count > 0) notes.Add(T.F("addons.needs", string.Join(", ", a.MissingDependencies)));
				if (notes.Count > 0) { row.Status = string.Join(" · ", notes); row.Tone = Tone.Warn; }
				if (a.State == AddonState.Managed && entry != null)
				{
					row.Menu.Add(new MenuChoice { Text = T.S("addons.hold"), Checked = view.Catalog.Any(c => c.Id == entry.Id && c.Held), Action = AddonAction.Hold, Arg = entry.Id });
					row.Menu.Add(new MenuChoice { Text = T.S("addons.prerelease"), Checked = view.Catalog.Any(c => c.Id == entry.Id && c.PreRelease), Action = AddonAction.PreRelease, Arg = entry.Id });
					row.Menu.Add(new MenuChoice { Text = T.S("addons.stop"), Action = AddonAction.Deselect, Arg = entry.Id });
				}
				if (a.State == AddonState.ByHand && entry != null && entry.Installable)
				{
					row.Button = AddonAction.TakeOver;
					row.ButtonText = T.S("addons.takeover.short");
					row.Menu.Add(new MenuChoice { Text = T.S("addons.takeover"), Action = AddonAction.TakeOver, Arg = entry.Id });
				}
				if (entry != null && entry.PageUrl != null) row.Menu.Add(new MenuChoice { Text = T.S("addons.page"), Action = AddonAction.OpenPage, Arg = entry.Id });
				if (a.State == AddonState.ByHand || a.State == AddonState.Other)
					row.Menu.Add(a.Ignored
						? new MenuChoice { Text = T.S("addons.show"), Action = AddonAction.Show, Arg = row.Key }
						: new MenuChoice { Text = T.S("addons.hide"), Action = AddonAction.Hide, Arg = row.Key });
				rows.Add(row);
			}
			return rows;
		}

		/// <summary>
		/// The note for an addon EldoUI's own modules cover, in the player's language: one line each for the modules
		/// that do its whole job, the ones that do part of it and the ones that work with it; null when there are none.
		/// </summary>
		public static string Covered(CatalogEntry entry)
		{
			if (entry == null || entry.CoveredBy.Count == 0) return null;
			var lines = new List<string>();
			foreach (var scope in new[] { "full", "part", "works" })
			{
				var names = entry.CoveredBy.Where(m => m.Scope == scope).Select(m => T.Lang == "da" ? m.Danish : m.English).ToList();
				if (names.Count == 0) continue;
				string list = names.Count <= 3 ? string.Join(", ", names) : string.Join(", ", names.Take(3)) + " " + T.F("addons.covered.more", names.Count - 3);
				lines.Add(T.F(scope == "full" ? "addons.covered" : "addons.covered." + scope, list));
			}
			return string.Join("\n", lines);
		}

		public static int HiddenCount(AddonsView view) { return view.Installed.Count(a => a.Ignored); }

		public static string StatusText(CatalogRow r)
		{
			if (r.StatusKey == null) return "";
			return T.F(r.StatusKey, r.StatusArg ?? "").Trim(' ', '·');
		}

		static Tone ToneOf(string key)
		{
			switch (key)
			{
				case "addons.status.current":
				case "addons.status.installed": return Tone.Ok;
				case "addons.status.available":
				case "addons.status.selected":
				case "addons.status.external":
				case "addons.status.held": return Tone.Info;
				case "addons.status.failed": return Tone.Error;
				case "addons.status.inuse":
				case "addons.status.byhand": return Tone.Warn;
				default: return Tone.Plain;
			}
		}

		/// <summary>A catalog category in the player's language.</summary>
		public static string Category(string category)
		{
			if (category == null) return "";
			string key = "addons.cat." + category.ToLowerInvariant().Replace(' ', '_');
			string text = T.S(key);
			return text == key ? category : text;
		}

		/// <summary>
		/// Applies a choice from the window to the settings. True when something changed and should be saved.
		/// Taking over an addon put there by hand only ever happens after the window asked the player.
		/// </summary>
		public static bool Apply(Settings s, AddonAction action, string arg)
		{
			switch (action)
			{
				case AddonAction.Select:
				case AddonAction.Add:
					if (!Settings.Selectable(arg)) return false;
					s.SelectCompanion(arg, true);
					return true;
				case AddonAction.Deselect:
					if (!Settings.Selectable(arg)) return false;
					s.SelectCompanion(arg, false);
					return true;
				case AddonAction.Hold: return Toggle(s.Held, s, arg);
				case AddonAction.PreRelease: return Toggle(s.PreRelease, s, arg);
				case AddonAction.TakeOver:
					var entry = AddonCatalog.Find(arg);
					if (entry == null || !entry.Installable) return false;
					s.SelectCompanion(arg, true);
					s.Adopted.Add(arg);
					return true;
				case AddonAction.Hide:
				case AddonAction.Show:
					var folders = (arg ?? "").Split('/').Where(f => f.Length > 0).ToList();
					if (folders.Count == 0) return false;
					foreach (string f in folders) { if (action == AddonAction.Hide) s.IgnoredFolders.Add(f); else s.IgnoredFolders.Remove(f); }
					return true;
				default:
					return false;
			}
		}

		/// <summary>A made-up AddOns folder, choices and check results, for the pictures of the window that CI takes.</summary>
		public static AddonsView Sample(string addOnsDir, bool busy = false)
		{
			var tocs = new[]
			{
				new[] { "EldoUI", "EldoUI", "0.7.0003", "" },
				new[] { "EldoUI_GuideData", "EldoUI Guide Data", "1.0.0", "EldoUI" },
				new[] { "DBM-Core", "Deadly Boss Mods", "11.2.5", "" },
				new[] { "DBM-StatusBarTimers", "DBM Timers", "11.2.5", "DBM-Core" },
				new[] { "MoveAny", "MoveAny", "1.8.12", "" },
				new[] { "Plater", "Plater Nameplates", "620", "" },
				new[] { "MyErrorFrame", "My Error Frame", "2.1", "BugSack" },
				new[] { "OldMapNotes", "Old Map Notes", "1.0", "" },
			};
			Directory.CreateDirectory(addOnsDir);
			foreach (var t in tocs)
			{
				string dir = Path.Combine(addOnsDir, t[0]);
				Directory.CreateDirectory(dir);
				string iface = t[0] == "OldMapNotes" ? "11507" : "16001";
				File.WriteAllText(Path.Combine(dir, t[0] + ".toc"), "## Interface: " + iface + "\n## Title: " + t[1] + "\n## Version: " + t[2] + "\n" + (t[3].Length > 0 ? "## Dependencies: " + t[3] + "\n" : ""));
			}
			var s = new Settings { AddOnsDir = addOnsDir };
			foreach (string id in new[] { "dbm", "move-any", "bigwigs", "plater", "elvui" }) s.SelectCompanion(id, true);
			s.Held.Add("move-any");
			s.ExtraFiles["dbm"] = new List<string> { "DBM-Core/DBM-Core.toc", "DBM-StatusBarTimers/DBM-StatusBarTimers.toc" };
			s.ExtraVersions["dbm"] = "11.2.5";
			s.ExtraFiles["move-any"] = new List<string> { "MoveAny/MoveAny.toc" };
			s.ExtraVersions["move-any"] = "1.8.12";
			var results = new List<CompanionResult>
			{
				new CompanionResult { Id = "dbm", Name = "DBM", Version = "11.2.5", Outcome = CompanionOutcome.Current },
				new CompanionResult { Id = "move-any", Name = "MoveAny", Version = "1.8.14", Outcome = CompanionOutcome.Held },
				new CompanionResult { Id = "bigwigs", Name = "BigWigs", Version = "v400", Outcome = CompanionOutcome.Available },
				new CompanionResult { Id = "elvui", Name = "ElvUI", Outcome = CompanionOutcome.NoForever },
				new CompanionResult { Id = "plater", Name = "Plater Nameplates", Outcome = CompanionOutcome.InUse, Folders = new List<string> { "Plater" } },
			};
			return AddonsView.Build(s, results, AddonScanner.Scan(addOnsDir, s), busy);
		}

		static bool Toggle(HashSet<string> set, Settings s, string id)
		{
			if (!Settings.Selectable(id) || !s.SelectedCompanions.Contains(id)) return false;
			if (!set.Remove(id)) set.Add(id);
			return true;
		}
	}
}
