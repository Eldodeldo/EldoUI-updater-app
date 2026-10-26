// Optional addons are fetched from their publishers, never bundled with EldoUI.
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
		public string Id, Name, Repository, MainFolder, FolderPattern, Description, Category, AssetHint;
		/// <summary>The AddOns folders the catalog lists for it (catalog addons only).</summary>
		public string[] Folders;
		/// <summary>A companion from earlier versions: needs the publisher's compatibility manifest, as before.</summary>
		public bool NeedsManifest { get { return MainFolder != null; } }

		public bool Owns(string path)
		{
			return !string.IsNullOrEmpty(path) && !path.Contains("\\") && !path.StartsWith("/")
				&& !path.Contains(":") && !path.TrimEnd('/').Split('/').Any(x => x.Length == 0 || x == "." || x == ".." || x.Any(char.IsControl) || Regex.IsMatch(x, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?\z", RegexOptions.IgnoreCase) || x.EndsWith(".") || x.EndsWith(" ") || x.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*', '\0' }) >= 0)
				&& !AddonInstaller.IsEldoUIFolder(path.Split('/')[0])
				&& Regex.IsMatch(path.Split('/')[0], FolderPattern, RegexOptions.CultureInvariant);
		}

		public static readonly Companion[] All = {
			new Companion { Id = "dbm", Name = "DBM", Repository = "DeadlyBossMods/DeadlyBossMods", MainFolder = "DBM-Core", FolderPattern = @"\ADBM-[A-Za-z0-9-]+\z" },
			new Companion { Id = "rxp", Name = "RestedXP", Repository = "RestedXP/RXPGuides", MainFolder = "RXPGuides", FolderPattern = @"\ARXPGuides\z" },
			new Companion { Id = "media", Name = "WeakAuras SharedMedia", Repository = "WeakAuras/WeakAuras_SharedMedia", MainFolder = "WeakAuras_SharedMedia", FolderPattern = @"\AWeakAuras_SharedMedia\z",
				Description = "Extra fonts, sounds and textures for WeakAuras and other addons.", Category = "UI" }
		};
	}

	public enum CompanionOutcome
	{
		Current,
		Available,
		Installed,
		Failed,
		/// <summary>Kept at the installed version by the player; a newer one is waiting.</summary>
		Held,
		/// <summary>The newest version is at a publisher the updater cannot install from (no published SHA-256).</summary>
		External,
		/// <summary>The publisher has no Forever build of it.</summary>
		NoForever,
		/// <summary>Some of its folders are already in AddOns, put there by hand; the player decides whether the updater takes them over.</summary>
		InUse,
	}

	public sealed class CompanionResult
	{
		public string Id, Name, Version, Error, PageUrl;
		public CompanionOutcome Outcome;
		public List<string> Folders;
	}

	public sealed class CompanionUpdater
	{
		readonly IWeb _web;
		readonly string _temp;
		readonly string _apiBase;
		public CompanionUpdater(IWeb web, string temp, string apiBase = "https://api.github.com") { _web = web; _temp = temp; _apiBase = apiBase; }

		/// <summary>The packaged file the publisher's release.json lists for the Forever flavor.</summary>
		public static string ForeverFilename(string manifest)
		{
			return ForeverFilename(manifest, false);
		}

		/// <summary>
		/// As ForeverFilename; wide also takes a "camelot" flavor or a Forever interface number (16000-16999),
		/// which packagers write for Forever builds too.
		/// </summary>
		public static string ForeverFilename(string manifest, bool wide)
		{
			var releases = Json.ParseObject(manifest).Arr("releases");
			foreach (var item in releases ?? new List<object>())
			{
				var release = item as Dictionary<string, object>;
				if (release == null || release.Bool("nolib")) continue;
				if ((release.Arr("metadata") ?? new List<object>()).OfType<Dictionary<string, object>>()
					.Any(m => m.Str("flavor") == "forever" || (wide && (m.Str("flavor") == "camelot" || Toc.IsForeverInterface((int)m.Num("interface"))))))
					return release.Str("filename");
			}
			throw new NoForeverBuild("No packaged Forever release is available.");
		}

		/// <summary>The addons to check, in a steady order: the earlier companions first, then the catalog's order.</summary>
		static IEnumerable<CatalogEntry> Selected(Settings settings)
		{
			var ids = new HashSet<string>(settings.SelectedCompanions);
			return AddonCatalog.All.Where(e => ids.Contains(e.Id))
				.OrderBy(e => e.Legacy != null ? Array.IndexOf(Companion.All, e.Legacy) : Companion.All.Length);
		}

		public async Task<List<CompanionResult>> CheckAsync(Settings settings, bool install, CancellationToken ct)
		{
			var results = new List<CompanionResult>();
			if (!settings.ExtraAddons) return results;
			foreach (var entry in Selected(settings))
			{
				ct.ThrowIfCancellationRequested();
				var result = new CompanionResult { Id = entry.Id, Name = entry.Legacy != null ? entry.Legacy.Name : entry.Name, PageUrl = entry.PageUrl };
				results.Add(result);
				string work = Path.Combine(_temp, "companion-" + Guid.NewGuid().ToString("N"));
				try
				{
					Directory.CreateDirectory(work);
					var engine = new UpdateEngine(_web, work, null, _apiBase);
					if (entry.Source == AddonSource.WowInterface || entry.Source == AddonSource.Tukui)
					{
						var external = await AddonSources.ResolveAsync(_web, entry, ct).ConfigureAwait(false);
						result.Version = external.Version;
						result.Outcome = external.Forever == false ? CompanionOutcome.NoForever : CompanionOutcome.External;
						continue;
					}
					if (!entry.Installable)
					{
						result.Outcome = CompanionOutcome.NoForever;
						continue;
					}
					var addon = entry.Package;
					var release = await AddonSources.GitHubAsync(engine, entry, settings.PreRelease.Contains(entry.Id), work, ct).ConfigureAwait(false);
					result.Version = release.Tag;
					string previous;
					List<string> files;
					bool installed = settings.ExtraFiles.TryGetValue(entry.Id, out files) && files.Count > 0
						&& files.All(f => addon.Owns(f) && File.Exists(Path.Combine(settings.AddOnsDir, f.Replace('/', Path.DirectorySeparatorChar))));
					if (installed && settings.ExtraVersions.TryGetValue(entry.Id, out previous) && previous == release.Tag)
					{
						result.Outcome = CompanionOutcome.Current;
						continue;
					}
					if (installed && settings.Held.Contains(entry.Id))
					{
						result.Outcome = CompanionOutcome.Held;
						continue;
					}
					result.Outcome = CompanionOutcome.Available;
					if (!install) continue;
					string zip = Path.Combine(work, "addon.zip");
					await engine.DownloadCheckedAsync(release.GitHubRelease, release.Asset, zip, 200L * 1024 * 1024, ct).ConfigureAwait(false);
					settings.ExtraFiles.TryGetValue(entry.Id, out files);
					var guard = new InstallGuard
					{
						// Earlier companions always updated a copy the player had put there; catalog addons ask first.
						Adopt = entry.Legacy != null || settings.Adopted.Contains(entry.Id),
						NeedsForeverToc = release.Forever != true,
						Taken = TakenFolders(settings, entry.Id),
					};
					settings.ExtraFiles[entry.Id] = CompanionInstaller.Install(addon, zip, settings.AddOnsDir, files, ct, guard);
					settings.ExtraVersions[entry.Id] = release.Tag;
					settings.Adopted.Remove(entry.Id);
					result.Outcome = CompanionOutcome.Installed;
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
				catch (NoForeverBuild ex)
				{
					result.Outcome = CompanionOutcome.NoForever;
					result.Error = ex.Message;
				}
				catch (FoldersInUse ex)
				{
					result.Outcome = CompanionOutcome.InUse;
					result.Folders = ex.Folders;
					result.Error = ex.Message;
				}
				catch (Exception ex) when (ex is OperationCanceledException || ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is WebFailure || ex is UnverifiedDownload || ex is System.Net.Http.HttpRequestException)
				{
					result.Outcome = CompanionOutcome.Failed;
					result.Error = ex.Message;
				}
				finally { if (Directory.Exists(work)) { try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
			}
			return results;
		}

		/// <summary>Folders that belong to EldoUI or to another addon the updater manages, with the owner's name.</summary>
		public static Dictionary<string, string> TakenFolders(Settings settings, string except)
		{
			var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string f in settings.InstalledFolders ?? new List<string>()) taken[f] = "EldoUI";
			foreach (var kv in settings.ExtraFiles)
			{
				if (kv.Key == except) continue;
				var entry = AddonCatalog.Find(kv.Key);
				foreach (string root in kv.Value.Select(p => p.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase))
					taken[root] = entry != null ? entry.Name : kv.Key;
			}
			return taken;
		}
	}

	/// <summary>The publisher's release has no build for WoW: Forever.</summary>
	public sealed class NoForeverBuild : IOException
	{
		public NoForeverBuild(string message) : base(message) { }
	}

	/// <summary>What an install of a catalog addon must respect besides its own folder rules.</summary>
	public sealed class InstallGuard
	{
		/// <summary>The player agreed that folders already in AddOns (put there by hand) are updated from now on.</summary>
		public bool Adopt;
		/// <summary>No publisher manifest named a Forever build, so a .toc in the package must.</summary>
		public bool NeedsForeverToc;
		/// <summary>Folder name → owner, for folders that belong to EldoUI or another managed addon.</summary>
		public Dictionary<string, string> Taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>Folders already in AddOns that the player put there; the updater only updates them once the player agrees.</summary>
	public sealed class FoldersInUse : IOException
	{
		public readonly List<string> Folders;
		public FoldersInUse(List<string> folders) : base("Already in the AddOns folder: " + string.Join(", ", folders) + ".") { Folders = folders; }
	}

	public static class CompanionInstaller
	{
		// File types no WoW addon needs (the list follows AddonForge, MIT; see NOTICE).
		static readonly HashSet<string> Executable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"exe", "dll", "sys", "scr", "com", "pif", "cpl", "msi", "msp", "bat", "cmd", "ps1", "psm1",
			"vbs", "vbe", "js", "jse", "wsf", "wsh", "hta", "jar", "py", "sh", "lnk", "reg", "inf",
			"ocx", "drv", "app", "dmg", "apk", "command", "dylib", "so", "pkg", "scpt", "tool", "terminal", "appimage", "desktop",
		};
		static readonly HashSet<string> Bundle = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"app", "bundle", "framework", "kext", "plugin", "workflow", "appex", "prefpane", "saver",
		};
		static readonly HashSet<string> Plain = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"lua", "xml", "toc", "tga", "blp", "png", "jpg", "jpeg", "gif", "ttf", "otf", "mp3", "ogg", "wav", "txt", "md", "json", "html", "css", "csv",
		};

		/// <summary>What makes a package entry unsafe to install (a program, a link), or null when it is fine.</summary>
		public static string Unsafe(ZipArchiveEntry entry)
		{
			string path = entry.FullName;
			// The Unix file type in the upper bits: only plain files and folders are taken, never a link.
			int type = (entry.ExternalAttributes >> 16) & 0xF000;
			if (type != 0 && type != 0x8000 && type != 0x4000) return "a link or special file (" + path + ")";
			bool folder = path.EndsWith("/");
			var parts = path.TrimEnd('/').Split('/');
			for (int i = 0; i < parts.Length - (folder ? 0 : 1); i++)
				if (Bundle.Contains(Extension(parts[i]))) return "an application bundle (" + path + ")";
			if (folder) return null;
			string ext = Extension(parts[parts.Length - 1]);
			if (Executable.Contains(ext)) return "a program file (" + path + ")";
			if (Plain.Contains(ext)) return null;
			var head = new byte[4];
			int n = 0;
			using (var s = entry.Open())
				while (n < 4) { int r = s.Read(head, n, 4 - n); if (r <= 0) break; n += r; }
			if (n >= 2 && head[0] == 'M' && head[1] == 'Z') return "a Windows program (" + path + ")";
			if (n >= 2 && head[0] == '#' && head[1] == '!') return "a script (" + path + ")";
			if (n == 4 && head[0] == 0x7F && head[1] == 'E' && head[2] == 'L' && head[3] == 'F') return "a Linux program (" + path + ")";
			if (n == 4 && ((head[0] == 0xFE && head[1] == 0xED && head[2] == 0xFA && (head[3] == 0xCE || head[3] == 0xCF))
				|| ((head[0] == 0xCE || head[0] == 0xCF) && head[1] == 0xFA && head[2] == 0xED && head[3] == 0xFE)
				|| (head[0] == 0xCA && head[1] == 0xFE && head[2] == 0xBA && (head[3] == 0xBE || head[3] == 0xBF))))
				return "a macOS program (" + path + ")";
			return null;
		}

		static string Extension(string name)
		{
			int dot = name.LastIndexOf('.');
			return dot < 0 ? "" : name.Substring(dot + 1);
		}

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

		public static List<string> Install(Companion addon, string zipPath, string addOnsDir, IEnumerable<string> previousFiles, CancellationToken ct, InstallGuard guard = null)
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
					foreach (var entry in zip.Entries)
					{
						string problem = Unsafe(entry);
						if (problem != null) throw new InvalidDataException("The package was refused: it contains " + problem + ", which no WoW addon needs.");
					}
					if (addon.MainFolder != null && !paths.Any(p => p.StartsWith(addon.MainFolder + "/", StringComparison.Ordinal) && p.EndsWith(".toc", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("The companion TOC is missing.");
					var newRoots = paths.Select(p => p.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
					var previousRoots = previous.Select(p => p.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
					if (guard != null) Check(guard, entries, target, newRoots, previousRoots);
					var roots = newRoots.Concat(previousRoots).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
				// A folder that could not be put back stays in the work folder, so nothing is ever lost.
				if (moved.Count == 0 && Directory.Exists(work))
					try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
			}
		}

		/// <summary>The catalog rules, checked before anything in AddOns changes.</summary>
		static void Check(InstallGuard guard, List<ZipArchiveEntry> entries, string target, List<string> newRoots, List<string> previousRoots)
		{
			var clash = newRoots.Where(r => guard.Taken.ContainsKey(r)).ToList();
			if (clash.Count > 0) throw new InvalidDataException("The package would replace " + string.Join(", ", clash) + ", which belongs to " + guard.Taken[clash[0]] + ".");
			var inUse = newRoots.Where(r => !previousRoots.Contains(r, StringComparer.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(target, r))).ToList();
			if (inUse.Count > 0 && !guard.Adopt) throw new FoldersInUse(inUse);
			var tocs = entries.Where(e => e.FullName.Count(c => c == '/') == 1 && e.FullName.EndsWith(".toc", StringComparison.OrdinalIgnoreCase))
				.Select(e => new { Root = e.FullName.Split('/')[0], Info = ReadToc(e) }).Where(t => t.Info != null).ToList();
			if (guard.NeedsForeverToc && !tocs.Any(t => t.Info.IsForever))
				throw new NoForeverBuild("No .toc in the package is made for WoW: Forever.");
			var needs = InstalledNeeds(target, newRoots);
			foreach (var t in tocs)
			{
				List<string> list;
				if (!needs.TryGetValue(t.Root, out list)) needs[t.Root] = list = new List<string>();
				list.AddRange(t.Info.Dependencies.Where(d => !string.Equals(d, t.Root, StringComparison.OrdinalIgnoreCase)));
			}
			var cycle = Dependencies.FindCycle(needs, newRoots);
			if (cycle != null) throw new InvalidDataException("The package's addons need each other in a loop: " + string.Join(" > ", cycle) + ".");
		}

		static TocInfo ReadToc(ZipArchiveEntry entry)
		{
			if (entry.Length > 256 * 1024) return null;
			using (var reader = new StreamReader(entry.Open(), System.Text.Encoding.UTF8))
				return Toc.Parse(reader.ReadToEnd(), Path.GetFileName(entry.FullName));
		}

		/// <summary>What each installed addon needs, except the folders the package replaces.</summary>
		static Dictionary<string, List<string>> InstalledNeeds(string addOns, List<string> replaced)
		{
			var needs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			if (!Directory.Exists(addOns)) return needs;
			foreach (string dir in Directory.GetDirectories(addOns))
			{
				string name = Path.GetFileName(dir);
				if (replaced.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith(".")) continue;
				var info = Toc.ReadFolder(dir);
				if (info != null) needs[name] = info.Dependencies;
			}
			return needs;
		}
	}
}
