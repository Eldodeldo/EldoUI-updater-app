using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EldoUI.Updater.Core;
using Xunit;

namespace EldoUI.Updater.Tests
{
	public class AddonRemoverTests
	{
		/// <summary>A game folder with Interface\AddOns and WTF, and a few addons in it.</summary>
		static string Game(TempDir t)
		{
			string addOns = t.Sub("_forever_", "Interface", "AddOns");
			Addon(addOns, "EldoUI", "");
			Addon(addOns, "EldoUI_Bags", "EldoUI");
			Addon(addOns, "EldoUI_GuideData", "");
			Addon(addOns, "BugSack", "");
			Addon(addOns, "MyErrorFrame", "BugSack");
			Addon(addOns, "OldMapNotes", "");
			Addon(addOns, "OldMapNotes_Options", "OldMapNotes");
			return addOns;
		}

		static void Addon(string addOns, string name, string needs)
		{
			Directory.CreateDirectory(Path.Combine(addOns, name, "Media"));
			File.WriteAllText(Path.Combine(addOns, name, name + ".toc"), "## Interface: 16001\n## Title: " + name + "\n## Version: 1.0\n" + (needs.Length > 0 ? "## Dependencies: " + needs + "\n" : ""));
			File.WriteAllText(Path.Combine(addOns, name, "Media", "big.tga"), new string('x', 4096));
		}

		static string Saved(TempDir t, string file, params string[] where)
		{
			string dir = Path.Combine(new[] { t.Path, "_forever_", "WTF", "Account" }.Concat(where).Concat(new[] { "SavedVariables" }).ToArray());
			Directory.CreateDirectory(dir);
			string path = Path.Combine(dir, file);
			File.WriteAllText(path, "-- saved");
			return path;
		}

		static InstalledAddon Row(List<InstalledAddon> rows, string folder) { return rows.Single(r => r.Folders.Contains(folder)); }

		[Fact]
		public void EldoUIsOwnFoldersCanNeverBeUninstalled()
		{
			using (var t = new TempDir())
			{
				string addOns = Game(t);
				var rows = AddonScanner.Scan(addOns, new Settings { AddOnsDir = addOns });
				Assert.False(AddonRemover.CanRemove(Row(rows, "EldoUI")));
				Assert.False(AddonRemover.CanRemove(Row(rows, "EldoUI_GuideData")));
				Assert.Null(AddonRemover.Plan(addOns, rows, new[] { AddonRemover.Key(Row(rows, "EldoUI")) }));
				Assert.Null(AddonRemover.Plan(addOns, rows, new[] { "EldoUI_GuideData" }));
				// One protected row in a batch refuses the whole batch.
				Assert.Null(AddonRemover.Plan(addOns, rows, new[] { "BugSack", AddonRemover.Key(Row(rows, "EldoUI")) }));

				// Even a plan made by hand cannot reach them.
				foreach (string folder in new[] { "EldoUI", "EldoUI_Bags", "EldoUI_GuideData" })
				{
					var plan = new RemovalPlan { Names = { folder }, Folders = { folder } };
					Assert.Throws<InvalidDataException>(() => AddonRemover.Remove(addOns, plan, true));
					Assert.True(Directory.Exists(Path.Combine(addOns, folder)));
				}
			}
		}

		[Theory]
		[InlineData("../Other")]
		[InlineData("..")]
		[InlineData(".git")]
		[InlineData("Sub/Folder")]
		[InlineData("Sub\\Folder")]
		[InlineData("C:")]
		[InlineData("")]
		[InlineData(" BugSack")]
		[InlineData("EldoUI_Anything")]
		public void OnlyPlainFoldersInAddOnsCanBeUninstalled(string folder)
		{
			Assert.False(AddonRemover.IsRemovableName(folder));
			using (var t = new TempDir())
			{
				string addOns = Game(t);
				Assert.Throws<InvalidDataException>(() => AddonRemover.Remove(addOns, new RemovalPlan { Names = { "x" }, Folders = { folder } }, false));
				Assert.True(Directory.Exists(Path.Combine(addOns, "EldoUI")));
				Assert.True(Directory.Exists(t.Sub("_forever_", "Interface", "AddOns", "BugSack")));
			}
		}

		[Fact]
		public void UnknownRowsAreNotPlanned()
		{
			using (var t = new TempDir())
			{
				string addOns = Game(t);
				var rows = AddonScanner.Scan(addOns, new Settings { AddOnsDir = addOns });
				Assert.Null(AddonRemover.Plan(addOns, rows, new[] { "NotThere" }));
				Assert.Null(AddonRemover.Plan(addOns, rows, new string[0]));
				// A key must name the whole row, not one folder of it.
				Assert.Null(AddonRemover.Plan(addOns, rows, new[] { "OldMapNotes" }));
				Assert.NotNull(AddonRemover.Plan(addOns, rows, new[] { "OldMapNotes/OldMapNotes_Options" }));
			}
		}

