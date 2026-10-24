using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EldoUI.Updater.Core;
using Xunit;

namespace EldoUI.Updater.Tests
{
	public class CompanionTests
	{
		static FakeWeb Serve(TempDir t, bool wrongDbmFlavor = false, bool corruptDbm = false)
		{
			var web = new FakeWeb();
			foreach (var addon in Companion.All.Where(a => a.Repository != null))
			{
				string zip = Zips.Make(t.Path, "1.0", addon.MainFolder);
				string name = addon.Id + ".zip";
				string root = "https://github.com/" + addon.Repository + "/releases/download/v1.0/";
				string meta = Path.Combine(t.Path, addon.Id + ".json");
				File.WriteAllText(meta, "{\"releases\":[{\"filename\":\"" + name + "\",\"metadata\":[{\"flavor\":\"" + (wrongDbmFlavor && addon.Id == "dbm" ? "mainline" : "forever") + "\"}]}]}");
				web.Files[root + name] = zip;
				web.Files[root + "release.json"] = meta;
				web.Api["https://api.github.com/repos/" + addon.Repository + "/releases/latest"] = "{\"tag_name\":\"v1.0\",\"assets\":["
					+ FakeWeb.AssetJson(name, root + name, new FileInfo(zip).Length, corruptDbm && addon.Id == "dbm" ? new string('0', 64) : FakeWeb.Sha(zip)) + ","
					+ FakeWeb.AssetJson("release.json", root + "release.json", new FileInfo(meta).Length, FakeWeb.Sha(meta)) + "]}";
			}
			return web;
		}

