// The newest public release of a GitHub repository, read from its public API.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EldoUI.Updater.Core
{
	public sealed class ReleaseAsset
	{
		public string Name;
		public string Url;
		public long Size;
		/// <summary>Lower-case hex SHA-256 that GitHub computed for the uploaded file, or null when GitHub has none.</summary>
		public string Sha256;
	}

	public sealed class Release
	{
		public string Tag;
		public string Version;
		public string Name;
		public string PageUrl;
		public DateTime? Published;
		public string Notes;
		public List<ReleaseAsset> Assets = new List<ReleaseAsset>();

		/// <summary>Reads GitHub's "get the latest release" answer. Drafts and pre-releases are refused.</summary>
		public static Release FromGitHub(string json)
		{
			return FromObject(Json.ParseObject(json));
		}

		/// <summary>Reads GitHub's "list releases" answer: the published, non-test releases, newest first as GitHub sends them.</summary>
		public static List<Release> ListFromGitHub(string json)
		{
			var list = Json.Parse(json) as List<object>;
			if (list == null) throw new FormatException("Expected a JSON list of releases.");
			var result = new List<Release>();
			foreach (var item in list)
			{
				var o = item as Dictionary<string, object>;
				var r = o == null ? null : FromObject(o);
				if (r != null && r.Version != null) result.Add(r);
			}
			return result;
		}

		static Release FromObject(Dictionary<string, object> o)
		{
			if (o.Bool("draft") || o.Bool("prerelease")) return null;
			var r = new Release
			{
				Tag = o.Str("tag_name"),
				Name = o.Str("name"),
				PageUrl = o.Str("html_url"),
				Notes = o.Str("body"),
			};
			r.Version = Versions.Clean(r.Tag);
			DateTime published;
			if (DateTime.TryParse(o.Str("published_at"), System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out published))
				r.Published = published;
			var assets = o.Arr("assets");
			if (assets != null)
			{
				foreach (var item in assets)
				{
					var a = item as Dictionary<string, object>;
					if (a == null) continue;
					string url = a.Str("browser_download_url");
					if (!IsGitHubDownload(url)) continue;
					r.Assets.Add(new ReleaseAsset
					{
						Name = a.Str("name"),
						Url = url,
						Size = (long)a.Num("size"),
						Sha256 = DigestHex(a.Str("digest")),
					});
				}
			}
			return r;
		}

		/// <summary>The first asset whose name matches the pattern (case-insensitive), or null.</summary>
		public ReleaseAsset Find(string pattern)
		{
			var re = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			foreach (var a in Assets)
				if (a.Name != null && re.IsMatch(a.Name)) return a;
			return null;
		}

		/// <summary>The only pages the app opens in the browser: HTTPS pages on github.com.</summary>
		public static bool IsGitHubPage(string url)
		{
			return IsGitHubDownload(url);
		}

		/// <summary>Only HTTPS downloads from github.com are followed.</summary>
		public static bool IsGitHubDownload(string url)
		{
			Uri u;
			return Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps
				&& string.Equals(u.Host, "github.com", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// A checksum file published with a release for the named file: "&lt;name&gt;.sha256", or a "SHA256SUMS" list.
		/// The file itself is never its own checksum file.
		/// </summary>
		public static bool IsChecksumFileFor(ReleaseAsset sums, ReleaseAsset file)
		{
			if (sums == null || file == null || sums == file || sums.Name == null || file.Name == null) return false;
			return string.Equals(sums.Name, file.Name + ".sha256", StringComparison.OrdinalIgnoreCase)
				|| Regex.IsMatch(sums.Name, @"^SHA256SUMS(\.txt)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		}

		static string DigestHex(string digest)
		{
			if (string.IsNullOrEmpty(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
			string hex = digest.Substring(7).ToLowerInvariant();
			return Regex.IsMatch(hex, "^[0-9a-f]{64}$") ? hex : null;
		}
	}

	/// <summary>Reads checksum files in the usual "sha256sum" form: one "&lt;hash&gt;  &lt;file name&gt;" per line.</summary>
	public static class Checksums
	{
		static readonly Regex Line = new Regex(@"^([0-9A-Fa-f]{64})(?:\s+\*?(.+))?$", RegexOptions.CultureInvariant);

		/// <summary>
		/// The lower-case SHA-256 the text lists for fileName, or null. A line with a hash alone counts only when
		/// bareHashAllowed (a "&lt;name&gt;.sha256" file that holds one hash). Two different hashes for one name count as none.
		/// </summary>
		public static string Find(string text, string fileName, bool bareHashAllowed = false)
		{
			if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fileName)) return null;
			string found = null;
			foreach (string raw in text.Split('\n'))
			{
				var m = Line.Match(raw.Trim());
				if (!m.Success) continue;
				string name = m.Groups[2].Success ? m.Groups[2].Value.Trim() : null;
				if (name != null)
				{
					// "./EldoUI-1.0.zip" and "dist/EldoUI-1.0.zip" name the same file.
					int slash = name.LastIndexOfAny(new[] { '/', '\\' });
					if (slash >= 0) name = name.Substring(slash + 1);
				}
				bool match = name != null ? string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase) : bareHashAllowed;
				if (!match) continue;
				string hex = m.Groups[1].Value.ToLowerInvariant();
				if (found != null && found != hex) return null;
				found = hex;
			}
			return found;
		}
	}
}
