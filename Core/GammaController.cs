using System;
using ScreenDimmer.Interop;

namespace ScreenDimmer.Core
{
    /// <summary>
    /// Controlador de atenuación directa por hardware mediante la tabla de búsqueda (LUT / Gamma Ramp) de la GPU.
    /// Este mecanismo actúa a nivel de la salida física del chip gráfico hacia el monitor, haciéndolo 100% inmune
    /// a Multi-Plane Overlays (MPO), DirectFlip, scrolls en navegadores, reproducciones de vídeo y diálogos modales.
    ///
    /// Windows valida las rampas gamma antes de aplicarlas: rechaza las que dejan el pico luminoso por debajo de
    /// la mitad del máximo y limita cuánto se pueden oscurecer los medios tonos. Por eso, pasado cierto nivel, no
    /// se puede seguir escalando la rampa linealmente (la escritura falla y antes provocaba que la aplicación
    /// cayera a la capa overlay, causando brillos distintos por pestaña o aplicación). Aquí se conserva el pico
    /// permitido y se oscurece la curva de medios tonos; si el driver aún así rechaza la rampa, se calibra en
    /// tiempo de ejecución el punto más oscuro que sí acepta.
    ///
    /// Windows también restablece la rampa cuando la pantalla se apaga y se vuelve a encender, al cambiar la
    /// resolución o al reanudar la sesión. Por eso el controlador conserva la rampa aplicada y expone
    /// <see cref="NeedsReapply"/> para que la aplicación pueda restaurarla cuando se pierda.
    /// </summary>
    public sealed class GammaController : IDisposable
    {
        private const int MaxDimming = 85;

        /// <summary>Pico mínimo (de 65535) que Windows acepta en una rampa gamma.</summary>
        private const double MinimumPeak = 32768.0;

        /// <summary>Diferencia tolerada entre la rampa aplicada y la actual antes de considerarla perdida.</summary>
        private const int RampTolerance = 400;

        private NativeMethods.RAMP _originalRamp;
        private bool _hasOriginalRamp = false;
        private IntPtr _screenDc = IntPtr.Zero;
        private bool _isDisposed = false;

        private NativeMethods.RAMP? _appliedRamp;
        private int _darkestAccepted = MaxDimming;
        private bool _hardwareRejected;

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
        /// Aplica atenuación a la tabla de color del hardware.
        /// Devuelve false solo cuando el hardware no admite ninguna escritura de gamma.
        /// </summary>
        /// <param name="percentage">Porcentaje de atenuación (0 a 85%).</param>
        /// <param name="active">Indica si la atenuación está habilitada.</param>
        public bool SetDimming(int percentage, bool active)
        {
            if (_isDisposed) return false;

            int requested = Math.Clamp(percentage, 0, MaxDimming);

            if (!active || requested <= 0)
            {
                return Restore();
            }

            // Un equipo que rechaza incluso la atenuación mínima no se reintenta en cada
            // cambio: se marca como no disponible hasta que cambie el DC (nuevo monitor,
            // reanudación de sesión, etc.).
            if (_hardwareRejected) return false;

            if (!TryInitialize()) return false;

            int target = Math.Min(requested, _darkestAccepted);

            if (TryWrite(target)) return true;

            // El DC pudo quedar inválido tras apagar/encender la pantalla.
            RefreshDeviceContext();
            if (!TryInitialize()) return false;
            if (TryWrite(target)) return true;

            // El driver rechazó la curva: se busca por bisección el nivel más oscuro
            // que sí acepta y se memoriza para no repetir la calibración en cada cambio.
            int low = 0;
            int high = target - 1;

            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (TryWrite(mid))
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            _darkestAccepted = low > 0 ? low : MaxDimming;

            if (low <= 0)
            {
                // Ni siquiera una atenuación mínima es aceptada: el hardware no sirve.
                IsSupported = false;
                _hardwareRejected = true;
                return false;
            }

            return TryWrite(low);
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
                _appliedRamp = null;
                return true;
            }

            RefreshDeviceContext();
            if (_screenDc == IntPtr.Zero) return false;

