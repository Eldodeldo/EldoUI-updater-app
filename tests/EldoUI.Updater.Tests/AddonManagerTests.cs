using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EldoUI.Updater.Core;
using Xunit;

namespace EldoUI.Updater.Tests
{
	public class CatalogTests
	{
		[Fact]
		public void CatalogIsPinnedAndHasNoWago()
		{
			Assert.Matches("^[0-9a-f]{40}$", AddonCatalog.Commit);
			Assert.DoesNotContain("wago", CatalogData.Json, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("AddonForge never", CatalogData.Json, StringComparison.Ordinal);
			Assert.True(AddonCatalog.All.Count >= 100);
			Assert.Equal(AddonCatalog.All.Count, AddonCatalog.All.Select(e => e.Id).Distinct().Count());
			Assert.Null(AddonCatalog.Find("buggrabber"));
			Assert.Null(AddonCatalog.Find("restedxp"));
		}

		[Fact]
		public void EarlierCompanionsKeepTheirIdsAndInstallRules()
		{
			foreach (var c in Companion.All)
			{
				var e = AddonCatalog.Find(c.Id);
				Assert.NotNull(e);
				Assert.Same(c, e.Legacy);
				Assert.Same(c, e.Package);
				Assert.True(e.Installable);
				Assert.True(c.NeedsManifest);
			}
			Assert.Equal("RestedXP/RXPGuides", AddonCatalog.Find("rxp").GitHub);
			Assert.False(string.IsNullOrEmpty(AddonCatalog.Find("rxp").Description));
		}

		[Fact]
		public void EveryEntryIsWellFormed()
		{
			foreach (var e in AddonCatalog.All)
			{
				Assert.False(string.IsNullOrEmpty(e.Name));
				Assert.False(string.IsNullOrEmpty(e.Category));
				Assert.DoesNotContain(e.Folders, AddonInstaller.IsEldoUIFolder);
				if (e.PageUrl != null) Assert.True(Release.IsAllowedPage(e.PageUrl), e.Id + " " + e.PageUrl);
				if (e.Source == AddonSource.GitHub)
				{
					Assert.NotEmpty(e.Folders);
					Assert.All(e.Folders, f => Assert.True(e.Package.Owns(f + "/" + f + ".toc"), e.Id + " " + f));
				}
				Assert.Equal(e.Source == AddonSource.GitHub && e.Forever != false, e.Installable);
			}
			Assert.Equal(AddonSource.WowInterface, AddonCatalog.Find("whats-training").Source);
			Assert.Equal(AddonSource.Tukui, AddonCatalog.Find("elvui").Source);
			Assert.False(AddonCatalog.Find("elvui").Installable);
			Assert.Equal(AddonSource.Page, AddonCatalog.Find("leatrix-plus").Source);
			Assert.Equal("https://www.curseforge.com/wow/addons/leatrix-plus", AddonCatalog.Find("leatrix-plus").PageUrl);
		}

		[Theory]
		[InlineData("{\"id\":\"x\",\"name\":\"X\",\"github\":\"a/b/c\",\"folders\":[\"X\"]}")]
		[InlineData("{\"id\":\"x\",\"name\":\"X\",\"github\":\"a/b\",\"folders\":[\"../X\"]}")]
		[InlineData("{\"id\":\"x\",\"name\":\"X\",\"github\":\"a/b\",\"folders\":[\"EldoUI_Bags\"]}")]
		[InlineData("{\"id\":\"x\",\"name\":\"X\",\"github\":\"a/b\",\"folders\":[]}")]
		[InlineData("{\"id\":\"X Y\",\"name\":\"X\",\"github\":\"a/b\",\"folders\":[\"X\"]}")]
		[InlineData("{\"id\":\"x\",\"name\":\"X\",\"tukui\":\"../x\"}")]
		public void MalformedEntriesAreLeftOut(string entry)
		{
			var list = AddonCatalog.Load("{\"addons\":[" + entry + "]}");
			Assert.DoesNotContain(list, e => e.Id == "x" || e.Id == "X Y");
		}

		[Fact]
		public void CatalogFoldersOwnTheirOwnSubfoldersOnly()
		{
			var bigwigs = AddonCatalog.Find("bigwigs").Package;
			Assert.True(bigwigs.Owns("BigWigs/BigWigs.toc"));
			Assert.True(bigwigs.Owns("BigWigs_NewZone/BigWigs_NewZone.toc"));
			Assert.False(bigwigs.Owns("Other/Other.toc"));
			Assert.False(bigwigs.Owns("BigWigsX/BigWigsX.toc"));
			Assert.False(bigwigs.Owns("../BigWigs/x.lua"));
			Assert.False(Companion.All[0].Owns("EldoUI/EldoUI.toc"));
		}

		[Theory]
		[InlineData("https://www.curseforge.com/wow/addons/leatrix-plus", true)]
		[InlineData("https://www.wowinterface.com/downloads/info25031", true)]
		[InlineData("https://tukui.org/elvui", true)]
		[InlineData("https://www.curseforge.com/wow/addons/leatrix-plus/download", false)]
		[InlineData("https://www.wowinterface.com/downloads/info25031?x=1", false)]
		[InlineData("http://tukui.org/elvui", false)]
		[InlineData("https://tukui.org.example.test/elvui", false)]
		[InlineData("https://addons.wago.io/addons/x", false)]
		public void PublisherPagesAreExact(string url, bool allowed)
		{
			Assert.Equal(allowed, Release.IsAllowedPage(url));
			Assert.False(Release.IsGitHubDownload(url));
		}

		[Theory]
		[InlineData("https://api.github.com/repos/a/b/releases/latest", true)]
		[InlineData("https://api.mmoui.com/v3/game/WOW/filedetails/25031.json", true)]
		[InlineData("https://api.tukui.org/v1/addon/elvui", true)]
		[InlineData("https://api.mmoui.com/v3/other", false)]
		[InlineData("http://api.github.com/repos/a/b/releases/latest", false)]
		[InlineData("https://api.github.com:8443/repos/a/b", false)]
		[InlineData("https://user@api.github.com/repos/a/b", false)]
		[InlineData("https://addons.wago.io/api/external/addons", false)]
		[InlineData("https://example.test/v1/addon/elvui", false)]
		public void OnlyKnownApisAreAsked(string url, bool allowed)
		{
			Assert.Equal(allowed, Web.IsAllowedApi(url));
		}
	}

