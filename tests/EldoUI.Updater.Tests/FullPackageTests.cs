using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EldoUI.Updater.Core;
using Xunit;
using Xunit.Abstractions;

namespace EldoUI.Updater.Tests
{
	/// <summary>
	/// The complete EldoUI package as 0.7.1000 ships it: the main addon and five companion addons that load only
	/// when needed, each in its own top folder with its own TOC and its own version.
	/// </summary>
	static class FullPackage
	{
		public const string Main = "EldoUI";

		/// <summary>The companion folders of the standard package. Replace this list when the published package changes.</summary>
		public static readonly string[] Companions = { "EldoUI_Emoji", "EldoUI_GuideData", "EldoUI_Locale_daDK", "EldoUI_LootData", "EldoUI_LootSim" };

		/// <summary>The developer companion. It is never part of the standard package.</summary>
		public const string Developer = "EldoUI_Dev";

		public static IEnumerable<string> Folders { get { return new[] { Main }.Concat(Companions); } }

		/// <summary>
		/// The version each companion TOC carries for a given main version. Data and media companions keep their own
		/// version; the language and loot preview companions move with the main addon.
		/// </summary>
		public static string CompanionVersion(string folder, string mainVersion)
		{
			switch (folder)
			{
				case "EldoUI_LootData": return "1.7.0";
				case "EldoUI_GuideData": return "1.0.0";
				case "EldoUI_Emoji": return "1.0.0";
				default: return mainVersion;
			}
		}

		public static string MainToc(string version)
		{
			return "## Interface: 16001\n## Title: EldoUI\n## Version: " + version + "\n\nCore\\init.lua\n";
		}

		public static string CompanionToc(string folder, string version)
		{
			return "## Interface: 16001\n## Title: " + folder.Replace('_', ' ') + "\n## Version: " + version
				+ "\n## Dependencies: EldoUI\n## LoadOnDemand: 1\n\ndata.lua\n";
		}

		/// <summary>
		/// A package zip. change can drop, rename or add entries: it gets the planned entries (name to text) and edits
		/// them before the zip is written.
		/// </summary>
		public static string Make(string dir, string mainVersion, Action<SortedDictionary<string, string>> change = null)
		{
			var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
			entries[Main + "/" + Main + ".toc"] = MainToc(mainVersion);
			entries[Main + "/Core/init.lua"] = "-- EldoUI " + mainVersion;
			entries[Main + "/Libs/LibStub/LibStub.lua"] = "-- LibStub";
			entries[Main + "/LICENSE"] = "license";
			foreach (string c in Companions)
			{
				string v = CompanionVersion(c, mainVersion);
				entries[c + "/" + c + ".toc"] = CompanionToc(c, v);
				entries[c + "/data.lua"] = "-- " + c + " " + v;
				entries[c + "/LICENSE"] = "license";
			}
			change?.Invoke(entries);
			string path = Path.Combine(dir, "EldoUI-" + mainVersion + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
			using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
				foreach (var e in entries)
					using (var w = new StreamWriter(z.CreateEntry(e.Key).Open(), new UTF8Encoding(false)))
						w.Write(e.Value);
			return path;
		}
	}

	/// <summary>
	/// A WoW: Forever game folder with saved settings in WTF, the player's in-game addon choices, other addons and a
	/// developer companion the player placed by hand.
	/// </summary>
	sealed class GameFolder
	{
		public readonly string Root, AddOns, Wtf;