		[Fact]
		public void UninstallRemovesOnlyThatAddonAndKeepsItsSavedSettings()
		{
			using (var t = new TempDir())
			{
				string addOns = Game(t);
				string account = Saved(t, "OldMapNotes.lua", "PLAYER");
				string character = Saved(t, "OldMapNotes_Options.lua.bak", "PLAYER", "Realm", "Hero");
				string other = Saved(t, "OldMapNotesPlus.lua", "PLAYER");
				var rows = AddonScanner.Scan(addOns, new Settings { AddOnsDir = addOns });
				var plan = AddonRemover.Plan(addOns, rows, new[] { AddonRemover.Key(Row(rows, "OldMapNotes")) });
				Assert.Equal(new[] { "OldMapNotes", "OldMapNotes_Options" }, plan.Folders);
				Assert.Equal(new[] { account, character }.OrderBy(f => f, StringComparer.OrdinalIgnoreCase), plan.SavedSettings);
				Assert.Empty(plan.NeededBy);

				var result = AddonRemover.Remove(addOns, plan, false);
				Assert.Equal(plan.Folders, result.Removed);
				Assert.Equal(0, result.SettingsDeleted);
				Assert.False(result.LeftOver);
				Assert.False(Directory.Exists(Path.Combine(addOns, "OldMapNotes")));
				Assert.False(Directory.Exists(Path.Combine(addOns, "OldMapNotes_Options")));
				Assert.True(File.Exists(account) && File.Exists(character) && File.Exists(other));
				foreach (string kept in new[] { "EldoUI", "EldoUI_Bags", "EldoUI_GuideData", "BugSack", "MyErrorFrame" })
					Assert.True(Directory.Exists(Path.Combine(addOns, kept)), kept);
				// Nothing is left behind, in AddOns or next to it.
				Assert.Equal(new[] { "AddOns" }, Directory.GetDirectories(t.Sub("_forever_", "Interface")).Select(Path.GetFileName));
				Assert.DoesNotContain(Directory.GetDirectories(addOns), d => Path.GetFileName(d).StartsWith("."));
			}
		}

		[Fact]
		public void SavedSettingsGoOnlyWhenTickedAndOnlyTheAddonsOwn()
		{
			using (var t = new TempDir())
			{
				string addOns = Game(t);
				string account = Saved(t, "BugSack.lua", "PLAYER");
				string backup = Saved(t, "bugsack.lua.bak", "PLAYER");
				string character = Saved(t, "BugSack.lua", "PLAYER", "Realm", "Hero");
				string similar = Saved(t, "BugSackExtra.lua", "PLAYER");
				string eldo = Saved(t, "EldoUI.lua", "PLAYER");
				string needer = Saved(t, "MyErrorFrame.lua", "PLAYER", "Realm", "Hero");
				var rows = AddonScanner.Scan(addOns, new Settings { AddOnsDir = addOns });
				var plan = AddonRemover.Plan(addOns, rows, new[] { "BugSack" });
				Assert.Equal(new[] { "MyErrorFrame" }, plan.NeededBy);
				Assert.Equal(3, plan.SavedSettings.Count);

				var result = AddonRemover.Remove(addOns, plan, true);
				Assert.Equal(3, result.SettingsDeleted);
				Assert.False(File.Exists(account) || File.Exists(backup) || File.Exists(character));
				Assert.True(File.Exists(similar) && File.Exists(eldo) && File.Exists(needer));
				Assert.True(Directory.Exists(Path.Combine(addOns, "MyErrorFrame")));
			}
		}

		[Fact]
		public void SeveralAddonsGoTogetherAndAFolderThatIsGoneStopsAllOfThem()
		{
			using (var t = new TempDir())
			{
				string addOns = Game(t);
				var rows = AddonScanner.Scan(addOns, new Settings { AddOnsDir = addOns });
				var plan = AddonRemover.Plan(addOns, rows, new[] { "BugSack", "MyErrorFrame" });
				Assert.Equal(2, plan.Names.Count);
				Assert.Empty(plan.NeededBy);

				Directory.Delete(Path.Combine(addOns, "MyErrorFrame"), true);
				Assert.Throws<IOException>(() => AddonRemover.Remove(addOns, plan, false));
				Assert.True(Directory.Exists(Path.Combine(addOns, "BugSack")));

				Addon(addOns, "MyErrorFrame", "BugSack");
				var result = AddonRemover.Remove(addOns, plan, false);
				Assert.Equal(new[] { "BugSack", "MyErrorFrame" }, result.Removed);
				Assert.False(Directory.Exists(Path.Combine(addOns, "BugSack")) || Directory.Exists(Path.Combine(addOns, "MyErrorFrame")));
			}
		}

