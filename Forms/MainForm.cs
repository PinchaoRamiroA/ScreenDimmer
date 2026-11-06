using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using ScreenDimmer.Core;
using ScreenDimmer.Interop;
using ScreenDimmer.Models;
using ScreenDimmer.Services;

namespace ScreenDimmer.Forms
{
    /// <summary>
    /// Ventana principal de ScreenDimmer con tema oscuro nativo Windows 11,
    /// controles interactivos en tiempo real, selector de perfiles y atajos globales.
    /// </summary>
    public partial class MainForm : Form
    {
        private readonly BrightnessController _controller;
        private readonly ProfileManager _profileManager;
        private readonly HotkeyManager _hotkeyManager;
        private readonly SettingsService _settingsService;
        private readonly StartupService _startupService;
        private readonly AppSettings _settings;

        private bool _isExiting = false;
        private bool _isInternalUpdating = false;
        private readonly System.Windows.Forms.Timer _reapplyTimer;

        // Controles de la interfaz gráfica
        private Label _lblTitle = null!;
        private Label _lblSubtitle = null!;
        private Label _lblStatusBadge = null!;
        private Label _lblPercentage = null!;
        private Label _lblInterpretation = null!;
        private TrackBar _trackDimming = null!;
        private Button _btnToggle = null!;
        private Button _btnReset = null!;
        private CheckBox _chkStartup = null!;
        private CheckBox _chkStartMinimized = null!;
        private readonly Dictionary<string, Button> _profileButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

        // Colores del tema oscuro Windows 11
        private static readonly Color BgDark = Color.FromArgb(24, 24, 27);         // Zinc 900
        private static readonly Color CardBg = Color.FromArgb(39, 39, 42);         // Zinc 800
        private static readonly Color CardBorder = Color.FromArgb(63, 63, 70);     // Zinc 700
        private static readonly Color AccentBlue = Color.FromArgb(14, 165, 233);    // Sky 500
        private static readonly Color AccentBlueHover = Color.FromArgb(2, 132, 199);// Sky 600
        private static readonly Color TextPrimary = Color.FromArgb(244, 244, 245);  // Zinc 100
        private static readonly Color TextMuted = Color.FromArgb(161, 161, 170);    // Zinc 400
        private static readonly Color ActiveGreen = Color.FromArgb(16, 185, 129);   // Emerald 500
        private static readonly Color ActiveGreenBg = Color.FromArgb(6, 78, 59);    // Emerald 900

        public MainForm(
            BrightnessController controller,
            ProfileManager profileManager,
            HotkeyManager hotkeyManager,
            SettingsService settingsService,
            StartupService startupService,
            AppSettings settings)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _hotkeyManager = hotkeyManager ?? throw new ArgumentNullException(nameof(hotkeyManager));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            InitializeComponent();
            ApplyCustomStyling();
            BuildModernUi();

            // Sincronizar eventos del controlador
            _controller.DimmingChanged += OnControllerDimmingChanged;
            _profileManager.ProfileChanged += OnProfileChanged;

            // Windows restablece la rampa gamma al apagar/encender la pantalla, al cambiar
            // la resolución o al reanudar la sesión. Este vigilante la reaplica si se perdió.
            _reapplyTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _reapplyTimer.Tick += (s, e) => _controller.ReapplyIfNeeded();
            _reapplyTimer.Start();

            // Asegurar que la ventana principal siempre flote por encima del overlay
            TopMost = true;
        }

        private void ApplyCustomStyling()
        {
            BackColor = BgDark;
            ForeColor = TextPrimary;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            DoubleBuffered = true;

            // Cargar icono oficial si está disponible
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
                if (File.Exists(iconPath))
                {
                    Icon = new Icon(iconPath);
                }
            }
            catch
            {
                // Fallback a icono predeterminado
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // Aplicar barra de título oscura inmersiva de Windows 11 vía DWM
            int darkMode = 1;
            NativeMethods.DwmSetWindowAttribute(
                Handle,
                NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref darkMode,
                sizeof(int)
            );

            // Registrar atajos de teclado globales
            if (_settings.HotkeysEnabled)
            {
                _hotkeyManager.RegisterHotkeys(Handle);
            }
        }

