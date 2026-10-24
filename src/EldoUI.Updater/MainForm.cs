// The updater's one window: what is installed, what is newest, where WoW is, and the main actions.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EldoUI.Updater
{
	enum StatusKind { Busy, Ok, Info, Warn, Error }
	enum MainAction { Check, Update, Install, ChooseFolder }

	sealed class ViewState
	{
		public string Installed;
		public string Latest;
		public string Folder;
		public DateTime? LastCheck;
		public StatusKind Kind = StatusKind.Info;
		public string Status = "";
		public string ErrorDetail;
		public MainAction Action = MainAction.Check;
		public bool Busy;
		public bool Autostart;
		public bool AutoInstall;
		public bool ExtraAddons;
		public string[] SelectedCompanions = new string[0];
		public string ExtraStatus;
		/// <summary>A newer version of the updater itself, when it installs by itself, and whether it is installing now.</summary>
		public string AppNew;
		public DateTime? AppAt;
		public bool AppBusy;
	}

	sealed class MainForm : Form
	{
		public event Action CheckClicked, UpdateClicked, ChooseFolderClicked, OpenAddOnsClicked, AppUpdateClicked;
		public event Action<bool> AutostartChanged, AutoInstallChanged;
		public event Action<string, bool> CompanionChanged;
		public event Action<bool> InstallExtrasClicked;
		public event Action DetailsClicked;
		public bool AllowClose;

		readonly Image _logo;
		readonly Label _status, _installed, _latest, _folder, _checked, _app, _appUpdate, _footer;
		readonly Label[] _labels = new Label[5];
		readonly ECheck _autostart, _autoInstall;
		readonly EButton _extras;
		readonly EButton _primary, _chooseFolder, _openAddOns;
		readonly Font _head, _tag, _body, _small, _link;
		readonly ToolTip _tips = new ToolTip();
		ViewState _state = new ViewState();
		bool _rendering;

		public MainForm(Icon icon, Image logo)
		{
			_logo = logo;
			_head = Theme.Head(16f);
			_tag = Theme.Body(9.5f);
			_body = Theme.Body(9.75f);
			_small = Theme.Body(8.25f);
			_link = Theme.Body(9.75f, FontStyle.Underline);

			SuspendLayout();
			AutoScaleDimensions = new SizeF(96f, 96f);
			AutoScaleMode = AutoScaleMode.Dpi;
			Text = T.S("app");
			Icon = icon;
			FormBorderStyle = FormBorderStyle.FixedSingle;
			MaximizeBox = false;
			StartPosition = FormStartPosition.CenterScreen;
			ClientSize = new Size(470, 482);
			BackColor = Theme.Panel;
			ForeColor = Theme.Text;
			Font = _body;
			DoubleBuffered = true;
			KeyPreview = true;

			_status = new Label { Location = new Point(42, 110), Size = new Size(408, 40), ForeColor = Theme.Text, Font = _body, BackColor = Color.Transparent };
			Controls.Add(_status);

			string[] keys = { "label.installed", "label.latest", "label.folder", "label.checked", "label.app" };
			var values = new Label[5];
			for (int i = 0; i < 5; i++)
			{
				int y = 162 + i * 26;
				_labels[i] = new Label { Location = new Point(22, y), Size = new Size(118, 22), ForeColor = Theme.Muted, Font = _body, Tag = keys[i] };
				values[i] = new Label { Location = new Point(140, y), Size = new Size(310, 22), ForeColor = Theme.Text, Font = _body, AutoEllipsis = true };
				Controls.Add(_labels[i]);
				Controls.Add(values[i]);
			}
			_installed = values[0];
			_latest = values[1];
			_folder = values[2];
			_checked = values[3];
			_app = values[4];
			// "Update now" beside the app's own version, shown only when a newer one is out.
			_appUpdate = new Label { Location = new Point(340, 266), Size = new Size(110, 22), ForeColor = Theme.Accent, Font = _link, Cursor = Cursors.Hand, TextAlign = ContentAlignment.TopRight, Visible = false };
			_appUpdate.Click += (s, e) => { if (AppUpdateClicked != null && !_state.AppBusy) AppUpdateClicked(); };
			Controls.Add(_appUpdate);
			_appUpdate.BringToFront();

			_autostart = new ECheck { Location = new Point(22, 304), Size = new Size(428, 26) };
			_autoInstall = new ECheck { Location = new Point(22, 332), Size = new Size(428, 26) };
			_autostart.CheckedChanged += (s, e) => { if (!_rendering && AutostartChanged != null) AutostartChanged(_autostart.Checked); };
			_autoInstall.CheckedChanged += (s, e) => { if (!_rendering && AutoInstallChanged != null) AutoInstallChanged(_autoInstall.Checked); };
			Controls.Add(_autostart);
			Controls.Add(_autoInstall);
			_extras = new EButton { Location = new Point(22, 360), Size = new Size(428, 28) };
			_extras.Click += (sender, args) => ShowCompanions();
			Controls.Add(_extras);

			_primary = new EButton { Primary = true, Location = new Point(22, 404), Size = new Size(156, 36) };
			_chooseFolder = new EButton { Location = new Point(188, 404), Size = new Size(136, 36) };
			_openAddOns = new EButton { Location = new Point(334, 404), Size = new Size(116, 36) };
			_primary.Click += (s, e) => OnPrimary();
			_chooseFolder.Click += (s, e) => { if (ChooseFolderClicked != null) ChooseFolderClicked(); };
			_openAddOns.Click += (s, e) => { if (OpenAddOnsClicked != null) OpenAddOnsClicked(); };
			Controls.Add(_primary);
			Controls.Add(_chooseFolder);
			Controls.Add(_openAddOns);

			_footer = new Label { Location = new Point(40, 452), Size = new Size(390, 20), ForeColor = Theme.Muted, Font = _small, TextAlign = ContentAlignment.MiddleCenter };
			Controls.Add(_footer);

			AcceptButton = _primary;
			ResumeLayout(false);
			ApplyTexts();
		}

		/// <summary>The window's scale (1 at 100 %), from the DPI of the monitor it is on.</summary>
		float K
		{
			get
			{
				try { return Dpi() / 96f; }
				catch (MissingMethodException) { return 1f; }   // runtimes without per-monitor DPI (preview tools)
			}
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		int Dpi() { return DeviceDpi; }

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			Theme.DarkTitleBar(Handle);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = true; }
			base.OnKeyDown(e);
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			// Closing the window keeps the updater running in the tray.
			if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
			{
				e.Cancel = true;
				Hide();
			}
			base.OnFormClosing(e);
		}

		void OnPrimary()
		{
			switch (_state.Action)
			{
				case MainAction.ChooseFolder: if (ChooseFolderClicked != null) ChooseFolderClicked(); break;
				case MainAction.Update:
				case MainAction.Install: if (UpdateClicked != null) UpdateClicked(); break;
				default: if (CheckClicked != null) CheckClicked(); break;
			}
		}

		void ShowCompanions()
		{
			var menu = new ContextMenuStrip { Renderer = new MenuRenderer(), ShowCheckMargin = true };
			foreach (var addon in Core.Companion.All)
			{
				var item = new ToolStripMenuItem(addon.Name) { Checked = _state.ExtraAddons && Array.IndexOf(_state.SelectedCompanions, addon.Id) >= 0 };
				item.Click += (sender, args) => CompanionChanged?.Invoke(addon.Id, !item.Checked);
				menu.Items.Add(item);
			}
			menu.Items.Add(new ToolStripSeparator());
			var selected = new ToolStripMenuItem(T.S("extras.install_selected")) { Enabled = _state.ExtraAddons };
			selected.Click += (sender, args) => InstallExtrasClicked?.Invoke(false);
			menu.Items.Add(selected);
			menu.Items.Add(T.S("extras.install_all"), null, (sender, args) => InstallExtrasClicked?.Invoke(true));
			menu.Items.Add(new ToolStripSeparator());
			menu.Items.Add(T.S("extras.details"), null, (sender, args) => DetailsClicked?.Invoke());
			menu.Closed += (sender, args) => menu.Dispose();
			menu.Show(_extras, new Point(0, _extras.Height));
		}

		public void ApplyTexts()
		{
			Text = T.S("app");
			foreach (var l in _labels) l.Text = T.S((string)l.Tag);
			_autostart.Text = T.S("check.autostart");
			_autoInstall.Text = T.S("check.auto");
			_extras.Text = T.S("check.extras");
			_chooseFolder.Text = T.S("btn.folder");
			_openAddOns.Text = T.S("btn.addons");
			_appUpdate.Text = T.S("btn.update");
			_footer.Text = "v" + Setup.Version + " · " + T.S("footer");
			Render(_state);
		}

		public void Render(ViewState s)
		{
			_state = s;
			_rendering = true;
			try
			{
				_status.Text = s.Status;
				_tips.SetToolTip(_status, s.ErrorDetail ?? "");
				_installed.Text = s.Installed ?? T.S("value.none");
				_latest.Text = s.Latest ?? T.S("value.unknown");
				_folder.Text = string.IsNullOrEmpty(s.Folder) ? T.S("value.nofolder") : s.Folder;
				_folder.ForeColor = string.IsNullOrEmpty(s.Folder) ? Theme.Warn : Theme.Text;
				_tips.SetToolTip(_folder, s.Folder ?? "");
				_checked.Text = Ago(s.LastCheck);
				_app.Text = AppText(s);
				_app.ForeColor = s.AppNew != null ? Theme.Accent : Theme.Text;
				_app.Width = s.AppNew != null && !s.AppBusy ? 200 : 310;
				_appUpdate.Visible = s.AppNew != null && !s.AppBusy;
				_autostart.Checked = s.Autostart;
				_autoInstall.Checked = s.AutoInstall;
				_extras.Enabled = !s.Busy;
				_tips.SetToolTip(_extras, s.ExtraStatus ?? T.S("extras.hint"));
				_primary.Text = T.S(s.Action == MainAction.ChooseFolder ? "btn.folder"
					: s.Action == MainAction.Update ? "btn.update"
					: s.Action == MainAction.Install ? "btn.install" : "btn.check");
				_primary.Enabled = !s.Busy;
				_chooseFolder.Visible = s.Action != MainAction.ChooseFolder;
				_chooseFolder.Enabled = !s.Busy;
				_openAddOns.Enabled = !string.IsNullOrEmpty(s.Folder);
				Invalidate(new Rectangle(0, (int)(100 * K), (int)(40 * K), (int)(50 * K)));
			}
			finally
			{
				_rendering = false;
			}
		}

		static string AppText(ViewState s)
		{
			if (s.AppNew == null) return T.F("value.app.current", Setup.Version);
			if (s.AppBusy) return T.F("value.app.updating", s.AppNew);
			double hours = s.AppAt.HasValue ? (s.AppAt.Value.ToUniversalTime() - DateTime.UtcNow).TotalHours : 0;
			return hours >= 1 ? T.F("value.app.new", s.AppNew, (int)Math.Ceiling(hours)) : T.F("value.app.soon", s.AppNew);
		}

		static string Ago(DateTime? when)
		{
			if (!when.HasValue) return T.S("value.never");
			var span = DateTime.UtcNow - when.Value.ToUniversalTime();
			if (span.TotalMinutes < 1) return T.S("value.justnow");
			if (span.TotalHours < 1) return T.F("value.minutes", (int)span.TotalMinutes);
			if (span.TotalHours < 48) return T.F("value.hours", (int)span.TotalHours);
			return when.Value.ToLocalTime().ToString("g");
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			float k = K;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

			// Header band: the same deep blue gradient as the website's hero.
			var band = new RectangleF(0, 0, ClientSize.Width, 96 * k);
			using (var grad = new LinearGradientBrush(band, Theme.Glow, Theme.Panel, LinearGradientMode.ForwardDiagonal))
				g.FillRectangle(grad, band);
			using (var pen = new Pen(Theme.Edge))
				g.DrawLine(pen, 0, band.Bottom, ClientSize.Width, band.Bottom);

			// The troll logo in a gold-edged circle.
			var logo = new RectangleF(22 * k, 18 * k, 60 * k, 60 * k);
			if (_logo != null)
			{
				using (var clip = new GraphicsPath())
				{
					clip.AddEllipse(logo);
					var saved = g.Save();
					g.SetClip(clip);
					g.DrawImage(_logo, logo);
					g.Restore(saved);
				}
			}
			using (var pen = new Pen(Theme.Ornament, 1.5f * k))
				g.DrawEllipse(pen, logo);

			TextRenderer.DrawText(g, T.S("app"), _head, new Point((int)(98 * k), (int)(22 * k)), Theme.Text, TextFormatFlags.NoPadding);
			TextRenderer.DrawText(g, T.S("tagline"), _tag, new Point((int)(99 * k), (int)(54 * k)), Theme.Muted, TextFormatFlags.NoPadding);

			// Status dot.
			Color dot = _state.Kind == StatusKind.Ok ? Theme.Ok : _state.Kind == StatusKind.Warn ? Theme.Warn
				: _state.Kind == StatusKind.Error ? Theme.Err : Theme.Accent;
			using (var b = new SolidBrush(dot))
				g.FillEllipse(b, 24 * k, 114 * k, 10 * k, 10 * k);
			if (_state.Busy)
				using (var halo = new Pen(Color.FromArgb(90, dot), 3 * k))
					g.DrawEllipse(halo, 21 * k, 111 * k, 16 * k, 16 * k);

			// A thin rule between the details and the actions, and the corner ornaments.
			using (var pen = new Pen(Theme.Edge))
			{
				g.DrawLine(pen, 22 * k, 154 * k, ClientSize.Width - 22 * k, 154 * k);
				g.DrawLine(pen, 22 * k, 296 * k, ClientSize.Width - 22 * k, 296 * k);
			}
			Theme.Ornaments(g, ClientRectangle, k);
			base.OnPaint(e);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_tips.Dispose();
				_head.Dispose();
				_tag.Dispose();
				_small.Dispose();
				_link.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
