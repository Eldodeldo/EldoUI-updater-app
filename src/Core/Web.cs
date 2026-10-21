// HTTPS to GitHub's public API and release downloads. No account and no token: only public data.
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace EldoUI.Updater.Core
{
	public interface IWeb
	{
		/// <summary>The body of a GitHub API answer, or null when it has not changed since the last call (ETag).</summary>
		Task<string> GetApiAsync(string url, CancellationToken ct);
		/// <summary>Downloads a file and returns its lower-case hex SHA-256.</summary>
		Task<string> DownloadAsync(string url, string targetPath, long maxBytes, CancellationToken ct);
	}

	public sealed class Web : IWeb, IDisposable
	{
		readonly HttpClient _http;
		readonly System.Collections.Generic.Dictionary<string, Tuple<string, string>> _etags =
			new System.Collections.Generic.Dictionary<string, Tuple<string, string>>();

		public Web(string userAgent)
		{
			// .NET Framework picks the TLS versions Windows offers; make sure TLS 1.2 is among them.
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			var handler = new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
			_http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
			_http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
		}

		public async Task<string> GetApiAsync(string url, CancellationToken ct)
		{
			using (var req = new HttpRequestMessage(HttpMethod.Get, url))
			{
				req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
				req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
				Tuple<string, string> cached;
				lock (_etags) _etags.TryGetValue(url, out cached);
				// An unchanged answer (304) does not count against GitHub's hourly limit.
				if (cached != null) req.Headers.TryAddWithoutValidation("If-None-Match", cached.Item1);
				using (var res = await _http.SendAsync(req, ct).ConfigureAwait(false))
				{
					if (res.StatusCode == HttpStatusCode.NotModified && cached != null) return cached.Item2;
					if (res.StatusCode == HttpStatusCode.NotFound) throw new WebFailure(WebFailureKind.NoRelease, "No release yet.");
					if ((int)res.StatusCode == 403 || (int)res.StatusCode == 429) throw new WebFailure(WebFailureKind.RateLimited, "GitHub asks to wait a while.");
					if (!res.IsSuccessStatusCode) throw new WebFailure(WebFailureKind.Other, "GitHub answered " + (int)res.StatusCode + ".");
					string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
					var tag = res.Headers.ETag;
					if (tag != null)
						lock (_etags) _etags[url] = Tuple.Create(tag.ToString(), body);
					return body;
				}
			}
		}

		public async Task<string> DownloadAsync(string url, string targetPath, long maxBytes, CancellationToken ct)
		{
			if (!Release.IsGitHubDownload(url)) throw new WebFailure(WebFailureKind.Other, "Refused a download that is not from github.com.");
			using (var res = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
			{
				if (!res.IsSuccessStatusCode) throw new WebFailure(WebFailureKind.Other, "The download answered " + (int)res.StatusCode + ".");
				using (var src = await res.Content.ReadAsStreamAsync().ConfigureAwait(false))
				using (var dst = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
				using (var sha = SHA256.Create())
				{
					var buf = new byte[81920];
					long total = 0;
					int n;
					while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
					{
						total += n;
						if (total > maxBytes) throw new WebFailure(WebFailureKind.Other, "The download is larger than expected.");
						sha.TransformBlock(buf, 0, n, null, 0);
						await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
					}
					sha.TransformFinalBlock(new byte[0], 0, 0);
					return Hex(sha.Hash);
				}
			}
		}

		public static string Hex(byte[] bytes)
		{
			var sb = new System.Text.StringBuilder(bytes.Length * 2);
			foreach (byte b in bytes) sb.Append(b.ToString("x2"));
			return sb.ToString();
		}

		public void Dispose() { _http.Dispose(); }
	}

	public enum WebFailureKind { Offline, NoRelease, RateLimited, Other }

	public sealed class WebFailure : Exception
	{
		public readonly WebFailureKind Kind;
		public WebFailure(WebFailureKind kind, string message) : base(message) { Kind = kind; }
	}
}
