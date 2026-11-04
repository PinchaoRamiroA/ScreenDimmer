using System;
using System.Collections.Generic;
using System.Linq;
using ScreenDimmer.Models;

namespace ScreenDimmer.Core
{
    /// <summary>
    /// Gestiona la selección y personalización de perfiles de iluminación.
    /// </summary>
    public class ProfileManager
    {
        private BrightnessProfile _activeProfile;
        private int _customPercentage;

        public BrightnessProfile ActiveProfile => _activeProfile;
        public int CustomPercentage => _customPercentage;

        public event Action<BrightnessProfile>? ProfileChanged;

        public ProfileManager(string initialProfileName = "Tarde", int customPercentage = 30)
        {
            _customPercentage = Math.Clamp(customPercentage, 0, 85);
            _activeProfile = GetProfileByName(initialProfileName);
        }

        public IReadOnlyList<BrightnessProfile> GetAllProfiles()
        {
            var list = new List<BrightnessProfile>(BrightnessProfile.DefaultProfiles)
            {
                new BrightnessProfile("Personalizado", _customPercentage, $"Nivel personalizado configurado por el usuario ({_customPercentage}%)")
            };
            return list;
        }

        public BrightnessProfile GetProfileByName(string name)
        {
            var standard = BrightnessProfile.DefaultProfiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (standard != null) return standard;

            if (name.Equals("Personalizado", StringComparison.OrdinalIgnoreCase))
            {
                return new BrightnessProfile("Personalizado", _customPercentage, $"Nivel personalizado ({_customPercentage}%)");
            }

            return BrightnessProfile.Afternoon;
        }

        public void SetCustomPercentage(int percentage)
        {
            _customPercentage = Math.Clamp(percentage, 0, 85);
            if (_activeProfile.Name.Equals("Personalizado", StringComparison.OrdinalIgnoreCase))
            {
                _activeProfile = new BrightnessProfile("Personalizado", _customPercentage, $"Nivel personalizado ({_customPercentage}%)");
                ProfileChanged?.Invoke(_activeProfile);
            }
        }

        public void SelectProfile(string name)
        {
            var profile = GetProfileByName(name);
            _activeProfile = profile;
            ProfileChanged?.Invoke(profile);
        }

        public void MatchOrSetCustom(int percentage)
        {
            var match = BrightnessProfile.DefaultProfiles.FirstOrDefault(p => p.DimmingPercentage == percentage);
            if (match != null)
            {
                _activeProfile = match;
            }
            else
            {
                _customPercentage = percentage;
                _activeProfile = new BrightnessProfile("Personalizado", _customPercentage, $"Nivel personalizado ({_customPercentage}%)");
            }
            ProfileChanged?.Invoke(_activeProfile);
        }
    }
}
