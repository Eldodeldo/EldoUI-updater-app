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
			};
			foreach (string lang in new[] { "en", "da" })
			{
				LinuxTexts.Use(lang);
				foreach (var st in states)
				{
					var v = st.Item2;
					string key = v.Status;
					v.Status = key == "state.installing" || key == "state.available" ? T.F(key, v.Latest) : T.S(key);
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
			Console.WriteLine(count + " pictures in " + Path.GetFullPath(dir));
			return count > 0 ? 0 : 1;
		}
	}
}
