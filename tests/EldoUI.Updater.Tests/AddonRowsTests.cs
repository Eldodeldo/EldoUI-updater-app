using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using EldoUI.Updater.Core;
using Xunit;

namespace EldoUI.Updater.Tests
{
	public class AddonRowsTests
	{
		static Settings Chosen(params string[] ids)
		{
			var s = new Settings { AddOnsDir = "/nowhere" };
			foreach (string id in ids) s.SelectCompanion(id, true);
			return s;
		}

		[Fact]
		public void ChoicesChangeOnlyWhatThePlayerPicked()
		{
			var s = new Settings();
			Assert.True(AddonRows.Apply(s, AddonAction.Select, "bigwigs"));
			Assert.Contains("bigwigs", s.SelectedCompanions);
			Assert.True(s.ExtraAddons);
			Assert.False(AddonRows.Apply(s, AddonAction.Select, "leatrix-plus"));
			Assert.False(AddonRows.Apply(s, AddonAction.Select, "no-such-addon"));
			Assert.False(AddonRows.Apply(s, AddonAction.Hold, "plater"));

			Assert.True(AddonRows.Apply(s, AddonAction.Hold, "bigwigs"));
			Assert.True(AddonRows.Apply(s, AddonAction.PreRelease, "bigwigs"));
			Assert.Contains("bigwigs", s.Held);
			Assert.Contains("bigwigs", s.PreRelease);
			Assert.True(AddonRows.Apply(s, AddonAction.Hold, "bigwigs"));
			Assert.DoesNotContain("bigwigs", s.Held);

			Assert.True(AddonRows.Apply(s, AddonAction.Deselect, "bigwigs"));
			Assert.DoesNotContain("bigwigs", s.SelectedCompanions);
			Assert.DoesNotContain("bigwigs", s.PreRelease);
			Assert.False(s.ExtraAddons);

			Assert.False(AddonRows.Apply(s, AddonAction.OpenPage, "bigwigs"));
		}

		[Fact]
		public void TakingOverNeedsAnInstallableAddon()
		{
			var s = new Settings();
			Assert.True(AddonRows.Apply(s, AddonAction.TakeOver, "plater"));
			Assert.Contains("plater", s.SelectedCompanions);
			Assert.Contains("plater", s.Adopted);
			Assert.False(AddonRows.Apply(s, AddonAction.TakeOver, "elvui"));
			Assert.False(AddonRows.Apply(s, AddonAction.TakeOver, "questie"));
			Assert.DoesNotContain("elvui", s.Adopted);
		}

		[Fact]
		public void HidingAndShowingTakesEveryFolderOfTheRow()
		{
			var s = new Settings();
			Assert.True(AddonRows.Apply(s, AddonAction.Hide, "MyAddon/MyAddon_Options"));
			Assert.Contains("MyAddon", s.IgnoredFolders);
			Assert.Contains("myaddon_options", s.IgnoredFolders);
			Assert.True(AddonRows.Apply(s, AddonAction.Show, "MyAddon/MyAddon_Options"));
			Assert.Empty(s.IgnoredFolders);
			Assert.False(AddonRows.Apply(s, AddonAction.Hide, ""));
		}

