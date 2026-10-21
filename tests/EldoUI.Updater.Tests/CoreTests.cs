using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EldoUI.Updater.Core;
using Xunit;

namespace EldoUI.Updater.Tests
{
	public sealed class TempDir : IDisposable
	{
		public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "eui-upd-" + Guid.NewGuid().ToString("N").Substring(0, 8));
		public TempDir() { Directory.CreateDirectory(Path); }
		public string Sub(params string[] parts) { return System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray()); }
		public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
	}

	static class Zips
	{
		public static string Make(string dir, string version, params string[] folders)
		{
			string path = Path.Combine(dir, "EldoUI-" + version + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
			using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
				foreach (string f in folders)
				{
					Add(z, f + "/" + f + ".toc", "## Interface: 16001\n## Version: " + version + "\n");
					Add(z, f + "/Core/init.lua", "-- " + f + " " + version);
				}
			return path;
		}

		public static string Raw(string dir, params (string name, string text)[] entries)
		{
			string path = Path.Combine(dir, "raw-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
			using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
				foreach (var e in entries) Add(z, e.name, e.text);
			return path;
		}

		static void Add(ZipArchive z, string name, string text)
		{
			using (var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false)))
				w.Write(text);
		}
	}

	public class VersionTests
	{
		[Theory]
		[InlineData("0.7.0002", "0.7.0000", 1)]
		[InlineData("v0.7.0002", "0.7.0002", 0)]
		[InlineData("0.7.0010", "0.7.0009", 1)]
		[InlineData("0.6.6005", "0.7.0000", -1)]
		[InlineData("1.0", "1.0.0", 0)]
		[InlineData("1.2.3-beta", "1.2.3", 0)]
		[InlineData("garbage", "0.0.1", -1)]
		public void Compares(string a, string b, int sign)
		{
			Assert.Equal(sign, Math.Sign(Versions.Compare(a, b)));
		}

		[Fact]
		public void NothingIsNewerThanItself()
		{
			Assert.False(Versions.IsNewer("v1.0.0", "1.0.0"));
			Assert.False(Versions.IsNewer(null, "1.0.0"));
			Assert.True(Versions.IsNewer("1.0.0", null));
		}
	}

	public class JsonTests
	{
		[Fact]
		public void RoundTrips()
		{
			var o = new Dictionary<string, object> { { "a", "x\"y\\\n" }, { "b", true }, { "c", null }, { "d", new List<string> { "p", "q" } }, { "e", 3.5 } };
			var back = Json.ParseObject(Json.Write(o));
			Assert.Equal("x\"y\\\n", back.Str("a"));
			Assert.True(back.Bool("b"));
			Assert.Null(back["c"]);
			Assert.Equal(new object[] { "p", "q" }, back.Arr("d").ToArray());
			Assert.Equal(3.5, back.Num("e"));
		}

		[Fact]
		public void ReadsUnicodeEscapes()
		{
			Assert.Equal("Æble ø", Json.ParseObject("{\"k\":\"\\u00c6ble \\u00f8\"}").Str("k"));
		}

		[Theory]
		[InlineData("{")]
		[InlineData("{\"a\":}")]
		[InlineData("[1,2")]
		[InlineData("{} x")]
		public void RefusesBrokenJson(string text)
		{
			Assert.Throws<FormatException>(() => Json.Parse(text));
		}
	}

	public class ReleaseTests
	{
		const string Sample = @"{
  ""tag_name"": ""v0.7.0002"", ""name"": ""EldoUI 0.7.0002"", ""draft"": false, ""prerelease"": false,
  ""html_url"": ""https://github.com/Eldodeldo/EldoUI/releases/tag/v0.7.0002"",
  ""published_at"": ""2026-10-21T17:21:36Z"",
  ""assets"": [
    { ""name"": ""notes.txt"", ""size"": 3, ""browser_download_url"": ""https://github.com/Eldodeldo/EldoUI/releases/download/v0.7.0002/notes.txt"" },
    { ""name"": ""EldoUI-0.7.0002.zip"", ""size"": 1234, ""digest"": ""sha256:ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789"",
      ""browser_download_url"": ""https://github.com/Eldodeldo/EldoUI/releases/download/v0.7.0002/EldoUI-0.7.0002.zip"" },
    { ""name"": ""EldoUI-evil.zip"", ""size"": 1, ""browser_download_url"": ""http://example.com/EldoUI-evil.zip"" }
  ]
}";

		[Fact]
		public void ReadsTheZipAndItsChecksum()
		{
			var r = Release.FromGitHub(Sample);
			Assert.Equal("0.7.0002", r.Version);
			Assert.Equal(new DateTime(2026, 10, 21, 17, 21, 36, DateTimeKind.Utc), r.Published.Value.ToUniversalTime());
			var zip = r.Find(UpdateEngine.AddonAssetPattern);
			Assert.Equal("EldoUI-0.7.0002.zip", zip.Name);
			Assert.Equal(1234, zip.Size);
			Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", zip.Sha256);
		}

		[Fact]
		public void IgnoresDownloadsThatAreNotFromGitHub()
		{
			var r = Release.FromGitHub(Sample);
			Assert.DoesNotContain(r.Assets, a => a.Name == "EldoUI-evil.zip");
		}

		[Fact]
		public void ListsOnlyPublishedReleases()
		{
			var list = Release.ListFromGitHub("[" + Sample + ",{\"tag_name\":\"v0.8.0-beta\",\"prerelease\":true},{\"tag_name\":\"v0.9\",\"draft\":true}]");
			Assert.Single(list);
			Assert.Equal("0.7.0002", list[0].Version);
		}

		[Fact]
		public void RefusesPreReleases()
		{
			Assert.Null(Release.FromGitHub("{\"tag_name\":\"v1\",\"prerelease\":true}"));
			Assert.Null(Release.FromGitHub("{\"tag_name\":\"v1\",\"draft\":true}"));
		}
	}

	public class InstallerTests
	{
		[Fact]
		public void InstallsUpdatesAndRemovesDroppedFolders()
		{
			using (var t = new TempDir())
			{
				string addOns = t.Sub("WoW", "_classic_beta_", "Interface", "AddOns");
				Directory.CreateDirectory(Path.Combine(addOns, "OtherAddon"));
				Directory.CreateDirectory(Path.Combine(addOns, "EldoUI_Dev"));

				var r1 = AddonInstaller.Install(Zips.Make(t.Path, "0.7.0001", "EldoUI", "EldoUI_Old"), addOns, null);
				Assert.Equal("0.7.0001", r1.Version);
				Assert.Equal(new[] { "EldoUI", "EldoUI_Old" }, r1.Folders);
				// A file the old version had and the new one does not must not linger.
				File.WriteAllText(Path.Combine(addOns, "EldoUI", "stale.lua"), "old");

				var r2 = AddonInstaller.Install(Zips.Make(t.Path, "0.7.0002", "EldoUI", "EldoUI_LootData"), addOns, r1.Folders);
				Assert.Equal("0.7.0002", AddonInstaller.InstalledVersion(addOns));
				Assert.Equal(new[] { "EldoUI_Old" }, r2.Removed);
				Assert.False(Directory.Exists(Path.Combine(addOns, "EldoUI_Old")));
				Assert.False(File.Exists(Path.Combine(addOns, "EldoUI", "stale.lua")));
				Assert.True(Directory.Exists(Path.Combine(addOns, "EldoUI_LootData")));
				Assert.True(Directory.Exists(Path.Combine(addOns, "OtherAddon")));
				Assert.True(Directory.Exists(Path.Combine(addOns, "EldoUI_Dev")), "a folder the updater never installed is never removed");
				Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(addOns), ".eldoui-updater-*"));
			}
		}

		[Fact]
		public void CreatesAddOnsWhenTheGameHasNone()
		{
			using (var t = new TempDir())
			{
				string addOns = t.Sub("WoW", "_classic_beta_", "Interface", "AddOns");
				AddonInstaller.Install(Zips.Make(t.Path, "1.0.0", "EldoUI"), addOns, null);
				Assert.Equal("1.0.0", AddonInstaller.InstalledVersion(addOns));
			}
		}

		[Theory]
		[InlineData("../evil/evil.toc")]
		[InlineData("EldoUI/../../evil.lua")]
		[InlineData("/abs/EldoUI.toc")]
		[InlineData("C:/x/EldoUI.toc")]
		public void RefusesUnsafePaths(string bad)
		{
			using (var t = new TempDir())
			{
				string zip = Zips.Raw(t.Path, ("EldoUI/EldoUI.toc", "## Version: 1.0"), (bad, "x"));
				Assert.Throws<InvalidDataException>(() => AddonInstaller.Install(zip, t.Sub("AddOns"), null));
				Assert.False(File.Exists(Path.Combine(t.Path, "evil.lua")));
			}
		}

		[Fact]
		public void RefusesFoldersThatAreNotEldoUI()
		{
			using (var t = new TempDir())
			{
				string zip = Zips.Raw(t.Path, ("EldoUI/EldoUI.toc", "## Version: 1.0"), ("Bartender4/Bartender4.toc", "x"));
				Assert.Throws<InvalidDataException>(() => AddonInstaller.Inspect(zip));
			}
		}

		[Fact]
		public void RefusesAZipWithoutTheMainToc()
		{
			using (var t = new TempDir())
			{
				Assert.Throws<InvalidDataException>(() => AddonInstaller.Inspect(Zips.Raw(t.Path, ("EldoUI/Core.lua", "x"))));
				Assert.Throws<InvalidDataException>(() => AddonInstaller.Inspect(Zips.Raw(t.Path, ("EldoUI/EldoUI.toc", "x"), ("EldoUI_X/a.lua", "x"))));
			}
		}

		[Fact]
		public void SkipsLooseFilesAtTheTop()
		{
			using (var t = new TempDir())
			{
				string addOns = t.Sub("AddOns");
				string zip = Zips.Raw(t.Path, ("README.txt", "hi"), ("EldoUI/EldoUI.toc", "## Version: 2.0"));
				AddonInstaller.Install(zip, addOns, null);
				Assert.False(File.Exists(Path.Combine(addOns, "README.txt")));
				Assert.Equal("2.0", AddonInstaller.InstalledVersion(addOns));
			}
		}

		[Fact]
		public void ABrokenZipLeavesTheOldVersionInPlace()
		{
			using (var t = new TempDir())
			{
				string addOns = t.Sub("AddOns");
				AddonInstaller.Install(Zips.Make(t.Path, "1.0.0", "EldoUI"), addOns, null);
				string broken = Path.Combine(t.Path, "broken.zip");
				File.WriteAllText(broken, "not a zip");
				Assert.ThrowsAny<Exception>(() => AddonInstaller.Install(broken, addOns, new[] { "EldoUI" }));
				Assert.Equal("1.0.0", AddonInstaller.InstalledVersion(addOns));
			}
		}
	}

	public class WowFolderTests
	{
		[Fact]
		public void ResolvesEveryLevelToAddOns()
		{
			using (var t = new TempDir())
			{
				string root = t.Sub("World of Warcraft");
				string addOns = Path.Combine(root, "_classic_beta_", "Interface", "AddOns");
				Directory.CreateDirectory(addOns);
				Directory.CreateDirectory(Path.Combine(root, "_retail_", "Interface", "AddOns"));
				Assert.Equal(addOns, WowFolders.ResolveAddOns(root));
				Assert.Equal(addOns, WowFolders.ResolveAddOns(Path.Combine(root, "_classic_beta_")));
				Assert.Equal(addOns, WowFolders.ResolveAddOns(Path.Combine(root, "_classic_beta_", "Interface")));
				Assert.Equal(addOns, WowFolders.ResolveAddOns(addOns + Path.DirectorySeparatorChar));
				Assert.Null(WowFolders.ResolveAddOns(t.Path));
				Assert.Null(WowFolders.ResolveAddOns(Path.Combine(root, "nope")));
			}
		}

		[Fact]
		public void FindsForeverFromTheRetailPathInTheRegistry()
		{
			using (var t = new TempDir())
			{
				string root = t.Sub("Games", "World of Warcraft");
				Directory.CreateDirectory(Path.Combine(root, "_retail_"));
				Directory.CreateDirectory(Path.Combine(root, "_classic_beta_"));
				Assert.Equal(Path.Combine(root, "_classic_beta_", "Interface", "AddOns"),
					WowFolders.Find(new[] { t.Sub("missing"), Path.Combine(root, "_retail_") + Path.DirectorySeparatorChar }));
			}
		}

		[Fact]
		public void FindsNothingWithoutForever()
		{
			using (var t = new TempDir())
			{
				Directory.CreateDirectory(t.Sub("World of Warcraft", "_retail_"));
				Assert.Null(WowFolders.Find(new[] { t.Sub("World of Warcraft") }));
			}
		}
	}

	public class SelfUpdatePolicyTests
	{
		static readonly DateTime Now = new DateTime(2026, 10, 21, 20, 0, 0, DateTimeKind.Utc);

		[Fact]
		public void InstallsADayAfterItWasFirstSeen()
		{
			var s = new Settings();
			var at = SelfUpdatePolicy.Note(s, "1.0.3", Now);
			Assert.Equal(Now.AddHours(24), at);
			Assert.False(SelfUpdatePolicy.Due(at, Now.AddHours(23)));
			Assert.True(SelfUpdatePolicy.Due(at, Now.AddHours(24)));
		}

		[Fact]
		public void SeeingTheSameVersionAgainKeepsTheWait()
		{
			var s = new Settings();
			SelfUpdatePolicy.Note(s, "1.0.3", Now);
			Assert.Equal(Now.AddHours(24), SelfUpdatePolicy.Note(s, "1.0.3", Now.AddHours(10)));
		}

		[Fact]
		public void ANewerVersionStartsANewWait()
		{
			var s = new Settings();
			SelfUpdatePolicy.Note(s, "1.0.3", Now);
			Assert.Equal(Now.AddHours(34), SelfUpdatePolicy.Note(s, "1.0.4", Now.AddHours(10)));
		}

		[Fact]
		public void TheWaitSurvivesARestart()
		{
			using (var t = new TempDir())
			{
				string path = t.Sub("settings.json");
				var s = new Settings();
				SelfUpdatePolicy.Note(s, "1.0.3", Now);
				s.Save(path);
				var back = Settings.Load(path);
				Assert.Equal(Now.AddHours(24), SelfUpdatePolicy.Note(back, "1.0.3", Now.AddHours(5)));
				Assert.True(SelfUpdatePolicy.Clear(back));
				Assert.False(SelfUpdatePolicy.Clear(back));
				Assert.Equal("", back.UpdaterSeenVersion);
			}
		}
	}

	public class SettingsTests
	{
		[Fact]
		public void SavesAndLoads()
		{
			using (var t = new TempDir())
			{
				string path = t.Sub("data", "settings.json");
				var s = new Settings { AddOnsDir = @"C:\WoW\_classic_beta_\Interface\AddOns", AutoInstall = false, Language = "da", LastCheck = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc) };
				s.InstalledFolders.AddRange(new[] { "EldoUI", "EldoUI_LootData" });
				s.Save(path);
				s.Save(path);
				var b = Settings.Load(path);
				Assert.Equal(s.AddOnsDir, b.AddOnsDir);
				Assert.False(b.AutoInstall);
				Assert.Equal("da", b.Language);
				Assert.Equal(s.InstalledFolders, b.InstalledFolders);
				Assert.Equal(s.LastCheck, b.LastCheck.Value.ToUniversalTime());
			}
		}

		[Fact]
		public void ABrokenFileStartsFresh()
		{
			using (var t = new TempDir())
			{
				string path = t.Sub("settings.json");
				File.WriteAllText(path, "{ not json");
				var s = Settings.Load(path);
				Assert.True(s.AutoInstall);
				Assert.Equal("", s.AddOnsDir);
			}
		}
	}

	/// <summary>A fake GitHub: answers from memory, serves zips from disk.</summary>
	sealed class FakeWeb : IWeb
	{
		public readonly Dictionary<string, string> Api = new Dictionary<string, string>();
		public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
		public Exception Fail;
		public int Downloads;

		public Task<string> GetApiAsync(string url, CancellationToken ct)
		{
			if (Fail != null) throw Fail;
			string body;
			if (!Api.TryGetValue(url, out body)) throw new WebFailure(WebFailureKind.NoRelease, "404");
			return Task.FromResult(body);
		}

		public Task<string> DownloadAsync(string url, string target, long max, CancellationToken ct)
		{
			Downloads++;
			File.Copy(Files[url], target, true);
			using (var sha = SHA256.Create())
			using (var f = File.OpenRead(target))
				return Task.FromResult(Web.Hex(sha.ComputeHash(f)));
		}

		public static string ReleaseJson(string version, string url, long size, string sha)
		{
			return "{\"tag_name\":\"v" + version + "\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"EldoUI-" + version + ".zip\",\"size\":" + size
				+ ",\"browser_download_url\":\"" + url + "\"" + (sha != null ? ",\"digest\":\"sha256:" + sha + "\"" : "") + "}]}";
		}
	}

	public class EngineTests
	{
		const string Latest = "https://api.github.com/repos/Eldodeldo/EldoUI/releases?per_page=10";

		static (FakeWeb web, string url) Serve(TempDir t, string version, bool goodSha = true)
		{
			var web = new FakeWeb();
			string zip = Zips.Make(t.Path, version, "EldoUI", "EldoUI_LootData");
			string url = "https://github.com/Eldodeldo/EldoUI/releases/download/v" + version + "/EldoUI-" + version + ".zip";
			string sha;
			using (var h = SHA256.Create())
			using (var f = File.OpenRead(zip))
				sha = Web.Hex(h.ComputeHash(f));
			web.Files[url] = zip;
			web.Api[Latest] = "[" + FakeWeb.ReleaseJson(version, url, new FileInfo(zip).Length, goodSha ? sha : new string('0', 64)) + "]";
			return (web, url);
		}

		static Settings With(TempDir t, bool auto = true)
		{
			string addOns = t.Sub("WoW", "_classic_beta_", "Interface", "AddOns");
			Directory.CreateDirectory(addOns);
			return new Settings { AddOnsDir = addOns, AutoInstall = auto };
		}

		[Fact]
		public async Task InstallsANewRelease()
		{
			using (var t = new TempDir())
			{
				var (web, _) = Serve(t, "0.7.0002");
				var s = With(t);
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(s, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Installed, r.Outcome);
				Assert.Equal("0.7.0002", r.Installed);
				Assert.Equal(new[] { "EldoUI", "EldoUI_LootData" }, s.InstalledFolders);
				Assert.NotNull(s.LastCheck);
				Assert.Empty(Directory.GetFiles(t.Sub("tmp")));

				var again = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(s, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.UpToDate, again.Outcome);
				Assert.Equal(1, web.Downloads);
			}
		}

		[Fact]
		public async Task OnlyAnnouncesWhenAutomaticInstallIsOff()
		{
			using (var t = new TempDir())
			{
				var (web, _) = Serve(t, "0.7.0002");
				var s = With(t, auto: false);
				var engine = new UpdateEngine(web, t.Sub("tmp"), null);
				Assert.Equal(CheckOutcome.UpdateAvailable, (await engine.CheckAsync(s, false, CancellationToken.None)).Outcome);
				Assert.Equal(0, web.Downloads);
				Assert.Equal(CheckOutcome.Installed, (await engine.CheckAsync(s, true, CancellationToken.None)).Outcome);
			}
		}

		[Fact]
		public async Task RefusesADownloadWithTheWrongChecksum()
		{
			using (var t = new TempDir())
			{
				var (web, _) = Serve(t, "0.7.0002", goodSha: false);
				var s = With(t);
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(s, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Failed, r.Outcome);
				Assert.Contains("checksum", r.Error);
				Assert.Null(AddonInstaller.InstalledVersion(s.AddOnsDir));
			}
		}

		[Fact]
		public async Task NeedsAFolder()
		{
			using (var t = new TempDir())
			{
				var (web, _) = Serve(t, "0.7.0002");
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(new Settings(), false, CancellationToken.None);
				Assert.Equal(CheckOutcome.NoFolder, r.Outcome);
				Assert.Equal("0.7.0002", r.Latest);
			}
		}

		[Fact]
		public async Task OfflineIsNotAFailure()
		{
			using (var t = new TempDir())
			{
				var (web, _) = Serve(t, "0.7.0002");
				web.Fail = new System.Net.Http.HttpRequestException("no network");
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(With(t), false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Offline, r.Outcome);
			}
		}

		[Fact]
		public async Task SkipsANewestReleaseWithoutItsZip()
		{
			using (var t = new TempDir())
			{
				var (web, _) = Serve(t, "0.7.0002");
				string older = web.Api[Latest].TrimStart('[').TrimEnd(']');
				web.Api[Latest] = "[{\"tag_name\":\"v0.7.0003\",\"draft\":false,\"prerelease\":false,\"assets\":[]}," + older + "]";
				var s = With(t);
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(s, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Installed, r.Outcome);
				Assert.Equal("0.7.0002", r.Installed);
			}
		}

		[Fact]
		public async Task NoZipAnywhereIsNotAFailure()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				web.Api[Latest] = "[{\"tag_name\":\"v0.7.0000\",\"draft\":false,\"prerelease\":false,\"assets\":[]}]";
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(With(t), false, CancellationToken.None);
				Assert.Equal(CheckOutcome.NoDownload, r.Outcome);
				Assert.Equal("0.7.0000", r.Latest);
				Assert.Equal(0, web.Downloads);
			}
		}

		[Fact]
		public async Task FindsANewerUpdater()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				web.Api["https://api.github.com/repos/Eldodeldo/EldoUI-updater-app/releases/latest"] =
					"{\"tag_name\":\"v1.1.0\",\"assets\":[{\"name\":\"EldoUI-Updater.exe\",\"size\":5,\"browser_download_url\":\"https://github.com/Eldodeldo/EldoUI-updater-app/releases/download/v1.1.0/EldoUI-Updater.exe\"}]}";
				var engine = new UpdateEngine(web, t.Sub("tmp"), null);
				Assert.NotNull(await engine.NewerUpdaterAsync("1.0.0", CancellationToken.None));
				Assert.Null(await engine.NewerUpdaterAsync("1.1.0", CancellationToken.None));
				Assert.Null(await new UpdateEngine(new FakeWeb(), t.Sub("tmp"), null).NewerUpdaterAsync("1.0.0", CancellationToken.None));
			}
		}
	}
}