            if (NativeMethods.SetDeviceGammaRamp(_screenDc, ref _originalRamp))
            {
                _appliedRamp = null;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Comprueba si la rampa realmente aplicada sigue activa en la GPU.
        /// Devuelve true cuando Windows la restableció (por ejemplo, al encender la
        /// pantalla o al recargar el perfil de color) y hay que volver a aplicarla.
        /// </summary>
        public bool NeedsReapply()
        {
            if (_isDisposed) return false;
            if (!TryInitialize()) return false;
            if (!_hasOriginalRamp) return false;
            if (_appliedRamp is null) return true;

            // Se consulta con un DC nuevo para no depender de un DC invalidado por
            // el apagado/encendido de la pantalla.
            IntPtr probeDc = NativeMethods.GetDC(IntPtr.Zero);
            if (probeDc == IntPtr.Zero) return true;

            try
            {
                var currentRamp = new NativeMethods.RAMP();
                if (!NativeMethods.GetDeviceGammaRamp(probeDc, ref currentRamp))
                {
                    return true;
                }

                return RampDiffers(currentRamp, _appliedRamp.Value);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, probeDc);
            }
        }

        private static bool RampDiffers(NativeMethods.RAMP current, NativeMethods.RAMP applied)
        {
            for (int i = 0; i < 256; i += 8)
            {
                if (Math.Abs(current.Red[i] - applied.Red[i]) > RampTolerance) return true;
                if (Math.Abs(current.Green[i] - applied.Green[i]) > RampTolerance) return true;
                if (Math.Abs(current.Blue[i] - applied.Blue[i]) > RampTolerance) return true;
            }

            return false;
        }

        private bool TryWrite(int percentage)
        {
            if (_screenDc == IntPtr.Zero) return false;

            var ramp = BuildDimmedRamp(percentage);
            if (!NativeMethods.SetDeviceGammaRamp(_screenDc, ref ramp))
            {
                return false;
            }

            _appliedRamp = ramp;
            return true;
        }

        /// <summary>
        /// Construye la rampa atenuada. Hasta el 50 % de atenuación se escala linealmente
        /// (comportamiento clásico y exacto). Más allá, Windows rechazaría el pico bajo,
        /// así que se mantiene el pico mínimo permitido y se oscurecen los medios tonos con
        /// una curva de potencia que conserva la misma cantidad de luz que el escalado lineal.
        /// </summary>
        private NativeMethods.RAMP BuildDimmedRamp(int percentage)
        {
            int clamped = Math.Clamp(percentage, 0, MaxDimming);
            double remaining = 1.0 - (clamped / 100.0);

            double maxOriginal = MaxChannelValue(_originalRamp);
            if (maxOriginal <= 0) maxOriginal = 65535.0;

            double peak = Math.Max(maxOriginal * remaining, Math.Min(maxOriginal, MinimumPeak));
            double meanTarget = maxOriginal * 0.5 * remaining;

            double exponent = meanTarget > 0 ? (peak / meanTarget) - 1.0 : 1.0;
            if (exponent < 1.0) exponent = 1.0;

            var dimmedRamp = new NativeMethods.RAMP
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256]
            };

            for (int i = 0; i < 256; i++)
            {
                dimmedRamp.Red[i] = ShapeChannel(_originalRamp.Red[i], maxOriginal, peak, exponent);
                dimmedRamp.Green[i] = ShapeChannel(_originalRamp.Green[i], maxOriginal, peak, exponent);
                dimmedRamp.Blue[i] = ShapeChannel(_originalRamp.Blue[i], maxOriginal, peak, exponent);
            }

            return dimmedRamp;
        }

        private static ushort ShapeChannel(ushort value, double maxOriginal, double peak, double exponent)
        {
            if (value == 0) return 0;

            double normalized = value / maxOriginal;
            double shaped = peak * Math.Pow(normalized, exponent);
            return (ushort)Math.Clamp(Math.Round(shaped), 0, 65535);
        }

        private static double MaxChannelValue(NativeMethods.RAMP ramp)
        {
            double max = 0;
            for (int i = 0; i < 256; i++)
            {
                if (ramp.Red[i] > max) max = ramp.Red[i];
                if (ramp.Green[i] > max) max = ramp.Green[i];
                if (ramp.Blue[i] > max) max = ramp.Blue[i];
            }
            return max;
        }

        private void RefreshDeviceContext()
        {
            if (_screenDc != IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, _screenDc);
                _screenDc = IntPtr.Zero;
            }

            _screenDc = NativeMethods.GetDC(IntPtr.Zero);

            // La calibración depende del DC y del monitor activo: se vuelve a calcular.
            _darkestAccepted = MaxDimming;
            _hardwareRejected = false;
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