		[Fact]
		public void CatalogRowsShowTheLastCheck()
		{
			var s = Chosen("bigwigs", "plater", "elvui", "move-any");
			s.Held.Add("move-any");
			var last = new List<CompanionResult>
			{
				new CompanionResult { Id = "bigwigs", Version = "v400", Outcome = CompanionOutcome.Available },
				new CompanionResult { Id = "plater", Outcome = CompanionOutcome.InUse, Folders = new List<string> { "Plater" } },
				new CompanionResult { Id = "elvui", Outcome = CompanionOutcome.NoForever },
				new CompanionResult { Id = "move-any", Version = "1.9", Outcome = CompanionOutcome.Held },
			};
			var view = AddonsView.Build(s, last, new List<InstalledAddon>(), false);
			CatalogRow Row(string id) { return view.Catalog.Single(r => r.Id == id); }
			Assert.Equal("addons.status.available", Row("bigwigs").StatusKey);
			Assert.Equal("v400", Row("bigwigs").StatusArg);
			Assert.True(Row("plater").InUse);
			Assert.Equal("addons.status.noforever", Row("elvui").StatusKey);
			Assert.True(Row("move-any").Held);
			Assert.False(Row("leatrix-plus").CanSelect);
			Assert.Equal("addons.status.page", Row("leatrix-plus").StatusKey);
			Assert.Equal("addons.status.noforever", Row("questie").StatusKey);
			Assert.Null(Row("dbm").StatusKey);

			var rows = AddonRows.Find(view, "", null);
			Assert.Equal(new[] { "bigwigs", "elvui", "move-any", "plater" }, rows.Take(4).Select(r => r.Key).OrderBy(k => k));
			var plater = rows.Single(r => r.Key == "plater");
			Assert.Equal(AddonAction.TakeOver, plater.Button);
			Assert.Contains(plater.Menu, m => m.Action == AddonAction.TakeOver && m.Arg == "plater");
			Assert.Contains(plater.Menu, m => m.Action == AddonAction.Hold && m.Checked == false);
			var page = rows.Single(r => r.Key == "leatrix-plus");
			Assert.Null(page.Checked);
			Assert.Equal(new[] { AddonAction.OpenPage }, page.Menu.Select(m => m.Action));
			var tukui = rows.Single(r => r.Key == "elvui");
			Assert.DoesNotContain(tukui.Menu, m => m.Action == AddonAction.Hold || m.Action == AddonAction.PreRelease);
			Assert.All(AddonRows.Find(view, "nameplate", null), r => Assert.True(
				r.Title.IndexOf("nameplate", StringComparison.OrdinalIgnoreCase) >= 0 || r.Detail.IndexOf("nameplate", StringComparison.OrdinalIgnoreCase) >= 0
				|| AddonCatalog.Find(r.Key).Folders.Any(f => f.IndexOf("nameplate", StringComparison.OrdinalIgnoreCase) >= 0)));
			Assert.All(AddonRows.Find(view, null, "Bags"), r => Assert.Equal("Bags", AddonCatalog.Find(r.Key).Category));
		}

		[Fact]
		public void TheSamplePicturesBuildAndCoverEveryRowKind()
		{
			using (var t = new TempDir())
			{
				var view = AddonRows.Sample(t.Sub("AddOns"));
				var installed = AddonRows.Installed(view, false);
				Assert.Contains(installed, r => r.Key.StartsWith("suggest:") && r.Button == AddonAction.Add);
				Assert.Contains(installed, r => r.Button == AddonAction.TakeOver);
				Assert.Contains(installed, r => r.Menu.Any(m => m.Action == AddonAction.Hide));
				Assert.Contains(installed, r => r.Menu.Any(m => m.Action == AddonAction.Deselect));
				var find = AddonRows.Find(view, "", null);
				Assert.Contains(find, r => r.Key == "plater" && r.Button == AddonAction.TakeOver);
				Assert.Contains(find, r => r.Key == "move-any" && r.Menu.Any(m => m.Action == AddonAction.Hold && m.Checked == true));
				Assert.Equal(0, AddonRows.HiddenCount(view));
			}
		}

		[Fact]
		public void AddonsEldoUICoversCarryANote()
		{
			Assert.False(string.IsNullOrEmpty(AddonCatalog.CoverageVersion));
			Assert.Equal(new[] { "Nameplates" }, AddonCatalog.Find("plater").CoveredBy.Select(m => m.Module));
			Assert.True(AddonCatalog.Find("elvui").CoveredBy.Count > 3);
			Assert.Empty(AddonCatalog.Find("texture-atlas-viewer").CoveredBy);
			Assert.All(AddonCatalog.All.SelectMany(e => e.CoveredBy), m => Assert.False(string.IsNullOrEmpty(m.English) || string.IsNullOrEmpty(m.Danish)));
			Assert.All(AddonCatalog.All.SelectMany(e => e.CoveredBy), m => Assert.Contains(m.Scope, new[] { "full", "part", "works" }));
			Assert.Null(AddonRows.Covered(AddonCatalog.Find("texture-atlas-viewer")));
		}

		[Fact]
		public void EachAddonNamesAModuleOnlyOnce()
		{
			foreach (var e in AddonCatalog.All)
				Assert.Equal(e.CoveredBy.Count, e.CoveredBy.Select(m => m.Module).Distinct().Count());
		}

