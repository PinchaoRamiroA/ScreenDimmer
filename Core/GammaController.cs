using System;
using System.Runtime.InteropServices;
using ScreenDimmer.Interop;

namespace ScreenDimmer.Core
{
    /// <summary>
    /// Controlador de atenuación directa por hardware mediante la tabla de búsqueda (LUT / Gamma Ramp) de la GPU.
    /// Este mecanismo actúa a nivel de la salida física del chip gráfico hacia el monitor, haciéndolo 100% inmune
    /// a Multi-Plane Overlays (MPO), DirectFlip, scrolls en navegadores, reproducciones de vídeo y diálogos modales.
    ///
    /// Windows restablece la rampa gamma cuando la pantalla se apaga y se vuelve a encender, al cambiar la
    /// resolución o al reanudar la sesión. Por eso este controlador conserva el nivel aplicado y expone
    /// <see cref="NeedsReapply"/> para que la aplicación pueda restaurar la atenuación cuando se pierda.
    /// </summary>
    public sealed class GammaController : IDisposable
    {
        private const int MaxDimming = 85;

        private NativeMethods.RAMP _originalRamp;
        private bool _hasOriginalRamp = false;
        private IntPtr _screenDc = IntPtr.Zero;
        private bool _isDisposed = false;

        public bool IsSupported { get; private set; }

        public GammaController()
        {
            TryInitialize();
        }

        /// <summary>
        /// Obtiene el DC de pantalla y captura la rampa original si aún no se conoce.
        /// Es seguro llamarlo repetidamente: se usa para recuperar el hardware después de
        /// que el DC quede inválido tras suspender o apagar la pantalla.
        /// </summary>
        public bool TryInitialize()
        {
            if (_isDisposed) return false;

            if (_screenDc == IntPtr.Zero)
            {
                _screenDc = NativeMethods.GetDC(IntPtr.Zero);
            }

            if (_screenDc == IntPtr.Zero)
            {
                IsSupported = false;
                return false;
            }

            if (!_hasOriginalRamp)
            {
                var ramp = new NativeMethods.RAMP();
                if (!NativeMethods.GetDeviceGammaRamp(_screenDc, ref ramp))
                {
                    // El DC pudo quedar inválido (suspensión / apagado de pantalla).
                    RefreshDeviceContext();
                    if (_screenDc == IntPtr.Zero)
                    {
                        IsSupported = false;
                        return false;
                    }

                    ramp = new NativeMethods.RAMP();
                    if (!NativeMethods.GetDeviceGammaRamp(_screenDc, ref ramp))
                    {
                        IsSupported = false;
                        return false;
                    }
                }

                _originalRamp = ramp;
                _hasOriginalRamp = true;
            }

            IsSupported = true;
            return true;
        }

        /// <summary>
        /// Aplica atenuación escalando los canales RGB de la tabla de color del hardware.
        /// </summary>
        /// <param name="percentage">Porcentaje de atenuación (0 a 85%).</param>
        /// <param name="active">Indica si la atenuación está habilitada.</param>
        public bool SetDimming(int percentage, bool active)
        {
            if (_isDisposed) return false;

            int clamped = Math.Clamp(percentage, 0, MaxDimming);

            if (!active || clamped <= 0)
            {
                return Restore();
            }

            if (!TryInitialize()) return false;

            var dimmedRamp = BuildDimmedRamp(clamped);
            if (NativeMethods.SetDeviceGammaRamp(_screenDc, ref dimmedRamp))
            {
                return true;
            }

            // El DC puede haber quedado inválido tras apagar/encender la pantalla.
            // Se refresca una vez y se reintenta antes de rendirse.
            RefreshDeviceContext();
            if (!TryInitialize()) return false;

            return NativeMethods.SetDeviceGammaRamp(_screenDc, ref dimmedRamp);
        }

        /// <summary>
        /// Restablece la calibración original de color del monitor.
        /// </summary>
        public bool Restore()
        {
            if (_isDisposed || !_hasOriginalRamp) return false;

            if (_screenDc == IntPtr.Zero)
            {
                _screenDc = NativeMethods.GetDC(IntPtr.Zero);
            }

            if (_screenDc == IntPtr.Zero) return false;

            if (NativeMethods.SetDeviceGammaRamp(_screenDc, ref _originalRamp))
            {
                return true;
            }

            RefreshDeviceContext();
            if (_screenDc == IntPtr.Zero) return false;

            return NativeMethods.SetDeviceGammaRamp(_screenDc, ref _originalRamp);
        }

        /// <summary>
        /// Comprueba si la atenuación solicitada sigue realmente activa en la GPU.
        /// Devuelve true cuando Windows restableció la rampa (por ejemplo, al encender
        /// la pantalla) y es necesario volver a aplicarla.
        /// </summary>
        public bool NeedsReapply(int percentage)
        {
            if (_isDisposed) return false;

            if (!TryInitialize()) return false;
            if (percentage <= 0) return false;
            if (!_hasOriginalRamp) return false;

            // Se consulta con un DC nuevo para no depender de un DC que pueda haber
            // quedado inválido tras apagar/encender la pantalla.
            IntPtr probeDc = NativeMethods.GetDC(IntPtr.Zero);
            if (probeDc == IntPtr.Zero) return true;

            try
            {
                var currentRamp = new NativeMethods.RAMP();
                if (!NativeMethods.GetDeviceGammaRamp(probeDc, ref currentRamp))
                {
                    return true;
                }

                var expectedRamp = BuildDimmedRamp(percentage);

                // Si los tres canales volvieron a estar más cerca del valor original que del
                // atenuado, la rampa fue restablecida externamente. Comparar los tres canales
                // evita reaplicar por cambios de temperatura de color (Night Light) u otras
                // aplicaciones que oscurezcan aún más la pantalla.
                return ChannelLost(currentRamp.Red, expectedRamp.Red, _originalRamp.Red)
                    && ChannelLost(currentRamp.Green, expectedRamp.Green, _originalRamp.Green)
                    && ChannelLost(currentRamp.Blue, expectedRamp.Blue, _originalRamp.Blue);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, probeDc);
            }
        }

        private static bool ChannelLost(ushort[] current, ushort[] expected, ushort[] original)
        {
            // Se comprueban dos puntos representativos de la curva. Se exige que la rampa
            // actual esté al menos al 80% del camino de vuelta hacia la original: así se
            // detecta un reinicio real sin reaplicar por otros atenuadores o Night Light.
            foreach (int i in new[] { 128, 255 })
            {
                int threshold = expected[i] + (int)((original[i] - expected[i]) * 0.8);
                if (current[i] <= threshold)
                {
                    return false;
                }
            }

            return true;
        }

        private NativeMethods.RAMP BuildDimmedRamp(int percentage)
        {
            int clamped = Math.Clamp(percentage, 0, MaxDimming);
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
                dimmedRamp.Red[i] = ScaleChannel(_originalRamp.Red[i], brightnessFactor);
                dimmedRamp.Green[i] = ScaleChannel(_originalRamp.Green[i], brightnessFactor);
                dimmedRamp.Blue[i] = ScaleChannel(_originalRamp.Blue[i], brightnessFactor);
            }

            return dimmedRamp;
        }

        private static ushort ScaleChannel(ushort value, double factor)
        {
            return (ushort)Math.Clamp(Math.Round(value * factor), 0, 65535);
        }

        private void RefreshDeviceContext()
        {
            if (_screenDc != IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, _screenDc);
                _screenDc = IntPtr.Zero;
            }

            _screenDc = NativeMethods.GetDC(IntPtr.Zero);
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
