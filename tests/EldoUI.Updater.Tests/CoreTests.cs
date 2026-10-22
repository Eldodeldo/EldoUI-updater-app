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
		[InlineData("1.2.3-beta", "1.2.3", -1)]
		[InlineData("1.2.3", "1.2.3-beta", 1)]
		[InlineData("0.7.0002-beta.1a2b3c4", "0.7.0002", -1)]
		[InlineData("0.7.0002-beta.1a2b3c4", "0.7.0001", 1)]
		[InlineData("1.2.3-beta", "1.2.3-rc", 0)]
		[InlineData("1.2.3+build.5", "1.2.3", 0)]
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

		[Fact]
		public void APreReleaseIsReplacedByItsRelease()
		{
			Assert.True(Versions.IsNewer("0.7.0002", "0.7.0002-beta.abc"));
			Assert.False(Versions.IsNewer("0.7.0002", "0.7.0003-beta.abc"));
			Assert.True(Versions.IsPreRelease("v1.2.3-beta+x"));
			Assert.False(Versions.IsPreRelease("1.2.3+build-5"));
			Assert.False(Versions.IsPreRelease("1.2.3"));
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

		[Theory]
		[InlineData("https://github.com/Eldodeldo/EldoUI", true)]
		[InlineData("https://github.com/Eldodeldo/EldoUI/releases/latest", true)]
		[InlineData("http://github.com/Eldodeldo/EldoUI", false)]
		[InlineData("https://github.com.example.com/x", false)]
		[InlineData("https://example.com/", false)]
		[InlineData("file:///C:/Windows/System32/calc.exe", false)]
		[InlineData("", false)]
		[InlineData(null, false)]
		public void OpensOnlyGitHubPages(string url, bool allowed)
		{
			Assert.Equal(allowed, Release.IsGitHubPage(url));
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
		public void FindsChecksumFilesForADownload()
		{
			var zip = new ReleaseAsset { Name = "EldoUI-1.0.zip" };
			Assert.True(Release.IsChecksumFileFor(new ReleaseAsset { Name = "SHA256SUMS.txt" }, zip));
			Assert.True(Release.IsChecksumFileFor(new ReleaseAsset { Name = "sha256sums" }, zip));
			Assert.True(Release.IsChecksumFileFor(new ReleaseAsset { Name = "EldoUI-1.0.zip.sha256" }, zip));
			Assert.False(Release.IsChecksumFileFor(new ReleaseAsset { Name = "Other.zip.sha256" }, zip));
			Assert.False(Release.IsChecksumFileFor(new ReleaseAsset { Name = "notes.txt" }, zip));
			Assert.False(Release.IsChecksumFileFor(zip, zip));
		}

		[Fact]
		public void RefusesPreReleases()
		{
			Assert.Null(Release.FromGitHub("{\"tag_name\":\"v1\",\"prerelease\":true}"));
			Assert.Null(Release.FromGitHub("{\"tag_name\":\"v1\",\"draft\":true}"));
		}
	}

	public class ChecksumTests
	{
		static readonly string A = new string('a', 64);
		static readonly string B = new string('b', 64);

		[Fact]
		public void ReadsTheLineForTheFile()
		{
			string text = B + "  EldoUI-Updater.exe\r\n" + A.ToUpperInvariant() + " *EldoUI-1.0.zip\r\n";
			Assert.Equal(A, Checksums.Find(text, "EldoUI-1.0.zip"));
			Assert.Equal(B, Checksums.Find(text, "eldoui-updater.exe"));
			Assert.Null(Checksums.Find(text, "EldoUI-2.0.zip"));
		}

		[Fact]
		public void IgnoresAFolderInFrontOfTheName()
		{
			Assert.Equal(A, Checksums.Find(A + "  ./dist/EldoUI-1.0.zip\n", "EldoUI-1.0.zip"));
		}

		[Fact]
		public void AHashAloneCountsOnlyInItsOwnFile()
		{
			Assert.Null(Checksums.Find(A + "\n", "EldoUI-1.0.zip"));
			Assert.Equal(A, Checksums.Find(A + "  \n", "EldoUI-1.0.zip", bareHashAllowed: true));
		}

		[Fact]
		public void TwoDifferentHashesCountAsNone()
		{
			Assert.Null(Checksums.Find(A + "  EldoUI-1.0.zip\n" + B + "  EldoUI-1.0.zip\n", "EldoUI-1.0.zip"));
			Assert.Equal(A, Checksums.Find(A + "  EldoUI-1.0.zip\n" + A + "  EldoUI-1.0.zip\n", "EldoUI-1.0.zip"));
		}

		[Theory]
		[InlineData("")]
		[InlineData("not a checksum file")]
		[InlineData("abc123  EldoUI-1.0.zip")]
		public void IgnoresLinesThatAreNotChecksums(string text)
		{
			Assert.Null(Checksums.Find(text, "EldoUI-1.0.zip"));
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
				Directory.CreateDirectory(Path.Combine(addOns, "EldoUI_Custom"));
				File.WriteAllText(Path.Combine(addOns, "EldoUI_Custom", "mine.lua"), "keep");

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
				Assert.Equal("keep", File.ReadAllText(Path.Combine(addOns, "EldoUI_Custom", "mine.lua")));
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
		public void TheReleasedGameWinsOverTheBetaWherever()
		{
			using (var t = new TempDir())
			{
				string beta = t.Sub("C", "Program Files (x86)", "World of Warcraft");
				string game = t.Sub("D", "Spil", "WoW");
				Directory.CreateDirectory(Path.Combine(beta, "_classic_beta_"));
				Directory.CreateDirectory(Path.Combine(game, "_forever_"));
				Assert.Equal(Path.Combine(game, "_forever_", "Interface", "AddOns"), WowFolders.Find(new[] { beta, game }));
				Assert.Equal(Path.Combine(beta, "_classic_beta_", "Interface", "AddOns"), WowFolders.Find(new[] { beta }));
			}
		}

		[Fact]
		public void MovesFromTheBetaToTheReleasedGameBesideIt()
		{
			using (var t = new TempDir())
			{
				string root = t.Sub("World of Warcraft");
				string beta = Path.Combine(root, "_classic_beta_", "Interface", "AddOns");
				Directory.CreateDirectory(beta);
				Assert.Equal(1, WowFolders.Rank(beta));
				Assert.Null(WowFolders.Better(beta));
				Directory.CreateDirectory(Path.Combine(root, "_forever_"));
				string game = Path.Combine(root, "_forever_", "Interface", "AddOns");
				Assert.Equal(game, WowFolders.Better(beta));
				Assert.Equal(0, WowFolders.Rank(game));
				Assert.Null(WowFolders.Better(game));
				Assert.Equal(game, WowFolders.ResolveAddOns(root));
			}
		}

		[Fact]
		public void ReadsBattleNetsInstallFolder()
		{
			var dirs = WowFolders.FromBattleNetConfig("{\"Client\":{\"Install\":{\"DefaultInstallPath\":\"E:\\\\Games\\\\\"}}}").ToList();
			Assert.Contains(Path.Combine(@"E:\Games\", "World of Warcraft"), dirs);
			Assert.Empty(WowFolders.FromBattleNetConfig("{}"));
			Assert.Empty(WowFolders.FromBattleNetConfig("not json"));
		}

		[Fact]
		public void ReadsInstalledGamesFromProductDb()
		{
			// Paths sit between binary bytes; the byte after one can be a printable letter.
			var bytes = new List<byte> { 0x0a, 0x10 };
			bytes.AddRange(Encoding.UTF8.GetBytes("wow_forever"));
			bytes.AddRange(new byte[] { 0x12, 0x15 });
			bytes.AddRange(Encoding.UTF8.GetBytes("F:/Mine spil/World of Warcraft"));
			bytes.AddRange(new byte[] { 0x32, 0x05, 0x00 });
			bytes.AddRange(Encoding.UTF8.GetBytes("C:/Program Files (x86)/Diablo IV"));
			bytes.Add(0x1a);
			var dirs = WowFolders.FromProductDb(bytes.ToArray()).ToList();
			Assert.Contains(@"F:\Mine spil\World of Warcraft", dirs);
			Assert.Contains(@"C:\Program Files (x86)\Diablo IV", dirs);
			Assert.Empty(WowFolders.FromProductDb(null));
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

	/// <summary>A test that only means something on Linux (it is reported as skipped elsewhere).</summary>
	public sealed class LinuxFactAttribute : FactAttribute
	{
		public LinuxFactAttribute()
		{
			if (!OperatingSystem.IsLinux()) Skip = "Linux only";
		}
	}

	public class WineFolderTests
	{
		[Fact]
		public void MapsWindowsPathsIntoThePrefix()
		{
			Assert.Equal(Path.Combine("/p", "drive_c", "Program Files (x86)/World of Warcraft"), WineFolders.ToUnix("/p", @"C:\Program Files (x86)\World of Warcraft\"));
			Assert.Equal(Path.Combine("/p", "dosdevices", "d:", "Spil/WoW"), WineFolders.ToUnix("/p", "D:/Spil//WoW"));
			Assert.Equal(Path.Combine("/p", "drive_c"), WineFolders.ToUnix("/p", @"c:\"));
			Assert.Null(WineFolders.ToUnix("/p", "relative/path"));
		}

		[Fact]
		public void ReadsLutrisAndSteamFiles()
		{
			var lutris = WineFolders.LutrisPrefixes("game:\n  exe: x.exe\n  prefix: ~/Games/battlenet\nwine:\n  prefix: '/mnt/ssd/wow'\n", "/home/me").ToList();
			Assert.Equal(new[] { Path.Combine("/home/me", "Games/battlenet"), "/mnt/ssd/wow" }, lutris);
			var libs = WineFolders.VdfLibraryPaths("\"libraryfolders\"\n{\n \"0\"\n {\n  \"path\"\t\t\"/home/me/.local/share/Steam\"\n }\n \"1\"\n {\n  \"path\"\t\t\"/mnt/games/SteamLibrary\"\n }\n}").ToList();
			Assert.Equal(new[] { "/home/me/.local/share/Steam", "/mnt/games/SteamLibrary" }, libs);
		}

		[Fact]
		public void FindsForeverInLutrisBottlesAndProton()
		{
			using (var t = new TempDir())
			{
				string home = t.Path;
				// A Bottles bottle with only the beta, and a Proton prefix with the released game.
				Directory.CreateDirectory(t.Sub(".local/share/bottles/bottles/Battlenet/drive_c/Program Files (x86)/World of Warcraft/_classic_beta_"));
				string proton = t.Sub(".steam/steam/steamapps/compatdata/123456/pfx");
				Directory.CreateDirectory(Path.Combine(proton, "drive_c/Program Files (x86)/World of Warcraft/_forever_"));
				string want = Path.Combine(proton, "drive_c/Program Files (x86)/World of Warcraft/_forever_", "Interface", "AddOns");
				Assert.Equal(Path.GetFullPath(want), Path.GetFullPath(WineFolders.Find(home)));
			}
		}

		// Wine's dosdevices\d: is a Linux symlink; Windows cannot even name a file "d:". The Linux CI job runs it.
		[LinuxFact]
		public void FollowsBattleNetInsideThePrefixToAnotherDrive()
		{
			using (var t = new TempDir())
			{
				string prefix = t.Sub("Games", "battlenet");
				string roaming = Path.Combine(prefix, "drive_c/users/steamuser/AppData/Roaming/Battle.net");
				Directory.CreateDirectory(roaming);
				File.WriteAllText(Path.Combine(roaming, "Battle.net.config"), "{\"Client\":{\"Install\":{\"DefaultInstallPath\":\"D:\\\\Spil\"}}}");
				// D: is a folder elsewhere, linked the way Wine does it.
				string dDrive = t.Sub("ssd");
				Directory.CreateDirectory(Path.Combine(dDrive, "Spil/World of Warcraft/_forever_"));
				Directory.CreateDirectory(Path.Combine(prefix, "dosdevices"));
				File.CreateSymbolicLink(Path.Combine(prefix, "dosdevices", "d:"), dDrive);
				string found = WineFolders.Find(t.Path);
				Assert.Equal(Path.Combine(prefix, "dosdevices", "d:", "Spil", "World of Warcraft", "_forever_", "Interface", "AddOns"), found);
			}
		}

		[Fact]
		public void NoPrefixFindsNothing()
		{
			using (var t = new TempDir())
			{
				Directory.CreateDirectory(t.Sub("Games", "notaprefix"));
				Assert.Empty(WineFolders.Prefixes(t.Path));
				Assert.Null(WineFolders.Find(t.Path));
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
				var s = new Settings { AddOnsDir = @"C:\WoW\_classic_beta_\Interface\AddOns", AutoInstall = false, Language = "da", Introduced = true, LastCheck = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc) };
				s.InstalledFolders.AddRange(new[] { "EldoUI", "EldoUI_LootData" });
				s.Save(path);
				s.Save(path);
				var b = Settings.Load(path);
				Assert.Equal(s.AddOnsDir, b.AddOnsDir);
				Assert.False(b.AutoInstall);
				Assert.Equal("da", b.Language);
				Assert.True(b.Introduced);
				Assert.False(new Settings().Introduced);
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

		public static string ReleaseJson(string version, string url, long size, string sha, params string[] moreAssets)
		{
			return "{\"tag_name\":\"v" + version + "\",\"draft\":false,\"prerelease\":false,\"assets\":[" + AssetJson("EldoUI-" + version + ".zip", url, size, sha)
				+ string.Concat(moreAssets.Select(a => "," + a)) + "]}";
		}

		public static string AssetJson(string name, string url, long size, string sha)
		{
			return "{\"name\":\"" + name + "\",\"size\":" + size + ",\"browser_download_url\":\"" + url + "\""
				+ (sha != null ? ",\"digest\":\"sha256:" + sha + "\"" : "") + "}";
		}

		public static string Sha(string path)
		{
			using (var h = SHA256.Create())
			using (var f = File.OpenRead(path))
				return Web.Hex(h.ComputeHash(f));
		}
	}

	public class EngineTests
	{
		const string Latest = "https://api.github.com/repos/Eldodeldo/EldoUI/releases?per_page=10";

		/// <summary>How a served release vouches for its zip.</summary>
		public enum Proof { Digest, WrongDigest, None, ChecksumFile, WrongChecksumFile }

		static (FakeWeb web, string url) Serve(TempDir t, string version, Proof proof = Proof.Digest)
		{
			var web = new FakeWeb();
			string zip = Zips.Make(t.Path, version, "EldoUI", "EldoUI_LootData");
			string baseUrl = "https://github.com/Eldodeldo/EldoUI/releases/download/v" + version + "/";
			string url = baseUrl + "EldoUI-" + version + ".zip";
			string sha = FakeWeb.Sha(zip);
			web.Files[url] = zip;
			string digest = proof == Proof.Digest ? sha : proof == Proof.WrongDigest ? new string('0', 64) : null;
			var more = new List<string>();
			if (proof == Proof.ChecksumFile || proof == Proof.WrongChecksumFile)
			{
				string sums = Path.Combine(t.Path, "SHA256SUMS-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".txt");
				File.WriteAllText(sums, new string('1', 64) + "  EldoUI-Updater.exe\n"
					+ (proof == Proof.ChecksumFile ? sha : new string('0', 64)) + "  EldoUI-" + version + ".zip\n");
				web.Files[baseUrl + "SHA256SUMS.txt"] = sums;
				more.Add(FakeWeb.AssetJson("SHA256SUMS.txt", baseUrl + "SHA256SUMS.txt", new FileInfo(sums).Length, null));
			}
			web.Api[Latest] = "[" + FakeWeb.ReleaseJson(version, url, new FileInfo(zip).Length, digest, more.ToArray()) + "]";
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
				var (web, _) = Serve(t, "0.7.0002", Proof.WrongDigest);
				var s = With(t);
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(s, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Unverified, r.Outcome);
				Assert.Contains("checksum", r.Error);
				Assert.Null(AddonInstaller.InstalledVersion(s.AddOnsDir));
			}
		}

		/// <summary>An AddOns folder with EldoUI 0.7.0001 installed by the updater, another addon, and an EldoUI folder the updater did not install.</summary>
		static Settings WithOldInstall(TempDir t)
		{
			var s = With(t);
			var first = AddonInstaller.Install(Zips.Make(t.Path, "0.7.0001", "EldoUI"), s.AddOnsDir, null);
			s.InstalledFolders = first.Folders.ToList();
			Directory.CreateDirectory(Path.Combine(s.AddOnsDir, "OtherAddon"));
			File.WriteAllText(Path.Combine(s.AddOnsDir, "OtherAddon", "OtherAddon.toc"), "other");
			Directory.CreateDirectory(Path.Combine(s.AddOnsDir, "EldoUI_Custom"));
			File.WriteAllText(Path.Combine(s.AddOnsDir, "EldoUI_Custom", "EldoUI_Custom.toc"), "custom");
			return s;
		}

		static string Snapshot(string addOns)
		{
			return string.Join("\n", Directory.GetFiles(addOns, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
				.Select(f => f.Substring(addOns.Length) + "=" + FakeWeb.Sha(f)));
		}

		[Fact]
		public async Task RefusesAReleaseWithoutAnyChecksumAndChangesNothing()
		{
			using (var t = new TempDir())
			{
				var (web, _) = Serve(t, "0.7.0002", Proof.None);
				var s = WithOldInstall(t);
				string before = Snapshot(s.AddOnsDir);
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(s, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Unverified, r.Outcome);
				Assert.Equal("0.7.0002", r.Latest);
				Assert.Contains("no published checksum", r.Error);
				Assert.Equal(0, web.Downloads);
				Assert.Equal(before, Snapshot(s.AddOnsDir));
				Assert.Equal(new[] { "EldoUI" }, s.InstalledFolders);
				Assert.Empty(Directory.GetFiles(t.Sub("tmp")));
			}
		}

		[Fact]
		public async Task UsesAChecksumFileWhenGitHubHasNoDigest()
		{
			using (var t = new TempDir())
			{
				var (web, _) = Serve(t, "0.7.0002", Proof.ChecksumFile);
				var s = With(t);
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(s, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Installed, r.Outcome);
				Assert.Equal("0.7.0002", AddonInstaller.InstalledVersion(s.AddOnsDir));
				Assert.Empty(Directory.GetFiles(t.Sub("tmp")));
			}
		}

		[Fact]
		public async Task RefusesAZipThatDiffersFromItsChecksumFile()
		{
			using (var t = new TempDir())
			{
				var (web, _) = Serve(t, "0.7.0002", Proof.WrongChecksumFile);
				var s = WithOldInstall(t);
				string before = Snapshot(s.AddOnsDir);
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(s, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Unverified, r.Outcome);
				Assert.Equal(before, Snapshot(s.AddOnsDir));
				Assert.Empty(Directory.GetFiles(t.Sub("tmp")));
			}
		}

		[Fact]
		public async Task TriesAgainAtTheNextCheck()
		{
			using (var t = new TempDir())
			{
				var (web, url) = Serve(t, "0.7.0002", Proof.None);
				var s = WithOldInstall(t);
				var engine = new UpdateEngine(web, t.Sub("tmp"), null);
				Assert.Equal(CheckOutcome.Unverified, (await engine.CheckAsync(s, false, CancellationToken.None)).Outcome);
				web.Api[Latest] = "[" + FakeWeb.ReleaseJson("0.7.0002", url, new FileInfo(web.Files[url]).Length, FakeWeb.Sha(web.Files[url])) + "]";
				Assert.Equal(CheckOutcome.Installed, (await engine.CheckAsync(s, false, CancellationToken.None)).Outcome);
			}
		}

		[Fact]
		public async Task AnUpdateTouchesOnlyEldoUIsOwnFolders()
		{
			using (var t = new TempDir())
			{
				var (web, _) = Serve(t, "0.7.0002");
				var s = WithOldInstall(t);
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(s, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Installed, r.Outcome);
				Assert.Equal("other", File.ReadAllText(Path.Combine(s.AddOnsDir, "OtherAddon", "OtherAddon.toc")));
				Assert.Equal("custom", File.ReadAllText(Path.Combine(s.AddOnsDir, "EldoUI_Custom", "EldoUI_Custom.toc")));
				Assert.Equal(new[] { "EldoUI", "EldoUI_LootData" }, s.InstalledFolders);
				Assert.Equal(new[] { "EldoUI", "EldoUI_Custom", "EldoUI_LootData", "OtherAddon" },
					Directory.GetDirectories(s.AddOnsDir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
			}
		}

		[Fact]
		public async Task ReplacesAPreReleaseWithTheSameRelease()
		{
			using (var t = new TempDir())
			{
				var (web, _) = Serve(t, "0.7.0002");
				var s = With(t);
				AddonInstaller.Install(Zips.Make(t.Path, "0.7.0002-beta.1a2b3c4", "EldoUI"), s.AddOnsDir, null);
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(s, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Installed, r.Outcome);
				Assert.Equal("0.7.0002", AddonInstaller.InstalledVersion(s.AddOnsDir));
			}
		}

		[Fact]
		public async Task KeepsANewerPreRelease()
		{
			using (var t = new TempDir())
			{
				var (web, _) = Serve(t, "0.7.0002");
				var s = With(t);
				AddonInstaller.Install(Zips.Make(t.Path, "0.7.0003-beta.1a2b3c4", "EldoUI"), s.AddOnsDir, null);
				var r = await new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(s, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.UpToDate, r.Outcome);
				Assert.Equal(0, web.Downloads);
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

		[Fact]
		public async Task ACheckForAnAppUpdateNamesEachOutcome()
		{
			using (var t = new TempDir())
			{
				var web = new FakeWeb();
				web.Api["https://api.github.com/repos/Eldodeldo/EldoUI-updater-app/releases/latest"] =
					"{\"tag_name\":\"v1.3.0\",\"assets\":[{\"name\":\"EldoUI-Updater.exe\",\"size\":5,\"browser_download_url\":\"https://github.com/Eldodeldo/EldoUI-updater-app/releases/download/v1.3.0/EldoUI-Updater.exe\"}]}";
				var engine = new UpdateEngine(web, t.Sub("tmp"), null);

				var newer = await engine.CheckUpdaterAsync("1.2.1", CancellationToken.None);
				Assert.Equal(AppCheckOutcome.Newer, newer.Outcome);
				Assert.Equal("1.3.0", newer.Release.Version);

				Assert.Equal(AppCheckOutcome.UpToDate, (await engine.CheckUpdaterAsync("1.3.0", CancellationToken.None)).Outcome);
				// A release without this platform's file is no update for it.
				Assert.Equal(AppCheckOutcome.UpToDate, (await engine.CheckUpdaterAsync("1.2.1", CancellationToken.None, UpdateEngine.LinuxUpdaterAssetPattern)).Outcome);

				// GitHub answers "not found" (no public release to see) or cannot be reached at all.
				var none = await new UpdateEngine(new FakeWeb(), t.Sub("tmp"), null).CheckUpdaterAsync("1.2.1", CancellationToken.None);
				Assert.Equal(AppCheckOutcome.Unreachable, none.Outcome);
				Assert.NotNull(none.Error);
				web.Fail = new System.Net.Http.HttpRequestException("no network");
				Assert.Equal(AppCheckOutcome.Unreachable, (await engine.CheckUpdaterAsync("1.2.1", CancellationToken.None)).Outcome);
				Assert.Null(await engine.NewerUpdaterAsync("1.2.1", CancellationToken.None));
			}
		}

		[Fact]
		public async Task TheAppsOwnUpdateNeedsATrustedChecksumToo()
		{
			using (var t = new TempDir())
			{
				string exe = Path.Combine(t.Path, "new.exe");
				File.WriteAllText(exe, "new updater");
				string sums = Path.Combine(t.Path, "SHA256SUMS.txt");
				File.WriteAllText(sums, FakeWeb.Sha(exe) + "  EldoUI-Updater.exe\r\n");
				const string Base = "https://github.com/Eldodeldo/EldoUI-updater-app/releases/download/v9.0.0/";
				var web = new FakeWeb();
				web.Files[Base + "EldoUI-Updater.exe"] = exe;
				web.Files[Base + "SHA256SUMS.txt"] = sums;
				var withSums = Release.FromGitHub("{\"tag_name\":\"v9.0.0\",\"assets\":[" + FakeWeb.AssetJson("EldoUI-Updater.exe", Base + "EldoUI-Updater.exe", 11, null)
					+ "," + FakeWeb.AssetJson("SHA256SUMS.txt", Base + "SHA256SUMS.txt", new FileInfo(sums).Length, null) + "]}");
				var bare = Release.FromGitHub("{\"tag_name\":\"v9.0.0\",\"assets\":[" + FakeWeb.AssetJson("EldoUI-Updater.exe", Base + "EldoUI-Updater.exe", 11, null) + "]}");
				var engine = new UpdateEngine(web, t.Sub("tmp"), null);

				Assert.Equal(FakeWeb.Sha(exe), await engine.DownloadUpdaterAsync(withSums, t.Sub("a.exe"), CancellationToken.None));
				await Assert.ThrowsAsync<UnverifiedDownload>(() => engine.DownloadUpdaterAsync(bare, t.Sub("b.exe"), CancellationToken.None));
				Assert.False(File.Exists(t.Sub("b.exe")));
			}
		}
	}

	public class BootCheckTests
	{
		static readonly DateTime Now = new DateTime(2026, 10, 22, 7, 0, 0, DateTimeKind.Utc);

		[Fact]
		public void ActiveAtStartUntilACheckReachesGitHub()
		{
			var b = new BootCheck(Now);
			Assert.True(b.Active(Now));
			Assert.True(b.Active(Now.AddMinutes(3)));
			b.Finish();
			Assert.False(b.Active(Now.AddMinutes(3)));
		}

		[Fact]
		public void EndsAfterTenMinutesWithoutNetwork()
		{
			var b = new BootCheck(Now);
			Assert.True(b.Active(Now.AddMinutes(9)));
			Assert.False(b.Active(Now.AddMinutes(10)));
		}
	}
}
