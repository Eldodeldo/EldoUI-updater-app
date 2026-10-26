// Finds an addon's newest release at its publisher. Every publisher's answer becomes the same AddonRelease,
// which says what it is, whether it is for Forever and where it comes from. Only a GitHub release with a
// published SHA-256 is ever installed; WoWInterface and TukUI publish none, so their releases are only shown.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EldoUI.Updater.Core
{
	public sealed class AddonRelease
	{
		/// <summary>"github", "wowinterface" or "tukui".</summary>
		public string Provider;
		/// <summary>The addon at that publisher: "owner/repo", a WoWInterface number or a TukUI name.</summary>
		public string SourceId;
		/// <summary>The version as the publisher names it, and the GitHub tag (the exact release).</summary>
		public string Version, Tag;
		public bool PreRelease;
		/// <summary>True when the publisher's own data names a Forever build, false when it names none, null when it does not say.</summary>
		public bool? Forever;
		public string FileName, Url, PageUrl;
		/// <summary>The folders the catalog says it installs.</summary>
		public string[] Folders = new string[0];
		/// <summary>The exact GitHub release and file: what is downloaded is what was looked at.</summary>
		public Release GitHubRelease;
		public ReleaseAsset Asset;
	}

	public static class AddonSources
	{
		public const string WowInterfaceApi = "https://api.mmoui.com/v3/game/WOW/filedetails/";
		public const string TukuiApi = "https://api.tukui.org/v1/addon/";
		static readonly Regex Classic = new Regex(@"classic|vanilla|bcc|tbc|wrath|wotlk|cata|mists|mop|mainline|retail", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		/// <summary>
		/// The newest GitHub release of a catalog addon and its Forever file. The publisher's release.json decides when
		/// there is one (and must name a Forever build); otherwise the one zip, or the zip the catalog or its name points
		/// at. A release with no zip, or several with nothing to tell them apart, is refused: the source archive GitHub
		/// makes of every tag is never installed.
		/// </summary>
		public static async Task<AddonRelease> GitHubAsync(UpdateEngine engine, CatalogEntry entry, bool preRelease, string work, CancellationToken ct)
		{
			var addon = entry.Package;
			var release = await engine.NewestAsync(addon.Repository, preRelease, ct).ConfigureAwait(false);
			if (release == null || string.IsNullOrEmpty(release.Tag)) throw new InvalidDataException("No release is available.");
			var result = new AddonRelease
			{
				Provider = "github", SourceId = addon.Repository, Tag = release.Tag, Version = release.Version ?? release.Tag,
				PreRelease = release.PreRelease, PageUrl = release.PageUrl, Folders = entry.Folders, GitHubRelease = release,
			};
			string root = "https://github.com/" + addon.Repository + "/releases/download/" + Uri.EscapeDataString(release.Tag) + "/";
			Func<ReleaseAsset, bool> exact = a => a.Name != null && string.Equals(a.Url, root + Uri.EscapeDataString(a.Name), StringComparison.Ordinal);
			var assets = release.Assets.Where(exact).ToList();
			var manifest = assets.FirstOrDefault(a => a.Name == "release.json");
			if (manifest != null)
			{
				string path = Path.Combine(work, "release.json");
				await engine.DownloadCheckedAsync(release, manifest, path, 1024 * 1024, ct).ConfigureAwait(false);
				string filename = CompanionUpdater.ForeverFilename(File.ReadAllText(path), !addon.NeedsManifest);
				result.Asset = assets.FirstOrDefault(a => a.Name == filename);
				result.Forever = true;
				if (result.Asset == null || !filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The Forever package is missing.");
			}
			else if (addon.NeedsManifest) throw new InvalidDataException("Publisher compatibility metadata is missing.");
			else result.Asset = PickZip(assets, entry.AssetHint);
			result.FileName = result.Asset.Name;
			result.Url = result.Asset.Url;
			return result;
		}

		/// <summary>The zip to install from a release without a release.json, or an error when none can be told apart.</summary>
		public static ReleaseAsset PickZip(IList<ReleaseAsset> assets, string hint)
		{
			var zips = assets.Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && a.Name.IndexOf("nolib", StringComparison.OrdinalIgnoreCase) < 0).ToList();
			if (zips.Count == 0) throw new InvalidDataException("The release has no addon package.");
			Func<IEnumerable<ReleaseAsset>, ReleaseAsset> one = list => { var l = list.Take(2).ToList(); return l.Count == 1 ? l[0] : null; };
			var pick = (string.IsNullOrEmpty(hint) ? null : one(zips.Where(a => a.Name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)))
				?? one(zips.Where(a => Regex.IsMatch(a.Name, "forever|camelot", RegexOptions.IgnoreCase)))
				?? one(zips)
				?? one(zips.Where(a => !Classic.IsMatch(a.Name)));
			if (pick == null) throw new InvalidDataException("The release has several packages and none is marked for Forever.");
			return pick;
		}

		/// <summary>The newest version at WoWInterface or TukUI. These publish no SHA-256, so the result is never installed.</summary>
		public static async Task<AddonRelease> ResolveAsync(IWeb web, CatalogEntry entry, CancellationToken ct)
		{
			if (entry.Source == AddonSource.WowInterface)
			{
				string body = await web.GetApiAsync(WowInterfaceApi + entry.WowInterface + ".json", ct).ConfigureAwait(false);
				return FromWowInterface(body, entry);
			}
			if (entry.Source == AddonSource.Tukui)
			{
				string body = await web.GetApiAsync(TukuiApi + Uri.EscapeDataString(entry.Tukui), ct).ConfigureAwait(false);
				return FromTukui(body, entry);
			}
			throw new InvalidOperationException("Not a WoWInterface or TukUI addon.");
		}

		/// <summary>WoWInterface's file details: a list with one entry (UIVersion, UIFileName, UICompatibility).</summary>
		public static AddonRelease FromWowInterface(string json, CatalogEntry entry)
		{
			var list = Json.Parse(json) as List<object>;
			var o = list == null ? null : list.FirstOrDefault() as Dictionary<string, object>;
			if (o == null || string.IsNullOrWhiteSpace(o.Str("UIVersion"))) throw new FormatException("WoWInterface sent no version.");
			var compat = o.Arr("UICompatibility");
			return new AddonRelease
			{
				Provider = "wowinterface", SourceId = entry.WowInterface.ToString(System.Globalization.CultureInfo.InvariantCulture),
				Version = Short(o.Str("UIVersion")), FileName = o.Str("UIFileName"), PageUrl = entry.PageUrl, Folders = entry.Folders,
				Forever = compat == null ? (bool?)null : compat.OfType<Dictionary<string, object>>().Any(c => (c.Str("version") ?? "").StartsWith("1.60", StringComparison.Ordinal)),
			};
		}

		/// <summary>TukUI's addon answer: version and the game patches it supports.</summary>
		public static AddonRelease FromTukui(string json, CatalogEntry entry)
		{
			var o = Json.ParseObject(json);
			if (string.IsNullOrWhiteSpace(o.Str("version"))) throw new FormatException("TukUI sent no version.");
			var patches = o.Arr("patch");
			return new AddonRelease
			{
				Provider = "tukui", SourceId = entry.Tukui, Version = Short(o.Str("version")), PageUrl = entry.PageUrl, Folders = entry.Folders,
				Forever = patches == null ? (bool?)null : patches.OfType<string>().Any(p => p.StartsWith("1.60", StringComparison.Ordinal)),
			};
		}

		static string Short(string value)
		{
			value = Toc.CleanTitle(value);
			return value.Length > 40 ? value.Substring(0, 40) : value;
		}
	}
}
