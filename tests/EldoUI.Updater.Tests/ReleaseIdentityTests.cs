using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EldoUI.Updater.Core;
using Xunit;

namespace EldoUI.Updater.Tests
{
	public class ReleaseIdentityTests
	{
		const string Releases = "https://api.github.com/repos/Eldodeldo/EldoUI/releases?per_page=10";

		sealed class MovingWeb : IWeb
		{
			public string Body;
			public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
			public Action DuringDownload;
			public int Reads, Downloads;
			public Task<string> GetApiAsync(string url, CancellationToken ct)
			{
				Assert.Equal(Releases, url);
				Reads++;
				return Task.FromResult(Body);
			}
			public Task<string> DownloadAsync(string url, string target, long maxBytes, CancellationToken ct)
			{
				Downloads++;
				DuringDownload?.Invoke();
				File.Copy(Files[url], target, true);
				return Task.FromResult(FakeWeb.Sha(target));
			}
		}

		static string Release(MovingWeb web, TempDir temp, string version, string contentVersion = null,
			string assetVersion = null, string urlVersion = null, string notes = "Release notes")
		{
			string zip = contentVersion == ""
				? Zips.Raw(temp.Path, ("EldoUI/EldoUI.toc", "## Interface: 16001\n"))
				: Zips.Make(temp.Path, contentVersion ?? version, "EldoUI", "EldoUI_LootData");
			string name = "EldoUI-" + (assetVersion ?? version) + ".zip";
			string url = "https://github.com/Eldodeldo/EldoUI/releases/download/v" + (urlVersion ?? version) + "/" + name;
			web.Files[url] = zip;
			return JsonSerializer.Serialize(new
			{
				tag_name = "v" + version, name = "EldoUI " + version,
				draft = false, prerelease = false, body = notes,
				html_url = "https://github.com/Eldodeldo/EldoUI/releases/tag/v" + version,
				published_at = "2026-10-24T12:00:00Z",
				assets = new[] { new { name, browser_download_url = url, size = new FileInfo(zip).Length, digest = "sha256:" + FakeWeb.Sha(zip) } }
			});
		}

		static Settings Settings(TempDir temp)
		{
			string folder = temp.Sub("WoW", "_forever_", "Interface", "AddOns");
			Directory.CreateDirectory(folder);
			return new Settings { AddOnsDir = folder, AutoInstall = true };
		}

		[Theory]
		[InlineData("0.7.0002", null)]
		[InlineData(null, "0.7.0002")]
		public async Task DoesNotAnnounceOrInstallAnotherReleasesAsset(string assetVersion, string urlVersion)
		{
			using (var temp = new TempDir())
			{
				var web = new MovingWeb();
				web.Body = "[" + Release(web, temp, "0.7.0003", assetVersion: assetVersion, urlVersion: urlVersion) + "]";
				var settings = Settings(temp);
				var result = await new UpdateEngine(web, temp.Sub("tmp"), null).CheckAsync(settings, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.NoDownload, result.Outcome);
				Assert.Null(result.Release);
				Assert.Equal(0, web.Downloads);
				Assert.False(Directory.Exists(Path.Combine(settings.AddOnsDir, "EldoUI")));
			}
		}

		[Theory]
		[InlineData("0.7.0002")]
		[InlineData("0.7.0004")]
		[InlineData("")]
		public async Task RejectsVerifiedZipWithAnotherVersionBeforeReplacingTheInstall(string contentVersion)
		{
			using (var temp = new TempDir())
			{
				var web = new MovingWeb();
				web.Body = "[" + Release(web, temp, "0.7.0003", contentVersion) + "]";
				var settings = Settings(temp);
				settings.InstalledFolders = AddonInstaller.Install(Zips.Make(temp.Path, "0.7.0001", "EldoUI"), settings.AddOnsDir, null).Folders;
				string oldToc = File.ReadAllText(Path.Combine(settings.AddOnsDir, "EldoUI", "EldoUI.toc"));
				var result = await new UpdateEngine(web, temp.Sub("tmp"), null).CheckAsync(settings, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Failed, result.Outcome);
				Assert.Contains("version", result.Error, StringComparison.OrdinalIgnoreCase);
				Assert.Equal(oldToc, File.ReadAllText(Path.Combine(settings.AddOnsDir, "EldoUI", "EldoUI.toc")));
				Assert.Equal(new[] { "EldoUI" }, settings.InstalledFolders);
				Assert.False(Directory.Exists(Path.Combine(settings.AddOnsDir, "EldoUI_LootData")));
				Assert.Empty(Directory.GetFiles(temp.Sub("tmp")));
			}
		}

		[Fact]
		public async Task FallsBackToAnOlderReleaseWithAMatchingAsset()
		{
			using (var temp = new TempDir())
			{
				var web = new MovingWeb();
				string wrong = Release(web, temp, "0.7.0003", assetVersion: "0.7.0002");
				string older = Release(web, temp, "0.7.0002");
				web.Body = "[" + wrong + "," + older + "]";
				var result = await new UpdateEngine(web, temp.Sub("tmp"), null).CheckAsync(Settings(temp), false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Installed, result.Outcome);
				Assert.Equal("0.7.0002", result.Latest);
				Assert.Equal("0.7.0002", result.Installed);
				Assert.EndsWith("/v0.7.0002", result.Release.PageUrl);
				Assert.Equal(1, web.Downloads);
			}
		}

		[Fact]
		public async Task KeepsOneReleaseDuringDownloadThenFollowsLaterReleasesAndEditedNotes()
		{
			using (var temp = new TempDir())
			{
				var web = new MovingWeb();
				string first = Release(web, temp, "0.7.0003", notes: "First release");
				string next = Release(web, temp, "0.7.0010", notes: "Next release");
				web.Body = "[" + first + "]";
				web.DuringDownload = () => web.Body = "[" + next + "]";
				var engine = new UpdateEngine(web, temp.Sub("tmp"), null);
				var settings = Settings(temp);
				var result = await engine.CheckAsync(settings, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.Installed, result.Outcome);
				Assert.Equal("0.7.0003", result.Installed);
				Assert.Equal("0.7.0003", result.Latest);
				Assert.Equal("First release", result.Release.Notes);
				Assert.EndsWith("/v0.7.0003", result.Release.PageUrl);
				Assert.Equal(1, web.Reads);
				web.DuringDownload = null;
				result = await engine.CheckAsync(settings, false, CancellationToken.None);
				Assert.Equal("0.7.0010", result.Installed);
				Assert.Equal("Next release", result.Release.Notes);
				Assert.Equal(2, web.Downloads);
				web.Body = "[" + next.Replace("Next release", "Corrected release notes") + "]";
				result = await engine.CheckAsync(settings, false, CancellationToken.None);
				Assert.Equal(CheckOutcome.UpToDate, result.Outcome);
				Assert.Equal("Corrected release notes", result.Release.Notes);
				Assert.Equal(2, web.Downloads);
			}
		}
	}
}
