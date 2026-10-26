// --snapshot DIR: pictures of the window in each state and language, rendered without a screen
// (Avalonia's headless platform with Skia), for review in CI.
using System;
using System.IO;
using Avalonia;
using Avalonia.Headless;

namespace EldoUI.Updater
{
	static class Snapshot
	{
		public static bool Running { get; private set; }

		public static int Run(string dir)
		{
			Running = true;
			Directory.CreateDirectory(dir);
			int count = 0;
			AppBuilder.Configure<App>().UseSkia().WithInterFont()
				.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
				.SetupWithoutStarting();
			const string folder = "/home/player/Games/battlenet/drive_c/Program Files (x86)/World of Warcraft/_forever_/Interface/AddOns";
			var states = new[]
			{
				Tuple.Create("uptodate", new ViewState { Installed = "0.7.0002", Latest = "0.7.0002", Folder = folder, LastCheck = DateTime.UtcNow.AddMinutes(-12), Kind = StatusKind.Ok, Status = "state.uptodate", Autostart = true, AutoInstall = true }),
				Tuple.Create("available", new ViewState { Installed = "0.7.0000", Latest = "0.7.0002", Folder = folder, LastCheck = DateTime.UtcNow.AddHours(-2), Kind = StatusKind.Info, Status = "state.available", Action = MainAction.Update, Autostart = true, AutoInstall = false }),
				Tuple.Create("installing", new ViewState { Installed = "0.7.0000", Latest = "0.7.0002", Folder = folder, LastCheck = DateTime.UtcNow, Kind = StatusKind.Busy, Status = "state.installing", Busy = true, Autostart = true, AutoInstall = true }),
				Tuple.Create("nofolder", new ViewState { Latest = "0.7.0002", Kind = StatusKind.Warn, Status = "state.nofolder", Action = MainAction.ChooseFolder, Autostart = true, AutoInstall = true }),
				Tuple.Create("appupdate", new ViewState { Installed = "0.7.0002", Latest = "0.7.0002", Folder = folder, LastCheck = DateTime.UtcNow.AddMinutes(-3), Kind = StatusKind.Ok, Status = "state.uptodate", Autostart = true, AutoInstall = true, AppNew = "9.9.9", AppAt = DateTime.UtcNow.AddHours(23.5) }),
				Tuple.Create("failed", new ViewState { Installed = "0.7.0000", Latest = "0.7.0002", Folder = folder, LastCheck = DateTime.UtcNow.AddMinutes(-1), Kind = StatusKind.Error, Status = "state.failed", Autostart = false, AutoInstall = true }),
				Tuple.Create("unverified", new ViewState { Installed = "0.7.0000", Latest = "0.7.0002", Folder = folder, LastCheck = DateTime.UtcNow.AddMinutes(-1), Kind = StatusKind.Error, Status = "state.unverified", Autostart = false, AutoInstall = true }),
			};
			foreach (string lang in new[] { "en", "da" })
			{
				LinuxTexts.Use(lang);
				foreach (var st in states)
				{
					var v = st.Item2;
					string key = v.Status;
					v.Status = key == "state.installing" || key == "state.available" || key == "state.unverified" ? T.F(key, v.Latest) : T.S(key);
					var w = new MainWindow(Assets.Logo());
					w.Show();
					w.Render(v);
					var frame = w.CaptureRenderedFrame();
					frame?.Save(Path.Combine(dir, "linux-" + st.Item1 + "-" + lang + ".png"));
					w.AllowClose = true;
					w.Close();
					v.Status = key;
					if (frame != null) count++;
				}
			}
			string sample = Path.Combine(Path.GetTempPath(), "eldoui-updater-snapshot-" + Guid.NewGuid().ToString("N").Substring(0, 8));
			try
			{
				var system = LightforgeChecks.Evaluate(new SystemInfo { Distribution = "Ubuntu 24.04.3 LTS", Architecture = "x86_64", Glibc = "2.39", FreeBytes = 120L * 1024 * 1024 * 1024, HasFonts = true, Has32BitLoader = false });
				var setups = new[]
				{
					Tuple.Create("new", new LightforgeStatus()),
					Tuple.Create("installed", new LightforgeStatus { Installed = true, Version = LightforgePackage.Version, Known = true, Ready = false, Outstanding = { "Proton", "Prefix", "BattleNet" }, ForeverDir = "/home/player/Games/Blizzard/games/World of Warcraft/_forever_" }),
				};
				foreach (string lang in new[] { "en", "da" })
				{
					LinuxTexts.Use(lang);
					var view = AddonRows.Sample(Path.Combine(sample, "AddOns"));
					var addons = new AddonsWindow(Assets.Logo());
					addons.Show();
					addons.Render(view);
					count += Save(addons, Path.Combine(dir, "linux-addons-find-" + lang + ".png"));
					addons.ShowInstalled(true);
					count += Save(addons, Path.Combine(dir, "linux-addons-installed-" + lang + ".png"));
					foreach (var size in new[] { Tuple.Create("small", 720, 540), Tuple.Create("large", 1280, 820) })
					{
						addons.Width = size.Item2;
						addons.Height = size.Item3;
						addons.ShowInstalled(false);
						count += Save(addons, Path.Combine(dir, "linux-addons-find-" + size.Item1 + "-" + lang + ".png"));
					}
					addons.AllowClose = true;
					addons.Close();
					foreach (var st in setups)
					{
						var setup = new SetupWindow(Assets.Logo(), null, null, "/home/player", null, sample);
						setup.Show();
						setup.Render(system, st.Item2);
						count += Save(setup, Path.Combine(dir, "linux-setup-" + st.Item1 + "-" + lang + ".png"));
						setup.AllowClose = true;
						setup.Close();
					}
				}
			}
			finally
			{
				try { Directory.Delete(sample, true); } catch { }
			}
			Console.WriteLine(count + " pictures in " + Path.GetFullPath(dir));
			return count > 0 ? 0 : 1;
		}

		static int Save(Avalonia.Controls.Window w, string path)
		{
			var frame = w.CaptureRenderedFrame();
			if (frame == null) return 0;
			frame.Save(path);
			return 1;
		}
	}
}