	public class TocTests
	{
		[Fact]
		public void ReadsTitleVersionInterfacesAndNeeds()
		{
			var t = Toc.Parse("﻿## Interface: 11507, 16001\n## Title: |cff00ff00Big|r Wigs |TInterface\\Icons\\x:16|t\n## Version: v1.2\n## Dependencies: Core, Lib\n## Dep: Other\n## RequiredDeps: Core\n## OptionalDeps: Ace3\n", "BigWigs.toc");
			Assert.Equal("Big Wigs", t.Title);
			Assert.Equal("v1.2", t.Version);
			Assert.Equal(new[] { 11507, 16001 }, t.Interfaces);
			Assert.Equal(new[] { "Core", "Lib", "Other" }, t.Dependencies);
			Assert.Equal(new[] { "Ace3" }, t.OptionalDependencies);
			Assert.True(t.IsForever);
		}

		[Theory]
		[InlineData("## Interface: 11507", "A.toc", false)]
		[InlineData("## Interface: 120000", "A.toc", false)]
		[InlineData("## Interface: 16001", "A.toc", true)]
		[InlineData("## Interface: 11507", "A_Forever.toc", true)]
		[InlineData("## Interface: 11507", "A-Camelot.toc", true)]
		[InlineData("", "A_Vanilla.toc", false)]
		public void KnowsAForeverBuild(string text, string file, bool forever)
		{
			Assert.Equal(forever, Toc.Parse(text, file).IsForever);
		}

		[Fact]
		public void FindsLoopsAndIgnoresChains()
		{
			var needs = new Dictionary<string, List<string>>
			{
				{ "A", new List<string> { "B" } }, { "B", new List<string> { "C" } }, { "C", new List<string>() },
				{ "X", new List<string> { "Y" } }, { "y", new List<string> { "x" } },
			};
			Assert.Null(Dependencies.FindCycle(needs, new[] { "A" }));
			Assert.Equal(new[] { "X", "Y", "x" }, Dependencies.FindCycle(needs, new[] { "X" }));
		}
	}

	public class AddonSourceTests
	{
		static ReleaseAsset Zip(string name) { return new ReleaseAsset { Name = name, Url = "https://github.com/a/b/releases/download/v1/" + name }; }

		[Fact]
		public void PicksTheForeverPackageOrRefuses()
		{
			Assert.Equal("Addon-DBM-Core.zip", AddonSources.PickZip(new[] { Zip("Addon-DBM-Core.zip"), Zip("Addon-DBM-GUI.zip") }, "DBM-Core").Name);
			Assert.Equal("x-forever.zip", AddonSources.PickZip(new[] { Zip("x-classic.zip"), Zip("x-forever.zip") }, null).Name);
			Assert.Equal("x.zip", AddonSources.PickZip(new[] { Zip("x.zip"), Zip("x-nolib.zip"), Zip("notes.txt") }, null).Name);
			Assert.Equal("x-1.0.zip", AddonSources.PickZip(new[] { Zip("x-1.0.zip"), Zip("x-1.0-classic.zip") }, null).Name);
			Assert.Equal("x-forever.zip", AddonSources.PickZip(new[] { Zip("x-1.zip"), Zip("x-forever.zip") }, ".zip").Name);
			Assert.Throws<InvalidDataException>(() => AddonSources.PickZip(new[] { Zip("x-a.zip"), Zip("x-b.zip") }, null));
			Assert.Throws<InvalidDataException>(() => AddonSources.PickZip(new[] { Zip("x.tar.gz") }, null));
			Assert.Throws<InvalidDataException>(() => AddonSources.PickZip(new ReleaseAsset[0], null));
		}

