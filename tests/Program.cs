using System;
using System.IO;
using ScreenDimmer.Core;
using ScreenDimmer.Models;
using ScreenDimmer.Services;

namespace ScreenDimmer.Tests
{
    internal class Program
    {
        private static int _passed = 0;
        private static int _failed = 0;

        [STAThread]
        private static int Main()
        {
            Console.WriteLine("=== Iniciando pruebas unitarias de ScreenDimmer ===");

            TestProfiles();
            TestAppSettingsValidation();
            TestSettingsPersistence();
            TestBrightnessController();
            TestProfileManager();
            TestStartupService();

            Console.WriteLine("\n=================================================");
            Console.WriteLine($"Resultados: {_passed} exitosas, {_failed} fallidas.");
            Console.WriteLine("=================================================");

            return _failed == 0 ? 0 : 1;
        }

        private static void Assert(bool condition, string testName)
        {
            if (condition)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[PASS] {testName}");
                _passed++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {testName}");
                _failed++;
            }
            Console.ResetColor();
        }

        private static void TestProfiles()
        {
            Console.WriteLine("\n--- Probando Perfiles de Brillo ---");
            Assert(BrightnessProfile.Day.DimmingPercentage == 15, "Perfil Día debe ser 15%");
            Assert(BrightnessProfile.Afternoon.DimmingPercentage == 30, "Perfil Tarde debe ser 30%");
            Assert(BrightnessProfile.Night.DimmingPercentage == 50, "Perfil Noche debe ser 50%");
            Assert(BrightnessProfile.Reading.DimmingPercentage == 65, "Perfil Lectura debe ser 65%");
            Assert(BrightnessProfile.DefaultProfiles.Count == 4, "Deben existir 4 perfiles predeterminados");
        }

        private static void TestAppSettingsValidation()
        {
            Console.WriteLine("\n--- Probando Validación de Configuración ---");
            var settings = new AppSettings
            {
                DimmingPercentage = 150, // Fuera de rango
                CustomDimmingPercentage = -20, // Fuera de rango
                StepDelta = 0,
                SelectedProfile = ""
            };

            settings.Validate();

            Assert(settings.DimmingPercentage == 85, "DimmingPercentage debe limitarse al máximo de 85%");
            Assert(settings.CustomDimmingPercentage == 0, "CustomDimmingPercentage debe limitarse al mínimo de 0%");
            Assert(settings.StepDelta >= 1, "StepDelta debe ser al menos 1");
            Assert(settings.SelectedProfile == "Tarde", "SelectedProfile vacío debe restablecer a 'Tarde'");
        }

        private static void TestSettingsPersistence()
        {
            Console.WriteLine("\n--- Probando Persistencia JSON ---");
            string tempFile = Path.Combine(Path.GetTempPath(), $"screendimmer_test_{Guid.NewGuid():N}.json");
            try
            {
                var service = new SettingsService(tempFile);

                var original = new AppSettings
                {
                    DimmingPercentage = 42,
                    IsEnabled = true,
                    SelectedProfile = "Noche",
                    CustomDimmingPercentage = 42,
                    StartWithWindows = true,
                    StartMinimized = true
                };

                bool saved = service.Save(original);
                Assert(saved, "Guardado de configuración debe retornar true");
                Assert(File.Exists(tempFile), "El archivo JSON debe existir en disco");

                var loaded = service.Load();
                Assert(loaded.DimmingPercentage == 42, "DimmingPercentage cargado debe ser 42");
                Assert(loaded.SelectedProfile == "Noche", "SelectedProfile cargado debe ser 'Noche'");
                Assert(loaded.StartWithWindows == true, "StartWithWindows cargado debe ser true");
                Assert(loaded.StartMinimized == true, "StartMinimized cargado debe ser true");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        private static void TestBrightnessController()
        {
            Console.WriteLine("\n--- Probando BrightnessController ---");
            using var overlay = new ScreenOverlay();
            var controller = new BrightnessController(overlay, 30, true);

            Assert(controller.DimmingLevel == 30, "Nivel inicial debe ser 30%");
            Assert(controller.IsEnabled == true, "Estado inicial debe ser activo");

            // Probar incremento
            controller.Increase(10);
            Assert(controller.DimmingLevel == 40, "Incrementar +10 debe resultar en 40%");

            // Probar límite superior (85%)
            controller.Increase(100);
            Assert(controller.DimmingLevel == 85, "No debe superar el 85% de atenuación máxima");

            // Probar decremento
            controller.Decrease(20);
            Assert(controller.DimmingLevel == 65, "Decrementar 20 desde 85 debe resultar en 65%");

            // Probar límite inferior (0%)
            controller.Decrease(200);
            Assert(controller.DimmingLevel == 0, "No debe descender por debajo de 0%");

            // Probar Toggle
            controller.SetEnabled(true);
            controller.Toggle();
            Assert(controller.IsEnabled == false, "Toggle desde activo debe pasar a inactivo");
            controller.Toggle();
            Assert(controller.IsEnabled == true, "Toggle desde inactivo debe pasar a activo");

            // Probar Reset
            controller.SetDimming(50);
            controller.Reset();
            Assert(controller.DimmingLevel == 0, "Reset debe fijar nivel en 0%");
            Assert(controller.IsEnabled == false, "Reset debe desactivar el controlador");
        }

        private static void TestProfileManager()
        {
            Console.WriteLine("\n--- Probando ProfileManager ---");
            var pm = new ProfileManager("Día", 25);

            Assert(pm.ActiveProfile.Name == "Día", "Perfil activo inicial debe ser 'Día'");
            Assert(pm.ActiveProfile.DimmingPercentage == 15, "Porcentaje del perfil activo debe ser 15%");

            pm.SelectProfile("Noche");
            Assert(pm.ActiveProfile.Name == "Noche", "Seleccionar 'Noche' debe activar perfil Noche");
            Assert(pm.ActiveProfile.DimmingPercentage == 50, "Porcentaje de 'Noche' debe ser 50%");

            pm.MatchOrSetCustom(65);
            Assert(pm.ActiveProfile.Name == "Lectura", "65% debe coincidir con el perfil 'Lectura'");

            pm.MatchOrSetCustom(37);
            Assert(pm.ActiveProfile.Name == "Personalizado", "37% debe asignar perfil 'Personalizado'");
            Assert(pm.CustomPercentage == 37, "CustomPercentage debe ser 37%");
        }

        private static void TestStartupService()
        {
            Console.WriteLine("\n--- Probando StartupService ---");
            var startup = new StartupService();
            Assert(startup.ShortcutPath.EndsWith("ScreenDimmer.lnk", StringComparison.OrdinalIgnoreCase),
                "El nombre del acceso directo debe ser ScreenDimmer.lnk");
            Assert(startup.ShortcutPath.Contains("Startup", StringComparison.OrdinalIgnoreCase),
                "La ruta del acceso directo debe ubicarse en la carpeta de Inicio de Windows");
        }
    }
}
