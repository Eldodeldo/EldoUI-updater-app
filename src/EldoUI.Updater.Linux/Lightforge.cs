// The Linux setup assistant's engine. Lightforge is a separate program by its own authors (GPL-3.0-or-later)
// that installs Battle.net and WoW on Linux. The assistant checks the system, downloads Lightforge's official
// release (a fixed version, checked against its SHA-256), and puts its files where Lightforge's own installer
// puts them, in the player's home folder. It never runs a shell, never asks for a password and never starts
// the game or Battle.net; the only Lightforge commands it runs are the read-only ones in LightforgeProbe.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EldoUI.Updater.Core;

namespace EldoUI.Updater
{
	/// <summary>The one Lightforge release the assistant installs. A newer one is adopted through an update of the updater.</summary>
	public static class LightforgePackage
	{
		public const string Version = "1.0.0-beta.7";
		public const string Tag = "v1.0.0-beta.7";
		public const string Repository = "LightforgeProject/lightforge";
		public const string FileName = "lightforge-1.0.0-beta.7-x86_64-linux.tar.gz";
		public const string Sha256 = "b40876447f470b8f2fef743acb2ce49c08eef3ecfa611df9b1e5fe371137d49c";
		public const string License = "GPL-3.0-or-later";
		public const string ProductCode = "wowf";
		public const string ProductFolder = "_forever_";
		public const string Root = "https://github.com/" + Repository + "/releases/download/" + Tag + "/";
		public const string Url = Root + FileName;
		public const string SumsUrl = Root + "SHA256SUMS";
		/// <summary>The project's page and the source of this exact release.</summary>
		public const string Page = "https://github.com/" + Repository;
		public const string Source = "https://github.com/" + Repository + "/tree/" + Tag;
		public const string DesktopEntry = "pro.tuxcore.Lightforge.desktop";
		const string IconName = "pro.tuxcore.Lightforge";

		public static bool IsIcon(string fileName)
		{
			return fileName == IconName + ".png" || fileName == IconName + ".svg";
		}
	}

	public enum CheckState { Ok, Warn, Fail }

	public sealed class SystemCheck
	{
		/// <summary>The text key of the line ("lf.check.arch"), the state and what was found.</summary>
		public string Key;
		public CheckState State;
		public string Found;
		/// <summary>What to do about it, when it is more than the fixed text of the key (an install command for this distribution).</summary>
		public string Fix;
	}

	/// <summary>What the system check reads. Filled from the real system by Read, or by hand in tests.</summary>
	public sealed class SystemInfo
	{
		public string Distribution, Architecture, Glibc;
		public long FreeBytes = -1;
		public bool Has32BitLoader;
		public bool HasFonts;
		/// <summary>The package family ("debian", "fedora", "arch", "suse"), or null.</summary>
		public string Family;
		/// <summary>Libraries Lightforge's window loads that this system does not have.</summary>
		public List<string> MissingWindowLibraries = new List<string>();

		/// <summary>What Lightforge's window loads: libudev directly, the rest when it opens (keyboard and X11/XWayland).</summary>
		public static readonly string[] WindowLibraries =
		{
			"libudev.so.1", "libxkbcommon.so.0", "libxkbcommon-x11.so.0", "libX11.so.6", "libX11-xcb.so.1", "libXcursor.so.1", "libXi.so.6",
		};

