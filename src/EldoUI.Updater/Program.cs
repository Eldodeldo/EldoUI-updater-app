// Start-up: one running copy per user, install on first start, and the command-line switches.
//
//   (none)          first start from a download: install, then open the window
//   --tray          start hidden in the tray (autostart uses this)
//   --updated       started by a self-update (shows a short notice)
//   --uninstall     remove the updater (add --quiet to skip the question)
//   --selftest      check the install logic in a temp folder and exit (CI)
//   --snapshot DIR  save pictures of the window in both languages and exit (CI)
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace EldoUI.Updater
{
	static class Program
	{
		const string MutexName = @"Local\EldoUI.Updater";
		const string ShowEventName = @"Local\EldoUI.Updater.Show";
		const string ExitEventName = @"Local\EldoUI.Updater.Exit";

		[STAThread]
		static int Main(string[] args)
		{
			bool Has(string a) { return args.Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase)); }

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			T.Use(Core.Settings.Load(Setup.SettingsPath).Language);

			if (Has("--selftest")) return SelfTest.Run(Console.Out);
			if (Has("--snapshot"))
			{
				int i = Array.FindIndex(args, a => string.Equals(a, "--snapshot", StringComparison.OrdinalIgnoreCase));
				return Snapshot.Run(i + 1 < args.Length ? args[i + 1] : "snapshots");
			}
			if (Has("--uninstall")) return Uninstall(Has("--quiet"));

			if (!Setup.RunningInstalled)
				return InstallAndStart();

			bool first;
			using (var mutex = new Mutex(true, MutexName, out first))
			{
				if (!first)
				{
					// Already running: bring its window forward instead of starting a second copy.
					Signal(ShowEventName);
					return 0;
				}
				using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
				using (var exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName))
				{
					Log.Write("Started " + Setup.Version + (args.Length > 0 ? " " + string.Join(" ", args) : ""));
					var app = new TrayApp(!Has("--tray"), Has("--updated"));
					var ui = SynchronizationContext.Current;
					var listener = new Thread(() =>
					{
						var handles = new WaitHandle[] { show, exit };
						while (true)
						{
							int which = WaitHandle.WaitAny(handles);
							if (which == 0) ui.Post(_ => app.ShowWindow(), null);
							else { ui.Post(_ => app.Exit(), null); return; }
						}
					}) { IsBackground = true };
					listener.Start();
					Application.Run(app);
					GC.KeepAlive(mutex);
				}
			}
			return 0;
		}

		static int InstallAndStart()
		{
			try
			{
				// A copy already in the tray steps aside so its exe can be replaced.
				if (Signal(ExitEventName))
				{
					using (var m = new Mutex(false, MutexName))
					{
						try { if (m.WaitOne(TimeSpan.FromSeconds(10))) m.ReleaseMutex(); }
						catch (AbandonedMutexException) { }
					}
				}
				bool firstInstall = !File.Exists(Setup.InstalledExe);
				string exe = Setup.Install();
				if (firstInstall) Setup.Autostart = true;
				Log.Write((firstInstall ? "Installed " : "Reinstalled ") + Setup.Version + " to " + exe);
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Setup.InstallDir });
				return 0;
			}
			catch (Exception ex)
			{
				Log.Write("Install failed: " + ex);
				MessageBox.Show(T.F("error.install", ex.Message), T.S("error.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
				return 1;
			}
		}

		static int Uninstall(bool quiet)
		{
			if (!quiet && MessageBox.Show(T.S("uninstall.body"), T.S("uninstall.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
				return 1;
			Signal(ExitEventName);
			Thread.Sleep(500);
			Setup.Uninstall();
			if (!quiet) MessageBox.Show(T.S("uninstall.done"), T.S("app"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return 0;
		}

		static bool Signal(string name)
		{
			try
			{
				EventWaitHandle h;
				if (!EventWaitHandle.TryOpenExisting(name, out h)) return false;
				using (h) h.Set();
				return true;
			}
			catch { return false; }
		}
	}

	static class Assets
	{
		static Stream Open(string name)
		{
			var asm = Assembly.GetExecutingAssembly();
			string res = asm.GetManifestResourceNames().First(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
			return asm.GetManifestResourceStream(res);
		}

		/// <summary>The troll icon: small for the tray, large for the window and taskbar.</summary>
		public static Icon Icon(bool small = true)
		{
			using (var s = Open("eldoui.ico"))
				return small ? new Icon(s, SystemInformation.SmallIconSize) : new Icon(s);
		}

		public static Image Logo()
		{
			using (var s = Open("logo.png"))
				return new Bitmap(Image.FromStream(s));
		}
	}
}
