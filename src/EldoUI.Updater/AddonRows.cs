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

	enum AddonAction { Select, Deselect, Hold, PreRelease, OpenPage, TakeOver, Hide, Show, Add, Uninstall, OpenFolder, Reinstall }

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

		/// <summary>
		/// The Installed list. marked holds the rows the player ticked to uninstall together; null shows no tick boxes.
		/// </summary>
		public static List<AddonRow> Installed(AddonsView view, bool showHidden, ICollection<string> marked = null)
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
				bool removable = AddonRemover.CanRemove(a);
				var row = new AddonRow
				{
					Key = AddonRemover.Key(a),
					Title = a.State == AddonState.GuideData ? T.S("addons.state.guidedata") : a.Name,
					Kind = a.State == AddonState.Managed && entry != null && view.Catalog.Any(c => c.Id == entry.Id && c.Held) ? T.S("extra.Held") : T.S("addons.state." + a.State.ToString().ToLowerInvariant()),
					Extra = a.Version ?? "",
					Detail = Detail(a),
					Note = Covered(entry),
					Tone = a.State == AddonState.Managed || a.State == AddonState.EldoUI || a.State == AddonState.GuideData ? Tone.Ok : Tone.Plain,
					Tip = removable ? null : T.S("addons.protected"),
				};
				if (marked != null && removable) row.Checked = marked.Contains(row.Key);
				var notes = new List<string>();
				if (!a.Forever) notes.Add(T.S("addons.notforever"));
				if (a.MissingDependencies.Count > 0) notes.Add(T.F("addons.needs", string.Join(", ", a.MissingDependencies)));
				if (notes.Count > 0) { row.Status = string.Join(" · ", notes); row.Tone = Tone.Warn; }
				if (a.State == AddonState.Managed && entry != null)
				{
					bool held = view.Catalog.Any(c => c.Id == entry.Id && c.Held);
					row.Menu.Add(new MenuChoice { Text = T.S("addons.hold"), Checked = held, Action = AddonAction.Hold, Arg = entry.Id });
					row.Menu.Add(new MenuChoice { Text = T.S("addons.prerelease"), Checked = view.Catalog.Any(c => c.Id == entry.Id && c.PreRelease), Action = AddonAction.PreRelease, Arg = entry.Id });
					row.Menu.Add(new MenuChoice { Text = T.S("addons.stop"), Action = AddonAction.Deselect, Arg = entry.Id });
					// A kept version is not replaced, so only an addon that follows its releases can be installed again.
					if (!held && CanReinstall(view, entry)) row.Menu.Add(new MenuChoice { Text = T.S("addons.reinstall"), Action = AddonAction.Reinstall, Arg = entry.Id });
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
				row.Menu.Add(new MenuChoice { Text = T.S("addons.openfolder"), Action = AddonAction.OpenFolder, Arg = MainFolder(a) });
				if (removable) row.Menu.Add(new MenuChoice { Text = T.S("addons.uninstall"), Action = AddonAction.Uninstall, Arg = row.Key });
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

		/// <summary>A catalog addon's name as the lists show it.</summary>
		public static string CatalogName(string id)
		{
			var entry = AddonCatalog.Find(id);
			return entry == null ? id : entry.Legacy != null ? entry.Legacy.Name : entry.Name;
		}

		static bool CanReinstall(AddonsView view, CatalogEntry entry)
		{
			return entry.Installable && entry.Source == AddonSource.GitHub && view.Catalog.Any(c => c.Id == entry.Id && c.Selected);
		}

		/// <summary>The folder named after the addon (the shortest name of its folders), for opening it.</summary>
		static string MainFolder(InstalledAddon a)
		{
			return a.Folders.OrderBy(f => f.Length).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).First();
		}

		/// <summary>The line under an installed addon: its folders, their size and when they were last installed or updated.</summary>
		public static string Detail(InstalledAddon a)
		{
			var parts = new List<string> { string.Join(", ", a.Folders) };
			if (a.Bytes > 0) parts.Add(Size(a.Bytes));
			if (a.Changed.HasValue) parts.Add(T.F("addons.updated", a.Changed.Value.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture)));
			return string.Join(" · ", parts);
		}

		/// <summary>A size the way a file manager shows it: 640 KB, 4.2 MB, 1.3 GB.</summary>
		public static string Size(long bytes)
		{
			var culture = System.Globalization.CultureInfo.CurrentCulture;
			if (bytes < 1024 * 1024) return Math.Max(1, (bytes + 1023) / 1024).ToString(culture) + " KB";
			if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString(bytes < 10L * 1024 * 1024 ? "0.0" : "0", culture) + " MB";
			return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", culture) + " GB";
		}

		/// <summary>The question before an uninstall, the same on Windows and Linux.</summary>
		public static string RemovalTitle(RemovalPlan plan)
		{
			return plan.Names.Count == 1 ? T.F("addons.uninstall.title", plan.Names[0]) : T.F("addons.uninstall.title.many", plan.Names.Count);
		}

		public static string RemovalBody(RemovalPlan plan)
		{
			bool many = plan.Names.Count > 1;
			var lines = new List<string> { T.F("addons.uninstall.body", string.Join(", ", plan.Folders)) };
			if (plan.Managed) lines.Add(T.S(many ? "addons.uninstall.managed.many" : "addons.uninstall.managed"));
			if (plan.NeededBy.Count > 0) lines.Add(T.F(many ? "addons.uninstall.needed.many" : "addons.uninstall.needed", string.Join(", ", plan.NeededBy)));
			lines.Add(T.S("addons.uninstall.final"));
			return string.Join("\n\n", lines);
		}

		/// <summary>The text of the saved settings box, or null when the game keeps none for these folders.</summary>
		public static string RemovalBox(RemovalPlan plan)
		{
			if (plan.SavedSettings.Count == 0) return null;
			return T.F(plan.Names.Count > 1 ? "addons.uninstall.box.many" : "addons.uninstall.box", Files(plan.SavedSettings.Count));
		}

		/// <summary>What the window says after an uninstall.</summary>
		public static string RemovalDone(RemovalPlan plan, RemovalResult result)
		{
			string text = plan.Names.Count == 1 ? T.F("addons.uninstall.done", plan.Names[0]) : T.F("addons.uninstall.done.many", plan.Names.Count);
			if (result.SettingsDeleted > 0) text += " " + T.F("addons.uninstall.done.settings", Files(result.SettingsDeleted));
			if (result.LeftOver) text += " " + T.S("addons.uninstall.leftover");
			return text;
		}

		static string Files(int count) { return count == 1 ? T.S("addons.uninstall.file") : T.F("addons.uninstall.files", count); }

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
				case AddonAction.Reinstall:
					List<string> files;
					if (!Settings.Selectable(arg) || !s.ExtraAddons || !s.SelectedCompanions.Contains(arg) || s.Held.Contains(arg)
						|| !s.ExtraFiles.TryGetValue(arg, out files) || files.Count == 0) return false;
					s.Reinstall.Add(arg);
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
				new[] { "EldoUI", "EldoUI", "0.7.0003", "", "6100" },
				new[] { "EldoUI_GuideData", "EldoUI Guide Data", "1.0.0", "EldoUI", "1240" },
				new[] { "DBM-Core", "Deadly Boss Mods", "11.2.5", "", "2460" },
				new[] { "DBM-StatusBarTimers", "DBM Timers", "11.2.5", "DBM-Core", "310" },
				new[] { "MoveAny", "MoveAny", "1.8.12", "", "920" },
				new[] { "Plater", "Plater Nameplates", "620", "", "3180" },
				new[] { "MyErrorFrame", "My Error Frame", "2.1", "BugSack", "64" },
				new[] { "OldMapNotes", "Old Map Notes", "1.0", "", "450" },
			};
			Directory.CreateDirectory(addOnsDir);
			foreach (var t in tocs)
			{
				string dir = Path.Combine(addOnsDir, t[0]);
				Directory.CreateDirectory(dir);
				string iface = t[0] == "OldMapNotes" ? "11507" : "16001";
				File.WriteAllText(Path.Combine(dir, t[0] + ".toc"), "## Interface: " + iface + "\n## Title: " + t[1] + "\n## Version: " + t[2] + "\n" + (t[3].Length > 0 ? "## Dependencies: " + t[3] + "\n" : ""));
				// A sparse file, so the sizes look real without writing megabytes.
				using (var media = new FileStream(Path.Combine(dir, "media.tga"), FileMode.Create)) media.SetLength(long.Parse(t[4]) * 1024);
			}
			// Saved settings for one addon, when the sample sits in a game folder (Interface\AddOns next to WTF).
			string flavorInterface = Path.GetDirectoryName(addOnsDir.TrimEnd('\\', '/'));
			if (flavorInterface != null && string.Equals(Path.GetFileName(flavorInterface), "Interface", StringComparison.OrdinalIgnoreCase))
			{
				string saved = Path.Combine(Path.GetDirectoryName(flavorInterface), "WTF", "Account", "PLAYER", "SavedVariables");
				Directory.CreateDirectory(saved);
				File.WriteAllText(Path.Combine(saved, "OldMapNotes.lua"), "OldMapNotesDB = {}\n");
				File.WriteAllText(Path.Combine(saved, "OldMapNotes.lua.bak"), "OldMapNotesDB = {}\n");
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