		public static SystemInfo Read(string home)
		{
			var os = new LinuxSystem();
			foreach (string f in new[] { "/etc/os-release", "/usr/lib/os-release" })
			{
				try { if (File.Exists(f)) { Requirements.ReadOsRelease(os, File.ReadAllText(f)); break; } }
				catch (IOException) { }
				catch (UnauthorizedAccessException) { }
			}
			var info = new SystemInfo
			{
				Family = Requirements.Family(os),
				MissingWindowLibraries = WindowLibraries.Where(l => !Loads(l)).ToList(),
				Distribution = OsRelease("/etc/os-release") ?? OsRelease("/usr/lib/os-release"),
				Architecture = RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64 ? "x86_64"
					: RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64"
					: RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
				Glibc = GlibcVersion(),
				Has32BitLoader = new[] { "/lib/ld-linux.so.2", "/lib32/ld-linux.so.2", "/usr/lib32/ld-linux.so.2", "/usr/lib/ld-linux.so.2", "/usr/lib/i386-linux-gnu/ld-linux.so.2" }.Any(File.Exists),
				HasFonts = new[] { "/usr/share/fonts", "/usr/local/share/fonts", Path.Combine(home, ".local/share/fonts"), Path.Combine(home, ".fonts") }.Any(HasFont),
			};
			try { info.FreeBytes = new DriveInfo(home).AvailableFreeSpace; } catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException) { }
			return info;
		}

