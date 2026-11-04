using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ScreenDimmer.Core;
using ScreenDimmer.Models;

namespace ScreenDimmer.Services
{
    /// <summary>
    /// Administra el icono del área de notificación (System Tray) y su menú contextual.
    /// Permite controlar la aplicación discretamente en segundo plano.
    /// </summary>
    public sealed class SystemTrayService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly BrightnessController _controller;
        private readonly ProfileManager _profileManager;
        private readonly StartupService _startupService;

        private ToolStripMenuItem _itemToggle = null!;
        private ToolStripMenuItem _itemStartup = null!;
        private ToolStripMenuItem _menuProfiles = null!;

        public Action? ShowMainWindowRequested { get; set; }
        public Action? ExitApplicationRequested { get; set; }

        public SystemTrayService(BrightnessController controller, ProfileManager profileManager, StartupService startupService)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));

            _contextMenu = new ContextMenuStrip();
            _notifyIcon = new NotifyIcon
            {
                ContextMenuStrip = _contextMenu,
                Visible = true
            };

            LoadIcon();
            BuildContextMenu();
            UpdateStatus(_controller.DimmingLevel, _controller.IsEnabled);

            _controller.DimmingChanged += UpdateStatus;
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindowRequested?.Invoke();
        }

        private void LoadIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                }
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }
        }

        private void BuildContextMenu()
        {
            _contextMenu.Items.Clear();

            // Título de la aplicación
            var itemHeader = new ToolStripMenuItem("ScreenDimmer")
            {
                Enabled = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _contextMenu.Items.Add(itemHeader);
            _contextMenu.Items.Add(new ToolStripSeparator());

            // Mostrar ventana
            var itemShow = new ToolStripMenuItem("Abrir ScreenDimmer", null, (s, e) => ShowMainWindowRequested?.Invoke())
            {
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            _contextMenu.Items.Add(itemShow);

            // Activar / Desactivar
            _itemToggle = new ToolStripMenuItem("Atenuación activa", null, (s, e) => _controller.Toggle())
            {
                CheckOnClick = true
            };
            _contextMenu.Items.Add(_itemToggle);

            // Submenú de perfiles
            _menuProfiles = new ToolStripMenuItem("Perfiles de iluminación");
            RebuildProfileSubmenu();
            _contextMenu.Items.Add(_menuProfiles);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // Iniciar con Windows
            _itemStartup = new ToolStripMenuItem("Iniciar con Windows", null, (s, e) =>
            {
                bool newState = !_startupService.IsStartupEnabled();
                _startupService.SetStartup(newState);
                _itemStartup.Checked = newState;
            })
            {
                Checked = _startupService.IsStartupEnabled()
            };
            _contextMenu.Items.Add(_itemStartup);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // Salir
            var itemExit = new ToolStripMenuItem("Salir de ScreenDimmer", null, (s, e) => ExitApplicationRequested?.Invoke());
            _contextMenu.Items.Add(itemExit);
        }

        public void RebuildProfileSubmenu()
        {
            _menuProfiles.DropDownItems.Clear();
            foreach (var profile in _profileManager.GetAllProfiles())
            {
                var profileItem = new ToolStripMenuItem(
                    $"{profile.Name} ({profile.DimmingPercentage}%)",
                    null,
                    (s, e) =>
                    {
                        _profileManager.SelectProfile(profile.Name);
                        _controller.ApplyProfile(profile);
                    }
                )
                {
                    Checked = _profileManager.ActiveProfile.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)
                };
                _menuProfiles.DropDownItems.Add(profileItem);
            }
        }

        private void UpdateStatus(int dimming, bool isEnabled)
        {
            if (_itemToggle != null)
            {
                _itemToggle.Checked = isEnabled;
            }

            string statusText = isEnabled && dimming > 0
                ? $"ScreenDimmer: {dimming}% (Activo)"
                : "ScreenDimmer: Inactivo";

            if (statusText.Length >= 64)
            {
                statusText = statusText.Substring(0, 63);
            }

            _notifyIcon.Text = statusText;
            RebuildProfileSubmenu();
        }

        public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon.ShowBalloonTip(2000, title, message, icon);
        }

        public void Dispose()
        {
            _controller.DimmingChanged -= UpdateStatus;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
        }
    }
}
