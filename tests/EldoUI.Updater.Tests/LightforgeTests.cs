using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EldoUI.Updater;
using EldoUI.Updater.Core;
using Xunit;
using Xunit.Abstractions;

namespace EldoUI.Updater.Tests
{
	public class LightforgeCheckTests
	{
		static SystemInfo Good() { return new SystemInfo { Distribution = "Fedora Linux 42", Architecture = "x86_64", Glibc = "2.41", FreeBytes = 50L << 30, Has32BitLoader = true, HasFonts = true }; }

		static CheckState State(SystemInfo info, string key) { return LightforgeChecks.Evaluate(info).Single(c => c.Key == key).State; }

		[Fact]
		public void AGoodSystemPasses()
		{
			var checks = LightforgeChecks.Evaluate(Good());
			Assert.All(checks, c => Assert.Equal(CheckState.Ok, c.State));
			Assert.True(LightforgeChecks.CanInstall(checks));
			Assert.Equal("50.0 GB", checks.Single(c => c.Key == "lf.check.disk").Found);
		}

		[Theory]
		[InlineData("2.36", CheckState.Ok)]
		[InlineData("2.40", CheckState.Ok)]
		[InlineData("3.0", CheckState.Ok)]
		[InlineData("2.35", CheckState.Warn)]
		[InlineData("2.34", CheckState.Warn)]
		[InlineData("2.31", CheckState.Fail)]
		[InlineData(null, CheckState.Fail)]
		[InlineData("musl", CheckState.Fail)]
		public void GlibcVersions(string glibc, CheckState state)
		{
			var info = Good();
			info.Glibc = glibc;
			Assert.Equal(state, State(info, "lf.check.glibc"));
			Assert.Equal(state != CheckState.Fail, LightforgeChecks.CanInstall(LightforgeChecks.Evaluate(info)));
		}

		[Fact]
		public void OtherProblemsAreShown()
		{
			var info = Good();
			info.Architecture = "aarch64";
			Assert.Equal(CheckState.Fail, State(info, "lf.check.arch"));
			info = Good();
			info.FreeBytes = 100L << 20;
			Assert.Equal(CheckState.Fail, State(info, "lf.check.disk"));
			info.FreeBytes = -1;
			Assert.Equal(CheckState.Warn, State(info, "lf.check.disk"));
			info = Good();
			info.Has32BitLoader = false;
			info.HasFonts = false;
			Assert.Equal(CheckState.Warn, State(info, "lf.check.32bit"));
			Assert.Equal(CheckState.Warn, State(info, "lf.check.fonts"));
			Assert.True(LightforgeChecks.CanInstall(LightforgeChecks.Evaluate(info)));
		}

		[Fact]
		public void ReadsTheDistributionName()
		{
			using (var t = new TempDir())
			{
				File.WriteAllText(t.Sub("os-release"), "NAME=\"Ubuntu\"\nPRETTY_NAME=\"Ubuntu 24.04.1 LTS\"\nID=ubuntu\n");
				Assert.Equal("Ubuntu 24.04.1 LTS", SystemInfo.OsRelease(t.Sub("os-release")));
				Assert.Null(SystemInfo.OsRelease(t.Sub("missing")));
			}
		}

		[Fact]
		public void PlansInTheHomeFolderOnly()
		{
			var plan = LightforgePlan.For("/home/a b", null);
			Assert.Equal("/home/a b/.local/bin/lightforge-gui", plan.Gui.Replace('\\', '/'));
			Assert.Equal("/home/a b/.local/share/applications/pro.tuxcore.Lightforge.desktop", plan.Desktop.Replace('\\', '/'));
			Assert.Equal("/home/a b/.local/share/icons", LightforgePlan.For("/home/a b", "relative/data").Icons.Replace('\\', '/'));
			Assert.Equal("/data/icons", LightforgePlan.For("/home/a b", "/data").Icons.Replace('\\', '/'));
			Assert.Throws<InvalidOperationException>(() => LightforgePlan.For("", null));
		}