		[Fact]
		public void TheNoteSaysHowMuchEldoUIDoes()
		{
			Assert.All(AddonCatalog.Find("plater").CoveredBy, m => Assert.Equal("full", m.Scope));
			Assert.Contains(AddonCatalog.Find("all-the-things").CoveredBy, m => m.Module == "Appearances" && m.Scope == "part");
			Assert.DoesNotContain(AddonCatalog.Find("all-the-things").CoveredBy, m => m.Scope == "full");
			Assert.All(AddonCatalog.Find("dbm").CoveredBy, m => Assert.Equal("works", m.Scope));
			Assert.NotEmpty(AddonCatalog.Find("dbm").CoveredBy);
		}

		[Fact]
		public void ScopesReadFromTheListAndDefaultToFull()
		{
			var list = new List<CatalogEntry> { new CatalogEntry { Id = "a" }, new CatalogEntry { Id = "b" } };
			AddonCatalog.WithCoverage(list, "{\"addons\":{\"a\":[{\"module\":\"X\",\"en\":\"X\"},{\"module\":\"Y\",\"scope\":\"part\",\"en\":\"Y\"},{\"module\":\"Z\",\"scope\":\"odd\",\"en\":\"Z\"}],\"b\":[{\"module\":\"W\",\"scope\":\"works\",\"en\":\"W\"}]}}");
			Assert.Equal(new[] { "full", "part", "full" }, list[0].CoveredBy.Select(m => m.Scope));
			Assert.Equal("works", list[1].CoveredBy.Single().Scope);
		}

		[Fact]
		public void TheCoverageListNamesOnlyCatalogAddons()
		{
			foreach (string id in Json.ParseObject(CoverageData.Json).Obj("addons").Keys)
				Assert.NotNull(AddonCatalog.Find(id));
		}

		[Fact]
		public void CoveredRowsShowTheNoteInBothLists()
		{
			using (var t = new TempDir())
			{
				var view = AddonRows.Sample(t.Sub("AddOns"));
				Assert.NotNull(AddonRows.Find(view, "", null).Single(r => r.Key == "plater").Note);
				Assert.Null(AddonRows.Find(view, "", null).Single(r => r.Key == "texture-atlas-viewer").Note);
				Assert.Contains(AddonRows.Installed(view, false), r => r.Note != null);
			}
		}

		[Fact]
		public void HiddenFoldersStayOutUntilAskedFor()
		{
			using (var t = new TempDir())
			{
				Directory.CreateDirectory(t.Sub("AddOns", "Lonely"));
				File.WriteAllText(t.Sub("AddOns", "Lonely", "Lonely.toc"), "## Interface: 16001\n");
				var s = new Settings { AddOnsDir = t.Sub("AddOns") };
				s.IgnoredFolders.Add("Lonely");
				var view = AddonsView.Build(s, null, AddonScanner.Scan(s.AddOnsDir, s), false);
				Assert.Equal(1, AddonRows.HiddenCount(view));
				Assert.DoesNotContain(AddonRows.Installed(view, false), r => r.Key == "Lonely");
				var shown = AddonRows.Installed(view, true).Single(r => r.Key == "Lonely");
				Assert.Contains(shown.Menu, m => m.Action == AddonAction.Show && m.Arg == "Lonely");
			}
		}

		[Fact]
		public void EveryInstalledAddonButEldoUICanBeUninstalled()
		{
			using (var t = new TempDir())
			{
				var view = AddonRows.Sample(t.Sub("AddOns"));
				var rows = AddonRows.Installed(view, false).Where(r => !r.Key.StartsWith("suggest:")).ToList();
				Assert.Equal(view.Installed.Count, rows.Count);
				foreach (var row in rows)
				{
					bool eldo = row.Key.Split('/').Any(AddonInstaller.IsEldoUIFolder);
					Assert.Equal(!eldo, row.Menu.Any(m => m.Action == AddonAction.Uninstall && m.Arg == row.Key));
					Assert.Contains(row.Menu, m => m.Action == AddonAction.OpenFolder);
					if (eldo) Assert.Equal(T.S("addons.protected"), row.Tip);
					// No tick boxes unless the window keeps a selection.
					Assert.Null(row.Checked);
				}
				Assert.Contains(rows, r => r.Title == "Old Map Notes" && r.Menu.Any(m => m.Action == AddonAction.Uninstall));

				var marked = new HashSet<string> { "OldMapNotes", "EldoUI" };
				var ticked = AddonRows.Installed(view, false, marked);
				Assert.True(ticked.Single(r => r.Key == "OldMapNotes").Checked);
				Assert.False(ticked.Single(r => r.Key == "MoveAny").Checked);
				Assert.Null(ticked.Single(r => r.Key == "EldoUI").Checked);
				Assert.All(ticked.Where(r => r.Key.StartsWith("suggest:")), r => Assert.Null(r.Checked));
			}
		}

