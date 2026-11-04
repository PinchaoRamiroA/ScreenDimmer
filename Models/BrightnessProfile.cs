using System;
using System.Collections.Generic;

namespace ScreenDimmer.Models
{
    /// <summary>
    /// Representa un perfil predefinido o personalizado de atenuación de pantalla.
    /// </summary>
    public class BrightnessProfile
    {
        public string Name { get; set; } = string.Empty;
        public int DimmingPercentage { get; set; }
        public string Description { get; set; } = string.Empty;

        public BrightnessProfile() { }

        public BrightnessProfile(string name, int percentage, string description)
        {
            Name = name;
            DimmingPercentage = Math.Clamp(percentage, 0, 85);
            Description = description;
        }

        public static readonly BrightnessProfile Day = new BrightnessProfile("Día", 15, "Oscurecimiento suave para entornos luminosos (15%)");
        public static readonly BrightnessProfile Afternoon = new BrightnessProfile("Tarde", 30, "Oscurecimiento moderado para luz intermedia (30%)");
        public static readonly BrightnessProfile Night = new BrightnessProfile("Noche", 50, "Oscurecimiento pronunciado para entornos oscuros (50%)");
        public static readonly BrightnessProfile Reading = new BrightnessProfile("Lectura", 65, "Atenuación intensa óptima para lectura nocturna (65%)");

        public static IReadOnlyList<BrightnessProfile> DefaultProfiles { get; } = new List<BrightnessProfile>
        {
            Day,
            Afternoon,
            Night,
            Reading
        };

        public override string ToString() => $"{Name} ({DimmingPercentage}%)";
    }
}
