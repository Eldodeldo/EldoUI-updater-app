// --snapshot DIR: pictures of the window in each state and language, for review in CI.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using EldoUI.Updater.Core;

namespace EldoUI.Updater
{
	static class Snapshot
	{
		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool PrintWindow(IntPtr hwnd, IntPtr target, uint flags);

		public static int Run(string dir)
		{
			Directory.CreateDirectory(dir);
			var states = new[]
			{
				Tuple.Create("uptodate", new ViewState { Installed = "0.7.0002", Latest = "0.7.0002", Folder = @"C:\Program Files (x86)\World of Warcraft\_forever_\Interface\AddOns", LastCheck = DateTime.UtcNow.AddMinutes(-12), Kind = StatusKind.Ok, Status = "state.uptodate", Autostart = true, AutoInstall = true }),
				Tuple.Create("available", new ViewState { Installed = "0.7.0000", Latest = "0.7.0002", Folder = @"D:\Games\World of Warcraft\_forever_\Interface\AddOns", LastCheck = DateTime.UtcNow.AddHours(-2), Kind = StatusKind.Info, Status = "state.available", Action = MainAction.Update, Autostart = true, AutoInstall = false }),
				Tuple.Create("installing", new ViewState { Installed = "0.7.0000", Latest = "0.7.0002", Folder = @"C:\Program Files (x86)\World of Warcraft\_forever_\Interface\AddOns", LastCheck = DateTime.UtcNow, Kind = StatusKind.Busy, Status = "state.installing", Busy = true, Autostart = true, AutoInstall = true }),
				Tuple.Create("nofolder", new ViewState { Latest = "0.7.0002", Kind = StatusKind.Warn, Status = "state.nofolder", Action = MainAction.ChooseFolder, Autostart = true, AutoInstall = true }),
				Tuple.Create("appupdate", new ViewState { Installed = "0.7.0002", Latest = "0.7.0002", Folder = @"C:\Program Files (x86)\World of Warcraft\_forever_\Interface\AddOns", LastCheck = DateTime.UtcNow.AddMinutes(-3), Kind = StatusKind.Ok, Status = "state.uptodate", Autostart = true, AutoInstall = true, AppNew = "9.9.9", AppAt = DateTime.UtcNow.AddHours(23.5) }),
				Tuple.Create("failed", new ViewState { Installed = "0.7.0000", Latest = "0.7.0002", Folder = @"C:\Program Files (x86)\World of Warcraft\_forever_\Interface\AddOns", LastCheck = DateTime.UtcNow.AddMinutes(-1), Kind = StatusKind.Error, Status = "state.failed", Autostart = false, AutoInstall = true }),
				Tuple.Create("unverified", new ViewState { Installed = "0.7.0000", Latest = "0.7.0002", Folder = @"C:\Program Files (x86)\World of Warcraft\_forever_\Interface\AddOns", LastCheck = DateTime.UtcNow.AddMinutes(-1), Kind = StatusKind.Error, Status = "state.unverified", Autostart = false, AutoInstall = true }),
			};
			int count = 0;
			foreach (string lang in new[] { "en", "da" })
			{
				T.Use(lang);
				foreach (var st in states)
				{
					var v = st.Item2;
					string key = v.Status;
					v.Status = key == "state.installing" || key == "state.available" || key == "state.unverified" ? T.F(key, v.Latest)
						: T.S(key);
					using (var form = new MainForm(Assets.Icon(false), Assets.Logo()) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false })
					{
						form.Show();
						form.Render(v);
						Capture(form, Path.Combine(dir, "window-" + st.Item1 + "-" + lang + ".png"));
						form.AllowClose = true;
						form.Close();
					}
					v.Status = key;
					count++;
				}
			}
			string sample = Path.Combine(Path.GetTempPath(), "eldoui-updater-snapshot-" + Guid.NewGuid().ToString("N").Substring(0, 8), "AddOns");
			try
			{
				foreach (string lang in new[] { "en", "da" })
				{
					T.Use(lang);
					var view = AddonRows.Sample(sample);
					using (var form = new AddonsForm(Assets.Icon(false)) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false })
					{
						form.Show();
						form.Render(view);
						Capture(form, Path.Combine(dir, "addons-find-" + lang + ".png"));
						form.ShowInstalled(true);
						Capture(form, Path.Combine(dir, "addons-installed-" + lang + ".png"));
						count += 2;
						form.ShowInstalled(false);
						foreach (var size in new[] { Tuple.Create("small", 720, 540), Tuple.Create("large", 1280, 820) })
						{
							form.ClientSize = new Size(size.Item2 * form.DeviceDpi / 96, size.Item3 * form.DeviceDpi / 96);
							Capture(form, Path.Combine(dir, "addons-find-" + size.Item1 + "-" + lang + ".png"));
							count++;
						}
					}
				}
			}
			finally
			{
				try { Directory.Delete(Path.GetDirectoryName(sample), true); } catch { }
			}
			Console.WriteLine(count + " pictures in " + Path.GetFullPath(dir));
			return count > 0 ? 0 : 1;
		}

		static void Capture(Form form, string path)
		{
			Application.DoEvents();
			using (var bmp = new Bitmap(form.Width, form.Height))
			{
				// Capture the native non-client frame too; DrawToBitmap uses a different caption.
				using (var graphics = Graphics.FromImage(bmp))
				{
					var target = graphics.GetHdc();
					try
					{
						if (!PrintWindow(form.Handle, target, 2)) throw new Win32Exception();
					}
					finally { graphics.ReleaseHdc(target); }
				}
				// Each control again on top, so runtimes that leave them out of the form's picture still show them.
				var client = form.PointToScreen(Point.Empty);
				int dx = client.X - form.Left, dy = client.Y - form.Top;
				foreach (Control c in form.Controls)
				{
					if (!c.Visible) continue;
					using (var part = new Bitmap(c.Width, c.Height))
					using (var g = Graphics.FromImage(bmp))
					{
						c.DrawToBitmap(part, new Rectangle(Point.Empty, c.Size));
						g.DrawImageUnscaled(part, c.Left + dx, c.Top + dy);
					}
				}
				bmp.Save(path, ImageFormat.Png);
			}
		}
	}
}
