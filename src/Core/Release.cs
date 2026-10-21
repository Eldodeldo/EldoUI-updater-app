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
			var o = Json.ParseObject(json);
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

		/// <summary>Only HTTPS downloads from github.com are followed.</summary>
		public static bool IsGitHubDownload(string url)
		{
			Uri u;
			return Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps
				&& string.Equals(u.Host, "github.com", StringComparison.OrdinalIgnoreCase);
		}

		static string DigestHex(string digest)
		{
			if (string.IsNullOrEmpty(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
			string hex = digest.Substring(7).ToLowerInvariant();
			return Regex.IsMatch(hex, "^[0-9a-f]{64}$") ? hex : null;
		}
	}
}