		public GameFolder(TempDir t)
		{
			Root = t.Sub("World of Warcraft", "_forever_");
			AddOns = Path.Combine(Root, "Interface", "AddOns");
			Wtf = Path.Combine(Root, "WTF");
			Directory.CreateDirectory(AddOns);
			string account = Path.Combine(Wtf, "Account", "PLAYER");
			string character = Path.Combine(account, "Realm", "Hero");
			Write(Path.Combine(Wtf, "Config.wtf"), "SET gxWindow \"1\"\n");
			Write(Path.Combine(account, "SavedVariables", "EldoUI.lua"), "EldoUIDB = { profile = \"Main\" }\n");
			Write(Path.Combine(account, "SavedVariables", "EldoUI_LootData.lua"), "EldoUILootDataDB = { seen = 3 }\n");
			Write(Path.Combine(account, "SavedVariables", "OtherAddon.lua"), "OtherAddonDB = {}\n");
			Write(Path.Combine(character, "SavedVariables", "EldoUI.lua"), "EldoUICharDB = { bars = 4 }\n");
			// The player's in-game enable/disable choices stay theirs.
			Write(Path.Combine(character, "AddOns.txt"), "EldoUI: enabled\nEldoUI_LootSim: disabled\nEldoUI_Emoji: disabled\nOtherAddon: enabled\n");
			Write(Path.Combine(AddOns, "OtherAddon", "OtherAddon.toc"), "## Interface: 16001\n## Version: 2.1\n");
			Write(Path.Combine(AddOns, "OtherAddon", "OtherAddon.lua"), "-- other");
			Write(Path.Combine(AddOns, FullPackage.Developer, FullPackage.Developer + ".toc"), "## Interface: 16001\n## Version: 0.7.0003\n## LoadOnDemand: 1\n");
		}

		/// <summary>The released 0.7.0003 install, put there by the updater: what an upgrade to 0.7.1000 starts from.</summary>
		public Settings Installed0003(TempDir t, params string[] extraManaged)
		{
			var settings = new Settings { AddOnsDir = AddOns, AutoInstall = true };
			string zip = FullPackage.Make(t.Path, "0.7.0003", e =>
			{
				foreach (string f in extraManaged) e[f + "/" + f + ".toc"] = "## Interface: 16001\n## Version: 0.7.0003\n";
			});
			settings.InstalledFolders = AddonInstaller.Install(zip, AddOns, null, "0.7.0003").Folders;
			// A file the old version had and the new one does not.
			Write(Path.Combine(AddOns, "EldoUI", "Modules", "Retired.lua"), "-- retired");
			return settings;
		}

		/// <summary>Every file below dir, by relative path, with the SHA-256 of its content. Folders end with "/".</summary>
		public static SortedDictionary<string, string> Snapshot(string dir)
		{
			var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
			if (!Directory.Exists(dir)) return result;
			foreach (string d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories))
				result[Path.GetRelativePath(dir, d).Replace('\\', '/') + "/"] = "";
			foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
				result[Path.GetRelativePath(dir, f).Replace('\\', '/')] = FakeWeb.Sha(f);
			return result;
		}

		/// <summary>Leftover work folders of an install (they sit next to AddOns).</summary>
		public string[] WorkFolders()
		{
			return Directory.GetDirectories(Path.GetDirectoryName(AddOns), ".eldoui-updater-*");
		}

