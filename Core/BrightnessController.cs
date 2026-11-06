using System;
using ScreenDimmer.Models;

namespace ScreenDimmer.Core
{
    /// <summary>
    /// Controlador central de nivel, estado y motor de atenuación.
    /// Soporta dos motores:
    /// 1. Hardware Gamma LUT: actúa en la GPU, 100% inmune a MPO, scroll de Chrome/Edge, vídeos y modales.
    /// 2. Capa Overlay: ventana borderless semitransparente Win32 (WS_EX_LAYERED).
    /// </summary>
    public class BrightnessController : IDisposable
    {
        private readonly ScreenOverlay _overlay;
        private readonly GammaController _gammaController;
        private int _dimmingLevel;
        private bool _isEnabled;
        private DimmingEngine _engine;

        public const int MinDimming = 0;
        public const int MaxDimming = 85;

        public int DimmingLevel => _dimmingLevel;
        public bool IsEnabled => _isEnabled;
        public DimmingEngine Engine => _engine;

        public event Action<int, bool>? DimmingChanged;
        public event Action<DimmingEngine>? EngineChanged;

        public BrightnessController(
            ScreenOverlay overlay,
            GammaController gammaController,
            int initialDimming = 30,
            bool initialEnabled = true,
            DimmingEngine initialEngine = DimmingEngine.Hardware)
        {
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            _gammaController = gammaController ?? throw new ArgumentNullException(nameof(gammaController));
            _dimmingLevel = Math.Clamp(initialDimming, MinDimming, MaxDimming);
            _isEnabled = initialEnabled;
            _engine = initialEngine;

            ApplyCurrent();
        }

        public void SetEngine(DimmingEngine newEngine)
        {
            if (_engine == newEngine) return;
            _engine = newEngine;

            if (_engine == DimmingEngine.Hardware)
            {
                _overlay.SetDimming(0, false);
            }
            else
            {
                _gammaController.Restore();
            }

            ApplyCurrent();
            EngineChanged?.Invoke(_engine);
        }

        public void SetDimming(int percentage)
        {
            int clamped = Math.Clamp(percentage, MinDimming, MaxDimming);
            bool changed = _dimmingLevel != clamped;
            bool wasEnabled = _isEnabled;

            _dimmingLevel = clamped;
            if (_dimmingLevel > 0 && !_isEnabled)
            {
                _isEnabled = true;
            }

            // Se aplica siempre, incluso cuando el nivel no cambió: Windows puede haber
            // restablecido la rampa gamma (apagado de pantalla, suspensión, cambio de
            // resolución) y el estado interno seguiría diciendo "80%" sin efecto real.
            ApplyCurrent();

            if (changed || wasEnabled != _isEnabled)
            {
                DimmingChanged?.Invoke(_dimmingLevel, _isEnabled);
            }
        }

        public void SetEnabled(bool enabled)
        {
            bool changed = _isEnabled != enabled;
            _isEnabled = enabled;

            ApplyCurrent();

            if (changed)
            {
                DimmingChanged?.Invoke(_dimmingLevel, _isEnabled);
            }
        }

        /// <summary>
        /// Reaplica la atenuación si el hardware perdió la rampa gamma.
        /// Pensado para un temporizador de vigilancia en la interfaz.
        /// </summary>
        public void ReapplyIfNeeded()
        {
            if (_engine != DimmingEngine.Hardware) return;
            if (!_isEnabled || _dimmingLevel <= 0) return;

            if (_gammaController.NeedsReapply(_dimmingLevel))
            {
                ApplyCurrent();
            }
        }

        private void ApplyCurrent()
        {
            if (_engine == DimmingEngine.Hardware && _gammaController.IsSupported)
            {
                if (_gammaController.SetDimming(_dimmingLevel, _isEnabled))
                {
                    _overlay.SetDimming(0, false);
                    return;
                }
            }

            // Si el hardware no está disponible o rechazó la escritura, se usa el overlay
            // para que el usuario nunca se quede sin atenuación.
            _gammaController.Restore();
            _overlay.SetDimming(_dimmingLevel, _isEnabled);
        }

        public void Toggle()
        {
            SetEnabled(!_isEnabled);
        }

        public void Increase(int delta = 5)
        {
            if (!_isEnabled)
            {
                _isEnabled = true;
            }
            SetDimming(_dimmingLevel + delta);
        }

        public void Decrease(int delta = 5)
        {
            SetDimming(_dimmingLevel - delta);
        }

        public void Reset()
        {
            _dimmingLevel = 0;
            _isEnabled = false;
            _overlay.SetDimming(0, false);
            _gammaController.Restore();
            DimmingChanged?.Invoke(0, false);
        }

        public void ApplyProfile(BrightnessProfile profile)
        {
            if (profile == null) return;
            _isEnabled = true;
            SetDimming(profile.DimmingPercentage);
        }

        public void Dispose()
        {
            Reset();
            _gammaController.Dispose();
        }
    }
}