		[Fact]
		public void ReadsWowInterfaceAndTukui()
		{
			var wt = AddonCatalog.Find("whats-training");
			var r = AddonSources.FromWowInterface("[{\"UIVersion\":\"v2.4\",\"UIFileName\":\"WT.zip\",\"UICompatibility\":[{\"version\":\"1.60.1\",\"name\":\"Forever\"}]}]", wt);
			Assert.Equal("v2.4", r.Version);
			Assert.True(r.Forever);
			Assert.Null(r.Asset);
			Assert.False(AddonSources.FromWowInterface("[{\"UIVersion\":\"2\",\"UICompatibility\":[{\"version\":\"1.15.7\"}]}]", wt).Forever);
			Assert.Null(AddonSources.FromWowInterface("[{\"UIVersion\":\"2\"}]", wt).Forever);
			Assert.Throws<FormatException>(() => AddonSources.FromWowInterface("[]", wt));
			var elv = AddonCatalog.Find("elvui");
			Assert.True(AddonSources.FromTukui("{\"version\":\"13.9\",\"patch\":[\"1.60.1\",\"1.15.7\"]}", elv).Forever);
			Assert.False(AddonSources.FromTukui("{\"version\":\"13.9\",\"patch\":[\"11.2.0\"]}", elv).Forever);
			Assert.Throws<FormatException>(() => AddonSources.FromTukui("{}", elv));
		}
	}

	public class CatalogInstallTests
	{
		const string Repo = "funkydude/BugSack";

		static string Pack(string dir, params (string name, string text)[] entries) { return Zips.Raw(dir, entries); }

		static string BugSack(string dir, string version, string iface = "16001", params (string name, string text)[] more)
		{
			var list = new List<(string, string)> { ("BugSack/BugSack.toc", "## Interface: " + iface + "\n## Version: " + version + "\n"), ("BugSack/core.lua", "-- " + version) };
			list.AddRange(more);
			return Zips.Raw(dir, list.ToArray());
		}

		/// <summary>Serves releases of BugSack: the newest stable one first, then any pre-release.</summary>
		static void Serve(FakeWeb web, string tag, string zip, bool digest = true, string preTag = null, string preZip = null, string assetUrl = null)
		{
			string root = "https://github.com/" + Repo + "/releases/download/" + tag + "/";
			string name = "BugSack-" + tag + ".zip";
			web.Files[root + name] = zip;
			string stable = "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false,\"assets\":["
				+ FakeWeb.AssetJson(name, assetUrl ?? root + name, new FileInfo(zip).Length, digest ? FakeWeb.Sha(zip) : null) + "]}";
			web.Api["https://api.github.com/repos/" + Repo + "/releases/latest"] = stable;
			string list = stable;
			if (preTag != null)
			{
				string proot = "https://github.com/" + Repo + "/releases/download/" + preTag + "/";
				string pname = "BugSack-" + preTag + ".zip";
				web.Files[proot + pname] = preZip;
				list = "{\"tag_name\":\"" + preTag + "\",\"draft\":false,\"prerelease\":true,\"assets\":[" + FakeWeb.AssetJson(pname, proot + pname, new FileInfo(preZip).Length, FakeWeb.Sha(preZip)) + "]}," + stable;
			}
			web.Api["https://api.github.com/repos/" + Repo + "/releases?per_page=10"] = "[" + list + "]";
		}

		static Settings Pick(TempDir t, string id = "bugsack")
		{
			var s = new Settings { AddOnsDir = t.Sub("AddOns") };
			s.SelectCompanion(id, true);
			return s;
		}

		[Fact]
		public async Task InstallsACatalogAddonThenFindsItCurrent()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				Serve(web, "v1.0", BugSack(t.Path, "1.0"));
				var s = Pick(t);
				var updater = new CompanionUpdater(web, t.Sub("temp"));
				var r = await updater.CheckAsync(s, true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.Installed, r.Single().Outcome);
				Assert.Equal("BugSack", r[0].Name);
				Assert.Equal("https://github.com/funkydude/BugSack", r[0].PageUrl);
				Assert.True(File.Exists(t.Sub("AddOns", "BugSack", "core.lua")));
				Assert.Equal("v1.0", s.ExtraVersions["bugsack"]);
				int downloads = web.Downloads;
				r = await updater.CheckAsync(s, true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.Current, r.Single().Outcome);
				Assert.Equal(downloads, web.Downloads);
			}
		}