		static void Write(string path, string text)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, text);
		}
	}

	public class FullPackageTests
	{
		const string Releases = "https://api.github.com/repos/Eldodeldo/EldoUI/releases?per_page=10";

		/// <summary>How the served release vouches for its zip.</summary>
		public enum Proof { Digest, ChecksumFile, WrongDigest, WrongChecksumFile, None }

		/// <summary>Serves zip as the release's EldoUI-&lt;version&gt;.zip, vouched for as proof says.</summary>
		static FakeWeb Serve(TempDir t, string version, string zip, Proof proof = Proof.Digest)
		{
			var web = new FakeWeb();
			string baseUrl = "https://github.com/Eldodeldo/EldoUI/releases/download/v" + version + "/";
			string name = "EldoUI-" + version + ".zip";
			web.Files[baseUrl + name] = zip;
			string sha = FakeWeb.Sha(zip);
			string digest = proof == Proof.Digest ? sha : proof == Proof.WrongDigest ? new string('0', 64) : null;
			var more = new List<string>();
			if (proof == Proof.ChecksumFile || proof == Proof.WrongChecksumFile)
			{
				string sums = Path.Combine(t.Path, "SHA256SUMS-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".txt");
				File.WriteAllText(sums, (proof == Proof.ChecksumFile ? sha : new string('0', 64)) + "  " + name + "\n");
				web.Files[baseUrl + "SHA256SUMS.txt"] = sums;
				more.Add(FakeWeb.AssetJson("SHA256SUMS.txt", baseUrl + "SHA256SUMS.txt", new FileInfo(sums).Length, null));
			}
			web.Api[Releases] = "[" + FakeWeb.ReleaseJson(version, baseUrl + name, new FileInfo(zip).Length, digest, more.ToArray()) + "]";
			return web;
		}

		/// <summary>The files a check fetches: the checksum file first when there is one, then the zip; none without a proof.</summary>
		static int Downloads(Proof proof)
		{
			return proof == Proof.None ? 0 : proof == Proof.ChecksumFile || proof == Proof.WrongChecksumFile ? 2 : 1;
		}

		static Task<CheckResult> Check(TempDir t, FakeWeb web, Settings settings)
		{
			return new UpdateEngine(web, t.Sub("tmp"), null).CheckAsync(settings, false, CancellationToken.None);
		}

		[Theory]
		[InlineData(Proof.Digest)]
		[InlineData(Proof.ChecksumFile)]
		public async Task InstallsTheMainAddonAndAllFiveCompanionsFromOneVerifiedArchive(Proof proof)
		{
			using (var t = new TempDir())
			{
				var game = new GameFolder(t);
				var wtf = GameFolder.Snapshot(game.Wtf);
				var other = GameFolder.Snapshot(Path.Combine(game.AddOns, "OtherAddon"));
				var dev = GameFolder.Snapshot(Path.Combine(game.AddOns, FullPackage.Developer));
				var settings = new Settings { AddOnsDir = game.AddOns, AutoInstall = true };
				var web = Serve(t, "0.7.1000", FullPackage.Make(t.Path, "0.7.1000"), proof);

				var r = await Check(t, web, settings);

				Assert.Equal(CheckOutcome.Installed, r.Outcome);
				Assert.Equal(Downloads(proof), web.Downloads);
				Assert.Equal("0.7.1000", r.Installed);
				Assert.Equal("0.7.1000", AddonInstaller.InstalledVersion(game.AddOns));
				Assert.Equal(FullPackage.Folders.OrderBy(f => f, StringComparer.Ordinal), settings.InstalledFolders);
				Assert.DoesNotContain(FullPackage.Developer, settings.InstalledFolders);
				foreach (string c in FullPackage.Companions)
				{
					string toc = Path.Combine(game.AddOns, c, c + ".toc");
					Assert.True(File.Exists(toc), c + " has its TOC");
					Assert.Equal(FullPackage.CompanionVersion(c, "0.7.1000"), AddonInstaller.ReadTocVersion(toc));
					Assert.Equal(FullPackage.CompanionToc(c, FullPackage.CompanionVersion(c, "0.7.1000")), File.ReadAllText(toc));
				}
				Assert.Equal(wtf, GameFolder.Snapshot(game.Wtf));
				Assert.Equal(other, GameFolder.Snapshot(Path.Combine(game.AddOns, "OtherAddon")));
				Assert.Equal(dev, GameFolder.Snapshot(Path.Combine(game.AddOns, FullPackage.Developer)));
				Assert.Empty(game.WorkFolders());
				Assert.Empty(Directory.GetFiles(t.Sub("tmp")));
			}
		}

		/// <summary>A release whose zip or proof is wrong in one way.</summary>
		public static IEnumerable<object[]> Refusals()
		{
			// The proof does not match: nothing is unpacked.
			yield return new object[] { "wrong digest", Proof.WrongDigest, "", CheckOutcome.Unverified };
			yield return new object[] { "wrong checksum file", Proof.WrongChecksumFile, "", CheckOutcome.Unverified };
			yield return new object[] { "no checksum", Proof.None, "", CheckOutcome.Unverified };
			// The proof matches, but the content is wrong: refused before any installed folder moves.
			yield return new object[] { "traversal", Proof.Digest, "traversal", CheckOutcome.Failed };
			yield return new object[] { "companion traversal", Proof.Digest, "companion-traversal", CheckOutcome.Failed };
			yield return new object[] { "companion without its TOC", Proof.Digest, "companion-no-toc", CheckOutcome.Failed };
			yield return new object[] { "companion with a TOC of another name", Proof.Digest, "companion-renamed-toc", CheckOutcome.Failed };
			yield return new object[] { "main addon without its TOC", Proof.Digest, "main-no-toc", CheckOutcome.Failed };
			yield return new object[] { "older main version", Proof.Digest, "main-0.7.0003", CheckOutcome.Failed };
			yield return new object[] { "newer main version", Proof.Digest, "main-0.7.1001", CheckOutcome.Failed };
			yield return new object[] { "main TOC without a version", Proof.Digest, "main-no-version", CheckOutcome.Failed };
			yield return new object[] { "folder that is not EldoUI", Proof.Digest, "foreign-folder", CheckOutcome.Failed };
		}

		static string RefusedZip(TempDir t, string content)
		{
			switch (content)
			{
				case "traversal":
					return FullPackage.Make(t.Path, "0.7.1000", e => e["EldoUI/../../../WTF/Config.wtf"] = "SET hacked \"1\"\n");
				case "companion-traversal":
					return FullPackage.Make(t.Path, "0.7.1000", e => e["EldoUI_LootData/../../OtherAddon/OtherAddon.lua"] = "-- replaced");
				case "companion-no-toc":
					return FullPackage.Make(t.Path, "0.7.1000", e => e.Remove("EldoUI_LootSim/EldoUI_LootSim.toc"));
				case "companion-renamed-toc":
					return FullPackage.Make(t.Path, "0.7.1000", e =>
					{
						e["EldoUI_Locale_daDK/EldoUI_Locale.toc"] = e["EldoUI_Locale_daDK/EldoUI_Locale_daDK.toc"];
						e.Remove("EldoUI_Locale_daDK/EldoUI_Locale_daDK.toc");
					});
				case "main-no-toc":
					return FullPackage.Make(t.Path, "0.7.1000", e => e.Remove("EldoUI/EldoUI.toc"));
				case "main-0.7.0003":
					return FullPackage.Make(t.Path, "0.7.1000", e => e["EldoUI/EldoUI.toc"] = FullPackage.MainToc("0.7.0003"));
				case "main-0.7.1001":
					return FullPackage.Make(t.Path, "0.7.1000", e => e["EldoUI/EldoUI.toc"] = FullPackage.MainToc("0.7.1001"));
				case "main-no-version":
					return FullPackage.Make(t.Path, "0.7.1000", e => e["EldoUI/EldoUI.toc"] = "## Interface: 16001\n## Title: EldoUI\n");
				case "foreign-folder":
					return FullPackage.Make(t.Path, "0.7.1000", e => e["OtherAddon/OtherAddon.toc"] = "## Interface: 16001\n## Version: 9.9\n");
				default:
					return FullPackage.Make(t.Path, "0.7.1000");
			}
		}

		[Theory]
		[MemberData(nameof(Refusals))]
		public async Task RefusesABadReleaseBeforeChangingTheInstall(string why, Proof proof, string content, CheckOutcome expected)
		{
			using (var t = new TempDir())
			{
				var game = new GameFolder(t);
				var settings = game.Installed0003(t);
				var folders = settings.InstalledFolders.ToList();
				var addOns = GameFolder.Snapshot(game.AddOns);
				var wtf = GameFolder.Snapshot(game.Wtf);
				var web = Serve(t, "0.7.1000", RefusedZip(t, content), proof);

				var r = await Check(t, web, settings);

				Assert.True(expected == r.Outcome, why + ": " + r.Outcome + " " + r.Error);
				Assert.Equal(Downloads(proof), web.Downloads);
				Assert.Equal("0.7.0003", AddonInstaller.InstalledVersion(game.AddOns));
				Assert.Equal(folders, settings.InstalledFolders);
				Assert.Equal(addOns, GameFolder.Snapshot(game.AddOns));
				Assert.Equal(wtf, GameFolder.Snapshot(game.Wtf));
				Assert.Empty(game.WorkFolders());
				Assert.Empty(Directory.GetFiles(t.Sub("tmp")));
				Assert.False(File.Exists(Path.Combine(game.Root, "Config.wtf")));
			}
		}

		[Fact]
		public void CompanionVersionsNeedNotMatchTheMainVersion()
		{
			using (var t = new TempDir())
			{
				var game = new GameFolder(t);
				// Every companion on its own version, one without a version line at all.
				string zip = FullPackage.Make(t.Path, "0.7.1000", e =>
				{
					e["EldoUI_LootSim/EldoUI_LootSim.toc"] = FullPackage.CompanionToc("EldoUI_LootSim", "0.4.2");
					e["EldoUI_Locale_daDK/EldoUI_Locale_daDK.toc"] = "## Interface: 16001\n## Dependencies: EldoUI\n## LoadOnDemand: 1\n";
				});
				Assert.Equal(FullPackage.Folders.OrderBy(f => f, StringComparer.Ordinal), AddonInstaller.Inspect(zip));

				var result = AddonInstaller.Install(zip, game.AddOns, null, "0.7.1000");

				Assert.Equal("0.7.1000", result.Version);
				Assert.Equal("1.7.0", AddonInstaller.ReadTocVersion(Path.Combine(game.AddOns, "EldoUI_LootData", "EldoUI_LootData.toc")));
				Assert.Equal("1.0.0", AddonInstaller.ReadTocVersion(Path.Combine(game.AddOns, "EldoUI_GuideData", "EldoUI_GuideData.toc")));
				Assert.Equal("1.0.0", AddonInstaller.ReadTocVersion(Path.Combine(game.AddOns, "EldoUI_Emoji", "EldoUI_Emoji.toc")));
				Assert.Equal("0.4.2", AddonInstaller.ReadTocVersion(Path.Combine(game.AddOns, "EldoUI_LootSim", "EldoUI_LootSim.toc")));
				Assert.Null(AddonInstaller.ReadTocVersion(Path.Combine(game.AddOns, "EldoUI_Locale_daDK", "EldoUI_Locale_daDK.toc")));

				// The installed list shows EldoUI at its main version and the guide data at its own; companions
				// that load only when needed are not reported as missing anything.
				var rows = AddonScanner.Scan(game.AddOns, new Settings { AddOnsDir = game.AddOns, InstalledFolders = result.Folders });
				var eldo = rows.Single(r => r.State == AddonState.EldoUI);
				Assert.Equal("0.7.1000", eldo.Version);
				Assert.Empty(eldo.MissingDependencies);
				var guide = rows.Single(r => r.State == AddonState.GuideData);
				Assert.Equal("1.0.0", guide.Version);
				Assert.Empty(guide.MissingDependencies);
			}
		}

		[Fact]
		public async Task UpgradeReplacesManagedFoldersAndKeepsSettingsAndOtherAddons()
		{
			using (var t = new TempDir())
			{
				var game = new GameFolder(t);
				// 0.7.0003 as installed by the updater, plus a folder an earlier release had and 0.7.1000 drops.
				var settings = game.Installed0003(t, "EldoUI_Retired");
				Assert.Contains("EldoUI_Retired", settings.InstalledFolders);
				// A folder of the EldoUI_ family the updater never installed stays the player's.
				Directory.CreateDirectory(Path.Combine(game.AddOns, "EldoUI_Custom"));
				File.WriteAllText(Path.Combine(game.AddOns, "EldoUI_Custom", "EldoUI_Custom.toc"), "## Version: 1\n");
				var wtf = GameFolder.Snapshot(game.Wtf);
				var kept = new[] { "OtherAddon", "EldoUI_Custom", FullPackage.Developer }
					.ToDictionary(f => f, f => GameFolder.Snapshot(Path.Combine(game.AddOns, f)));
				var web = Serve(t, "0.7.1000", FullPackage.Make(t.Path, "0.7.1000"));

				var r = await Check(t, web, settings);

				Assert.Equal(CheckOutcome.Installed, r.Outcome);
				Assert.Equal(new[] { "EldoUI_Retired" }, r.Install.Removed);
				Assert.False(Directory.Exists(Path.Combine(game.AddOns, "EldoUI_Retired")));
				Assert.False(File.Exists(Path.Combine(game.AddOns, "EldoUI", "Modules", "Retired.lua")));
				Assert.Equal(FullPackage.Folders.OrderBy(f => f, StringComparer.Ordinal), settings.InstalledFolders);
				foreach (string f in FullPackage.Folders)
					Assert.True(File.Exists(Path.Combine(game.AddOns, f, f + ".toc")), f + " has its TOC");
				Assert.Equal("0.7.1000", AddonInstaller.InstalledVersion(game.AddOns));
				Assert.Equal("0.7.1000", AddonInstaller.ReadTocVersion(Path.Combine(game.AddOns, "EldoUI_LootSim", "EldoUI_LootSim.toc")));
				Assert.Equal("1.7.0", AddonInstaller.ReadTocVersion(Path.Combine(game.AddOns, "EldoUI_LootData", "EldoUI_LootData.toc")));
				Assert.Equal(wtf, GameFolder.Snapshot(game.Wtf));
				foreach (var k in kept) Assert.Equal(k.Value, GameFolder.Snapshot(Path.Combine(game.AddOns, k.Key)));
				Assert.Empty(game.WorkFolders());

				// The next check finds nothing to do and changes nothing.
				var after = GameFolder.Snapshot(game.AddOns);
				Assert.Equal(CheckOutcome.UpToDate, (await Check(t, web, settings)).Outcome);
				Assert.Equal(1, web.Downloads);
				Assert.Equal(after, GameFolder.Snapshot(game.AddOns));
			}
		}

		[Fact]
		public async Task AnInstallThatFailsPartwayPutsEveryOldFolderBack()
		{
			using (var t = new TempDir())
			{
				var game = new GameFolder(t);
				// 0.7.0003 without the loot preview companion, and a file where that folder will go: the first five
				// folders move in, then the last move fails.
				var settings = new Settings { AddOnsDir = game.AddOns, AutoInstall = true };
				string old = FullPackage.Make(t.Path, "0.7.0003", e =>
				{
					foreach (string k in e.Keys.Where(k => k.StartsWith("EldoUI_LootSim/", StringComparison.Ordinal)).ToList()) e.Remove(k);
				});
				settings.InstalledFolders = AddonInstaller.Install(old, game.AddOns, null, "0.7.0003").Folders;
				File.WriteAllText(Path.Combine(game.AddOns, "EldoUI_LootSim"), "in the way");
				var folders = settings.InstalledFolders.ToList();
				var addOns = GameFolder.Snapshot(game.AddOns);
				var wtf = GameFolder.Snapshot(game.Wtf);
				var web = Serve(t, "0.7.1000", FullPackage.Make(t.Path, "0.7.1000"));

				var r = await Check(t, web, settings);

				Assert.Equal(CheckOutcome.Failed, r.Outcome);
				Assert.Equal("0.7.0003", AddonInstaller.InstalledVersion(game.AddOns));
				Assert.Equal(folders, settings.InstalledFolders);
				Assert.Equal(addOns, GameFolder.Snapshot(game.AddOns));
				Assert.Equal(wtf, GameFolder.Snapshot(game.Wtf));
				Assert.Empty(game.WorkFolders());
				Assert.Empty(Directory.GetFiles(t.Sub("tmp")));

				// Once the file is gone, the next check installs the whole package.
				File.Delete(Path.Combine(game.AddOns, "EldoUI_LootSim"));
				Assert.Equal(CheckOutcome.Installed, (await Check(t, web, settings)).Outcome);
				Assert.Equal(FullPackage.Folders.OrderBy(f => f, StringComparer.Ordinal), settings.InstalledFolders);
				Assert.Equal(wtf, GameFolder.Snapshot(game.Wtf));
			}
		}

		[Fact]
		public void TheDeveloperCompanionIsNeitherInstalledNorRemoved()
		{
			using (var t = new TempDir())
			{
				var game = new GameFolder(t);
				var dev = GameFolder.Snapshot(Path.Combine(game.AddOns, FullPackage.Developer));
				var first = AddonInstaller.Install(FullPackage.Make(t.Path, "0.7.0003"), game.AddOns, null, "0.7.0003");
				var second = AddonInstaller.Install(FullPackage.Make(t.Path, "0.7.1000"), game.AddOns, first.Folders, "0.7.1000");
				Assert.DoesNotContain(FullPackage.Developer, first.Folders);
				Assert.DoesNotContain(FullPackage.Developer, second.Folders);
				Assert.Empty(second.Removed);
				Assert.Equal(dev, GameFolder.Snapshot(Path.Combine(game.AddOns, FullPackage.Developer)));
			}
		}
	}

	/// <summary>
	/// A published EldoUI release zip, checked end to end in a temporary game folder. Set ELDOUI_RELEASE_ZIP to the
	/// downloaded file, and ELDOUI_RELEASE_VERSION and ELDOUI_RELEASE_SHA256 to the release's version and published
	/// SHA-256; ELDOUI_PREVIOUS_ZIP, when set, is installed first to check the upgrade from it. Skipped when the
	/// first three are not set.
	/// </summary>
	public class ReleasePackageTests
	{
		readonly ITestOutputHelper _out;
		public ReleasePackageTests(ITestOutputHelper output) { _out = output; }

		public sealed class ReleaseZipFactAttribute : FactAttribute
		{
			public ReleaseZipFactAttribute()
			{
				string zip = Environment.GetEnvironmentVariable("ELDOUI_RELEASE_ZIP");
				if (string.IsNullOrEmpty(zip) || !File.Exists(zip)
					|| string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ELDOUI_RELEASE_VERSION"))
					|| string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ELDOUI_RELEASE_SHA256")))
					Skip = "Needs ELDOUI_RELEASE_ZIP, ELDOUI_RELEASE_VERSION and ELDOUI_RELEASE_SHA256";
			}
		}

		[ReleaseZipFact]
		public void TheReleaseZipInstallsAsTheStandardPackage()
		{
			string zip = Environment.GetEnvironmentVariable("ELDOUI_RELEASE_ZIP");
			string version = Versions.Clean(Environment.GetEnvironmentVariable("ELDOUI_RELEASE_VERSION"));
			string sha = FakeWeb.Sha(zip);
			_out.WriteLine(Path.GetFileName(zip) + "  version " + version + "  sha256 " + sha + "  " + new FileInfo(zip).Length + " bytes");
			Assert.Equal(Environment.GetEnvironmentVariable("ELDOUI_RELEASE_SHA256").Trim().ToLowerInvariant(), sha);
			Assert.Equal("EldoUI-" + version + ".zip", Path.GetFileName(zip));

			var folders = AddonInstaller.Inspect(zip);
			Assert.Equal(FullPackage.Folders.OrderBy(f => f, StringComparer.Ordinal), folders);
			Assert.DoesNotContain(FullPackage.Developer, folders);

			using (var t = new TempDir())
			{
				var game = new GameFolder(t);
				var wtf = GameFolder.Snapshot(game.Wtf);
				var other = GameFolder.Snapshot(Path.Combine(game.AddOns, "OtherAddon"));
				// With ELDOUI_PREVIOUS_ZIP, the previous release is installed first, so this is the upgrade a player gets.
				string previousZip = Environment.GetEnvironmentVariable("ELDOUI_PREVIOUS_ZIP");
				IEnumerable<string> previous = new[] { "EldoUI" };
				if (!string.IsNullOrEmpty(previousZip))
				{
					var before = AddonInstaller.Install(previousZip, game.AddOns, null);
					_out.WriteLine("upgrading from " + Path.GetFileName(previousZip) + " (EldoUI " + before.Version + ")");
					previous = before.Folders;
				}
				var result = AddonInstaller.Install(zip, game.AddOns, previous, version);
				Assert.Equal(version, result.Version);
				Assert.Equal(version, AddonInstaller.InstalledVersion(game.AddOns));
				Assert.Equal(folders, result.Folders);
				foreach (string c in FullPackage.Companions)
				{
					string toc = Path.Combine(game.AddOns, c, c + ".toc");
					string text = File.ReadAllText(toc);
					_out.WriteLine(c + "  version " + (AddonInstaller.ReadTocVersion(toc) ?? "(none)"));
					Assert.Matches(new Regex(@"(?im)^\s*##\s*LoadOnDemand\s*:\s*1\s*$"), text);
					Assert.Matches(new Regex(@"(?im)^\s*##\s*(Dependencies|RequiredDeps)\s*:.*\bEldoUI\b"), text);
				}
				Assert.Equal(wtf, GameFolder.Snapshot(game.Wtf));
				Assert.Equal(other, GameFolder.Snapshot(Path.Combine(game.AddOns, "OtherAddon")));
				Assert.Empty(game.WorkFolders());
			}
		}
	}
}
