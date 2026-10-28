// --selftest: the install path on the real .NET Framework, in a temp folder, without the network.
// CI runs it on Windows; the unit tests cover the same code in more detail.
using System;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using EldoUI.Updater.Core;

namespace EldoUI.Updater
{
	static class SelfTest
	{
		public static int Run(TextWriter output)
		{
			string root = Path.Combine(Path.GetTempPath(), "eldoui-updater-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
			int failures = 0;
			Action<bool, string> check = (ok, what) =>
			{
				output.WriteLine((ok ? "ok    " : "FAIL  ") + what);
				if (!ok) failures++;
			};
			try
			{
				string addOns = Path.Combine(root, "World of Warcraft", "_classic_beta_", "Interface", "AddOns");
				Directory.CreateDirectory(addOns);
				Directory.CreateDirectory(Path.Combine(addOns, "SomeOtherAddon"));
				File.WriteAllText(Path.Combine(addOns, "SomeOtherAddon", "SomeOtherAddon.toc"), "## Version: 1");

				check(WowFolders.ResolveAddOns(Path.Combine(root, "World of Warcraft")) == addOns, "the install folder resolves to Forever's AddOns");
				check(WowFolders.Find(new[] { Path.Combine(root, "World of Warcraft", "_retail_") }) == null, "a missing install is not found");
				Directory.CreateDirectory(Path.Combine(root, "World of Warcraft", "_retail_"));
				check(WowFolders.Find(new[] { Path.Combine(root, "World of Warcraft", "_retail_") }) == addOns, "the registry's _retail_ path leads to Forever");

				string zip1 = MakeZip(root, "0.7.0001", "EldoUI", "EldoUI_LootData", "EldoUI_Old");
				var r1 = AddonInstaller.Install(zip1, addOns, null);
				check(r1.Version == "0.7.0001" && AddonInstaller.InstalledVersion(addOns) == "0.7.0001", "first install");
				string zip2 = MakeZip(root, "0.7.0002", "EldoUI", "EldoUI_LootData");
				var r2 = AddonInstaller.Install(zip2, addOns, r1.Folders, "0.7.0002");
				check(AddonInstaller.InstalledVersion(addOns) == "0.7.0002", "update installs the new version");
				bool wrongVersion = false;
				try { AddonInstaller.Install(zip1, addOns, r2.Folders, "0.7.0003"); }
				catch (InvalidDataException) { wrongVersion = true; }
				check(wrongVersion && AddonInstaller.InstalledVersion(addOns) == "0.7.0002",
					"a zip for another release is rejected before replacing EldoUI");
				check(!Directory.Exists(Path.Combine(addOns, "EldoUI_Old")) && r2.Removed.Contains("EldoUI_Old"), "a folder the release dropped is removed");
				check(Directory.Exists(Path.Combine(addOns, "SomeOtherAddon")), "other addons are untouched");
				check(Directory.GetDirectories(Path.GetDirectoryName(addOns), ".eldoui-updater-*").Length == 0, "no work folder is left behind");

				using (var form = new MainForm(Assets.Icon(false), Assets.Logo()) {
					StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false })
				{
					form.Render(new ViewState());
					int extras = 0;
					form.ExtrasClicked += () => extras++;
					form.Show();
					int appChecks = 0, appUpdates = 0;
					form.AppCheckClicked += () => appChecks++;
					form.AppUpdateClicked += () => appUpdates++;
					var appButton = (Button) typeof(MainForm).GetField("_appUpdate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
					check(appButton.Visible && appButton.Enabled && appButton.Text == T.S("btn.app_check"), "the app version always has a manual check button");
					appButton.PerformClick();
					form.Render(new ViewState { AppChecking = true });
					appButton.PerformClick();
					check(appChecks == 1 && !appButton.Enabled && appButton.Text == T.S("btn.app_checking"), "an active app check disables duplicate clicks");
					form.Render(new ViewState { AppNew = "9.9.9" });
					appButton.PerformClick();
					check(appUpdates == 1 && appChecks == 1 && appButton.Text == T.S("btn.update"), "the same app button switches to Update now");
					var extrasButton = (Button) typeof(MainForm).GetField("_extras", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
					extrasButton.PerformClick();
					check(extras == 1, "the extra addons button opens the addons window");
					form.AllowClose = true;
					form.Close();
				}

				using (var addonsForm = new AddonsForm(Assets.Icon(false)) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false })
				{
					var settings = new Settings { AddOnsDir = addOns };
					settings.SelectCompanion("dbm", true);
					var actions = new System.Collections.Generic.List<string>();
					addonsForm.ActionRequested += (action, arg) => actions.Add(action + ":" + arg);
					bool installSelected = false, installAll = false;
					addonsForm.InstallClicked += all => { if (all) installAll = true; else installSelected = true; };
					addonsForm.Show();
					addonsForm.Render(AddonsView.Build(settings, null, AddonScanner.Scan(addOns, settings), false));
					var list = (RowList) typeof(AddonsForm).GetField("_list", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(addonsForm);
					check(list.Rows.Count >= AddonCatalog.All.Count - 5 && list.Rows[0].Key == "dbm" && list.Rows[0].Checked == true, "the catalog lists the player's addons first");
					check(list.Rows.Any(r => r.Key == "details" && r.Checked == null && r.Menu.Any(m => m.Action == AddonAction.OpenPage)), "an addon without a Forever download only links to its page");
					var search = (TextBox) typeof(AddonsForm).GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(addonsForm);
					int everything = list.Rows.Count;
					search.Text = "Deadly Boss";
					check(list.Rows.Count < everything && list.Rows.Any(r => r.Key == "dbm"), "searching narrows the list");
					search.Text = "";
					var install = (Button) typeof(AddonsForm).GetField("_install", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(addonsForm);
					var recommended = (Button) typeof(AddonsForm).GetField("_recommended", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(addonsForm);
					install.PerformClick();
					recommended.PerformClick();
					check(installSelected && installAll, "Install selected and Install all recommended reach the updater");
					var tabInstalled = (Button) typeof(AddonsForm).GetField("_tabInstalled", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(addonsForm);
					tabInstalled.PerformClick();
					check(list.Rows.Any(r => r.Title == "SomeOtherAddon" && r.Menu.Any(m => m.Action == AddonAction.Hide)), "the installed list shows other addons and lets the player hide them");
					check(list.Rows.Any(r => r.Title == "SomeOtherAddon" && r.Menu.Any(m => m.Action == AddonAction.Uninstall) && r.Checked == false)
						&& !list.Rows.Any(r => r.Key.Split('/').Any(AddonInstaller.IsEldoUIFolder) && (r.Checked != null || r.Menu.Any(m => m.Action == AddonAction.Uninstall))),
						"every installed addon but EldoUI can be uninstalled");
					var uninstall = (Button) typeof(AddonsForm).GetField("_uninstall", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(addonsForm);
					check(uninstall.Visible && !uninstall.Enabled, "Uninstall selected waits for a ticked row");
					string otherKey = list.Rows.First(r => r.Title == "SomeOtherAddon").Key;
					addonsForm.Mark(otherKey);
					check(uninstall.Enabled && list.Rows.First(r => r.Key == otherKey).Checked == true, "a ticked row can be uninstalled with Uninstall selected");
					var plan = AddonRemover.Plan(addOns, AddonScanner.Scan(addOns, settings), new[] { otherKey });
					using (var question = new UninstallDialog(plan, Assets.Icon(false)) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) })
					{
						question.Show();
						Application.DoEvents();
						var focused = question.ActiveControl as Button;
						check(!question.DeleteSettings && focused != null && focused.DialogResult == DialogResult.Cancel, "the uninstall question keeps saved settings and has Cancel ready");
					}
					string savedDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(addOns)), "WTF", "Account", "PLAYER", "SavedVariables");
					Directory.CreateDirectory(savedDir);
					File.WriteAllText(Path.Combine(savedDir, "SomeOtherAddon.lua"), "-- saved");
					var withSettings = AddonRemover.Plan(addOns, AddonScanner.Scan(addOns, settings), new[] { otherKey });
					using (var question = new UninstallDialog(withSettings, Assets.Icon(false)) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) })
					{
						question.Show();
						Application.DoEvents();
						var box = question.Controls.OfType<ECheck>().Single();
						var text = question.Controls.OfType<Label>().First(l => l.Text == AddonRows.RemovalBody(withSettings));
						check(box.Visible && box.Top >= text.Bottom && box.Bottom <= question.ClientSize.Height, "the saved settings box has its own place in the uninstall question");
						box.Checked = true;
						question.Hide();
						check(question.DeleteSettings, "a ticked saved settings box is read after the question closes");
					}
					check(Directory.Exists(Path.Combine(addOns, "SomeOtherAddon")), "asking deletes nothing");
					addonsForm.Render(AddonsView.Build(settings, null, AddonScanner.Scan(addOns, settings), true));
					check(!install.Enabled && !recommended.Enabled, "nothing can be installed while a check runs");
					check(actions.Count == 0, "rendering never changes a choice by itself");
					check(addonsForm.FormBorderStyle == FormBorderStyle.Sizable && addonsForm.MinimumSize.Width > 0, "the Extra addons window can be resized, down to a readable minimum");
					int nameWidth = list.ColumnWidths[0];
					addonsForm.ClientSize = new Size(addonsForm.ClientSize.Width + 300, addonsForm.ClientSize.Height + 100);
					Application.DoEvents();
					check(list.ColumnWidths.Count == 4 && list.ColumnWidths[0] > nameWidth, "the columns grow with the window");
					check(list.RowHeights.Count == list.Rows.Count && list.RowHeights.All(h => h > 0), "every row is as tall as its text needs");
					addonsForm.Dispose();
				}

				string bad = Path.Combine(root, "bad.zip");
				using (var z = ZipFile.Open(bad, ZipArchiveMode.Create))
					Add(z, "Evil/Evil.toc", "## Version: 1");
				bool refused = false;
				try { AddonInstaller.Install(bad, addOns, r2.Folders); } catch (InvalidDataException) { refused = true; }
				check(refused && AddonInstaller.InstalledVersion(addOns) == "0.7.0002", "a zip that is not EldoUI is refused and nothing changes");

				var rel = Release.FromGitHub("{\"tag_name\":\"v0.7.0002\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"EldoUI-0.7.0002.zip\",\"size\":10,\"browser_download_url\":\"https://github.com/Eldodeldo/EldoUI/releases/download/v0.7.0002/EldoUI-0.7.0002.zip\",\"digest\":\"sha256:" + new string('a', 64) + "\"}]}");
				check(rel.Version == "0.7.0002" && rel.Find(UpdateEngine.AddonAssetPattern) != null, "GitHub's release answer is read");
				check(Versions.IsNewer("0.7.0010", "0.7.0002") && !Versions.IsNewer("0.7.0002", "v0.7.0002"), "versions compare");

				var s = new Settings { AddOnsDir = addOns };
				s.InstalledFolders.Add("EldoUI");
				string settingsPath = Path.Combine(root, "settings.json");
				s.Save(settingsPath);
				s.Save(settingsPath);
				var back = Settings.Load(settingsPath);
				check(back.AddOnsDir == addOns && back.InstalledFolders.Count == 1, "settings are saved and read back");
				check(Assets.Icon() != null && Assets.Logo() != null, "the icon and logo are in the exe");
				// Reads the .exe.config (TLS and DPI settings), so a broken config fails here and not on a player's PC.
				using (new Web("EldoUI-Updater/selftest")) { }
				check(System.Configuration.ConfigurationManager.GetSection("System.Windows.Forms.ApplicationConfigurationSection") != null
					|| !File.Exists(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile), "the .exe.config is read");
			}
			catch (Exception ex)
			{
				check(false, "unexpected error: " + ex);
			}
			finally
			{
				try { Directory.Delete(root, true); } catch { }
			}
			output.WriteLine(failures == 0 ? "selftest passed" : failures + " selftest check(s) failed");
			return failures == 0 ? 0 : 1;
		}

		static string MakeZip(string dir, string version, params string[] folders)
		{
			string path = Path.Combine(dir, "EldoUI-" + version + "-" + folders.Length + ".zip");
			using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
			{
				foreach (string f in folders)
				{
					Add(z, f + "/" + f + ".toc", "## Interface: 16001\n## Version: " + version + "\n");
					Add(z, f + "/Core/init.lua", "-- " + f);
				}
			}
			return path;
		}

		static void Add(ZipArchive z, string name, string text)
		{
			var e = z.CreateEntry(name);
			using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false)))
				w.Write(text);
		}
	}
}