		[Fact]
		public async Task ACatalogAddonWithoutAForeverTocIsNotInstalled()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				Serve(web, "v1.0", BugSack(t.Path, "1.0", "11507"));
				var s = Pick(t);
				var r = await new CompanionUpdater(web, t.Sub("temp")).CheckAsync(s, true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.NoForever, r.Single().Outcome);
				Assert.False(Directory.Exists(t.Sub("AddOns", "BugSack")));
				Assert.False(s.ExtraFiles.ContainsKey("bugsack"));
			}
		}

		[Fact]
		public async Task NoChecksumMeansNoDownload()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				Serve(web, "v1.0", BugSack(t.Path, "1.0"), digest: false);
				var r = await new CompanionUpdater(web, t.Sub("temp")).CheckAsync(Pick(t), true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.Failed, r.Single().Outcome);
				Assert.Equal(0, web.Downloads);
				Assert.False(Directory.Exists(t.Sub("AddOns", "BugSack")));
			}
		}

		[Theory]
		[InlineData("https://github.com/someone/else/releases/download/v1.0/BugSack-v1.0.zip")]
		[InlineData("https://github.com/funkydude/BugSack/releases/download/v0.9/BugSack-v1.0.zip")]
		public async Task AFileFromAnotherReleaseIsNotTaken(string url)
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				Serve(web, "v1.0", BugSack(t.Path, "1.0"), assetUrl: url);
				var r = await new CompanionUpdater(web, t.Sub("temp")).CheckAsync(Pick(t), true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.Failed, r.Single().Outcome);
				Assert.Equal(0, web.Downloads);
			}
		}

		[Fact]
		public async Task ASourceOnlyReleaseIsRefused()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				web.Api["https://api.github.com/repos/" + Repo + "/releases/latest"] = "{\"tag_name\":\"v1.0\",\"assets\":[]}";
				var r = await new CompanionUpdater(web, t.Sub("temp")).CheckAsync(Pick(t), true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.Failed, r.Single().Outcome);
				Assert.Contains("no addon package", r[0].Error);
			}
		}

		[Fact]
		public async Task TestReleasesOnlyWhenChosen()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				Serve(web, "v1.0", BugSack(t.Path, "1.0"), preTag: "v1.1-beta", preZip: BugSack(t.Path, "1.1-beta"));
				var s = Pick(t);
				var updater = new CompanionUpdater(web, t.Sub("temp"));
				Assert.Equal("v1.0", (await updater.CheckAsync(s, true, CancellationToken.None)).Single().Version);
				s.PreRelease.Add("bugsack");
				var r = await updater.CheckAsync(s, true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.Installed, r.Single().Outcome);
				Assert.Equal("v1.1-beta", s.ExtraVersions["bugsack"]);
				Assert.Contains("1.1-beta", File.ReadAllText(t.Sub("AddOns", "BugSack", "core.lua")));
			}
		}

		[Fact]
		public async Task AHeldAddonStaysAtItsVersion()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				Serve(web, "v1.0", BugSack(t.Path, "1.0"));
				var s = Pick(t);
				var updater = new CompanionUpdater(web, t.Sub("temp"));
				await updater.CheckAsync(s, true, CancellationToken.None);
				s.Held.Add("bugsack");
				Serve(web, "v2.0", BugSack(t.Path, "2.0"));
				var r = await updater.CheckAsync(s, true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.Held, r.Single().Outcome);
				Assert.Equal("v2.0", r[0].Version);
				Assert.Contains("1.0", File.ReadAllText(t.Sub("AddOns", "BugSack", "core.lua")));
				s.Held.Remove("bugsack");
				Assert.Equal(CompanionOutcome.Installed, (await updater.CheckAsync(s, true, CancellationToken.None)).Single().Outcome);
			}
		}

		[Fact]
		public async Task AHandInstalledCopyIsOnlyTakenOverWhenThePlayerAgrees()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				Serve(web, "v1.0", BugSack(t.Path, "1.0"));
				Directory.CreateDirectory(t.Sub("AddOns", "BugSack"));
				File.WriteAllText(t.Sub("AddOns", "BugSack", "BugSack.toc"), "## Version: mine");
				File.WriteAllText(t.Sub("AddOns", "BugSack", "notes.txt"), "my notes");
				var s = Pick(t);
				var updater = new CompanionUpdater(web, t.Sub("temp"));
				var r = await updater.CheckAsync(s, true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.InUse, r.Single().Outcome);
				Assert.Equal(new[] { "BugSack" }, r[0].Folders);
				Assert.Equal("## Version: mine", File.ReadAllText(t.Sub("AddOns", "BugSack", "BugSack.toc")));
				s.Adopted.Add("bugsack");
				r = await updater.CheckAsync(s, true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.Installed, r.Single().Outcome);
				Assert.Equal("my notes", File.ReadAllText(t.Sub("AddOns", "BugSack", "notes.txt")));
				Assert.Contains("1.0", File.ReadAllText(t.Sub("AddOns", "BugSack", "BugSack.toc")));
				Assert.Empty(s.Adopted);
			}
		}

		[Fact]
		public async Task AFolderOfAnotherManagedAddonIsNeverReplaced()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				Serve(web, "v1.0", BugSack(t.Path, "1.0"));
				var s = Pick(t);
				s.Adopted.Add("bugsack");
				s.ExtraFiles["betterbags"] = new List<string> { "BugSack/BugSack.toc" };
				Directory.CreateDirectory(t.Sub("AddOns", "BugSack"));
				File.WriteAllText(t.Sub("AddOns", "BugSack", "BugSack.toc"), "other");
				var r = await new CompanionUpdater(web, t.Sub("temp")).CheckAsync(s, true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.Failed, r.Single().Outcome);
				Assert.Contains("BetterBags", r[0].Error);
				Assert.Equal("other", File.ReadAllText(t.Sub("AddOns", "BugSack", "BugSack.toc")));
				s.ExtraFiles.Remove("betterbags");
				s.InstalledFolders = new List<string> { "BugSack" };
				Assert.Contains("EldoUI", (await new CompanionUpdater(web, t.Sub("temp")).CheckAsync(s, true, CancellationToken.None)).Single().Error);
			}
		}

		[Fact]
		public async Task WowInterfaceAddonsAreCheckedButNeverDownloaded()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				web.Api[AddonSources.WowInterfaceApi + "25031.json"] = "[{\"UIVersion\":\"3.1\",\"UIDownload\":\"https://cdn.wowinterface.com/x.zip\",\"UICompatibility\":[{\"version\":\"1.60.1\"}]}]";
				var s = Pick(t, "whats-training");
				var r = await new CompanionUpdater(web, t.Sub("temp")).CheckAsync(s, true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.External, r.Single().Outcome);
				Assert.Equal("3.1", r[0].Version);
				Assert.Equal("https://www.wowinterface.com/downloads/info25031", r[0].PageUrl);
				Assert.Equal(0, web.Downloads);
			}
		}

		[Fact]
		public async Task OfflinePublisherFailsOnlyThatAddon()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				Serve(web, "v1.0", BugSack(t.Path, "1.0"));
				var s = Pick(t);
				s.SelectCompanion("whats-training", true);
				var r = await new CompanionUpdater(web, t.Sub("temp")).CheckAsync(s, true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.Installed, r.Single(x => x.Id == "bugsack").Outcome);
				Assert.Equal(CompanionOutcome.Failed, r.Single(x => x.Id == "whats-training").Outcome);
			}
		}

		[Fact]
		public async Task CancellingStopsBeforeAnythingChanges()
		{
			using (var t = new TempDir())
			using (var cancel = new CancellationTokenSource())
			{
				var web = new FakeWeb();
				Serve(web, "v1.0", BugSack(t.Path, "1.0"));
				cancel.Cancel();
				await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CompanionUpdater(web, t.Sub("temp")).CheckAsync(Pick(t), true, cancel.Token));
				Assert.False(Directory.Exists(t.Sub("AddOns", "BugSack")));
			}
		}

		[Fact]
		public void CamelotAndInterfaceNumbersCountOnlyForCatalogAddons()
		{
			string camelot = "{\"releases\":[{\"filename\":\"c.zip\",\"metadata\":[{\"flavor\":\"camelot\"}]}]}";
			string iface = "{\"releases\":[{\"filename\":\"n.zip\",\"nolib\":true,\"metadata\":[{\"flavor\":\"forever\"}]},{\"filename\":\"i.zip\",\"metadata\":[{\"flavor\":\"x\",\"interface\":16001}]}]}";
			Assert.Equal("c.zip", CompanionUpdater.ForeverFilename(camelot, true));
			Assert.Equal("i.zip", CompanionUpdater.ForeverFilename(iface, true));
			Assert.Throws<NoForeverBuild>(() => CompanionUpdater.ForeverFilename(camelot));
			Assert.Throws<NoForeverBuild>(() => CompanionUpdater.ForeverFilename(iface));
		}
	}

	public class PackageSafetyTests
	{
		static string ZipWith(string dir, string name, byte[] data, int unixType = 0)
		{
			string path = Path.Combine(dir, "pkg-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
			using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
			{
				using (var w = new StreamWriter(z.CreateEntry("BugSack/BugSack.toc").Open())) w.Write("## Interface: 16001\n");
				var e = z.CreateEntry(name);
				if (unixType != 0) e.ExternalAttributes = (unixType | 0x1ED) << 16;
				using (var s = e.Open()) s.Write(data, 0, data.Length);
			}
			return path;
		}

		public static IEnumerable<object[]> Unsafe()
		{
			yield return new object[] { "BugSack/helper.exe", Encoding.ASCII.GetBytes("hello"), 0 };
			yield return new object[] { "BugSack/run.lua.bat", Encoding.ASCII.GetBytes("@echo"), 0 };
			yield return new object[] { "BugSack/tool.ps1", Encoding.ASCII.GetBytes("x"), 0 };
			yield return new object[] { "BugSack/README", new byte[] { (byte)'M', (byte)'Z', 0x90, 0 }, 0 };
			yield return new object[] { "BugSack/data.bin", new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' }, 0 };
			yield return new object[] { "BugSack/data.dat", new byte[] { 0xCF, 0xFA, 0xED, 0xFE }, 0 };
			yield return new object[] { "BugSack/install", Encoding.ASCII.GetBytes("#!/bin/sh\n"), 0 };
			yield return new object[] { "BugSack/Helper.app/Contents/Info.plist", Encoding.ASCII.GetBytes("x"), 0 };
			yield return new object[] { "BugSack/link.lua", Encoding.ASCII.GetBytes("/etc/passwd"), 0xA000 };
		}

		[Theory]
		[MemberData(nameof(Unsafe))]
		public void ProgramsAndLinksAreRefusedBeforeAnythingChanges(string name, byte[] data, int unixType)
		{
			using (var t = new TempDir())
			{
				Directory.CreateDirectory(t.Sub("AddOns", "BugSack"));
				File.WriteAllText(t.Sub("AddOns", "BugSack", "BugSack.toc"), "original");
				var addon = AddonCatalog.Find("bugsack").Package;
				var ex = Assert.Throws<InvalidDataException>(() => CompanionInstaller.Install(addon, ZipWith(t.Path, name, data, unixType), t.Sub("AddOns"),
					new[] { "BugSack/BugSack.toc" }, CancellationToken.None, new InstallGuard()));
				Assert.Contains("refused", ex.Message);
				Assert.Equal("original", File.ReadAllText(t.Sub("AddOns", "BugSack", "BugSack.toc")));
				Assert.Empty(Directory.GetDirectories(t.Path, ".eldoui-companion-*"));
			}
		}

		[Fact]
		public void EarlierCompanionsAreCheckedForProgramsToo()
		{
			using (var t = new TempDir())
			{
				var rxp = Companion.All.Single(c => c.Id == "rxp");
				string zip = Zips.Raw(t.Path, ("RXPGuides/RXPGuides.toc", "## Interface: 16001"), ("RXPGuides/x.dll", "x"));
				Assert.Throws<InvalidDataException>(() => CompanionInstaller.Install(rxp, zip, t.Sub("AddOns"), null, CancellationToken.None));
				Assert.False(Directory.Exists(t.Sub("AddOns", "RXPGuides")));
			}
		}

		[Fact]
		public void MediaAndTextFilesPass()
		{
			using (var t = new TempDir())
			{
				var addon = AddonCatalog.Find("bugsack").Package;
				string zip = ZipWith(t.Path, "BugSack/Media/alert.ogg", new byte[] { (byte)'M', (byte)'Z', 1, 2 });
				var files = CompanionInstaller.Install(addon, zip, t.Sub("AddOns"), null, CancellationToken.None, new InstallGuard());
				Assert.Contains("BugSack/Media/alert.ogg", files);
			}
		}

		[Fact]
		public void ADependencyLoopIsRefused()
		{
			using (var t = new TempDir())
			{
				Directory.CreateDirectory(t.Sub("AddOns", "BugGrabber"));
				File.WriteAllText(t.Sub("AddOns", "BugGrabber", "BugGrabber.toc"), "## Interface: 16001\n## Dependencies: BugSack\n");
				string zip = Zips.Raw(t.Path, ("BugSack/BugSack.toc", "## Interface: 16001\n## Dependencies: BugGrabber\n"));
				var ex = Assert.Throws<InvalidDataException>(() => CompanionInstaller.Install(AddonCatalog.Find("bugsack").Package, zip, t.Sub("AddOns"), null, CancellationToken.None, new InstallGuard()));
				Assert.Contains("loop", ex.Message);
				Assert.False(Directory.Exists(t.Sub("AddOns", "BugSack")));
			}
		}

		[Fact]
		public void AFolderOutsideTheCatalogListIsRefused()
		{
			using (var t = new TempDir())
			{
				string zip = Zips.Raw(t.Path, ("BugSack/BugSack.toc", "## Interface: 16001"), ("Unrelated/Unrelated.toc", "## Interface: 16001"));
				Assert.Throws<InvalidDataException>(() => CompanionInstaller.Install(AddonCatalog.Find("bugsack").Package, zip, t.Sub("AddOns"), null, CancellationToken.None, new InstallGuard()));
				Assert.False(Directory.Exists(t.Sub("AddOns", "BugSack")));
			}
		}

		[Fact]
		public void AFailedRestoreKeepsTheBackup()
		{
			using (var t = new TempDir())
			{
				var addon = AddonCatalog.Find("bugsack").Package;
				string addOns = t.Sub("AddOns");
				var first = CompanionInstaller.Install(addon, Zips.Raw(t.Path, ("BugSack/BugSack.toc", "## Interface: 16001\n## Version: 1")), addOns, null, CancellationToken.None, new InstallGuard());
				// A file where the new folder must go makes placing fail after the old copy was moved aside.
				string zip = Zips.Raw(t.Path, ("BugSack/BugSack.toc", "## Interface: 16001\n## Version: 2"), ("BugSack_Data/BugSack_Data.toc", "## Interface: 16001"));
				File.WriteAllText(Path.Combine(addOns, "BugSack_Data"), "a file in the way");
				Assert.ThrowsAny<IOException>(() => CompanionInstaller.Install(addon, zip, addOns, first, CancellationToken.None, new InstallGuard { Adopt = true }));
				Assert.Contains("## Version: 1", File.ReadAllText(Path.Combine(addOns, "BugSack", "BugSack.toc")));
				Assert.Equal("a file in the way", File.ReadAllText(Path.Combine(addOns, "BugSack_Data")));
			}
		}
	}

	public class ScannerTests
	{
		static void Folder(TempDir t, string name, string toc)
		{
			Directory.CreateDirectory(t.Sub("AddOns", name));
			File.WriteAllText(t.Sub("AddOns", name, name + ".toc"), toc);
		}

		[Fact]
		public void GroupsWhatIsInstalled()
		{
			using (var t = new TempDir())
			{
				Folder(t, "EldoUI", "## Interface: 16001\n## Version: 0.7.0003\n");
				Folder(t, "EldoUI_LootData", "## Interface: 16001\n## Version: 0.7.0003\n");
				Folder(t, "EldoUI_GuideData", "## Interface: 16001\n## Title: Eldostar guides\n## Version: 0.2.6\n");
				Folder(t, "RXPGuides", "## Interface: 16001\n## Version: 4.0\n");
				Folder(t, "BigWigs", "## Interface: 16001\n## Title: BigWigs\n## Version: 400\n");
				Folder(t, "BigWigs_Core", "## Interface: 16001\n");
				Folder(t, "MyAddon", "## Interface: 11507\n## Title: |cffff0000My|r Addon\n## Dependencies: DBM-Core, Blizzard_Calendar\n");
				Folder(t, "MyAddon_Options", "## Interface: 11507\n## Dependencies: MyAddon\n");
				Folder(t, "Lonely", "## Interface: 16001\n");
				Directory.CreateDirectory(t.Sub("AddOns", ".eldoui-companion-old"));
				var s = new Settings { AddOnsDir = t.Sub("AddOns") };
				s.ExtraFiles["rxp"] = new List<string> { "RXPGuides/RXPGuides.toc" };
				s.ExtraVersions["rxp"] = "v4.0.1";
				s.IgnoredFolders.Add("Lonely");
				var rows = AddonScanner.Scan(s.AddOnsDir, s);

				var eldo = rows.Single(r => r.State == AddonState.EldoUI);
				Assert.Equal(new[] { "EldoUI", "EldoUI_LootData" }, eldo.Folders);
				Assert.Equal("0.7.0003", eldo.Version);
				var guide = rows.Single(r => r.State == AddonState.GuideData);
				Assert.Equal("0.2.6", guide.Version);
				var rxp = rows.Single(r => r.State == AddonState.Managed);
				Assert.Equal("rxp", rxp.CatalogId);
				Assert.Equal("v4.0.1", rxp.Version);
				var bw = rows.Single(r => r.State == AddonState.ByHand);
				Assert.Equal("bigwigs", bw.CatalogId);
				Assert.Equal(new[] { "BigWigs", "BigWigs_Core" }, bw.Folders);
				var mine = rows.Single(r => r.Name == "My Addon");
				Assert.Equal(AddonState.Other, mine.State);
				Assert.Equal(new[] { "MyAddon", "MyAddon_Options" }, mine.Folders);
				Assert.False(mine.Forever);
				Assert.Equal(new[] { "DBM-Core" }, mine.MissingDependencies);
				Assert.True(rows.Single(r => r.Folders.Contains("Lonely")).Ignored);
				Assert.DoesNotContain(rows, r => r.Folders.Any(f => f.StartsWith(".")));

				var suggestions = AddonScanner.Suggestions(rows, s);
				Assert.Equal(new[] { "dbm" }, suggestions.Select(e => e.Id));
				s.SelectCompanion("dbm", true);
				Assert.Empty(AddonScanner.Suggestions(rows, s));
			}
		}

		[Fact]
		public void AMissingFolderListsNothing()
		{
			Assert.Empty(AddonScanner.Scan(null, new Settings()));
			Assert.Empty(AddonScanner.Scan(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid()), new Settings()));
		}
	}

	public class AddonListTests
	{
		[Fact]
		public void ExportHoldsChoicesOnly()
		{
			var s = new Settings { AddOnsDir = "/home/someone/Games/World of Warcraft/_forever_/Interface/AddOns" };
			s.SelectCompanion("bigwigs", true);
			s.SelectCompanion("rxp", true);
			s.PreRelease.Add("bigwigs");
			s.Held.Add("rxp");
			s.ExtraFiles["rxp"] = new List<string> { "RXPGuides/RXPGuides.toc" };
			string text = AddonList.Export(s);
			Assert.DoesNotContain("someone", text);
			Assert.DoesNotContain("/", text);
			Assert.DoesNotContain("\\", text);
			Assert.DoesNotContain("RXPGuides", text);

			var back = new Settings();
			List<string> unknown;
			var choices = AddonList.Import(text, out unknown);
			Assert.Empty(unknown);
			AddonList.Apply(back, choices);
			Assert.Equal(new[] { "bigwigs", "rxp" }, back.SelectedCompanions);
			Assert.True(back.ExtraAddons);
			Assert.Contains("bigwigs", back.PreRelease);
			Assert.Contains("rxp", back.Held);
			Assert.DoesNotContain("rxp", back.PreRelease);
		}

		[Fact]
		public void UnknownAndPageOnlyNamesAreReported()
		{
			List<string> unknown;
			var choices = AddonList.Import("{\"format\":\"eldoui-addons\",\"version\":1,\"addons\":[{\"id\":\"dbm\"},{\"id\":\"nope\"},{\"id\":\"leatrix-plus\"},{\"id\":\"dbm\"},{\"id\":\"../x\"}]}", out unknown);
			Assert.Equal(new[] { "dbm" }, choices.Select(c => c.Id));
			Assert.Equal(new[] { "nope", "leatrix-plus", "../x" }, unknown);
		}

		[Theory]
		[InlineData("{\"format\":\"other\",\"version\":1,\"addons\":[]}")]
		[InlineData("{\"format\":\"eldoui-addons\",\"version\":2,\"addons\":[]}")]
		[InlineData("[1,2]")]
		[InlineData("not json")]
		public void OtherFilesAreRefused(string text)
		{
			List<string> unknown;
			Assert.ThrowsAny<FormatException>(() => AddonList.Import(text, out unknown));
		}

		[Fact]
		public void AFileTooLargeIsRefused()
		{
			List<string> unknown;
			Assert.Throws<FormatException>(() => AddonList.Import(new string(' ', 70000), out unknown));
		}
	}

	public class AddonSettingsTests
	{
		[Fact]
		public void ChoicesRoundTripAndUnknownNamesAreDropped()
		{
			using (var t = new TempDir())
			{
				var s = new Settings();
				s.SelectCompanion("bigwigs", true);
				s.Held.Add("bigwigs");
				s.PreRelease.Add("bigwigs");
				s.Adopted.Add("bigwigs");
				s.IgnoredFolders.Add("MyAddon");
				s.Save(t.Sub("settings.json"));
				string text = File.ReadAllText(t.Sub("settings.json")).Replace("\"bigwigs\"", "\"bigwigs\", \"gone\"");
				File.WriteAllText(t.Sub("settings.json"), text);
				var back = Settings.Load(t.Sub("settings.json"));
				Assert.Equal(new[] { "bigwigs" }, back.SelectedCompanions);
				Assert.Equal(new[] { "bigwigs" }, back.Held);
				Assert.Equal(new[] { "bigwigs" }, back.PreRelease);
				Assert.Equal(new[] { "bigwigs" }, back.Adopted);
				Assert.Contains("myaddon", back.IgnoredFolders);
			}
		}

		[Fact]
		public void OlderSettingsFilesStillLoad()
		{
			using (var t = new TempDir())
			{
				File.WriteAllText(t.Sub("settings.json"), "{\"extraAddons\":true,\"selectedCompanions\":[\"dbm\",\"rxp\",\"media\"]}");
				var back = Settings.Load(t.Sub("settings.json"));
				Assert.Equal(new[] { "dbm", "rxp", "media" }, back.SelectedCompanions);
				Assert.Empty(back.Held);
				Assert.Empty(back.PreRelease);
			}
		}

		[Fact]
		public void PageOnlyAddonsCannotBeSelectedAndDeselectingClearsChoices()
		{
			var s = new Settings();
			Assert.Throws<ArgumentException>(() => s.SelectCompanion("leatrix-plus", true));
			s.SelectCompanion("bigwigs", true);
			s.Held.Add("bigwigs");
			s.PreRelease.Add("bigwigs");
			s.SelectCompanion("bigwigs", false);
			Assert.Empty(s.Held);
			Assert.Empty(s.PreRelease);
		}
	}
}