		[Theory]
		[InlineData("/home/a/.local/bin/lightforge-gui", "/home/a/.local/bin/lightforge-gui")]
		[InlineData("/home/a b/.local/bin/lightforge-gui", "\"/home/a b/.local/bin/lightforge-gui\"")]
		[InlineData("/home/$x\"`/lightforge-gui", "\"/home/\\$x\\\"\\`/lightforge-gui\"")]
		[InlineData("/home/100%/lightforge-gui", "\"/home/100%%/lightforge-gui\"")]
		public void MenuEntryPathsAreQuoted(string path, string exec)
		{
			Assert.Equal(exec, LightforgeInstaller.QuoteExec(path));
		}

		[Fact]
		public void TheMenuEntryGetsTheFullPath()
		{
			string entry = "[Desktop Entry]\nName=Lightforge\nExec=lightforge-gui\nTryExec=lightforge-gui\nIcon=pro.tuxcore.Lightforge\n";
			string written = LightforgeInstaller.Desktop(entry, "/home/a b/.local/bin/lightforge-gui");
			Assert.Contains("\nExec=\"/home/a b/.local/bin/lightforge-gui\"\n", written);
			Assert.Contains("\nTryExec=/home/a b/.local/bin/lightforge-gui\n", written);
			Assert.Contains("Icon=pro.tuxcore.Lightforge", written);
			Assert.Null(LightforgeInstaller.Desktop("[Desktop Entry]\nExec=sh -c something\n", "/x"));
		}
	}

	/// <summary>A Lightforge package made for the tests, in the shape of the real one.</summary>
	static class FakePackage
	{
		static readonly byte[] Elf = { 0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0 };
		const string Top = "lightforge-" + LightforgePackage.Version + "/";

		public static string Make(string dir, Action<TarWriter> extra = null, bool withGui = true, bool elf = true, string top = Top)
		{
			string path = Path.Combine(dir, "pkg-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tar.gz");
			using (var file = File.Create(path))
			using (var gz = new GZipStream(file, CompressionLevel.Fastest))
			using (var w = new TarWriter(gz, TarEntryFormat.Pax))
			{
				w.WriteEntry(new PaxTarEntry(TarEntryType.Directory, top));
				Add(w, top + "lightforge", elf ? Elf : Encoding.ASCII.GetBytes("#!/bin/sh"));
				if (withGui) Add(w, top + "lightforge-gui", Elf);
				Add(w, top + "pro.tuxcore.Lightforge.desktop", Encoding.UTF8.GetBytes("[Desktop Entry]\nType=Application\nName=Lightforge\nExec=lightforge-gui\nTryExec=lightforge-gui\nIcon=pro.tuxcore.Lightforge\n"));
				Add(w, top + "LICENSE", Encoding.UTF8.GetBytes("GNU GENERAL PUBLIC LICENSE"));
				Add(w, top + "icons/hicolor/48x48/apps/pro.tuxcore.Lightforge.png", new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' });
				Add(w, top + "icons/hicolor/48x48/apps/other.png", new byte[] { 1 });
				if (extra != null) extra(w);
			}
			return path;
		}

		public static void Add(TarWriter w, string name, byte[] data)
		{
			var e = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(data) };
			w.WriteEntry(e);
		}
	}

	public class LightforgeUnpackTests
	{
		[Fact]
		public void UnpacksTheExpectedFiles()
		{
			using (var t = new TempDir())
			{
				string dest = LightforgeInstaller.Unpack(FakePackage.Make(t.Path), t.Sub("work"), CancellationToken.None);
				Assert.True(File.Exists(Path.Combine(dest, "lightforge-gui")));
				var files = LightforgeInstaller.Files(dest, LightforgePlan.For("/home/p", null));
				Assert.Equal(4, files.Count);
				Assert.Contains(files, f => f.Item2.Replace('\\', '/') == "/home/p/.local/share/icons/hicolor/48x48/apps/pro.tuxcore.Lightforge.png");
				Assert.DoesNotContain(files, f => f.Item2.EndsWith("other.png"));
			}
		}

