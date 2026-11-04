using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using ScreenDimmer.Interop;

namespace ScreenDimmer.Core
{
    /// <summary>
    /// Ventana de superposición borderless y click-through para atenuación de pantalla.
    /// Utiliza estilos nativos Win32 (WS_EX_LAYERED, WS_EX_TRANSPARENT, WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW)
    /// para permitir que los eventos de teclado y ratón atraviesen completamente la capa sin interferencias.
    /// </summary>
    public sealed class ScreenOverlay : Form
    {
        private bool _isDimmingActive = false;
        private int _dimmingPercentage = 0;
        private bool _isDisposed = false;

        public bool IsDimmingActive => _isDimmingActive;
        public int DimmingPercentage => _dimmingPercentage;

        public ScreenOverlay()
        {
            // Configuración base de la ventana
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            TopMost = true;

            // Optimización de renderizado para evitar parpadeos
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.Opaque, true);

            UpdateBoundsToScreen();

            // Suscribirse a cambios en la configuración o resolución de pantallas
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Aplicar estilos extendidos Win32 requeridos:
                // WS_EX_LAYERED: permite opacidad personalizada por capa
                // WS_EX_TRANSPARENT: permite click-through total (los clics pasan a la ventana inferior)
                // WS_EX_NOACTIVATE: evita que la ventana robe el foco al mostrarse o cambiar
                // WS_EX_TOOLWINDOW: evita que aparezca en el conmutador Alt+Tab
                // WS_EX_TOPMOST: mantiene la ventana por encima del resto
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED |
                              NativeMethods.WS_EX_TRANSPARENT |
                              NativeMethods.WS_EX_NOACTIVATE |
                              NativeMethods.WS_EX_TOOLWINDOW |
                              NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        /// <summary>
        /// Actualiza las dimensiones del overlay para cubrir la pantalla principal del portátil.
        /// </summary>
        public void UpdateBoundsToScreen()
        {
            if (_isDisposed) return;

            Rectangle screenBounds = Screen.PrimaryScreen?.Bounds ?? Screen.GetBounds(Point.Empty);
            Bounds = screenBounds;

            if (IsHandleCreated)
            {
                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HWND_TOPMOST,
                    screenBounds.X,
                    screenBounds.Y,
                    screenBounds.Width,
                    screenBounds.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER | NativeMethods.SWP_SHOWWINDOW
                );
            }
        }

        /// <summary>
        /// Aplica un nivel de atenuación en tiempo real (0% a 85%).
        /// No utiliza bucles continuos; la actualización se realiza únicamente ante cambios de estado.
        /// </summary>
        /// <param name="percentage">Porcentaje entre 0 y 85.</param>
        /// <param name="active">Indica si el efecto de atenuación está habilitado.</param>
        public void SetDimming(int percentage, bool active)
        {
            if (_isDisposed) return;

            _dimmingPercentage = Math.Clamp(percentage, 0, 85);
            _isDimmingActive = active && _dimmingPercentage > 0;

            if (!IsHandleCreated)
            {
                CreateHandle();
            }

            if (!_isDimmingActive)
            {
                // Si la atenuación está inactiva o en 0%, ocultar o fijar alfa en 0
                NativeMethods.SetLayeredWindowAttributes(Handle, 0, 0, NativeMethods.LWA_ALPHA);
                NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
                return;
            }

            // Calcular byte de transparencia (0 - 255)
            // 85% corresponde a aprox. 217 de 255
            byte alpha = (byte)Math.Round((_dimmingPercentage / 100.0) * 255.0);

            // Asegurar que la ventana cubra la pantalla y esté visible sin robar foco
            UpdateBoundsToScreen();
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            NativeMethods.SetLayeredWindowAttributes(Handle, 0, alpha, NativeMethods.LWA_ALPHA);

            // Reforzar posición topmost sin foco
            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
            );
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            try
            {
                UpdateBoundsToScreen();
                if (_isDimmingActive)
                {
                    SetDimming(_dimmingPercentage, true);
                }
            }
            catch
            {
                // Proteger contra excepciones durante cambios bruscos de hardware gráfico
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                }

                // Restaurar estado visual antes de cerrar
                if (IsHandleCreated)
                {
                    NativeMethods.SetLayeredWindowAttributes(Handle, 0, 0, NativeMethods.LWA_ALPHA);
                    NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
                }

                _isDisposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
