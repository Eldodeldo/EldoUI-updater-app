// Optional companion packages are fetched from their publishers, never bundled with EldoUI.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EldoUI.Updater.Core
{
	public sealed class Companion
	{
		public string Id, Name, Repository, MainFolder, FolderPattern;
		public bool Owns(string path)
		{
			return !string.IsNullOrEmpty(path) && !path.Contains("\\") && !path.StartsWith("/")
				&& !path.Contains(":") && !path.TrimEnd('/').Split('/').Any(x => x.Length == 0 || x.Any(char.IsControl) || Regex.IsMatch(x, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?\z", RegexOptions.IgnoreCase) || x.EndsWith(".") || x.EndsWith(" ") || x.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*', '\0' }) >= 0)
				&& Regex.IsMatch(path.Split('/')[0], FolderPattern, RegexOptions.CultureInvariant);
		}
		public static readonly Companion[] All = {
			new Companion { Id = "dbm", Name = "DBM", Repository = "DeadlyBossMods/DeadlyBossMods", MainFolder = "DBM-Core", FolderPattern = @"\ADBM-[A-Za-z0-9-]+\z" },
			new Companion { Id = "rxp", Name = "RestedXP", Repository = "RestedXP/RXPGuides", MainFolder = "RXPGuides", FolderPattern = @"\ARXPGuides\z" },
			new Companion { Id = "media", Name = "WeakAuras SharedMedia", Repository = "WeakAuras/WeakAuras_SharedMedia", MainFolder = "WeakAuras_SharedMedia", FolderPattern = @"\AWeakAuras_SharedMedia\z" }
		};
	}

	public enum CompanionOutcome { Current, Available, Installed, Failed }
	public sealed class CompanionResult
	{
		public string Name, Version, Error;
		public CompanionOutcome Outcome;
	}

	public sealed class CompanionUpdater
	{
		readonly IWeb _web;
		readonly string _temp;
		public CompanionUpdater(IWeb web, string temp) { _web = web; _temp = temp; }

		public static string ForeverFilename(string manifest)
		{
			var releases = Json.ParseObject(manifest).Arr("releases");
			foreach (var item in releases ?? new List<object>())
			{
				var release = item as Dictionary<string, object>;
				if (release == null || release.Bool("nolib")) continue;
				if ((release.Arr("metadata") ?? new List<object>()).OfType<Dictionary<string, object>>()
					.Any(m => m.Str("flavor") == "forever")) return release.Str("filename");
			}
			throw new InvalidDataException("No packaged Forever release is available.");
		}

		public async Task<List<CompanionResult>> CheckAsync(Settings settings, bool install, CancellationToken ct)
		{
			var results = new List<CompanionResult>();
			if (!settings.ExtraAddons) return results;
			foreach (var addon in Companion.All.Where(a => settings.SelectedCompanions.Contains(a.Id)))
			{
				ct.ThrowIfCancellationRequested();
				var result = new CompanionResult { Name = addon.Name };
				results.Add(result);
				string work = Path.Combine(_temp, "companion-" + Guid.NewGuid().ToString("N"));
				try
				{
					Directory.CreateDirectory(work);
					var engine = new UpdateEngine(_web, work, null);
					var release = await engine.LatestAsync(addon.Repository, ct).ConfigureAwait(false);
					if (release == null) throw new InvalidDataException("No stable release is available.");
					result.Version = release.Tag;
					var metadata = release.Assets.FirstOrDefault(a => a.Name == "release.json");
					if (metadata == null) throw new InvalidDataException("Publisher compatibility metadata is missing.");
					string manifest = Path.Combine(work, "release.json");
					await engine.DownloadCheckedAsync(release, metadata, manifest, 1024 * 1024, ct).ConfigureAwait(false);
					string filename = ForeverFilename(File.ReadAllText(manifest));
					var asset = release.Assets.FirstOrDefault(a => a.Name == filename);
					if (asset == null || !filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The Forever package is missing.");
					string previous;
					List<string> files;
					if (settings.ExtraVersions.TryGetValue(addon.Id, out previous) && previous == release.Tag
						&& settings.ExtraFiles.TryGetValue(addon.Id, out files) && files.Count > 0
						&& files.All(f => addon.Owns(f) && File.Exists(Path.Combine(settings.AddOnsDir, f.Replace('/', Path.DirectorySeparatorChar)))))
					{
						result.Outcome = CompanionOutcome.Current;
						continue;
					}
					result.Outcome = CompanionOutcome.Available;
					if (!install) continue;
					string zip = Path.Combine(work, "addon.zip");
					await engine.DownloadCheckedAsync(release, asset, zip, 200L * 1024 * 1024, ct).ConfigureAwait(false);
					settings.ExtraFiles.TryGetValue(addon.Id, out files);
					settings.ExtraFiles[addon.Id] = CompanionInstaller.Install(addon, zip, settings.AddOnsDir, files, ct);
					settings.ExtraVersions[addon.Id] = release.Tag;
					result.Outcome = CompanionOutcome.Installed;
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
				catch (Exception ex) when (ex is OperationCanceledException || ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is WebFailure || ex is UnverifiedDownload || ex is System.Net.Http.HttpRequestException)
				{
					result.Outcome = CompanionOutcome.Failed;
					result.Error = ex.Message;
				}
				finally { if (Directory.Exists(work)) { try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
			}
			return results;
		}
	}

	public static class CompanionInstaller
	{
		static void CopyTree(string source, string target, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("A companion folder contains a symbolic link.");
			Directory.CreateDirectory(target);
			foreach (string file in Directory.GetFiles(source))
			{
				if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("A companion file is a symbolic link.");
				File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
			}
			foreach (string dir in Directory.GetDirectories(source)) CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)), ct);
		}

		public static List<string> Install(Companion addon, string zipPath, string addOnsDir, IEnumerable<string> previousFiles, CancellationToken ct)
		{
			string target = Path.GetFullPath(addOnsDir);
			var previous = (previousFiles ?? Enumerable.Empty<string>()).ToList();
			if (previous.Any(p => !addon.Owns(p) || !p.Contains("/"))) throw new InvalidDataException("Invalid previous companion file ownership.");
			string work = Path.Combine(Path.GetDirectoryName(target), ".eldoui-companion-" + Guid.NewGuid().ToString("N"));
			string fresh = Path.Combine(work, "new"), backup = Path.Combine(work, "old");
			var moved = new List<string>();
			var placed = new List<string>();
			try
			{
				using (var zip = ZipFile.OpenRead(zipPath))
				{
					if (zip.Entries.Count == 0 || zip.Entries.Count > 50000 || zip.Entries.Any(e => e.Length > 512L * 1024 * 1024) || zip.Entries.Sum(e => e.Length) > 512L * 1024 * 1024) throw new InvalidDataException("Invalid companion archive size.");
					var entries = zip.Entries.Where(e => !e.FullName.EndsWith("/")).ToList();
					var paths = entries.Select(e => e.FullName).ToList();
					if (zip.Entries.Any(e => !addon.Owns(e.FullName)) || paths.Any(p => !p.Contains("/"))
						|| paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count) throw new InvalidDataException("The package contains an unsafe or unrelated path.");
					if (!paths.Any(p => p.StartsWith(addon.MainFolder + "/", StringComparison.Ordinal) && p.EndsWith(".toc", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("The companion TOC is missing.");
					var roots = paths.Concat(previous).Select(p => p.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
					Directory.CreateDirectory(fresh);
					Directory.CreateDirectory(backup);
					foreach (string root in roots)
					{
						string old = Path.Combine(target, root);
						if (Directory.Exists(old)) CopyTree(old, Path.Combine(fresh, root), ct);
					}
					foreach (string old in previous.Except(paths, StringComparer.OrdinalIgnoreCase))
					{
						string path = Path.Combine(fresh, old.Replace('/', Path.DirectorySeparatorChar));
						if (File.Exists(path)) File.Delete(path);
					}
					foreach (var entry in entries)
					{
						ct.ThrowIfCancellationRequested();
						string path = Path.Combine(fresh, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
						Directory.CreateDirectory(Path.GetDirectoryName(path));
						entry.ExtractToFile(path, true);
					}
					ct.ThrowIfCancellationRequested();
					Directory.CreateDirectory(target);
					foreach (string root in roots)
					{
						string old = Path.Combine(target, root);
						if (Directory.Exists(old)) { Directory.Move(old, Path.Combine(backup, root)); moved.Add(root); }
					}
					foreach (string root in roots)
						if (Directory.Exists(Path.Combine(fresh, root))) { Directory.Move(Path.Combine(fresh, root), Path.Combine(target, root)); placed.Add(root); }
					moved.Clear();
					return paths;
				}
			}
			catch
			{
				foreach (string root in placed)
					try { Directory.Delete(Path.Combine(target, root), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
				foreach (string root in moved.ToList())
					try { Directory.Move(Path.Combine(backup, root), Path.Combine(target, root)); moved.Remove(root); } catch (IOException) { } catch (UnauthorizedAccessException) { }
				throw;
			}
			finally
			{
				if (moved.Count == 0 && Directory.Exists(work))
					try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
			}
		}
	}
}