		/// <summary>PRETTY_NAME from an os-release file, or null.</summary>
		public static string OsRelease(string path)
		{
			try
			{
				if (!File.Exists(path)) return null;
				foreach (string line in File.ReadLines(path))
				{
					var m = Regex.Match(line, "^PRETTY_NAME=\"?([^\"]*)\"?\\s*$");
					if (m.Success && m.Groups[1].Value.Length > 0) return m.Groups[1].Value.Length > 80 ? m.Groups[1].Value.Substring(0, 80) : m.Groups[1].Value;
				}
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
			return null;
		}

		[DllImport("libc.so.6", EntryPoint = "gnu_get_libc_version")]
		static extern IntPtr gnu_get_libc_version();

		static string GlibcVersion()
		{
			try { return Marshal.PtrToStringAnsi(gnu_get_libc_version()); }
			catch (DllNotFoundException) { return null; }
			catch (EntryPointNotFoundException) { return null; }
		}

		static bool Loads(string library)
		{
			try
			{
				if (!NativeLibrary.TryLoad(library, out IntPtr handle)) return false;
				NativeLibrary.Free(handle);
				return true;
			}
			catch (Exception) { return false; }
		}

		static bool HasFont(string dir)
		{
			try
			{
				return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
					.Any(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase));
			}
			catch (IOException) { return false; }
			catch (UnauthorizedAccessException) { return false; }
		}
	}

	public static class LightforgeChecks
	{
		/// <summary>Room Lightforge's own files need, with some to spare (the game itself needs more, chosen in Lightforge).</summary>
		public const long NeededBytes = 300L * 1024 * 1024;

		public static List<SystemCheck> Evaluate(SystemInfo info)
		{
			var list = new List<SystemCheck>
			{
				new SystemCheck { Key = "lf.check.distro", State = CheckState.Ok, Found = info.Distribution },
				new SystemCheck { Key = "lf.check.arch", State = info.Architecture == "x86_64" ? CheckState.Ok : CheckState.Fail, Found = info.Architecture },
			};
			var glibc = new SystemCheck { Key = "lf.check.glibc", Found = info.Glibc };
			var v = Regex.Match(info.Glibc ?? "", @"\A(\d+)\.(\d+)");
			if (!v.Success) glibc.State = CheckState.Fail;
			else
			{
				int major = int.Parse(v.Groups[1].Value), minor = int.Parse(v.Groups[2].Value);
				// Lightforge asks for 2.36; its window needs 2.35 (it runs on Ubuntu 22.04), its authors have not tested that.
				glibc.State = major > 2 || (major == 2 && minor >= 36) ? CheckState.Ok : major == 2 && minor >= 35 ? CheckState.Warn : CheckState.Fail;
			}
			list.Add(glibc);
			list.Add(new SystemCheck { Key = "lf.check.fonts", State = info.HasFonts ? CheckState.Ok : CheckState.Warn });
			var window = new SystemCheck { Key = "lf.check.window", State = info.MissingWindowLibraries.Count == 0 ? CheckState.Ok : CheckState.Warn };
			if (window.State != CheckState.Ok)
			{
				window.Found = string.Join(", ", info.MissingWindowLibraries);
				string cmd = InstallCommand(WindowPackages(info.MissingWindowLibraries, info.Family), info.Family);
				window.Fix = cmd != null ? T.F("lf.fix.window.cmd", cmd) : T.S("lf.fix.window");
			}
			list.Add(window);
			var bit32 = new SystemCheck { Key = "lf.check.32bit", State = info.Has32BitLoader ? CheckState.Ok : CheckState.Warn };
			if (!info.Has32BitLoader && ThirtyTwoBitCommand(info.Family) != null) bit32.Fix = T.F("lf.fix.32bit.cmd", ThirtyTwoBitCommand(info.Family));
			list.Add(bit32);
			list.Add(new SystemCheck
			{
				Key = "lf.check.disk",
				State = info.FreeBytes < 0 ? CheckState.Warn : info.FreeBytes >= NeededBytes ? CheckState.Ok : CheckState.Fail,
				Found = info.FreeBytes < 0 ? null : (info.FreeBytes / (1024.0 * 1024 * 1024)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB",
			});
			return list;
		}

		public static bool CanInstall(IEnumerable<SystemCheck> checks) { return checks.All(c => c.State != CheckState.Fail); }

		/// <summary>The package of each window library in a family, in the order of WindowLibraries.</summary>
		static readonly Dictionary<string, string[]> WindowPackageNames = new Dictionary<string, string[]>
		{
			{ "debian", new[] { "libudev1", "libxkbcommon0", "libxkbcommon-x11-0", "libx11-6", "libx11-xcb1", "libxcursor1", "libxi6" } },
			{ "fedora", new[] { "systemd-libs", "libxkbcommon", "libxkbcommon-x11", "libX11", "libX11-xcb", "libXcursor", "libXi" } },
			{ "arch", new[] { "systemd-libs", "libxkbcommon", "libxkbcommon-x11", "libx11", "libx11", "libxcursor", "libxi" } },
			{ "suse", new[] { "libudev1", "libxkbcommon0", "libxkbcommon-x11-0", "libX11-6", "libX11-xcb1", "libXcursor1", "libXi6" } },
		};

		/// <summary>The packages that provide the missing window libraries in a family, or null for an unknown family.</summary>
		public static string WindowPackages(IEnumerable<string> missing, string family)
		{
			string[] names;
			if (family == null || !WindowPackageNames.TryGetValue(family, out names)) return null;
			var list = missing.Select(l => Array.IndexOf(SystemInfo.WindowLibraries, l)).Where(i => i >= 0).Select(i => names[i]).Distinct().ToList();
			return list.Count == 0 ? null : string.Join(" ", list);
		}

		/// <summary>The command that installs packages in a family, or null.</summary>
		public static string InstallCommand(string packages, string family)
		{
			if (string.IsNullOrEmpty(packages)) return null;
			switch (family)
			{
				case "debian": return "sudo apt install " + packages;
				case "fedora": return "sudo dnf install " + packages;
				case "arch": return "sudo pacman -S " + packages;
				case "suse": return "sudo zypper install " + packages;
				default: return null;
			}
		}

		/// <summary>The 32-bit libraries Battle.net needs, as Lightforge's own install guide names them for each family.</summary>
		public static string ThirtyTwoBitCommand(string family)
		{
			switch (family)
			{
				case "debian": return "sudo dpkg --add-architecture i386 && sudo apt update && sudo apt install wine32:i386";
				case "fedora": return "sudo dnf install wine.i686";
				case "arch": return "sudo pacman -S wine";
				case "suse": return "sudo zypper install wine-32bit";
				default: return null;
			}
		}
	}

	/// <summary>Where Lightforge's files go: the same places its own installer uses, all in the player's home folder.</summary>
	public sealed class LightforgePlan
	{
		public string Bin, Apps, Icons;
		public string Cli { get { return Path.Combine(Bin, "lightforge"); } }
		public string Gui { get { return Path.Combine(Bin, "lightforge-gui"); } }
		public string Desktop { get { return Path.Combine(Apps, LightforgePackage.DesktopEntry); } }

		/// <summary>The plan for a home folder; XDG_DATA_HOME is used only when it is an absolute path, as the standard says.</summary>
		public static LightforgePlan For(string home, string xdgDataHome)
		{
			if (string.IsNullOrEmpty(home) || !Path.IsPathRooted(home)) throw new InvalidOperationException("The home folder is unknown.");
			string data = !string.IsNullOrEmpty(xdgDataHome) && Path.IsPathRooted(xdgDataHome) ? xdgDataHome : Path.Combine(home, ".local", "share");
			return new LightforgePlan { Bin = Path.Combine(home, ".local", "bin"), Apps = Path.Combine(data, "applications"), Icons = Path.Combine(data, "icons") };
		}
	}

	public static class LightforgeInstaller
	{
		const long MaxDownload = 200L * 1024 * 1024;
		const long MaxUnpacked = 256L * 1024 * 1024;

		/// <summary>
		/// Downloads the pinned package and its release's checksum list from github.com. Both must name the pinned
		/// SHA-256, or UnverifiedDownload is thrown and the file is deleted.
		/// </summary>
		public static async Task<string> DownloadAsync(IWeb web, string work, CancellationToken ct)
		{
			Directory.CreateDirectory(work);
			string sums = Path.Combine(work, "SHA256SUMS");
			await web.DownloadAsync(LightforgePackage.SumsUrl, sums, 64 * 1024, ct).ConfigureAwait(false);
			if (Checksums.Find(File.ReadAllText(sums), LightforgePackage.FileName) != LightforgePackage.Sha256)
				throw new UnverifiedDownload("Lightforge's checksum list does not name the expected package, so nothing was installed.");
			string tar = Path.Combine(work, LightforgePackage.FileName);
			string sha = await web.DownloadAsync(LightforgePackage.Url, tar, MaxDownload, ct).ConfigureAwait(false);
			if (!string.Equals(sha, LightforgePackage.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				try { File.Delete(tar); } catch (IOException) { }
				throw new UnverifiedDownload("The Lightforge download does not match its published checksum, so nothing was installed.");
			}
			return tar;
		}

		/// <summary>
		/// Unpacks the package into a new folder in work. Only plain files and folders inside the release's own top
		/// folder are taken; a link, a device or a path that leaves it refuses the whole package.
		/// </summary>
		public static string Unpack(string tarGz, string work, CancellationToken ct)
		{
			string top = "lightforge-" + LightforgePackage.Version + "/";
			string dest = Path.Combine(work, "unpacked");
			if (Directory.Exists(dest)) Directory.Delete(dest, true);
			Directory.CreateDirectory(dest);
			long total = 0;
			int count = 0;
			using (var file = File.OpenRead(tarGz))
			using (var gz = new GZipStream(file, CompressionMode.Decompress))
			using (var reader = new TarReader(gz))
			{
				TarEntry e;
				while ((e = reader.GetNextEntry()) != null)
				{
					ct.ThrowIfCancellationRequested();
					if (e.EntryType == TarEntryType.GlobalExtendedAttributes) continue;
					string name = e.Name.Replace('\\', '/');
					if (++count > 2000) throw new InvalidDataException("The Lightforge package has too many files.");
					bool folder = e.EntryType == TarEntryType.Directory;
					bool plain = e.EntryType == TarEntryType.RegularFile || e.EntryType == TarEntryType.V7RegularFile;
					if (!folder && !plain) throw new InvalidDataException("The Lightforge package holds a link or special file (" + name + "), so it was refused.");
					string rel = name.TrimEnd('/') + (folder ? "/" : "");
					if (rel == top) continue;
					if (!rel.StartsWith(top, StringComparison.Ordinal) || rel.Split('/').Any(p => p == ".." || p == ".") || rel.Contains("//") || rel.Any(char.IsControl))
						throw new InvalidDataException("The Lightforge package has an unexpected path (" + name + "), so it was refused.");
					string path = Path.Combine(dest, rel.Substring(top.Length).TrimEnd('/'));
					if (folder) { Directory.CreateDirectory(path); continue; }
					total += e.Length;
					if (e.Length > MaxDownload || total > MaxUnpacked) throw new InvalidDataException("The Lightforge package is larger than expected.");
					Directory.CreateDirectory(Path.GetDirectoryName(path));
					using (var output = File.Create(path))
						if (e.DataStream != null) e.DataStream.CopyTo(output);
				}
			}
			foreach (string need in new[] { "lightforge", "lightforge-gui", LightforgePackage.DesktopEntry, "LICENSE" })
				if (!File.Exists(Path.Combine(dest, need))) throw new InvalidDataException("The Lightforge package is missing " + need + ".");
			foreach (string program in new[] { "lightforge", "lightforge-gui" })
				if (!IsElf64(Path.Combine(dest, program))) throw new InvalidDataException(program + " in the Lightforge package is not a 64-bit Linux program.");
			return dest;
		}

		static bool IsElf64(string path)
		{
			var head = new byte[5];
			using (var f = File.OpenRead(path))
				if (f.Read(head, 0, 5) != 5) return false;
			return head[0] == 0x7F && head[1] == 'E' && head[2] == 'L' && head[3] == 'F' && head[4] == 2;
		}

		/// <summary>The files to place: unpacked source, target and Unix mode.</summary>
		public static List<Tuple<string, string, UnixFileMode>> Files(string unpacked, LightforgePlan plan)
		{
			const UnixFileMode program = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
			const UnixFileMode data = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
			var list = new List<Tuple<string, string, UnixFileMode>>
			{
				Tuple.Create(Path.Combine(unpacked, "lightforge-gui"), plan.Gui, program),
				Tuple.Create(Path.Combine(unpacked, "lightforge"), plan.Cli, program),
				Tuple.Create(Path.Combine(unpacked, LightforgePackage.DesktopEntry), plan.Desktop, data),
			};
			string icons = Path.Combine(unpacked, "icons", "hicolor");
			if (Directory.Exists(icons))
				foreach (string f in Directory.GetFiles(icons, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
					if (LightforgePackage.IsIcon(Path.GetFileName(f)))
						list.Add(Tuple.Create(f, Path.Combine(plan.Icons, "hicolor", Path.GetRelativePath(icons, f)), data));
			return list;
		}

		/// <summary>The menu entry with Lightforge's full path, as its own installer writes it. Null when the entry is not the expected one.</summary>
		public static string Desktop(string text, string gui)
		{
			var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
			int exec = lines.IndexOf("Exec=lightforge-gui"), tryExec = lines.IndexOf("TryExec=lightforge-gui");
			if (exec < 0 || !lines.Contains("[Desktop Entry]")) return null;
			lines[exec] = "Exec=" + QuoteExec(gui);
			if (tryExec >= 0) lines[tryExec] = "TryExec=" + gui;
			return string.Join("\n", lines);
		}

		/// <summary>A program path for a menu entry's Exec line, quoted the way the desktop entry standard asks when needed.</summary>
		public static string QuoteExec(string path)
		{
			if (Regex.IsMatch(path, @"\A[A-Za-z0-9/_.+-]+\z")) return path;
			var sb = new StringBuilder("\"");
			foreach (char c in path)
			{
				if (c == '"' || c == '`' || c == '$' || c == '\\') sb.Append('\\');
				sb.Append(c);
			}
			// A percent sign is a field code in an Exec line; a literal one is written twice.
			return sb.Append('"').ToString().Replace("%", "%%");
		}

		/// <summary>
		/// Puts the unpacked files in place. A file already there is kept as a backup until every file is placed, and
		/// put back if anything fails; a backup that cannot be put back stays next to it (".eldoui-old"). A target
		/// that is a link or a folder stops the install before anything changes.
		/// </summary>
		public static void Place(string unpacked, LightforgePlan plan, CancellationToken ct)
		{
			var files = Files(unpacked, plan);
			foreach (var f in files)
			{
				if (Directory.Exists(f.Item2) || new FileInfo(f.Item2).LinkTarget != null)
					throw new IOException(f.Item2 + " is not a plain file, so nothing was changed.");
			}
			string desktop = Desktop(File.ReadAllText(files[2].Item1), plan.Gui);
			if (desktop == null) throw new InvalidDataException("Lightforge's menu entry is not the expected one, so nothing was changed.");
			var staged = new List<string>();
			var backups = new List<Tuple<string, string>>();
			var placed = new List<string>();
			try
			{
				foreach (var f in files)
				{
					ct.ThrowIfCancellationRequested();
					Directory.CreateDirectory(Path.GetDirectoryName(f.Item2));
					string next = f.Item2 + ".eldoui-new";
					if (f.Item2 == plan.Desktop) File.WriteAllText(next, desktop, new UTF8Encoding(false));
					else File.Copy(f.Item1, next, true);
					File.SetUnixFileMode(next, f.Item3);
					staged.Add(next);
				}
				ct.ThrowIfCancellationRequested();
				foreach (var f in files)
				{
					if (File.Exists(f.Item2))
					{
						string old = f.Item2 + ".eldoui-old";
						File.Move(f.Item2, old, true);
						backups.Add(Tuple.Create(f.Item2, old));
					}
					File.Move(f.Item2 + ".eldoui-new", f.Item2);
					staged.Remove(f.Item2 + ".eldoui-new");
					placed.Add(f.Item2);
				}
			}
			catch
			{
				foreach (string p in placed) TryDelete(p);
				foreach (var b in backups)
					try { File.Move(b.Item2, b.Item1, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
				backups.Clear();
				throw;
			}
			finally
			{
				foreach (string s in staged) TryDelete(s);
				foreach (var b in backups) TryDelete(b.Item2);
			}
		}

		static void TryDelete(string path)
		{
			try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
		}
	}

	public sealed class ProcessOutput
	{
		public int ExitCode;
		public string Output = "";
		public bool TimedOut;
	}

	/// <summary>Runs a program with its arguments as a list: no shell, so spaces or symbols in a path are only text.</summary>
	public interface IProcessRunner
	{
		Task<ProcessOutput> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct);
		/// <summary>Starts a program and leaves it running (opening Lightforge's window).</summary>
		void Start(string file, IReadOnlyList<string> args);
	}

	public sealed class ProcessRunner : IProcessRunner
	{
		public async Task<ProcessOutput> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
		{
			var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
			foreach (string a in args) psi.ArgumentList.Add(a);
			using (var p = Process.Start(psi))
			{
				p.StandardInput.Close();
				var output = p.StandardOutput.ReadToEndAsync();
				var errors = p.StandardError.ReadToEndAsync();
				using (var limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
				{
					limit.CancelAfter(timeout);
					try { await p.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
					catch (OperationCanceledException)
					{
						try { p.Kill(true); } catch (InvalidOperationException) { }
						ct.ThrowIfCancellationRequested();
						return new ProcessOutput { ExitCode = -1, TimedOut = true };
					}
				}
				string text = await output.ConfigureAwait(false);
				await errors.ConfigureAwait(false);
				return new ProcessOutput { ExitCode = p.ExitCode, Output = text.Length > 64 * 1024 ? text.Substring(0, 64 * 1024) : text };
			}
		}

		public void Start(string file, IReadOnlyList<string> args)
		{
			var psi = new ProcessStartInfo(file) { UseShellExecute = false };
			foreach (string a in args) psi.ArgumentList.Add(a);
			using (Process.Start(psi)) { }
		}
	}

	/// <summary>What Lightforge says about itself, read with its read-only commands.</summary>
	public sealed class LightforgeStatus
	{
		public bool Installed;
		/// <summary>The version it reports, and whether it is the release the assistant knows how to read.</summary>
		public string Version;
		public bool Known;
		public bool? Ready;
		public List<string> Outstanding = new List<string>();
		/// <summary>Where Lightforge puts WoW: Forever, and whether it is installed there.</summary>
		public string ForeverDir;
		public bool ForeverInstalled;
		/// <summary>The text key of what went wrong while reading, or null.</summary>
		public string Problem;

		/// <summary>The AddOns folder of Lightforge's WoW: Forever, once the game is installed there.</summary>
		public string ForeverAddOns
		{
			get { return ForeverInstalled && ForeverDir != null ? Path.Combine(ForeverDir, "Interface", "AddOns") : null; }
		}
	}

	public sealed class LightforgeProbe
	{
		/// <summary>The only Lightforge commands the assistant runs. Each one only reports; none changes anything.</summary>
		public static readonly IReadOnlyList<string[]> ReadOnlyCommands = new[]
		{
			new[] { "--version" },
			new[] { "setup", "--status" },
			new[] { "status", "--product", LightforgePackage.ProductCode, "--offline" },
		};

		readonly IProcessRunner _runner;
		readonly TimeSpan _timeout;
		public LightforgeProbe(IProcessRunner runner, TimeSpan? timeout = null) { _runner = runner; _timeout = timeout ?? TimeSpan.FromSeconds(20); }

		Task<ProcessOutput> Run(string cli, string[] args, CancellationToken ct)
		{
			if (!ReadOnlyCommands.Any(c => c.SequenceEqual(args))) throw new InvalidOperationException("Only Lightforge's read-only commands are run.");
			return _runner.RunAsync(cli, args, _timeout, ct);
		}

		public async Task<LightforgeStatus> ReadAsync(LightforgePlan plan, CancellationToken ct)
		{
			var s = new LightforgeStatus();
			if (!File.Exists(plan.Cli)) return s;
			s.Installed = true;
			var version = await Run(plan.Cli, ReadOnlyCommands[0], ct).ConfigureAwait(false);
			var m = Regex.Match(version.Output.Trim(), @"\Alightforge (\S{1,40})\z");
			if (version.TimedOut || version.ExitCode != 0 || !m.Success) { s.Problem = "lf.problem.version"; return s; }
			s.Version = m.Groups[1].Value;
			s.Known = s.Version == LightforgePackage.Version;
			// Another version may print something else: it is shown as it is, and not read further.
			if (!s.Known) { s.Problem = "lf.problem.unknown"; return s; }

			var setup = await Run(plan.Cli, ReadOnlyCommands[1], ct).ConfigureAwait(false);
			var ready = Regex.Match(setup.Output, @"^ready\s+(true|false)(?:\s+outstanding:\s*\[([^\]]*)\])?\s*$", RegexOptions.Multiline);
			if (setup.TimedOut || setup.ExitCode != 0 || !ready.Success) { s.Problem = "lf.problem.status"; return s; }
			s.Ready = ready.Groups[1].Value == "true";
			s.Outstanding = ready.Groups[2].Value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

			var status = await Run(plan.Cli, ReadOnlyCommands[2], ct).ConfigureAwait(false);
			string product = Field(status.Output, "product"), dir = Field(status.Output, "install dir"), installed = Field(status.Output, "installed");
			if (status.TimedOut || status.ExitCode != 0 || product != LightforgePackage.ProductCode || dir == null || installed == null) { s.Problem = "lf.problem.status"; return s; }
			// Only the Forever folder is taken, never the retail game or another flavor.
			if (!Path.IsPathRooted(dir) || Path.GetFileName(dir.TrimEnd('/')) != LightforgePackage.ProductFolder) { s.Problem = "lf.problem.product"; return s; }
			s.ForeverDir = dir.TrimEnd('/');
			s.ForeverInstalled = installed == "true" && Directory.Exists(s.ForeverDir);
			return s;
		}

		static string Field(string text, string name)
		{
			var m = Regex.Match(text ?? "", "^" + Regex.Escape(name) + @"\s{2,}(.+?)\s*$", RegexOptions.Multiline);
			return m.Success ? m.Groups[1].Value : null;
		}
	}
}
