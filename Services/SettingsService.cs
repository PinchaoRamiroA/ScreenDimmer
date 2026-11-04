using System;
using System.IO;
using System.Text.Json;
using ScreenDimmer.Models;

namespace ScreenDimmer.Services
{
    /// <summary>
    /// Servicio de persistencia para la configuración de la aplicación en formato JSON.
    /// Ubicación predeterminada: %APPDATA%\ScreenDimmer\settings.json
    /// </summary>
    public class SettingsService
    {
        private readonly string _settingsFilePath;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public SettingsService(string? customFilePath = null)
        {
            if (string.IsNullOrWhiteSpace(customFilePath))
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ScreenDimmer"
                );
                Directory.CreateDirectory(appDataDir);
                _settingsFilePath = Path.Combine(appDataDir, "settings.json");
            }
            else
            {
                _settingsFilePath = customFilePath;
            }
        }

        public string SettingsFilePath => _settingsFilePath;

        public AppSettings Load()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        settings.Validate();
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Error al cargar configuración: {ex.Message}");
            }

            var defaultSettings = new AppSettings();
            defaultSettings.Validate();
            return defaultSettings;
        }

        public bool Save(AppSettings settings)
        {
            if (settings == null) return false;

            try
            {
                settings.Validate();
                string directory = Path.GetDirectoryName(_settingsFilePath)!;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string tempPath = _settingsFilePath + ".tmp";
                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(tempPath, json);

                File.Move(tempPath, _settingsFilePath, true);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Error al guardar configuración: {ex.Message}");
                return false;
            }
        }
    }
}
