// Start-up on Linux: one running copy per user, install on first start, and the command-line switches.
//
//   (none)          first start from a download: install, then open the window
//   --tray          start hidden in the tray (autostart uses this)
//   --updated       started by a self-update (forgets the version it was waiting for)
//   --uninstall     remove the updater (EldoUI stays in the game)
//   --check-system  say whether this system has what the updater needs, and whether it was tested on it
//   --selftest      check the Linux search and settings in a temp folder and exit (CI)
//   --snapshot DIR  save pictures of the window in both languages and exit (CI)
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace EldoUI.Updater
{
	static class Program
	{
		[STAThread]
		static int Main(string[] args)
		{
			bool Has(string a) { return args.Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase)); }

			LinuxTexts.Apply();
			LinuxTexts.Use(Core.Settings.Load(Setup.SettingsPath).Language);

			if (Has("--selftest")) return SelfTest.Run(Console.Out);
			if (Has("--snapshot"))
			{
				int i = Array.FindIndex(args, a => string.Equals(a, "--snapshot", StringComparison.OrdinalIgnoreCase));
				return Snapshot.Run(i + 1 < args.Length ? args[i + 1] : "snapshots");
			}
			if (Has("--check-system"))
			{
				var system = Requirements.Read();
				Console.WriteLine(Requirements.Report(system, Setup.Version));
				// What WoW Linux setup checks before it installs Lightforge, with the same fixes it shows.
				Console.WriteLine();
				Console.WriteLine(T.S("lf.title") + ":");
				foreach (var c in LightforgeChecks.Evaluate(SystemInfo.Read(Setup.Home)))
				{
					string state = c.State == CheckState.Ok ? "ok  " : c.State == CheckState.Warn ? "note" : "FAIL";
					Console.WriteLine("  " + state + "  " + T.S(c.Key) + ": " + (c.Found ?? (c.State == CheckState.Ok ? T.S("lf.found.yes") : T.S("lf.found.none"))));
					if (c.State != CheckState.Ok) Console.WriteLine("        " + (c.Fix ?? T.S("lf.fix." + c.Key.Substring("lf.check.".Length) + (c.Key == "lf.check.glibc" ? (c.State == CheckState.Warn ? ".warn" : ".fail") : ""))));
				}
				return Requirements.CanRun(system) ? 0 : 1;
			}
			if (Has("--uninstall"))
			{
				Instance.Send("exit");
				Thread.Sleep(500);
				Setup.Uninstall();
				Console.WriteLine(T.S("uninstall.done"));
				return 0;
			}

			// Nothing is installed on a system the window cannot open on; the player is told what to add instead.
			var sys = Requirements.Read();
			if (!Requirements.CanRun(sys))
			{
				string report = Requirements.Report(sys, Setup.Version);
				Log.Write("Cannot start: " + report.Replace(Environment.NewLine, " | "));
				Console.Error.WriteLine(report);
				Setup.Notice(T.S("req.title"), report);
				return 1;
			}

			// A downloaded AppImage installs itself and starts the installed copy.
			if (Setup.IsAppImage && !Setup.RunningInstalled) return InstallAndStart();
			if (Setup.RunningInstalled) Setup.RefreshEntries();

			using (var instance = Instance.TryOwn())
			{
				if (instance == null)
				{
					// Already running: bring its window forward instead of starting a second copy.
					Instance.Send("show");
					return 0;
				}
				Log.Write("Started " + Setup.Version + (args.Length > 0 ? " " + string.Join(" ", args) : "") + (Setup.Unpacked ? " (unpacked, no FUSE)" : ""));
				try
				{
					return BuildApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
				}
				catch (Exception ex)
				{
					// Said in the log, the terminal and a desktop notice, instead of closing without a word.
					Log.Write("Could not start: " + ex);
					string message = T.F("error.start", ex.GetBaseException().Message, Log.PathName);
					Console.Error.WriteLine(message);
					Setup.Notice(T.S("req.title"), message);
					return 1;
				}
			}
		}

		public static AppBuilder BuildApp()
		{
			return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
		}

		static int InstallAndStart()
		{
			try
			{
				// A copy already in the tray steps aside so its file can be replaced.
				if (Instance.Send("exit")) Thread.Sleep(1500);
				bool firstInstall = !File.Exists(Setup.InstalledApp);
				string app = Setup.Install();
				if (firstInstall) Setup.Autostart = true;
				Log.Write((firstInstall ? "Installed " : "Reinstalled ") + Setup.Version + " to " + app);
				var psi = new System.Diagnostics.ProcessStartInfo(app) { UseShellExecute = false, WorkingDirectory = Setup.InstallDir };
				// The new copy must not think it is still the downloaded AppImage.
				psi.Environment.Remove("APPIMAGE");
				psi.Environment.Remove("APPDIR");
				psi.Environment.Remove("ARGV0");
				System.Diagnostics.Process.Start(psi);
				return 0;
			}
			catch (Exception ex)
			{
				Log.Write("Install failed: " + ex);
				Console.Error.WriteLine(T.F("error.install", ex.Message));
				return 1;
			}
		}
	}

	sealed class App : Application
	{
		TrayApp _tray;

		public override void Initialize()
		{
			Name = "EldoUI Updater";
			RequestedThemeVariant = ThemeVariant.Dark;
			Styles.Add(new FluentTheme());
			// Fluent's accent (checkboxes, focus) in EldoUI's blue.
			Resources["SystemAccentColor"] = Palette.Accent;
			Resources["SystemAccentColorLight1"] = Palette.Accent;
			Resources["SystemAccentColorDark1"] = Palette.Accent;
		}

		public override void OnFrameworkInitializationCompleted()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && !Snapshot.Running)
			{
				var args = desktop.Args ?? new string[0];
				bool Has(string a) { return args.Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase)); }
				_tray = new TrayApp(!Has("--tray"), Has("--updated"), () => desktop.Shutdown());
				TrayIcon.SetIcons(this, new TrayIcons { _tray.Tray });
				Instance.Listen(cmd => Dispatcher.UIThread.Post(() =>
				{
					if (cmd == "show") _tray.ShowWindow();
					else if (cmd == "exit") _tray.Exit();
				}));
			}
			base.OnFrameworkInitializationCompleted();
		}
	}

	/// <summary>
	/// One running copy per user: the first copy listens on a socket in the user's runtime folder; a second
	/// copy sends "show" (or "exit" before an update replaces the file) and quits.
	/// </summary>
	static class Instance
	{
		static Socket _server;

		static string SocketPath
		{
			get
			{
				string dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
				if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Setup.TempDir;
				Directory.CreateDirectory(dir);
				return Path.Combine(dir, "eldoui-updater.sock");
			}
		}

		/// <summary>Takes the socket, or null when another copy already has it.</summary>
		public static IDisposable TryOwn()
		{
			if (Send("ping")) return null;
			try { if (File.Exists(SocketPath)) File.Delete(SocketPath); } catch { }
			_server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			_server.Bind(new UnixDomainSocketEndPoint(SocketPath));
			_server.Listen(4);
			return new Owner();
		}

		public static void Listen(Action<string> onCommand)
		{
			if (_server == null) return;
			new Thread(() =>
			{
				while (true)
				{
					try
					{
						using (var c = _server.Accept())
						{
							var buf = new byte[32];
							int n = c.Receive(buf);
							string cmd = Encoding.ASCII.GetString(buf, 0, Math.Max(0, n)).Trim();
							if (cmd != "ping") onCommand(cmd);
						}
					}
					catch { return; }
				}
			}) { IsBackground = true, Name = "instance" }.Start();
		}

		/// <summary>Sends a command to the running copy. True when one answered.</summary>
		public static bool Send(string cmd)
		{
			try
			{
				if (!File.Exists(SocketPath)) return false;
				using (var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
				{
					s.Connect(new UnixDomainSocketEndPoint(SocketPath));
					s.Send(Encoding.ASCII.GetBytes(cmd));
					return true;
				}
			}
			catch { return false; }
		}

		sealed class Owner : IDisposable
		{
			public void Dispose()
			{
				try { _server?.Dispose(); } catch { }
				try { File.Delete(SocketPath); } catch { }
			}
		}
	}

	static class Assets
	{
		public static Stream Open(string name)
		{
			var asm = Assembly.GetExecutingAssembly();
			string res = asm.GetManifestResourceNames().First(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
			return asm.GetManifestResourceStream(res);
		}

		/// <summary>The troll, for the tray, the window and the app menu.</summary>
		public static Bitmap Logo()
		{
			using (var s = Open("logo.png"))
				return new Bitmap(s);
		}
	}
}
