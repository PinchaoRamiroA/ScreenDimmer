using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using ScreenDimmer.Interop;

namespace ScreenDimmer.Core
{
    /// <summary>
    /// Ventana de superposición borderless y click-through para atenuación de pantalla.
    /// Garantiza oscurecimiento visual efectivo sin parpadeos, rebotes de brillo ni interferencia con el ratón.
    /// </summary>
    public sealed class ScreenOverlay : Form
    {
        private bool _isDimmingActive = false;
        private int _dimmingPercentage = 0;
        private byte _currentAlpha = 0;
        private Rectangle _lastBounds = Rectangle.Empty;
        private bool _isDisposed = false;

        public bool IsDimmingActive => _isDimmingActive;
        public int DimmingPercentage => _dimmingPercentage;

        public ScreenOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            TopMost = true;

            // Configurar estilos para renderizado sólido sin rebote
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.Opaque, true);

            UpdateBoundsToScreen(force: true);

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Estilos Win32 requeridos:
                // WS_EX_LAYERED: transparencia nativa por capa (alpha blending)
                // WS_EX_TRANSPARENT: click-through total (eventos del ratón atraviesan la capa)
                // WS_EX_NOACTIVATE: no roba el foco de la ventana activa
                // WS_EX_TOOLWINDOW: no aparece en Alt+Tab ni en la barra de tareas
                // WS_EX_TOPMOST: se mantiene por encima de las ventanas normales
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED |
                              NativeMethods.WS_EX_TRANSPARENT |
                              NativeMethods.WS_EX_NOACTIVATE |
                              NativeMethods.WS_EX_TOOLWINDOW |
                              NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // Aplicar opacidad inicial si está activa
            if (_isDimmingActive && _currentAlpha > 0)
            {
                NativeMethods.SetLayeredWindowAttributes(Handle, 0, _currentAlpha, NativeMethods.LWA_ALPHA);
                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
                );
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_ERASEBKGND = 0x0014;
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;

            switch (m.Msg)
            {
                case WM_ERASEBKGND:
                    // Rellenar directamente con brocha negra sólida (BLACK_BRUSH)
                    // Esto evita que Windows borre con el color por defecto (blanco) durante cambios de foco o pestaña.
                    NativeMethods.RECT rc;
                    NativeMethods.GetClientRect(Handle, out rc);
                    IntPtr hbr = NativeMethods.GetStockObject(NativeMethods.BLACK_BRUSH);
                    NativeMethods.FillRect(m.WParam, ref rc, hbr);
                    m.Result = (IntPtr)1;
                    return;

                case WM_NCHITTEST:
                    // Click-through nativo instantáneo
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
            }

            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Evitar borrado estándar con brocha de fondo del sistema
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Garantizar que la superficie siempre contenga píxeles negros puros para que el canal alfa los oscurezca
            e.Graphics.Clear(Color.Black);
        }

        /// <summary>
        /// Actualiza las dimensiones del overlay si la resolución ha cambiado físicamente.
        /// </summary>
        public void UpdateBoundsToScreen(bool force = false)
        {
            if (_isDisposed) return;

            Rectangle screenBounds = SystemInformation.VirtualScreen;
            if (screenBounds.IsEmpty)
            {
                screenBounds = Screen.PrimaryScreen?.Bounds ?? Screen.GetBounds(Point.Empty);
            }

            if (!force && screenBounds == _lastBounds)
            {
                return;
            }

            _lastBounds = screenBounds;
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
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER
                );
            }
        }

        /// <summary>
        /// Aplica un nivel de atenuación en tiempo real (0% a 85%).
        /// </summary>
        public void SetDimming(int percentage, bool active)
        {
            if (_isDisposed) return;

            int clamped = Math.Clamp(percentage, 0, 85);
            bool shouldBeActive = active && clamped > 0;
            byte alpha = shouldBeActive ? (byte)Math.Round((clamped / 100.0) * 255.0) : (byte)0;

            _dimmingPercentage = clamped;
            _isDimmingActive = shouldBeActive;
            _currentAlpha = alpha;

            if (!_isDimmingActive)
            {
                if (IsHandleCreated)
                {
                    NativeMethods.SetLayeredWindowAttributes(Handle, 0, 0, NativeMethods.LWA_ALPHA);
                }
                Hide();
                return;
            }

            // Si debe estar activo, garantizar que la ventana exista y esté visible
            UpdateBoundsToScreen(force: false);

            if (!IsHandleCreated)
            {
                CreateHandle();
            }

            NativeMethods.SetLayeredWindowAttributes(Handle, 0, alpha, NativeMethods.LWA_ALPHA);

            if (!Visible)
            {
                Show();
                NativeMethods.SetWindowPos(
                    Handle,
                    NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
                );
            }
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            try
            {
                // Filtrar falsos positivos: sólo actualizar si las dimensiones físicas de la pantalla cambiaron
                Rectangle current = SystemInformation.VirtualScreen;
                if (current.IsEmpty)
                {
                    current = Screen.PrimaryScreen?.Bounds ?? Screen.GetBounds(Point.Empty);
                }

                if (current != _lastBounds)
                {
                    UpdateBoundsToScreen(force: true);
                }
            }
            catch
            {
                // Protección ante cambios bruscos de monitor
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
