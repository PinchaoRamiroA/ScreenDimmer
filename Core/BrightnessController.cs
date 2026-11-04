using System;
using ScreenDimmer.Models;

namespace ScreenDimmer.Core
{
    /// <summary>
    /// Controlador central de nivel y estado de atenuación.
    /// Coordina la actualización del ScreenOverlay y notifica los cambios a la interfaz y servicios.
    /// </summary>
    public class BrightnessController
    {
        private readonly ScreenOverlay _overlay;
        private int _dimmingLevel;
        private bool _isEnabled;

        public const int MinDimming = 0;
        public const int MaxDimming = 85;

        public int DimmingLevel => _dimmingLevel;
        public bool IsEnabled => _isEnabled;

        /// <summary>
        /// Se dispara cuando el nivel o el estado de activación cambia.
        /// Parámetros: (int nuevoNivel, bool estaHabilitado)
        /// </summary>
        public event Action<int, bool>? DimmingChanged;

        public BrightnessController(ScreenOverlay overlay, int initialDimming = 30, bool initialEnabled = true)
        {
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            _dimmingLevel = Math.Clamp(initialDimming, MinDimming, MaxDimming);
            _isEnabled = initialEnabled;

            // Sincronizar overlay inicial
            _overlay.SetDimming(_dimmingLevel, _isEnabled);
        }

        public void SetDimming(int percentage)
        {
            int clamped = Math.Clamp(percentage, MinDimming, MaxDimming);
            if (_dimmingLevel == clamped && _isEnabled) return;

            _dimmingLevel = clamped;
            if (_dimmingLevel > 0 && !_isEnabled)
            {
                _isEnabled = true;
            }

            _overlay.SetDimming(_dimmingLevel, _isEnabled);
            DimmingChanged?.Invoke(_dimmingLevel, _isEnabled);
        }

        public void SetEnabled(bool enabled)
        {
            if (_isEnabled == enabled) return;

            _isEnabled = enabled;
            _overlay.SetDimming(_dimmingLevel, _isEnabled);
            DimmingChanged?.Invoke(_dimmingLevel, _isEnabled);
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
            DimmingChanged?.Invoke(0, false);
        }

        public void ApplyProfile(BrightnessProfile profile)
        {
            if (profile == null) return;
            _isEnabled = true;
            SetDimming(profile.DimmingPercentage);
        }
    }
}