		[Fact]
		public void OnlyAnAddonThatFollowsItsReleasesCanBeReinstalled()
		{
			using (var t = new TempDir())
			{
				var view = AddonRows.Sample(t.Sub("AddOns"));
				var rows = AddonRows.Installed(view, false);
				Assert.Contains(rows.Single(r => r.Key.StartsWith("DBM-Core")).Menu, m => m.Action == AddonAction.Reinstall && m.Arg == "dbm");
				// MoveAny is kept at its version, so installing the newest one again would break that promise.
				Assert.DoesNotContain(rows.Single(r => r.Key == "MoveAny").Menu, m => m.Action == AddonAction.Reinstall);

				var s = new Settings();
				s.SelectCompanion("bugsack", true);
				Assert.False(AddonRows.Apply(s, AddonAction.Reinstall, "bugsack"));
				s.ExtraFiles["bugsack"] = new List<string> { "BugSack/BugSack.toc" };
				Assert.True(AddonRows.Apply(s, AddonAction.Reinstall, "bugsack"));
				Assert.Contains("bugsack", s.Reinstall);
				s.Held.Add("bugsack");
				s.Reinstall.Clear();
				Assert.False(AddonRows.Apply(s, AddonAction.Reinstall, "bugsack"));
				Assert.False(AddonRows.Apply(s, AddonAction.Uninstall, "BugSack"));
				Assert.False(AddonRows.Apply(s, AddonAction.OpenFolder, "BugSack"));
				Assert.Empty(s.Reinstall);
			}
		}

		[Fact]
		public void InstalledRowsShowSizeAndDate()
		{
			var a = new InstalledAddon { Name = "X", Folders = { "X", "X_Options" }, Bytes = 3 * 1024 * 1024 + 300 * 1024, Changed = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc) };
			string detail = AddonRows.Detail(a);
			Assert.StartsWith("X, X_Options · ", detail);
			Assert.Contains(" MB · ", detail);
			Assert.Contains("2026", detail);
			Assert.Equal("X", AddonRows.Detail(new InstalledAddon { Folders = { "X" } }));
			Assert.EndsWith(" KB", AddonRows.Size(10));
			Assert.EndsWith(" MB", AddonRows.Size(50L * 1024 * 1024));
			Assert.EndsWith(" GB", AddonRows.Size(2L * 1024 * 1024 * 1024));
		}

