// --snapshot DIR: pictures of the window in each state and language, for review in CI.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace EldoUI.Updater
{
	static class Snapshot
	{
		public static int Run(string dir)
		{
			Directory.CreateDirectory(dir);
			var states = new[]
			{
				Tuple.Create("uptodate", new ViewState { Installed = "0.7.0002", Latest = "0.7.0002", Folder = @"C:\Program Files (x86)\World of Warcraft\_classic_beta_\Interface\AddOns", LastCheck = DateTime.UtcNow.AddMinutes(-12), Kind = StatusKind.Ok, Status = "state.uptodate", Autostart = true, AutoInstall = true }),
				Tuple.Create("available", new ViewState { Installed = "0.7.0000", Latest = "0.7.0002", Folder = @"D:\Games\World of Warcraft\_classic_beta_\Interface\AddOns", LastCheck = DateTime.UtcNow.AddHours(-2), Kind = StatusKind.Info, Status = "state.available", Action = MainAction.Update, Autostart = true, AutoInstall = false }),
				Tuple.Create("installing", new ViewState { Installed = "0.7.0000", Latest = "0.7.0002", Folder = @"C:\Program Files (x86)\World of Warcraft\_classic_beta_\Interface\AddOns", LastCheck = DateTime.UtcNow, Kind = StatusKind.Busy, Status = "state.installing", Busy = true, Autostart = true, AutoInstall = true }),
				Tuple.Create("nofolder", new ViewState { Latest = "0.7.0002", Kind = StatusKind.Warn, Status = "state.nofolder", Action = MainAction.ChooseFolder, Autostart = true, AutoInstall = true }),
				Tuple.Create("failed", new ViewState { Installed = "0.7.0000", Latest = "0.7.0002", Folder = @"C:\Program Files (x86)\World of Warcraft\_classic_beta_\Interface\AddOns", LastCheck = DateTime.UtcNow.AddMinutes(-1), Kind = StatusKind.Error, Status = "state.failed", Autostart = false, AutoInstall = true }),
			};
			int count = 0;
			foreach (string lang in new[] { "en", "da" })
			{
				T.Use(lang);
				foreach (var st in states)
				{
					var v = st.Item2;
					string key = v.Status;
					v.Status = key == "state.installing" || key == "state.available" ? T.F(key, v.Latest)
						: T.S(key);
					using (var form = new MainForm(Assets.Icon(false), Assets.Logo()) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false })
					{
						form.Show();
						form.Render(v);
						Application.DoEvents();
						using (var bmp = new Bitmap(form.Width, form.Height))
						{
							form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
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
							bmp.Save(Path.Combine(dir, "window-" + st.Item1 + "-" + lang + ".png"), ImageFormat.Png);
						}
						form.AllowClose = true;
						form.Close();
					}
					v.Status = key;
					count++;
				}
			}
			Console.WriteLine(count + " pictures in " + Path.GetFullPath(dir));
			return count > 0 ? 0 : 1;
		}
	}
}
