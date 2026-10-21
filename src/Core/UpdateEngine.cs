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
		public async Task<CheckResult> CheckAsync(Settings settings, bool forceInstall, CancellationToken ct)
		{
			await _gate.WaitAsync(ct).ConfigureAwait(false);
			try
			{
				var r = new CheckResult { Installed = AddonInstaller.InstalledVersion(settings.AddOnsDir) };
				try
				{
					// The newest release that has its zip: a release without one (still being uploaded, or
					// published without it) is skipped rather than reported as an error.
					string body = await _web.GetApiAsync(_apiBase + "/repos/" + AddonRepo + "/releases?per_page=10", ct).ConfigureAwait(false);
					var releases = Release.ListFromGitHub(body);
					settings.LastCheck = DateTime.UtcNow;
					r.Release = releases.FirstOrDefault(x => x.Find(AddonAssetPattern) != null);
					if (r.Release == null)
					{
						r.Outcome = CheckOutcome.NoDownload;
						r.Latest = releases.Count > 0 ? releases[0].Version : null;
						return r;
					}
					r.Latest = r.Release.Version;
					if (string.IsNullOrEmpty(settings.AddOnsDir) || !Directory.Exists(Path.GetDirectoryName(settings.AddOnsDir.TrimEnd('\\', '/')) ?? ""))
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
				_gate.Release();
			}
		}

		async Task<InstallResult> InstallAsync(Release release, Settings settings, CancellationToken ct)
		{
			var asset = release.Find(AddonAssetPattern);
			Directory.CreateDirectory(_tempDir);
			string zip = Path.Combine(_tempDir, "EldoUI-" + release.Version + ".zip");
			try
			{
				_log("Downloading " + asset.Name);
				string sha = await DownloadCheckedAsync(asset, zip, MaxAddonZip, ct).ConfigureAwait(false);
				var result = AddonInstaller.Install(zip, settings.AddOnsDir, settings.InstalledFolders);
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

		/// <summary>Downloads an asset and checks its size and GitHub's SHA-256 (when GitHub has one). Returns the SHA-256.</summary>
		public async Task<string> DownloadCheckedAsync(ReleaseAsset asset, string target, long maxBytes, CancellationToken ct)
		{
			string sha = await _web.DownloadAsync(asset.Url, target, maxBytes, ct).ConfigureAwait(false);
			long size = new FileInfo(target).Length;
			if (asset.Size > 0 && size != asset.Size)
				throw new InvalidDataException("The download has the wrong size (" + size + " bytes, expected " + asset.Size + ").");
			if (asset.Sha256 != null && !string.Equals(sha, asset.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("The download does not match GitHub's checksum.");
			return sha;
		}

		/// <summary>
		/// The updater's own newest release when it is newer than the running one and has this platform's file
		/// (assetPattern: the exe by default, LinuxUpdaterAssetPattern on Linux), else null. Never throws.
		/// </summary>
		public async Task<Release> NewerUpdaterAsync(string currentVersion, CancellationToken ct, string assetPattern = UpdaterAssetPattern)
		{
			try
			{
				var r = await LatestAsync(UpdaterRepo, ct).ConfigureAwait(false);
				if (r != null && r.Find(assetPattern) != null && Versions.IsNewer(r.Version, currentVersion)) return r;
			}
			catch (Exception ex)
			{
				_log("Updater check skipped: " + ex.Message);
			}
			return null;
		}

		public Task<string> DownloadUpdaterAsync(Release r, string target, CancellationToken ct, string assetPattern = UpdaterAssetPattern)
		{
			return DownloadCheckedAsync(r.Find(assetPattern), target, MaxUpdaterFile, ct);
		}
	}
}
