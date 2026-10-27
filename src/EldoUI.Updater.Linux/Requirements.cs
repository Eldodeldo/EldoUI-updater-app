// What the updater itself needs on Linux, checked before it installs itself: a graphical session (X11, or
// XWayland on a Wayland desktop) and the system's fontconfig and X11 libraries, which its window draws with.
// FUSE is not required: without it the AppImage unpacks itself on each start instead of mounting, and the app
// menu and login entries say so, so they keep working. Every desktop distribution has all of this; a minimal
// or server install may not, and then the player is told what to install before anything changes.
// It also holds the distributions each release is tested on, which --check-system reports.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace EldoUI.Updater
{
	/// <summary>What the requirement check reads. Filled from the real system by Requirements.Read, or by hand in tests.</summary>
	public sealed class LinuxSystem
	{
		/// <summary>os-release: ID ("ubuntu"), ID_LIKE ("debian"), VERSION_ID ("24.04") and PRETTY_NAME.</summary>
		public string Id, VersionId, Name;
		public string[] IdLike = new string[0];
		/// <summary>DISPLAY is set: an X11 session, or a Wayland session with XWayland.</summary>
		public bool Display;
		/// <summary>WAYLAND_DISPLAY is set.</summary>
		public bool Wayland;
		public bool Fontconfig, X11, Fuse;
	}

	public enum Need { Display, Fontconfig, X11, Fuse }

	/// <summary>A distribution a release was tested on: its os-release ID and the VERSION_IDs (none for a rolling release).</summary>
	public sealed class TestedDistro
	{
		public string Id, Name;
		public string[] Versions;
	}

	public static class Requirements
	{
		/// <summary>
		/// The distributions this release was tested on: the AppImage started by an ordinary user, installed itself,
		/// found WoW: Forever in a Wine, Lutris or Steam prefix, installed EldoUI from its GitHub release, and
		/// removed itself again with --uninstall. The list on the project's wiki has the details.
		/// </summary>
		public static readonly TestedDistro[] Tested =
		{
			new TestedDistro { Id = "ubuntu", Name = "Ubuntu", Versions = new[] { "24.04", "22.04" } },
			new TestedDistro { Id = "linuxmint", Name = "Linux Mint", Versions = new[] { "22" } },
			new TestedDistro { Id = "debian", Name = "Debian", Versions = new[] { "12" } },
			new TestedDistro { Id = "fedora", Name = "Fedora", Versions = new[] { "44" } },
			new TestedDistro { Id = "arch", Name = "Arch Linux", Versions = null },
			new TestedDistro { Id = "opensuse-leap", Name = "openSUSE Leap", Versions = new[] { "15.5" } },
		};

		/// <summary>FUSE only makes the start faster; everything else stops the updater before it installs itself.</summary>
		public static bool Blocks(Need need) { return need != Need.Fuse; }

		public static List<Need> Missing(LinuxSystem s)
		{
			var list = new List<Need>();
			if (!s.Display) list.Add(Need.Display);
			if (!s.Fontconfig) list.Add(Need.Fontconfig);
			if (!s.X11) list.Add(Need.X11);
			if (!s.Fuse) list.Add(Need.Fuse);
			return list;
		}

		public static bool CanRun(LinuxSystem s) { return !Missing(s).Any(Blocks); }

		/// <summary>The package family: "debian", "fedora", "arch", "suse", or null when it is none of these.</summary>
		public static string Family(LinuxSystem s)
		{
			var ids = new[] { s.Id ?? "" }.Concat(s.IdLike ?? new string[0]).Select(x => x.ToLowerInvariant()).ToList();
			if (ids.Any(x => x == "debian" || x == "ubuntu")) return "debian";
			if (ids.Any(x => x == "fedora" || x == "rhel" || x == "centos")) return "fedora";
			if (ids.Any(x => x == "arch" || x == "steamos")) return "arch";
			if (ids.Any(x => x == "suse" || x == "opensuse" || x.StartsWith("opensuse", StringComparison.Ordinal) || x == "sles")) return "suse";
			return null;
		}

		/// <summary>The packages that provide a need in a family, or null for a need no package provides.</summary>
		public static string Packages(Need need, string family)
		{
			switch (need)
			{
				case Need.Fontconfig:
					return family == "debian" ? "libfontconfig1" : "fontconfig";
				case Need.X11:
					return family == "debian" ? "libx11-6 libice6 libsm6"
						: family == "fedora" ? "libX11 libICE libSM"
						: family == "suse" ? "libX11-6 libICE6 libSM6"
						: "libx11 libice libsm";
				case Need.Fuse:
					return "fuse3";
				default:
					return null;
			}
		}

		/// <summary>The command that installs a need's packages in a family, or null when the family is unknown.</summary>
		public static string InstallCommand(Need need, string family)
		{
			string pkgs = Packages(need, family);
			if (pkgs == null) return null;
			switch (family)
			{
				case "debian": return "sudo apt install " + pkgs;
				case "fedora": return "sudo dnf install " + pkgs;
				case "arch": return "sudo pacman -S " + pkgs;
				case "suse": return "sudo zypper install " + pkgs;
				default: return null;
			}
		}

		/// <summary>The tested entry for this system, or null when it was not among the tested ones.</summary>
		public static TestedDistro Match(LinuxSystem s)
		{
			return Tested.FirstOrDefault(t => string.Equals(t.Id, s.Id, StringComparison.OrdinalIgnoreCase)
				&& (t.Versions == null || t.Versions.Any(v => VersionMatches(v, s.VersionId))));
		}

		/// <summary>"22" matches 22, 22.1 and 22.2; "24.04" matches only 24.04.</summary>
		static bool VersionMatches(string tested, string actual)
		{
			if (string.IsNullOrEmpty(actual)) return false;
			return actual == tested || actual.StartsWith(tested + ".", StringComparison.Ordinal);
		}

		/// <summary>The tested distributions as one line: "Ubuntu 24.04 and 22.04, Linux Mint 22, …, Arch Linux".</summary>
		public static string TestedList()
		{
			return string.Join(", ", Tested.Select(t => t.Versions == null ? t.Name : t.Name + " " + string.Join(T.S("req.and"), t.Versions)));
		}

		/// <summary>One line per missing need, saying how to fix it on this distribution.</summary>
		public static string Advice(Need need, LinuxSystem s)
		{
			string family = Family(s);
			string cmd = InstallCommand(need, family);
			string how = cmd != null ? T.F("req.cmd", cmd) : T.F("req.pkg", Packages(need, family));
			switch (need)
			{
				case Need.Display: return s.Wayland ? T.S("req.wayland") : T.S("req.display");
				case Need.Fontconfig: return T.F("req.fontconfig", how);
				case Need.X11: return T.F("req.x11", how);
				default: return T.F("req.fuse", how);
			}
		}

		/// <summary>What --check-system prints, and what the player reads when the updater cannot start.</summary>
		public static string Report(LinuxSystem s, string version)
		{
			var sb = new StringBuilder();
			var missing = Missing(s);
			bool runs = !missing.Any(Blocks);
			sb.AppendLine(T.F("req.system", string.IsNullOrEmpty(s.Name) ? "Linux" : s.Name, version));
			sb.AppendLine(runs ? T.S("req.ready") : T.S("req.blocked"));
			foreach (Need n in missing) sb.AppendLine("- " + Advice(n, s));
			var tested = Match(s);
			sb.AppendLine(tested != null ? T.F("req.tested", s.Name) : T.F("req.untested", TestedList()));
			return sb.ToString().TrimEnd();
		}

		/// <summary>An app menu or login entry's Exec line. Without FUSE the AppImage is told to unpack itself.</summary>
		public static string DesktopExec(string app, string args, bool unpack)
		{
			string exec = "\"" + app.Replace("\"", "\\\"") + "\"" + (string.IsNullOrEmpty(args) ? "" : " " + args);
			return unpack ? "env APPIMAGE_EXTRACT_AND_RUN=1 " + exec : exec;
		}

		/// <summary>Reads an os-release file into a LinuxSystem (the other fields stay as they are).</summary>
		public static void ReadOsRelease(LinuxSystem s, string text)
		{
			foreach (string line in (text ?? "").Split('\n'))
			{
				var m = Regex.Match(line.Trim(), "^([A-Z_]+)=\"?([^\"]*)\"?$");
				if (!m.Success) continue;
				string v = m.Groups[2].Value.Trim();
				switch (m.Groups[1].Value)
				{
					case "ID": s.Id = v; break;
					case "ID_LIKE": s.IdLike = v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); break;
					case "VERSION_ID": s.VersionId = v; break;
					case "PRETTY_NAME": s.Name = v.Length > 80 ? v.Substring(0, 80) : v; break;
				}
			}
		}

		/// <summary>This system, read without changing anything.</summary>
		public static LinuxSystem Read()
		{
			var s = new LinuxSystem
			{
				Display = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")),
				Wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")),
				Fontconfig = Loads("libfontconfig.so.1"),
				X11 = Loads("libX11.so.6"),
				Fuse = FuseUsable(),
			};
			foreach (string f in new[] { "/etc/os-release", "/usr/lib/os-release" })
			{
				try
				{
					if (!File.Exists(f)) continue;
					ReadOsRelease(s, File.ReadAllText(f));
					break;
				}
				catch (IOException) { }
				catch (UnauthorizedAccessException) { }
			}
			return s;
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

		[DllImport("libc", EntryPoint = "access", SetLastError = true)]
		static extern int access(string path, int mode);

		/// <summary>True when the AppImage can mount itself: a fusermount on PATH, as the AppImage looks for it, and /dev/fuse open to this user.</summary>
		public static bool FuseUsable()
		{
			try
			{
				string path = Environment.GetEnvironmentVariable("PATH") ?? "";
				bool fusermount = path.Split(':').Where(d => d.Length > 0)
					.Any(d => File.Exists(Path.Combine(d, "fusermount3")) || File.Exists(Path.Combine(d, "fusermount")));
				const int R_OK = 4, W_OK = 2;
				return fusermount && access("/dev/fuse", R_OK | W_OK) == 0;
			}
			catch (Exception) { return false; }
		}
	}
}