		[Fact]
		public void ALinkedAddonFolderIsRemovedWithoutTouchingWhatItPointsTo()
		{
			if (!OperatingSystem.IsLinux()) return;
			using (var t = new TempDir())
			{
				string addOns = Game(t);
				string source = t.Sub("MyAddonSource");
				Addon(t.Path, "MyAddonSource", "");
				Directory.CreateSymbolicLink(Path.Combine(addOns, "MyAddon"), source);
				var rows = AddonScanner.Scan(addOns, new Settings { AddOnsDir = addOns });
				var plan = AddonRemover.Plan(addOns, rows, new[] { "MyAddon" });
				AddonRemover.Remove(addOns, plan, false);
				Assert.False(Directory.Exists(Path.Combine(addOns, "MyAddon")));
				Assert.True(File.Exists(Path.Combine(source, "Media", "big.tga")));
			}
		}

		[Fact]
		public void LeftoversFromAnEarlierUninstallAreCleanedUp()
		{
			using (var t = new TempDir())
			{
				string addOns = Game(t);
				string left = Path.Combine(addOns, ".eldoui-remove-1234abcd", "Old", "file.lua");
				Directory.CreateDirectory(Path.GetDirectoryName(left));
				File.WriteAllText(left, "--");
				string install = t.Sub("_forever_", "Interface", ".eldoui-updater-1234abcd");
				Directory.CreateDirectory(install);
				AddonRemover.CleanUp(addOns);
				AddonRemover.CleanUp("");
				Assert.False(Directory.Exists(Path.Combine(addOns, ".eldoui-remove-1234abcd")));
				// Only the uninstall's own work folders: an install's backup is never deleted from here.
				Assert.True(Directory.Exists(install));
			}
		}

		[Fact]
		public void AnUninstalledManagedAddonIsNotInstalledAgain()
		{
			using (var t = new TempDir())
			{
				string addOns = Game(t);
				var s = new Settings { AddOnsDir = addOns };
				s.SelectCompanion("bugsack", true);
				s.SelectCompanion("dbm", true);
				s.ExtraFiles["bugsack"] = new List<string> { "BugSack/BugSack.toc" };
				s.ExtraVersions["bugsack"] = "v1.0";
				s.Held.Add("bugsack");
				s.IgnoredFolders.Add("OldMapNotes");
				var rows = AddonScanner.Scan(addOns, s);
				Assert.Equal(AddonState.Managed, Row(rows, "BugSack").State);
				var plan = AddonRemover.Plan(addOns, rows, new[] { "BugSack" });
				Assert.True(plan.Managed);
				Assert.Equal(new[] { "bugsack" }, plan.CatalogIds);
				AddonRemover.Remove(addOns, plan, false);
				AddonRemover.Forget(s, plan);
				Assert.DoesNotContain("bugsack", s.SelectedCompanions);
				Assert.False(s.ExtraFiles.ContainsKey("bugsack") || s.ExtraVersions.ContainsKey("bugsack") || s.Held.Contains("bugsack"));
				Assert.Contains("dbm", s.SelectedCompanions);
				Assert.Contains("OldMapNotes", s.IgnoredFolders);

				var other = AddonRemover.Plan(addOns, AddonScanner.Scan(addOns, s), new[] { "OldMapNotes/OldMapNotes_Options" });
				AddonRemover.Forget(s, other);
				Assert.DoesNotContain("OldMapNotes", s.IgnoredFolders);
			}
		}

		[Fact]
		public void TheRecommendedDefaultsStayWhenNothingIsChosenYet()
		{
			var s = new Settings();
			AddonRemover.Forget(s, new RemovalPlan { CatalogIds = { "dbm" }, Folders = { "DBM-Core" } });
			Assert.Equal(new[] { "dbm", "rxp", "media" }, s.SelectedCompanions);
		}

		[Fact]
		public void TheScannerMeasuresEachAddon()
		{
			using (var t = new TempDir())
			{
				string addOns = Game(t);
				var row = Row(AddonScanner.Scan(addOns, new Settings { AddOnsDir = addOns }), "OldMapNotes");
				Assert.True(row.Bytes >= 2 * 4096, row.Bytes.ToString());
				Assert.NotNull(row.Changed);
				Assert.Equal(new[] { "BugSack" }, Row(AddonScanner.Scan(addOns, new Settings { AddOnsDir = addOns }), "MyErrorFrame").Dependencies);
				// A row's own folders are not listed as what it needs.
				Assert.Empty(row.Dependencies);
			}
		}

		[Fact]
		public void FolderPathsStayInAddOns()
		{
			using (var t = new TempDir())
			{
				string addOns = Game(t);
				Assert.Equal(Path.Combine(addOns, "BugSack"), AddonRemover.FolderPath(addOns, "BugSack"));
				Assert.Null(AddonRemover.FolderPath(addOns, "../AddOns"));
				Assert.Null(AddonRemover.FolderPath(addOns, "NotThere"));
				Assert.Null(AddonRemover.FolderPath("", "BugSack"));
			}
		}
	}
}