		[Fact]
		public async Task DefaultIsOffAndDoesNotDownloadAnything()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				Assert.Empty(await new CompanionUpdater(web, t.Sub("temp")).CheckAsync(new Settings(), true, CancellationToken.None));
				Assert.Equal(0, web.Downloads);
			}
		}

		[Fact]
		public async Task InstallsThreeThenChecksAgain()
		{
			using (var t = new TempDir())
			{
				var web = Serve(t);
				var settings = new Settings { ExtraAddons = true, AddOnsDir = t.Sub("_forever_", "Interface", "AddOns") };
				var updater = new CompanionUpdater(web, t.Sub("temp"));
				var results = await updater.CheckAsync(settings, true, CancellationToken.None);
				Assert.Equal(3, results.Count(r => r.Outcome == CompanionOutcome.Installed));
				Assert.Equal(3, results.Count);
				Assert.False(Directory.Exists(Path.Combine(settings.AddOnsDir, "Details")));
				int downloads = web.Downloads;
				results = await updater.CheckAsync(settings, true, CancellationToken.None);
				Assert.Equal(3, results.Count(r => r.Outcome == CompanionOutcome.Current));
				Assert.Equal(downloads + 3, web.Downloads); // Compatibility manifests, no package downloads.
				Assert.Empty(Directory.GetDirectories(t.Sub("temp")));
			}
		}

		[Theory]
		[InlineData(true, false)]
		[InlineData(false, true)]
		public async Task WrongFlavorOrChecksumDoesNotBlockOtherAddons(bool wrongFlavor, bool corrupt)
		{
			using (var t = new TempDir())
			{
				var settings = new Settings { ExtraAddons = true, AddOnsDir = t.Sub("AddOns") };
				var results = await new CompanionUpdater(Serve(t, wrongFlavor, corrupt), t.Sub("temp")).CheckAsync(settings, true, CancellationToken.None);
				Assert.Equal(CompanionOutcome.Failed, results[0].Outcome);
				Assert.Equal(2, results.Count(r => r.Outcome == CompanionOutcome.Installed));
				Assert.False(Directory.Exists(Path.Combine(settings.AddOnsDir, "DBM-Core")));
				Assert.False(settings.ExtraVersions.ContainsKey("dbm"));
			}
		}

		[Fact]
		public async Task CheckOnlyNeverInstalls()
		{
			using (var t = new TempDir())
			{
				var settings = new Settings { ExtraAddons = true, AddOnsDir = t.Sub("AddOns") };
				var results = await new CompanionUpdater(Serve(t), t.Sub("temp")).CheckAsync(settings, false, CancellationToken.None);
				Assert.Equal(3, results.Count(r => r.Outcome == CompanionOutcome.Available));
				Assert.False(Directory.Exists(settings.AddOnsDir));
			}
		}

		[Fact]
		public void PreservesPersonalGuidesAndRemovesOnlyPreviouslyManagedFiles()
		{
			using (var t = new TempDir())
			{
				var addon = Companion.All.Single(a => a.Id == "rxp");
				string addOns = t.Sub("AddOns");
				Directory.CreateDirectory(Path.Combine(addOns, "RXPGuides", "Guides"));
				string custom = Path.Combine(addOns, "RXPGuides", "Guides", "my-guide.lua");
				File.WriteAllText(custom, "personal guide");
				string old = Zips.Raw(t.Path, ("RXPGuides/RXPGuides.toc", "## Version: 1.0"), ("RXPGuides/old.lua", "old"));
				var files = CompanionInstaller.Install(addon, old, addOns, null, CancellationToken.None);
				CompanionInstaller.Install(addon, Zips.Make(t.Path, "2.0", "RXPGuides"), addOns, files, CancellationToken.None);
				Assert.Equal("personal guide", File.ReadAllText(custom));
				Assert.False(File.Exists(Path.Combine(addOns, "RXPGuides", "old.lua")));
			}
		}

		[Theory]
		[InlineData("RXPGuides/../Other/file.lua")]
		[InlineData("Other/Other.toc")]
		[InlineData("/RXPGuides/file.lua")]
		[InlineData("RXPGuides/file:stream")]
		[InlineData("RXPGuides//file.lua")]
		[InlineData("RXPGuides/CON.lua")]
		[InlineData("RXPGuides/bad\nname.lua")]
		public void RejectsUnrelatedAndUnsafePathsWithoutTouchingExistingFiles(string bad)
		{
			using (var t = new TempDir())
			{
				var addon = Companion.All.Single(a => a.Id == "rxp");
				string addOns = t.Sub("AddOns");
				Directory.CreateDirectory(Path.Combine(addOns, "RXPGuides"));
				string original = Path.Combine(addOns, "RXPGuides", "RXPGuides.toc");
				File.WriteAllText(original, "original");
				Assert.Throws<InvalidDataException>(() => CompanionInstaller.Install(addon,
					Zips.Raw(t.Path, ("RXPGuides/RXPGuides.toc", "new"), (bad, "bad")), addOns, null, CancellationToken.None));
				Assert.Equal("original", File.ReadAllText(original));
			}
		}

		[Fact]
		public void FailedPlacementRestoresThePreviousAddon()
		{
			using (var t = new TempDir())
			{
				var addon = Companion.All.Single(a => a.Id == "dbm");
				string addOns = t.Sub("AddOns");
				Directory.CreateDirectory(Path.Combine(addOns, "DBM-Core"));
				string original = Path.Combine(addOns, "DBM-Core", "DBM-Core.toc");
				File.WriteAllText(original, "original");
				File.WriteAllText(Path.Combine(addOns, "DBM-GUI"), "conflicting file");
				Assert.Throws<IOException>(() => CompanionInstaller.Install(addon,
					Zips.Make(t.Path, "2.0", "DBM-Core", "DBM-GUI"), addOns, null, CancellationToken.None));
				Assert.Equal("original", File.ReadAllText(original));
				Assert.Equal("conflicting file", File.ReadAllText(Path.Combine(addOns, "DBM-GUI")));
				Assert.Empty(Directory.GetDirectories(t.Path, ".eldoui-companion-*"));
			}
		}

		[Fact]
		public async Task MainEngineRunsCompanionsWithoutReplacingItsOwnResult()
		{
			using (var t = new TempDir())
			{
				var web = Serve(t);
				string zip = Zips.Make(t.Path, "0.7.0002", "EldoUI");
				const string url = "https://github.com/Eldodeldo/EldoUI/releases/download/v0.7.0002/EldoUI-0.7.0002.zip";
				web.Files[url] = zip;
				web.Api["https://api.github.com/repos/Eldodeldo/EldoUI/releases?per_page=10"] = "[" + FakeWeb.ReleaseJson("0.7.0002", url, new FileInfo(zip).Length, FakeWeb.Sha(zip)) + "]";
				Directory.CreateDirectory(t.Sub("_forever_"));
				var settings = new Settings { ExtraAddons = true, AddOnsDir = t.Sub("_forever_", "Interface", "AddOns") };
				var engine = new UpdateEngine(web, t.Sub("temp"), null);
				var result = await engine.CheckAsync(settings, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Installed, result.Outcome);
				Assert.Equal("0.7.0002", result.Installed);
				Assert.Equal(3, result.Companions.Count(r => r.Outcome == CompanionOutcome.Installed));
				Assert.False(engine.Busy);
			}
		}

		[Fact]
		public void CancellationLeavesOriginalFilesUntouched()
		{
			using (var t = new TempDir())
			using (var cancel = new CancellationTokenSource())
			{
				var addon = Companion.All.Single(a => a.Id == "rxp");
				Directory.CreateDirectory(t.Sub("AddOns", "RXPGuides"));
				File.WriteAllText(t.Sub("AddOns", "RXPGuides", "RXPGuides.toc"), "original");
				cancel.Cancel();
				Assert.Throws<OperationCanceledException>(() => CompanionInstaller.Install(addon,
					Zips.Make(t.Path, "2.0", "RXPGuides"), t.Sub("AddOns"), null, cancel.Token));
				Assert.Equal("original", File.ReadAllText(t.Sub("AddOns", "RXPGuides", "RXPGuides.toc")));
			}
		}

		[Fact]
		public void CompanionSettingsRoundTrip()
		{
			using (var t = new TempDir())
			{
				var s = new Settings { ExtraAddons = true };
				s.ExtraVersions["rxp"] = "v1.0";
				s.ExtraFiles["rxp"] = new List<string> { "RXPGuides/RXPGuides.toc" };
				s.Save(t.Sub("settings.json"));
				var restored = Settings.Load(t.Sub("settings.json"));
				Assert.True(restored.ExtraAddons);
				Assert.Equal("v1.0", restored.ExtraVersions["rxp"]);
				Assert.Equal(s.ExtraFiles["rxp"], restored.ExtraFiles["rxp"]);
			}
		}
	}
}
