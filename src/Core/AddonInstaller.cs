// Puts the folders of an EldoUI release zip into Interface\AddOns.
//
// The zip holds one top folder per addon (EldoUI, EldoUI_LootData, ...), each with its own .toc.
// Everything is unpacked next to AddOns first and checked; only then is each old folder moved aside
// and the new one moved in. If a move fails, the old folders are put back. Saved settings live in
// the game's WTF folder, so they are never touched.
//
// The updater manages only EldoUI's own folders: the ones in the zip, and the ones it installed earlier.
// Every other folder in AddOns, including an EldoUI folder that is in neither list, is left exactly as it is.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace EldoUI.Updater.Core
{
	public sealed class InstallResult
	{
		public string Version;
		public List<string> Folders = new List<string>();
		public List<string> Removed = new List<string>();
	}

	public static class AddonInstaller
	{
		public const string MainFolder = "EldoUI";
		static readonly Regex FolderName = new Regex(@"^EldoUI(_[A-Za-z0-9]+)?$", RegexOptions.CultureInvariant);
		const long MaxUnpackedBytes = 512L * 1024 * 1024;
		const int MaxEntries = 50000;

		/// <summary>The top folders in the zip, checked. Throws InvalidDataException when the zip is not an EldoUI release.</summary>
		public static List<string> Inspect(string zipPath)
		{
			using (var zip = ZipFile.OpenRead(zipPath))
				return Inspect(zip);
		}

		static List<string> Inspect(ZipArchive zip)
		{
			if (zip.Entries.Count == 0 || zip.Entries.Count > MaxEntries) throw new InvalidDataException("The zip is empty or far too large.");
			var folders = new SortedSet<string>(StringComparer.Ordinal);
			var tocs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			long total = 0;
			foreach (var e in zip.Entries)
			{
				string path = e.FullName.Replace('\\', '/');
				if (path.Length == 0 || path.StartsWith("/") || path.Contains(":") || path.Split('/').Any(p => p == ".." || p == "."))
					throw new InvalidDataException("The zip has an unsafe path: " + e.FullName);
				int slash = path.IndexOf('/');
				if (slash < 0)
				{
					// A loose file at the top (a readme) is skipped, never installed.
					continue;
				}
				string top = path.Substring(0, slash);
				if (!FolderName.IsMatch(top)) throw new InvalidDataException("The zip has a folder that is not part of EldoUI: " + top);
				folders.Add(top);
				if (string.Equals(path, top + "/" + top + ".toc", StringComparison.OrdinalIgnoreCase)) tocs.Add(top);
				total += e.Length;
				if (total > MaxUnpackedBytes) throw new InvalidDataException("The zip unpacks to more than 512 MB.");
			}
			if (!folders.Contains(MainFolder) || !tocs.Contains(MainFolder)) throw new InvalidDataException("The zip has no EldoUI/EldoUI.toc.");
			foreach (string f in folders)
				if (!tocs.Contains(f)) throw new InvalidDataException("The folder " + f + " in the zip has no " + f + ".toc.");
			return folders.ToList();
		}

		/// <summary>
		/// Installs the zip into addOnsDir. previousFolders are the folders the last install put there:
		/// any of them the new release no longer has is removed. Other folders are never touched.
		/// </summary>
		public static InstallResult Install(string zipPath, string addOnsDir, IEnumerable<string> previousFolders)
		{
			if (!Directory.Exists(addOnsDir)) Directory.CreateDirectory(addOnsDir);
			string addOns = Path.GetFullPath(addOnsDir);
			string work = Path.Combine(Path.GetDirectoryName(addOns.TrimEnd(Path.DirectorySeparatorChar)) ?? addOns,
				".eldoui-updater-" + Guid.NewGuid().ToString("N").Substring(0, 8));
			string fresh = Path.Combine(work, "new");
			string old = Path.Combine(work, "old");
			var result = new InstallResult();
			var moved = new List<string>();     // folders moved aside, to put back on failure
			var placed = new List<string>();    // new folders moved in, to take out on failure
			try
			{
				Directory.CreateDirectory(fresh);
				Directory.CreateDirectory(old);
				using (var zip = ZipFile.OpenRead(zipPath))
				{
					result.Folders = Inspect(zip);
					foreach (var e in zip.Entries)
					{
						string path = e.FullName.Replace('\\', '/');
						if (path.IndexOf('/') < 0) continue;
						string target = Path.GetFullPath(Path.Combine(fresh, path.Replace('/', Path.DirectorySeparatorChar)));
						if (!target.StartsWith(fresh + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
							throw new InvalidDataException("The zip has an unsafe path: " + e.FullName);
						if (path.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
						Directory.CreateDirectory(Path.GetDirectoryName(target));
						e.ExtractToFile(target, true);
					}
				}
				result.Version = ReadTocVersion(Path.Combine(fresh, MainFolder, MainFolder + ".toc"));

				var remove = (previousFolders ?? Enumerable.Empty<string>())
					.Where(f => f != null && FolderName.IsMatch(f) && !result.Folders.Contains(f)).Distinct().ToList();
				foreach (string f in result.Folders.Concat(remove))
				{
					string current = Path.Combine(addOns, f);
					if (!Directory.Exists(current)) continue;
					Directory.Move(current, Path.Combine(old, f));
					moved.Add(f);
				}
				foreach (string f in result.Folders)
				{
					Directory.Move(Path.Combine(fresh, f), Path.Combine(addOns, f));
					placed.Add(f);
				}
				result.Removed = remove.Where(moved.Contains).ToList();
				moved.Clear();
				return result;
			}
			catch
			{
				// Put the old folders back exactly as they were.
				foreach (string f in placed)
					TryDelete(Path.Combine(addOns, f));
				foreach (string f in moved.ToList())
				{
					try { Directory.Move(Path.Combine(old, f), Path.Combine(addOns, f)); moved.Remove(f); } catch { }
				}
				throw;
			}
			finally
			{
				// A folder that could not be put back stays in the work folder, so nothing is ever lost.
				if (moved.Count == 0) TryDelete(work);
			}
		}

		/// <summary>The "## Version:" of a .toc, or null.</summary>
		public static string ReadTocVersion(string tocPath)
		{
			if (!File.Exists(tocPath)) return null;
			foreach (string line in File.ReadLines(tocPath))
			{
				var m = Regex.Match(line, @"^\s*##\s*Version\s*:\s*(.+?)\s*$", RegexOptions.IgnoreCase);
				if (m.Success) return Versions.Clean(m.Groups[1].Value);
			}
			return null;
		}

		/// <summary>The installed EldoUI version in an AddOns folder, or null when EldoUI is not there.</summary>
		public static string InstalledVersion(string addOnsDir)
		{
			if (string.IsNullOrEmpty(addOnsDir)) return null;
			return ReadTocVersion(Path.Combine(addOnsDir, MainFolder, MainFolder + ".toc"));
		}

		static void TryDelete(string dir)
		{
			try
			{
				if (!Directory.Exists(dir)) return;
				foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
					File.SetAttributes(f, FileAttributes.Normal);
				Directory.Delete(dir, true);
			}
			catch { }
		}
	}
}
