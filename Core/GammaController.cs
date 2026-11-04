using System;
using System.Runtime.InteropServices;
using ScreenDimmer.Interop;

namespace ScreenDimmer.Core
{
    /// <summary>
    /// Controlador de atenuación directa por hardware mediante la tabla de búsqueda (LUT / Gamma Ramp) de la GPU.
    /// Este mecanismo actúa a nivel de la salida física del chip gráfico hacia el monitor, haciéndolo 100% inmune
    /// a Multi-Plane Overlays (MPO), DirectFlip, scrolls en navegadores, reproducciones de vídeo y diálogos modales.
    /// </summary>
    public sealed class GammaController : IDisposable
    {
        private NativeMethods.RAMP _originalRamp;
        private bool _hasOriginalRamp = false;
        private IntPtr _screenDc = IntPtr.Zero;
        private bool _isDisposed = false;

        public bool IsSupported { get; private set; }

        public GammaController()
        {
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                _screenDc = NativeMethods.GetDC(IntPtr.Zero);
                if (_screenDc != IntPtr.Zero)
                {
                    _originalRamp = new NativeMethods.RAMP();
                    IsSupported = NativeMethods.GetDeviceGammaRamp(_screenDc, ref _originalRamp);
                    _hasOriginalRamp = IsSupported;
                }
            }
            catch
            {
                IsSupported = false;
            }
        }

        /// <summary>
        /// Aplica atenuación escalando los canales RGB de la tabla de color del hardware.
        /// </summary>
        /// <param name="percentage">Porcentaje de atenuación (0 a 85%).</param>
        /// <param name="active">Indica si la atenuación está habilitada.</param>
        public bool SetDimming(int percentage, bool active)
        {
            if (_isDisposed || !IsSupported || !_hasOriginalRamp) return false;

            if (_screenDc == IntPtr.Zero)
            {
                _screenDc = NativeMethods.GetDC(IntPtr.Zero);
            }

            if (!active || percentage <= 0)
            {
                return Restore();
            }

            int clamped = Math.Clamp(percentage, 0, 85);
            // Factor de brillo restante: 0% = 1.0 (brillo completo), 85% = 0.15
            double brightnessFactor = 1.0 - (clamped / 100.0);

            var dimmedRamp = new NativeMethods.RAMP
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256]
            };

            for (int i = 0; i < 256; i++)
            {
                dimmedRamp.Red[i] = (ushort)Math.Clamp(Math.Round(_originalRamp.Red[i] * brightnessFactor), 0, 65535);
                dimmedRamp.Green[i] = (ushort)Math.Clamp(Math.Round(_originalRamp.Green[i] * brightnessFactor), 0, 65535);
                dimmedRamp.Blue[i] = (ushort)Math.Clamp(Math.Round(_originalRamp.Blue[i] * brightnessFactor), 0, 65535);
            }

            return NativeMethods.SetDeviceGammaRamp(_screenDc, ref dimmedRamp);
        }

        /// <summary>
        /// Restablece la calibración original de color del monitor.
        /// </summary>
        public bool Restore()
        {
            if (!_hasOriginalRamp) return false;

            if (_screenDc == IntPtr.Zero)
            {
                _screenDc = NativeMethods.GetDC(IntPtr.Zero);
            }

            if (_screenDc != IntPtr.Zero)
            {
                return NativeMethods.SetDeviceGammaRamp(_screenDc, ref _originalRamp);
            }

            return false;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                Restore();
                if (_screenDc != IntPtr.Zero)
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, _screenDc);
                    _screenDc = IntPtr.Zero;
                }
                _isDisposed = true;
            }
        }
    }
}
