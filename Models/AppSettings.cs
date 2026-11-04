using System;

namespace ScreenDimmer.Models
{
    public enum DimmingEngine
    {
        Hardware = 0,
        Overlay = 1
    }

    /// <summary>
    /// Estructura de configuración persistente de la aplicación.
    /// Se almacena en formato JSON en el directorio de datos de usuario de Windows.
    /// </summary>
    public class AppSettings
    {
        public DimmingEngine Engine { get; set; } = DimmingEngine.Hardware;
        public int DimmingPercentage { get; set; } = 30;
        public bool IsEnabled { get; set; } = true;
        public string SelectedProfile { get; set; } = "Tarde";
        public int CustomDimmingPercentage { get; set; } = 30;
        public bool StartWithWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool HotkeysEnabled { get; set; } = true;
        public int StepDelta { get; set; } = 5;

        public void Validate()
        {
            DimmingPercentage = Math.Clamp(DimmingPercentage, 0, 85);
            CustomDimmingPercentage = Math.Clamp(CustomDimmingPercentage, 0, 85);
            StepDelta = Math.Clamp(StepDelta, 1, 20);
            if (string.IsNullOrWhiteSpace(SelectedProfile))
            {
                SelectedProfile = "Tarde";
            }
        }
    }
}
