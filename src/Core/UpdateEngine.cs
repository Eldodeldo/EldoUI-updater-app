// Checks for a new EldoUI release and installs it. The tray app calls this; nothing here shows UI.
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EldoUI.Updater.Core
{
	public enum CheckOutcome
	{
		/// <summary>No WoW: Forever AddOns folder is known yet.</summary>
		NoFolder,
		/// <summary>No EldoUI release has its zip file yet (a release can be published before its zip is attached).</summary>
		NoDownload,
		UpToDate,
		/// <summary>A newer EldoUI is out and waits for the player (automatic install is off).</summary>
		UpdateAvailable,
		Installed,
		/// <summary>
		/// The newest EldoUI could not be matched to a trusted SHA-256 (none published, or a different file arrived).
		/// Nothing was changed; the next check tries again.
		/// </summary>
		Unverified,
		Offline,
		Failed,
	}

	public sealed class CheckResult
	{
		public CheckOutcome Outcome;
		public string Installed;
		public string Latest;
		public Release Release;
		public string Error;
		public InstallResult Install;
		public System.Collections.Generic.List<CompanionResult> Companions;
	}

	public sealed class UpdateEngine
	{
		public const string AddonRepo = "Eldodeldo/EldoUI";
		public const string AddonAssetPattern = @"^EldoUI-v?[0-9][0-9.]*\.zip$";
		public const string UpdaterRepo = "Eldodeldo/EldoUI-updater-app";
		/// <summary>The updater's own file: the Windows exe, and the Linux AppImage in the same release.</summary>
		public const string UpdaterAssetPattern = @"^EldoUI-Updater\.exe$";
		public const string LinuxUpdaterAssetPattern = @"^EldoUI-Updater-x86_64\.AppImage$";
		const long MaxAddonZip = 200L * 1024 * 1024;
		const long MaxUpdaterFile = 200L * 1024 * 1024;
		const long MaxChecksumFile = 64L * 1024;

		readonly IWeb _web;
		readonly string _apiBase;
		readonly string _tempDir;
		readonly Action<string> _log;
		readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

		public UpdateEngine(IWeb web, string tempDir, Action<string> log, string apiBase = "https://api.github.com")
		{
			_web = web;
			_tempDir = tempDir;
			_log = log ?? (_ => { });
			_apiBase = apiBase.TrimEnd('/');
		}

		public bool Busy { get { return _gate.CurrentCount == 0; } }

		public async Task<Release> LatestAsync(string repo, CancellationToken ct)
		{
			string body = await _web.GetApiAsync(_apiBase + "/repos/" + repo + "/releases/latest", ct).ConfigureAwait(false);
			return Release.FromGitHub(body);
		}

		/// <summary>Looks for a new EldoUI and installs it when install is true (or settings say so).</summary>
		public async Task<CheckResult> CheckAsync(Settings settings, bool forceInstall, CancellationToken ct, bool installCompanions = false)
		{
			await _gate.WaitAsync(ct).ConfigureAwait(false);
			var r = new CheckResult();
			try
			{
				try
				{
					r.Installed = AddonInstaller.InstalledVersion(settings.AddOnsDir);
					// The newest release that has its zip: a release without one (still being uploaded, or
					// published without it) is skipped rather than reported as an error.
					string body = await _web.GetApiAsync(_apiBase + "/repos/" + AddonRepo + "/releases?per_page=10", ct).ConfigureAwait(false);
					var releases = Release.ListFromGitHub(body);
					settings.LastCheck = DateTime.UtcNow;
					r.Release = releases.FirstOrDefault(x => AddonAsset(x) != null);
					if (r.Release == null)
					{
						r.Outcome = CheckOutcome.NoDownload;
						r.Latest = releases.Count > 0 ? releases[0].Version : null;
						return r;
					}
					r.Latest = r.Release.Version;
					string interfaceDir = string.IsNullOrEmpty(settings.AddOnsDir) ? null
						: Path.GetDirectoryName(settings.AddOnsDir.TrimEnd('\\', '/'));
					bool hasInstallRoot = Directory.Exists(interfaceDir)
						|| (WowFolders.Rank(settings.AddOnsDir) != int.MaxValue
							&& Directory.Exists(Path.GetDirectoryName(interfaceDir ?? "")));
					if (!hasInstallRoot)
					{
						r.Outcome = CheckOutcome.NoFolder;
						return r;
					}
					if (r.Installed != null && !Versions.IsNewer(r.Latest, r.Installed))
					{
						r.Outcome = CheckOutcome.UpToDate;
						return r;
					}
					if (!forceInstall && !settings.AutoInstall)
					{
						r.Outcome = CheckOutcome.UpdateAvailable;
						return r;
					}
					r.Install = await InstallAsync(r.Release, settings, ct).ConfigureAwait(false);
					r.Installed = r.Install.Version ?? r.Latest;
					r.Outcome = CheckOutcome.Installed;
					return r;
				}
				catch (UnverifiedDownload ex)
				{
					r.Outcome = CheckOutcome.Unverified;
					r.Error = ex.Message;
				}
				catch (HttpRequestException ex)
				{
					r.Outcome = CheckOutcome.Offline;
					r.Error = ex.Message;
				}
				catch (TaskCanceledException) when (!ct.IsCancellationRequested)
				{
					r.Outcome = CheckOutcome.Offline;
					r.Error = "The connection timed out.";
				}
				catch (WebFailure ex)
				{
					r.Outcome = ex.Kind == WebFailureKind.Offline ? CheckOutcome.Offline : CheckOutcome.Failed;
					r.Error = ex.Message;
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is FormatException)
				{
					r.Outcome = CheckOutcome.Failed;
					r.Error = ex.Message;
				}
				if (r.Error != null) _log("Check failed: " + r.Error);
				return r;
			}
			finally
			{
				try
				{
					if (settings.ExtraAddons && WowFolders.Rank(settings.AddOnsDir) != int.MaxValue
						&& Directory.Exists(Path.GetDirectoryName(Path.GetDirectoryName(settings.AddOnsDir.TrimEnd('\\', '/'))))
						&& r.Outcome != CheckOutcome.NoFolder && r.Outcome != CheckOutcome.Failed
						&& r.Outcome != CheckOutcome.Offline && !ct.IsCancellationRequested)
						r.Companions = await new CompanionUpdater(_web, _tempDir).CheckAsync(settings, installCompanions || forceInstall || settings.AutoInstall, ct).ConfigureAwait(false);
				}
				finally { _gate.Release(); }
			}
		}

		// Match the selected release, not merely any EldoUI zip attached to it.
		static ReleaseAsset AddonAsset(Release release)
		{
			if (release == null || release.Version == null || release.Tag == null
				|| !System.Text.RegularExpressions.Regex.IsMatch(release.Version, @"\A[0-9]+(?:\.[0-9]+)+\z")) return null;
			string root = "https://github.com/" + AddonRepo + "/releases/download/" + Uri.EscapeDataString(release.Tag) + "/";
			return release.Assets.FirstOrDefault(a =>
				(string.Equals(a.Name, "EldoUI-" + release.Version + ".zip", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(a.Name, "EldoUI-v" + release.Version + ".zip", StringComparison.OrdinalIgnoreCase))
				&& string.Equals(a.Url, root + Uri.EscapeDataString(a.Name), StringComparison.Ordinal));
		}

		async Task<InstallResult> InstallAsync(Release release, Settings settings, CancellationToken ct)
		{
			var asset = AddonAsset(release);
			if (asset == null) throw new InvalidDataException("The release has no matching EldoUI download.");
			Directory.CreateDirectory(_tempDir);
			string zip = Path.Combine(_tempDir, "EldoUI-" + release.Version + ".zip");
			try
			{
				_log("Downloading " + asset.Name);
				string sha = await DownloadCheckedAsync(release, asset, zip, MaxAddonZip, ct).ConfigureAwait(false);
				var result = AddonInstaller.Install(zip, settings.AddOnsDir, settings.InstalledFolders, release.Version);
				settings.InstalledFolders = result.Folders.ToList();
				_log("Installed EldoUI " + (result.Version ?? release.Version) + " (" + string.Join(", ", result.Folders) + "), sha256 " + sha
					+ (result.Removed.Count > 0 ? "; removed " + string.Join(", ", result.Removed) : ""));
				return result;
			}
			finally
			{
				try { File.Delete(zip); } catch { }
			}
		}

		/// <summary>
		/// Downloads an asset of a release and checks its size and SHA-256 against a trusted value (see TrustedSha256Async).
		/// Throws UnverifiedDownload when there is no trusted value or the file does not match it. Returns the SHA-256.
		/// </summary>
		public async Task<string> DownloadCheckedAsync(Release release, ReleaseAsset asset, string target, long maxBytes, CancellationToken ct)
		{
			// Without a trusted checksum the file is not even downloaded.
			string trusted = await TrustedSha256Async(release, asset, ct).ConfigureAwait(false);
			if (trusted == null)
				throw new UnverifiedDownload(asset.Name + " has no published checksum, so it was not installed.");
			string sha = await _web.DownloadAsync(asset.Url, target, maxBytes, ct).ConfigureAwait(false);
			long size = new FileInfo(target).Length;
			if (asset.Size > 0 && size != asset.Size)
				throw new InvalidDataException("The download has the wrong size (" + size + " bytes, expected " + asset.Size + ").");
			if (!string.Equals(sha, trusted, StringComparison.OrdinalIgnoreCase))
				throw new UnverifiedDownload(asset.Name + " does not match its published checksum, so it was not installed.");
			return sha;
		}

		/// <summary>
		/// The SHA-256 the release publishes for the asset: GitHub's digest of the uploaded file, or else its line in
		/// a checksum file of the same release ("&lt;name&gt;.sha256" or "SHA256SUMS.txt"). Null when there is neither.
		/// </summary>
		public async Task<string> TrustedSha256Async(Release release, ReleaseAsset asset, CancellationToken ct)
		{
			if (asset == null) return null;
			if (asset.Sha256 != null) return asset.Sha256;
			if (release == null) return null;
			var files = release.Assets.Where(a => Release.IsChecksumFileFor(a, asset))
				.OrderBy(a => a.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList();
			if (files.Count == 0) return null;
			Directory.CreateDirectory(_tempDir);
			foreach (var file in files)
			{
				string path = Path.Combine(_tempDir, "checksums-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
				try
				{
					await _web.DownloadAsync(file.Url, path, MaxChecksumFile, ct).ConfigureAwait(false);
					bool single = file.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase);
					string sha = Checksums.Find(File.ReadAllText(path), asset.Name, single);
					if (sha != null)
					{
						_log("Checksum for " + asset.Name + " read from " + file.Name);
						return sha;
					}
				}
				finally
				{
					try { File.Delete(path); } catch { }
				}
			}
			return null;
		}

		/// <summary>
		/// The updater's own newest release when it is newer than the running one and has this platform's file
		/// (assetPattern: the exe by default, LinuxUpdaterAssetPattern on Linux), else null. Never throws.
		/// </summary>
		public async Task<Release> NewerUpdaterAsync(string currentVersion, CancellationToken ct, string assetPattern = UpdaterAssetPattern)
		{
			var r = await CheckUpdaterAsync(currentVersion, ct, assetPattern).ConfigureAwait(false);
			return r.Outcome == AppCheckOutcome.Newer ? r.Release : null;
		}

		/// <summary>
		/// Asks GitHub for the updater's own newest release: Newer (with the release) when it is newer than the running
		/// version and has this platform's file, UpToDate when not, Unreachable (with the reason) when GitHub gave no
		/// answer. Never throws.
		/// </summary>
		public async Task<AppCheckResult> CheckUpdaterAsync(string currentVersion, CancellationToken ct, string assetPattern = UpdaterAssetPattern)
		{
			try
			{
				var r = await LatestAsync(UpdaterRepo, ct).ConfigureAwait(false);
				if (r != null && r.Find(assetPattern) != null && Versions.IsNewer(r.Version, currentVersion))
					return new AppCheckResult { Outcome = AppCheckOutcome.Newer, Release = r };
				return new AppCheckResult { Outcome = AppCheckOutcome.UpToDate, Release = r };
			}
			catch (Exception ex)
			{
				_log("Updater check skipped: " + ex.Message);
				return new AppCheckResult { Outcome = AppCheckOutcome.Unreachable, Error = ex.Message };
			}
		}

		public Task<string> DownloadUpdaterAsync(Release r, string target, CancellationToken ct, string assetPattern = UpdaterAssetPattern)
		{
			return DownloadCheckedAsync(r, r.Find(assetPattern), target, MaxUpdaterFile, ct);
		}
	}

	public enum AppCheckOutcome
	{
		/// <summary>A newer version of the updater is published for this platform.</summary>
		Newer,
		UpToDate,
		/// <summary>GitHub could not be asked, or did not show the updater's releases.</summary>
		Unreachable,
	}

	public sealed class AppCheckResult
	{
		public AppCheckOutcome Outcome;
		public Release Release;
		public string Error;
	}

	/// <summary>A download that has no trusted SHA-256, or does not match it. Nothing is installed from it.</summary>
	public sealed class UnverifiedDownload : Exception
	{
		public UnverifiedDownload(string message) : base(message) { }
	}
}