		public static IEnumerable<object[]> Bad()
		{
			yield return new object[] { (Action<TarWriter>)(w => w.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "lightforge-" + LightforgePackage.Version + "/evil") { LinkName = "/etc/passwd" })) };
			yield return new object[] { (Action<TarWriter>)(w => w.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, "lightforge-" + LightforgePackage.Version + "/evil") { LinkName = "lightforge-" + LightforgePackage.Version + "/LICENSE" })) };
			yield return new object[] { (Action<TarWriter>)(w => FakePackage.Add(w, "lightforge-" + LightforgePackage.Version + "/../evil", new byte[] { 1 })) };
			yield return new object[] { (Action<TarWriter>)(w => FakePackage.Add(w, "other/evil", new byte[] { 1 })) };
			yield return new object[] { (Action<TarWriter>)(w => FakePackage.Add(w, "/etc/evil", new byte[] { 1 })) };
			yield return new object[] { (Action<TarWriter>)(w => w.WriteEntry(new PaxTarEntry(TarEntryType.Fifo, "lightforge-" + LightforgePackage.Version + "/pipe"))) };
		}

		[Theory]
		[MemberData(nameof(Bad))]
		public void LinksAndOutsidePathsRefuseThePackage(Action<TarWriter> extra)
		{
			using (var t = new TempDir())
			{
				Assert.Throws<InvalidDataException>(() => LightforgeInstaller.Unpack(FakePackage.Make(t.Path, extra), t.Sub("work"), CancellationToken.None));
				Assert.False(File.Exists(t.Sub("evil")));
			}
		}

		[Fact]
		public void AnIncompleteOrWrongPackageIsRefused()
		{
			using (var t = new TempDir())
			{
				Assert.Throws<InvalidDataException>(() => LightforgeInstaller.Unpack(FakePackage.Make(t.Path, withGui: false), t.Sub("w1"), CancellationToken.None));
				Assert.Throws<InvalidDataException>(() => LightforgeInstaller.Unpack(FakePackage.Make(t.Path, elf: false), t.Sub("w2"), CancellationToken.None));
				Assert.Throws<InvalidDataException>(() => LightforgeInstaller.Unpack(FakePackage.Make(t.Path, top: "lightforge-0.9/"), t.Sub("w3"), CancellationToken.None));
			}
		}

		[Fact]
		public async Task TheDownloadMustMatchThePinnedChecksumTwice()
		{
			using (var t = new TempDir())
			{
				string tar = FakePackage.Make(t.Path);
				string sums = t.Sub("sums");
				var web = new FakeWeb();
				web.Files[LightforgePackage.Url] = tar;
				web.Files[LightforgePackage.SumsUrl] = sums;
				// The checksum list names the pinned hash, but the file that arrives is another one.
				File.WriteAllText(sums, LightforgePackage.Sha256 + "  " + LightforgePackage.FileName + "\n");
				await Assert.ThrowsAsync<UnverifiedDownload>(() => LightforgeInstaller.DownloadAsync(web, t.Sub("w1"), CancellationToken.None));
				Assert.False(File.Exists(Path.Combine(t.Sub("w1"), LightforgePackage.FileName)));
				// The list names another hash: the package is not even downloaded.
				File.WriteAllText(sums, new string('a', 64) + "  " + LightforgePackage.FileName + "\n");
				int before = web.Downloads;
				await Assert.ThrowsAsync<UnverifiedDownload>(() => LightforgeInstaller.DownloadAsync(web, t.Sub("w2"), CancellationToken.None));
				Assert.Equal(before + 1, web.Downloads);
				Assert.StartsWith("https://github.com/LightforgeProject/lightforge/releases/download/v1.0.0-beta.7/", LightforgePackage.Url);
				Assert.True(Release.IsGitHubDownload(LightforgePackage.Url) && Release.IsGitHubDownload(LightforgePackage.SumsUrl));
			}
		}
	}

	public class LightforgePlaceTests
	{
		static LightforgePlan Plan(TempDir t) { return LightforgePlan.For(t.Sub("home dir"), null); }

		[LinuxFact]
		public void PlacesFilesWithTheirModes()
		{
			using (var t = new TempDir())
			{
				var plan = Plan(t);
				string dest = LightforgeInstaller.Unpack(FakePackage.Make(t.Path), t.Sub("work"), CancellationToken.None);
				LightforgeInstaller.Place(dest, plan, CancellationToken.None);
				Assert.True(File.GetUnixFileMode(plan.Gui).HasFlag(UnixFileMode.UserExecute));
				Assert.False(File.GetUnixFileMode(plan.Desktop).HasFlag(UnixFileMode.UserExecute));
				Assert.Contains("Exec=\"" + plan.Gui + "\"", File.ReadAllText(plan.Desktop));
				Assert.True(File.Exists(Path.Combine(plan.Icons, "hicolor/48x48/apps/pro.tuxcore.Lightforge.png")));
				Assert.Empty(Directory.GetFiles(t.Sub("home dir"), "*.eldoui-*", SearchOption.AllDirectories));
			}
		}

		[LinuxFact]
		public void ReplacesAnOlderInstallAndLeavesNothingBehind()
		{
			using (var t = new TempDir())
			{
				var plan = Plan(t);
				Directory.CreateDirectory(plan.Bin);
				File.WriteAllText(plan.Cli, "old");
				File.WriteAllText(Path.Combine(plan.Bin, "my-tool"), "mine");
				LightforgeInstaller.Place(LightforgeInstaller.Unpack(FakePackage.Make(t.Path), t.Sub("work"), CancellationToken.None), plan, CancellationToken.None);
				Assert.NotEqual("old", File.ReadAllText(plan.Cli));
				Assert.Equal("mine", File.ReadAllText(Path.Combine(plan.Bin, "my-tool")));
				Assert.Empty(Directory.GetFiles(plan.Bin, "*.eldoui-*"));
			}
		}

		[LinuxFact]
		public void ALinkWhereAFileGoesStopsBeforeAnyChange()
		{
			using (var t = new TempDir())
			{
				var plan = Plan(t);
				Directory.CreateDirectory(plan.Bin);
				File.WriteAllText(plan.Cli, "old");
				File.WriteAllText(t.Sub("elsewhere"), "do not touch");
				File.CreateSymbolicLink(plan.Gui, t.Sub("elsewhere"));
				string dest = LightforgeInstaller.Unpack(FakePackage.Make(t.Path), t.Sub("work"), CancellationToken.None);
				Assert.Throws<IOException>(() => LightforgeInstaller.Place(dest, plan, CancellationToken.None));
				Assert.Equal("old", File.ReadAllText(plan.Cli));
				Assert.Equal("do not touch", File.ReadAllText(t.Sub("elsewhere")));
				Assert.False(File.Exists(plan.Desktop));
			}
		}

		[LinuxFact]
		public void AFailureHalfwayPutsTheOldFilesBack()
		{
			using (var t = new TempDir())
			{
				var plan = Plan(t);
				Directory.CreateDirectory(plan.Bin);
				File.WriteAllText(plan.Gui, "old gui");
				File.WriteAllText(plan.Cli, "old cli");
				// The icons folder cannot be made: a file has its name.
				Directory.CreateDirectory(Path.GetDirectoryName(plan.Icons));
				File.WriteAllText(plan.Icons, "a file in the way");
				string dest = LightforgeInstaller.Unpack(FakePackage.Make(t.Path), t.Sub("work"), CancellationToken.None);
				Assert.ThrowsAny<IOException>(() => LightforgeInstaller.Place(dest, plan, CancellationToken.None));
				Assert.Equal("old gui", File.ReadAllText(plan.Gui));
				Assert.Equal("old cli", File.ReadAllText(plan.Cli));
				Assert.False(File.Exists(plan.Desktop));
				Assert.Empty(Directory.GetFiles(t.Sub("home dir"), "*.eldoui-*", SearchOption.AllDirectories));
			}
		}

		[LinuxFact]
		public void CancellingChangesNothing()
		{
			using (var t = new TempDir())
			using (var cancel = new CancellationTokenSource())
			{
				var plan = Plan(t);
				string dest = LightforgeInstaller.Unpack(FakePackage.Make(t.Path), t.Sub("work"), CancellationToken.None);
				cancel.Cancel();
				Assert.Throws<OperationCanceledException>(() => LightforgeInstaller.Place(dest, plan, cancel.Token));
				Assert.False(File.Exists(plan.Gui));
			}
		}
	}

	public class ProcessRunnerTests
	{
		[LinuxFact]
		public async Task ArgumentsAreNeverReadByAShell()
		{
			string tricky = "a b; rm -rf ~ $(touch x) `id` \"q\" & | > out";
			var o = await new ProcessRunner().RunAsync("/bin/echo", new[] { tricky }, TimeSpan.FromSeconds(10), CancellationToken.None);
			Assert.Equal(0, o.ExitCode);
			Assert.Equal(tricky + "\n", o.Output);
		}

		[LinuxFact]
		public async Task ATimeoutIsReportedNotWaitedOut()
		{
			var watch = System.Diagnostics.Stopwatch.StartNew();
			var o = await new ProcessRunner().RunAsync("/bin/sleep", new[] { "30" }, TimeSpan.FromMilliseconds(200), CancellationToken.None);
			Assert.True(o.TimedOut);
			Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
		}
	}

	sealed class FakeRunner : IProcessRunner
	{
		public readonly List<string[]> Calls = new List<string[]>();
		public readonly Dictionary<string, ProcessOutput> Answers = new Dictionary<string, ProcessOutput>();
		public int Started;

		public Task<ProcessOutput> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
		{
			Calls.Add(args.ToArray());
			ProcessOutput o;
			return Task.FromResult(Answers.TryGetValue(string.Join(" ", args), out o) ? o : new ProcessOutput { ExitCode = 2, Output = "unknown command" });
		}

		public void Start(string file, IReadOnlyList<string> args) { Started++; }

		public static FakeRunner Lightforge(string version, string installDir, bool installed)
		{
			var r = new FakeRunner();
			r.Answers["--version"] = new ProcessOutput { Output = "lightforge " + version + "\n" };
			r.Answers["setup --status"] = new ProcessOutput { Output = "-- proton          not installed\n   builds seen     0\n\nready           false  outstanding: [Proton, Prefix, BattleNet]\n" };
			r.Answers["status --product wowf --offline"] = new ProcessOutput { Output = "product         wowf\ninstall dir     " + installDir + "\nbuild key       -\ninstalled       " + (installed ? "true" : "false") + "\n\nbutton          [ INSTALL ]\n" };
			return r;
		}
	}

	public class LightforgeProbeTests
	{
		static LightforgePlan Installed(TempDir t)
		{
			var plan = LightforgePlan.For(t.Sub("home"), null);
			Directory.CreateDirectory(plan.Bin);
			File.WriteAllText(plan.Cli, "x");
			return plan;
		}

		[Fact]
		public async Task NotInstalledRunsNothing()
		{
			using (var t = new TempDir())
			{
				var runner = new FakeRunner();
				var s = await new LightforgeProbe(runner).ReadAsync(LightforgePlan.For(t.Sub("home"), null), CancellationToken.None);
				Assert.False(s.Installed);
				Assert.Empty(runner.Calls);
			}
		}

		[Fact]
		public async Task ReadsTheKnownVersionWithReadOnlyCommandsOnly()
		{
			using (var t = new TempDir())
			{
				string forever = t.Sub("home", "Games", "Blizzard", "games", "World of Warcraft", "_forever_");
				Directory.CreateDirectory(forever);
				var runner = FakeRunner.Lightforge(LightforgePackage.Version, forever, true);
				var s = await new LightforgeProbe(runner).ReadAsync(Installed(t), CancellationToken.None);
				Assert.Null(s.Problem);
				Assert.True(s.Known);
				Assert.False(s.Ready);
				Assert.Equal(new[] { "Proton", "Prefix", "BattleNet" }, s.Outstanding);
				Assert.Equal(Path.Combine(forever, "Interface", "AddOns"), s.ForeverAddOns);
				Assert.All(runner.Calls, c => Assert.Contains(LightforgeProbe.ReadOnlyCommands, r => r.SequenceEqual(c)));
				Assert.Equal(3, runner.Calls.Count);
				Assert.Equal(0, runner.Started);
			}
		}

		[Fact]
		public async Task AnotherVersionIsShownButNotRead()
		{
			using (var t = new TempDir())
			{
				var runner = FakeRunner.Lightforge("1.2.0", "/x/_forever_", true);
				var s = await new LightforgeProbe(runner).ReadAsync(Installed(t), CancellationToken.None);
				Assert.Equal("1.2.0", s.Version);
				Assert.False(s.Known);
				Assert.Equal("lf.problem.unknown", s.Problem);
				Assert.Single(runner.Calls);
				Assert.Null(s.ForeverAddOns);
			}
		}

		[Theory]
		[InlineData("/home/p/Games/World of Warcraft/_retail_", "lf.problem.product")]
		[InlineData("relative/_forever_", "lf.problem.product")]
		public async Task OnlyTheForeverFolderIsTaken(string dir, string problem)
		{
			using (var t = new TempDir())
			{
				var s = await new LightforgeProbe(FakeRunner.Lightforge(LightforgePackage.Version, dir, true)).ReadAsync(Installed(t), CancellationToken.None);
				Assert.Equal(problem, s.Problem);
				Assert.Null(s.ForeverAddOns);
			}
		}

		[Fact]
		public async Task FailuresAndTimeoutsAreNeverSuccess()
		{
			using (var t = new TempDir())
			{
				var plan = Installed(t);
				var runner = FakeRunner.Lightforge(LightforgePackage.Version, "/x/_forever_", true);
				runner.Answers["--version"] = new ProcessOutput { ExitCode = 1, Output = "lightforge " + LightforgePackage.Version };
				Assert.Equal("lf.problem.version", (await new LightforgeProbe(runner).ReadAsync(plan, CancellationToken.None)).Problem);
				runner = FakeRunner.Lightforge(LightforgePackage.Version, "/x/_forever_", true);
				runner.Answers["setup --status"] = new ProcessOutput { TimedOut = true, ExitCode = -1 };
				Assert.Equal("lf.problem.status", (await new LightforgeProbe(runner).ReadAsync(plan, CancellationToken.None)).Problem);
				runner = FakeRunner.Lightforge(LightforgePackage.Version, "/x/_forever_", true);
				runner.Answers["status --product wowf --offline"] = new ProcessOutput { Output = "something new" };
				Assert.Equal("lf.problem.status", (await new LightforgeProbe(runner).ReadAsync(plan, CancellationToken.None)).Problem);
				runner = FakeRunner.Lightforge(LightforgePackage.Version, "/x/_forever_", true);
				var s = await new LightforgeProbe(runner).ReadAsync(plan, CancellationToken.None);
				Assert.Null(s.Problem);
				Assert.False(s.ForeverInstalled); // Reported installed, but the folder is not there.
			}
		}

		[Fact]
		public void TheReadOnlyListHasNoProvisioningCommand()
		{
			var words = LightforgeProbe.ReadOnlyCommands.SelectMany(c => c).ToList();
			foreach (string forbidden in new[] { "play", "battlenet-run", "battlenet-setup", "preflight", "prefix-init", "download", "uninstall", "proton-install", "setup" })
				if (forbidden == "setup") Assert.All(LightforgeProbe.ReadOnlyCommands.Where(c => c[0] == "setup"), c => Assert.Equal("--status", c[1]));
				else Assert.DoesNotContain(forbidden, words);
		}
	}

	/// <summary>
	/// The real package, end to end, in a temporary home folder: set ELDOUI_LIGHTFORGE_TARBALL to the downloaded
	/// release file. It unpacks, places and then runs the installed program's read-only commands.
	/// </summary>
	public class LightforgeRealPackageTests
	{
		readonly ITestOutputHelper _out;
		public LightforgeRealPackageTests(ITestOutputHelper output) { _out = output; }

		public sealed class RealPackageFactAttribute : FactAttribute
		{
			public RealPackageFactAttribute()
			{
				string tarball = Environment.GetEnvironmentVariable("ELDOUI_LIGHTFORGE_TARBALL");
				if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(tarball) || !File.Exists(tarball)) Skip = "Needs Linux and ELDOUI_LIGHTFORGE_TARBALL";
			}
		}

		[RealPackageFact]
		public async Task InstallsTheRealReleaseInATemporaryHome()
		{
			string tarball = Environment.GetEnvironmentVariable("ELDOUI_LIGHTFORGE_TARBALL");
			using (var sha = System.Security.Cryptography.SHA256.Create())
			using (var f = File.OpenRead(tarball))
				Assert.Equal(LightforgePackage.Sha256, Web.Hex(sha.ComputeHash(f)));
			using (var t = new TempDir())
			{
				var plan = LightforgePlan.For(t.Sub("home with space"), null);
				string dest = LightforgeInstaller.Unpack(tarball, t.Sub("work"), CancellationToken.None);
				LightforgeInstaller.Place(dest, plan, CancellationToken.None);
				foreach (string f in Directory.GetFiles(t.Sub("home with space"), "*", SearchOption.AllDirectories).OrderBy(x => x))
					_out.WriteLine(Path.GetRelativePath(t.Path, f) + "  " + File.GetUnixFileMode(f));
				_out.WriteLine(File.ReadAllText(plan.Desktop));
				// The real program, run with the temporary home so nothing outside it is read or written.
				var runner = new HomeRunner(t.Sub("home with space"));
				var s = await new LightforgeProbe(runner).ReadAsync(plan, CancellationToken.None);
				_out.WriteLine("version " + s.Version + ", ready " + s.Ready + " (" + string.Join(", ", s.Outstanding) + "), Forever at " + s.ForeverDir + ", installed " + s.ForeverInstalled);
				Assert.Null(s.Problem);
				Assert.Equal(LightforgePackage.Version, s.Version);
				Assert.Equal(Path.Combine(t.Sub("home with space"), "Games", "Blizzard", "games", "World of Warcraft", "_forever_"), s.ForeverDir);
				Assert.False(s.ForeverInstalled);
			}
		}

		sealed class HomeRunner : IProcessRunner
		{
			readonly string _home;
			public HomeRunner(string home) { _home = home; }

			public async Task<ProcessOutput> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
			{
				var psi = new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
				foreach (string a in args) psi.ArgumentList.Add(a);
				psi.Environment["HOME"] = _home;
				psi.Environment.Remove("XDG_CONFIG_HOME");
				psi.Environment.Remove("XDG_DATA_HOME");
				psi.Environment.Remove("XDG_CACHE_HOME");
				using (var p = System.Diagnostics.Process.Start(psi))
				{
					string text = await p.StandardOutput.ReadToEndAsync();
					await p.WaitForExitAsync(ct);
					return new ProcessOutput { ExitCode = p.ExitCode, Output = text };
				}
			}

			public void Start(string file, IReadOnlyList<string> args) { throw new InvalidOperationException("Not in tests."); }
		}
	}
}