		[Theory]
		[InlineData("en")]
		[InlineData("da")]
		public void TheUninstallQuestionSaysWhatHappens(string language)
		{
			T.Use(language);
			try
			{
				var one = new RemovalPlan { Names = { "Old Map Notes" }, Folders = { "OldMapNotes", "OldMapNotes_Options" }, NeededBy = { "Map Pins" }, SavedSettings = { "a.lua" }, Managed = true };
				Assert.Contains("Old Map Notes", AddonRows.RemovalTitle(one));
				string body = AddonRows.RemovalBody(one);
				Assert.Contains("OldMapNotes, OldMapNotes_Options", body);
				Assert.Contains("Map Pins", body);
				Assert.Contains(T.S("addons.uninstall.managed"), body);
				Assert.EndsWith(T.S("addons.uninstall.final"), body);
				Assert.Contains(T.S("addons.uninstall.file"), AddonRows.RemovalBox(one));
				Assert.Null(AddonRows.RemovalBox(new RemovalPlan { Names = { "X" }, Folders = { "X" } }));
				var many = new RemovalPlan { Names = { "A", "B" }, Folders = { "A", "B" }, SavedSettings = { "a.lua", "b.lua" } };
				Assert.Contains("2", AddonRows.RemovalTitle(many));
				Assert.Contains(T.F("addons.uninstall.files", 2), AddonRows.RemovalBox(many));
				Assert.DoesNotContain(T.S("addons.uninstall.managed"), AddonRows.RemovalBody(many));
				string done = AddonRows.RemovalDone(one, new RemovalResult { SettingsDeleted = 1, LeftOver = true });
				Assert.Contains("Old Map Notes", done);
				Assert.Contains(T.S("addons.uninstall.leftover"), done);
			}
			finally { T.Use("en"); }
		}
	}

	/// <summary>Every text the apps ask for exists in English and Danish.</summary>
	public class TextKeyTests
	{
		static Dictionary<string, string> Table(string name)
		{
			return (Dictionary<string, string>)typeof(T).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
		}

		static string Root()
		{
			string dir = AppContext.BaseDirectory;
			while (dir != null && !Directory.Exists(Path.Combine(dir, "src", "Core"))) dir = Path.GetDirectoryName(dir);
			Assert.NotNull(dir);
			return dir;
		}

		static IEnumerable<string> UsedKeys(string folder)
		{
			foreach (string file in Directory.GetFiles(folder, "*.cs"))
				foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\bT\.[SF]\(""([a-z0-9_.]*[a-z0-9_])"""))
					yield return m.Groups[1].Value;
		}

		static void Covers(IEnumerable<string> keys, Dictionary<string, string> en, Dictionary<string, string> da)
		{
			var missing = keys.Distinct().Where(k => !en.ContainsKey(k) || !da.ContainsKey(k)).ToList();
			Assert.True(missing.Count == 0, "Missing texts: " + string.Join(", ", missing));
		}

		[Fact]
		public void EnglishAndDanishHaveTheSameTexts()
		{
			Assert.Empty(Table("En").Keys.Except(Table("Da").Keys));
			Assert.Empty(Table("Da").Keys.Except(Table("En").Keys));
			Assert.Empty(LinuxTexts.English().Keys.Except(LinuxTexts.Danish().Keys));
			Assert.Empty(LinuxTexts.Danish().Keys.Except(LinuxTexts.English().Keys));
		}

		[Fact]
		public void EveryTextTheAppsUseExists()
		{
			string root = Root();
			Covers(UsedKeys(Path.Combine(root, "src", "EldoUI.Updater")), Table("En"), Table("Da"));
			var en = new Dictionary<string, string>(Table("En"));
			var da = new Dictionary<string, string>(Table("Da"));
			foreach (var kv in LinuxTexts.English()) en[kv.Key] = kv.Value;
			foreach (var kv in LinuxTexts.Danish()) da[kv.Key] = kv.Value;
			Covers(UsedKeys(Path.Combine(root, "src", "EldoUI.Updater.Linux")), en, da);
		}

		[Fact]
		public void TextsMadeFromStatesExist()
		{
			var keys = new List<string>();
			keys.AddRange(Enum.GetNames(typeof(CompanionOutcome)).Select(o => "extra." + o));
			keys.AddRange(Enum.GetNames(typeof(CompanionOutcome)).Select(o => "addons.status." + o.ToLowerInvariant()));
			keys.AddRange(new[] { "addons.status.page", "addons.status.noforever", "addons.status.installed", "addons.status.byhand", "addons.status.selected" });
			keys.AddRange(Enum.GetNames(typeof(AddonState)).Select(o => "addons.state." + o.ToLowerInvariant()));
			keys.AddRange(Enum.GetNames(typeof(AddonSource)).Select(o => "addons.source." + o.ToLowerInvariant()));
			keys.AddRange(AddonCatalog.Categories.Select(c => "addons.cat." + c.ToLowerInvariant().Replace(' ', '_')));
			Covers(keys, Table("En"), Table("Da"));

			var lf = new List<string> { "lf.problem.version", "lf.problem.unknown", "lf.problem.status", "lf.problem.product" };
			foreach (var c in LightforgeChecks.Evaluate(new SystemInfo()))
			{
				lf.Add(c.Key);
				lf.Add("lf.fix." + c.Key.Substring("lf.check.".Length) + (c.Key == "lf.check.glibc" ? ".fail" : ""));
			}
			lf.Add("lf.fix.glibc.warn");
			Covers(lf, LinuxTexts.English(), LinuxTexts.Danish());
		}
	}
}