        private void BuildModernUi()
        {
            SuspendLayout();

            // 1. Panel de encabezado
            Panel pnlHeader = new Panel
            {
                Location = new Point(20, 16),
                Size = new Size(420, 50),
                BackColor = Color.Transparent
            };

            _lblTitle = new Label
            {
                Text = "ScreenDimmer",
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(0, 0),
                AutoSize = true
            };

            _lblSubtitle = new Label
            {
                Text = "Atenuación avanzada para Windows 11",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                Location = new Point(2, 28),
                AutoSize = true
            };

            _lblStatusBadge = new Label
            {
                Text = "● ACTIVO",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = ActiveGreen,
                BackColor = ActiveGreenBg,
                Size = new Size(95, 24),
                Location = new Point(325, 10),
                TextAlign = ContentAlignment.MiddleCenter
            };

            pnlHeader.Controls.Add(_lblTitle);
            pnlHeader.Controls.Add(_lblSubtitle);
            pnlHeader.Controls.Add(_lblStatusBadge);

            // 2. Tarjeta central de nivel de brillo y Slider
            Panel cardDimming = new Panel
            {
                Location = new Point(20, 75),
                Size = new Size(420, 195),
                BackColor = CardBg,
                Padding = new Padding(16)
            };
            cardDimming.Paint += (s, e) =>
            {
                using var pen = new Pen(CardBorder, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, cardDimming.Width - 1, cardDimming.Height - 1);
            };

            _lblPercentage = new Label
            {
                Text = $"{_controller.DimmingLevel} %",
                Font = new Font("Segoe UI", 32F, FontStyle.Bold),
                ForeColor = AccentBlue,
                Location = new Point(16, 12),
                Size = new Size(180, 55),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lblInterpretation = new Label
            {
                Text = GetInterpretationText(_controller.DimmingLevel),
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = TextPrimary,
                Location = new Point(190, 24),
                Size = new Size(215, 35),
                TextAlign = ContentAlignment.MiddleRight
            };

            _trackDimming = new TrackBar
            {
                Location = new Point(16, 75),
                Size = new Size(388, 45),
                Minimum = 0,
                Maximum = 85,
                TickFrequency = 5,
                TickStyle = TickStyle.BottomRight,
                Value = _controller.DimmingLevel,
                BackColor = CardBg
            };
            _trackDimming.Scroll += OnTrackbarScroll;

            Label lblMinMark = new Label
            {
                Text = "0% (Original)",
                Font = new Font("Segoe UI", 8F),
                ForeColor = TextMuted,
                Location = new Point(16, 120),
                AutoSize = true
            };

            Label lblMaxMark = new Label
            {
                Text = "85% (Máx)",
                Font = new Font("Segoe UI", 8F),
                ForeColor = TextMuted,
                Location = new Point(340, 120),
                Size = new Size(64, 15),
                TextAlign = ContentAlignment.TopRight
            };

            // Botones de acción dentro de la tarjeta
            _btnToggle = new Button
            {
                Text = _controller.IsEnabled ? "Desactivar atenuación" : "Activar atenuación",
                Location = new Point(16, 145),
                Size = new Size(230, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = _controller.IsEnabled ? AccentBlue : CardBorder,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnToggle.FlatAppearance.BorderSize = 0;
            _btnToggle.Click += (s, e) => _controller.Toggle();

            _btnReset = new Button
            {
                Text = "Restablecer (0%)",
                Location = new Point(255, 145),
                Size = new Size(149, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = CardBorder,
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            _btnReset.FlatAppearance.BorderSize = 0;
            _btnReset.Click += (s, e) => _controller.Reset();

            cardDimming.Controls.Add(_lblPercentage);
            cardDimming.Controls.Add(_lblInterpretation);
            cardDimming.Controls.Add(_trackDimming);
            cardDimming.Controls.Add(lblMinMark);
            cardDimming.Controls.Add(lblMaxMark);
            cardDimming.Controls.Add(_btnToggle);
            cardDimming.Controls.Add(_btnReset);

            // 3. Sección de Perfiles de Iluminación
            Label lblProfilesTitle = new Label
            {
                Text = "PERFILES DE ILUMINACIÓN",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = TextMuted,
                Location = new Point(20, 280),
                AutoSize = true
            };

            TableLayoutPanel pnlProfiles = new TableLayoutPanel
            {
                Location = new Point(20, 300),
                Size = new Size(420, 60),
                ColumnCount = 5,
                RowCount = 1,
                BackColor = Color.Transparent
            };

            for (int i = 0; i < 5; i++)
            {
                pnlProfiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            }

            AddProfileButton(pnlProfiles, "Día", 15, 0);
            AddProfileButton(pnlProfiles, "Tarde", 30, 1);
            AddProfileButton(pnlProfiles, "Noche", 50, 2);
            AddProfileButton(pnlProfiles, "Lectura", 65, 3);
            AddProfileButton(pnlProfiles, "Personalizado", _settings.CustomDimmingPercentage, 4);

            // 4. Tarjeta de Atajos y Opciones
            Panel cardSettings = new Panel
            {
                Location = new Point(20, 372),
                Size = new Size(420, 225),
                BackColor = CardBg,
                Padding = new Padding(14)
            };
            cardSettings.Paint += (s, e) =>
            {
                using var pen = new Pen(CardBorder, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, cardSettings.Width - 1, cardSettings.Height - 1);
            };

            Button btnToggleEngine = new Button
            {
                Text = _controller.Engine == DimmingEngine.Hardware
                    ? "⚡ Modo: Hardware GPU (Inmune a scroll y vídeos)"
                    : "🪟 Modo: Capa Overlay (Ventana semitransparente)",
                Location = new Point(14, 12),
                Size = new Size(392, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = _controller.Engine == DimmingEngine.Hardware ? Color.FromArgb(12, 74, 110) : CardBorder,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.8F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnToggleEngine.FlatAppearance.BorderSize = 1;
            btnToggleEngine.FlatAppearance.BorderColor = AccentBlue;
            btnToggleEngine.Click += (s, e) =>
            {
                var newEngine = _controller.Engine == DimmingEngine.Hardware ? DimmingEngine.Overlay : DimmingEngine.Hardware;
                _controller.SetEngine(newEngine);
                _settings.Engine = newEngine;
                _settingsService.Save(_settings);

                btnToggleEngine.Text = newEngine == DimmingEngine.Hardware
                    ? "⚡ Modo: Hardware GPU (Inmune a scroll y vídeos)"
                    : "🪟 Modo: Capa Overlay (Ventana semitransparente)";
                btnToggleEngine.BackColor = newEngine == DimmingEngine.Hardware ? Color.FromArgb(12, 74, 110) : CardBorder;
            };

            _chkStartup = new CheckBox
            {
                Text = "Iniciar automáticamente con Windows",
                Location = new Point(14, 52),
                Size = new Size(390, 22),
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 9F),
                Checked = _startupService.IsStartupEnabled(),
                Cursor = Cursors.Hand
            };
            _chkStartup.CheckedChanged += (s, e) =>
            {
                _startupService.SetStartup(_chkStartup.Checked, _chkStartMinimized.Checked);
                _settings.StartWithWindows = _chkStartup.Checked;
                _settingsService.Save(_settings);
            };

            _chkStartMinimized = new CheckBox
            {
                Text = "Iniciar minimizado en la bandeja del sistema",
                Location = new Point(14, 78),
                Size = new Size(390, 22),
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 9F),
                Checked = _settings.StartMinimized,
                Cursor = Cursors.Hand
            };
            _chkStartMinimized.CheckedChanged += (s, e) =>
            {
                _settings.StartMinimized = _chkStartMinimized.Checked;
                _settingsService.Save(_settings);
            };

            Label lblHotkeysTitle = new Label
            {
                Text = "ATAJOS DE TECLADO GLOBALES",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = TextMuted,
                Location = new Point(14, 110),
                AutoSize = true
            };

            Panel pnlHotkeys = new Panel
            {
                Location = new Point(14, 130),
                Size = new Size(392, 80),
                BackColor = Color.FromArgb(28, 28, 31)
            };

            Label lblHk1 = new Label { Text = "Ctrl + Alt + ↑", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = AccentBlue, Location = new Point(8, 8), Size = new Size(95, 18) };
            Label lblHk1Desc = new Label { Text = "Reducir atenuación (-5%)", Font = new Font("Segoe UI", 8.5F), ForeColor = TextMuted, Location = new Point(105, 8), Size = new Size(270, 18) };

            Label lblHk2 = new Label { Text = "Ctrl + Alt + ↓", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = AccentBlue, Location = new Point(8, 26), Size = new Size(95, 18) };
            Label lblHk2Desc = new Label { Text = "Aumentar atenuación (+5%)", Font = new Font("Segoe UI", 8.5F), ForeColor = TextMuted, Location = new Point(105, 26), Size = new Size(270, 18) };

            Label lblHk3 = new Label { Text = "Ctrl + Alt + 0", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = AccentBlue, Location = new Point(8, 44), Size = new Size(95, 18) };
            Label lblHk3Desc = new Label { Text = "Desactivar atenuación", Font = new Font("Segoe UI", 8.5F), ForeColor = TextMuted, Location = new Point(105, 44), Size = new Size(270, 18) };

            Label lblHk4 = new Label { Text = "Ctrl + Alt + 1", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = AccentBlue, Location = new Point(8, 62), Size = new Size(95, 18) };
            Label lblHk4Desc = new Label { Text = "Alternar efecto activo/inactivo", Font = new Font("Segoe UI", 8.5F), ForeColor = TextMuted, Location = new Point(105, 62), Size = new Size(270, 18) };

            pnlHotkeys.Controls.AddRange(new Control[] { lblHk1, lblHk1Desc, lblHk2, lblHk2Desc, lblHk3, lblHk3Desc, lblHk4, lblHk4Desc });

            cardSettings.Controls.Add(btnToggleEngine);
            cardSettings.Controls.Add(_chkStartup);
            cardSettings.Controls.Add(_chkStartMinimized);
            cardSettings.Controls.Add(lblHotkeysTitle);
            cardSettings.Controls.Add(pnlHotkeys);

            // 5. Pie de página
            Label lblFooter = new Label
            {
                Text = "Al cerrar esta ventana, ScreenDimmer continuará activo en la bandeja del sistema.",
                Font = new Font("Segoe UI", 7.8F),
                ForeColor = TextMuted,
                Location = new Point(20, 615),
                Size = new Size(420, 32),
                TextAlign = ContentAlignment.TopCenter
            };

            Controls.Add(pnlHeader);
            Controls.Add(cardDimming);
            Controls.Add(lblProfilesTitle);
            Controls.Add(pnlProfiles);
            Controls.Add(cardSettings);
            Controls.Add(lblFooter);

            ResumeLayout(false);

            UpdateProfileHighlight(_profileManager.ActiveProfile.Name);
            UpdateUiState(_controller.DimmingLevel, _controller.IsEnabled);
        }

        private void AddProfileButton(TableLayoutPanel panel, string name, int percentage, int column)
        {
            Button btn = new Button
            {
                Text = $"{name}\n{percentage}%",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = CardBg,
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Margin = new Padding(2),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = CardBorder;

            btn.Click += (s, e) =>
            {
                _profileManager.SelectProfile(name);
                var prof = _profileManager.ActiveProfile;
                _controller.ApplyProfile(prof);
            };

            _profileButtons[name] = btn;
            panel.Controls.Add(btn, column, 0);
        }

        private void OnTrackbarScroll(object? sender, EventArgs e)
        {
            if (_isInternalUpdating) return;

            int val = _trackDimming.Value;
            _profileManager.MatchOrSetCustom(val);
            _controller.SetDimming(val);
        }

        private void OnControllerDimmingChanged(int level, bool isEnabled)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnControllerDimmingChanged(level, isEnabled)));
                return;
            }

            UpdateUiState(level, isEnabled);
        }

        private void OnProfileChanged(BrightnessProfile profile)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnProfileChanged(profile)));
                return;
            }

            UpdateProfileHighlight(profile.Name);
            if (_profileButtons.TryGetValue("Personalizado", out var customBtn))
            {
                customBtn.Text = $"Personalizado\n{_profileManager.CustomPercentage}%";
            }
        }

        private void UpdateUiState(int level, bool isEnabled)
        {
            _isInternalUpdating = true;
            try
            {
                _lblPercentage.Text = $"{level} %";
                _lblInterpretation.Text = GetInterpretationText(level);
                _trackDimming.Value = Math.Clamp(level, 0, 85);

                if (isEnabled && level > 0)
                {
                    _lblStatusBadge.Text = "● ACTIVO";
                    _lblStatusBadge.ForeColor = ActiveGreen;
                    _lblStatusBadge.BackColor = ActiveGreenBg;
                    _btnToggle.Text = "Desactivar atenuación";
                    _btnToggle.BackColor = AccentBlueHover;
                }
                else
                {
                    _lblStatusBadge.Text = "○ INACTIVO";
                    _lblStatusBadge.ForeColor = TextMuted;
                    _lblStatusBadge.BackColor = Color.FromArgb(39, 39, 42);
                    _btnToggle.Text = "Activar atenuación";
                    _btnToggle.BackColor = CardBorder;
                }

                // Guardar preferencias en segundo plano
                _settings.DimmingPercentage = level;
                _settings.IsEnabled = isEnabled;
                _settings.SelectedProfile = _profileManager.ActiveProfile.Name;
                _settings.CustomDimmingPercentage = _profileManager.CustomPercentage;
                _settingsService.Save(_settings);
            }
            finally
            {
                _isInternalUpdating = false;
            }
        }

        private void UpdateProfileHighlight(string activeProfileName)
        {
            foreach (var kvp in _profileButtons)
            {
                bool isSelected = kvp.Key.Equals(activeProfileName, StringComparison.OrdinalIgnoreCase);
                kvp.Value.BackColor = isSelected ? Color.FromArgb(14, 165, 233, 40) : CardBg;
                kvp.Value.FlatAppearance.BorderColor = isSelected ? AccentBlue : CardBorder;
                kvp.Value.Font = new Font(kvp.Value.Font, isSelected ? FontStyle.Bold : FontStyle.Regular);
            }
        }

        private static string GetInterpretationText(int level)
        {
            if (level == 0) return "Pantalla original";
            if (level <= 20) return "Oscurecimiento ligero";
            if (level <= 45) return "Oscurecimiento moderado";
            if (level <= 65) return "Oscurecimiento fuerte";
            return "Oscurecimiento muy intenso";
        }

        protected override void WndProc(ref Message m)
        {
            // Procesar combinaciones de teclas registradas globalmente
            if (_hotkeyManager.ProcessMessage(ref m))
            {
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Cumplir con el requisito: "Cerrar la ventana principal debe minimizar la aplicación a la bandeja, no finalizar el proceso."
            if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnFormClosing(e);
        }

        public void ExitApplication()
        {
            _reapplyTimer.Stop();
            _reapplyTimer.Dispose();
            _isExiting = true;
            Close();
            Application.Exit();
        }
    }
}
