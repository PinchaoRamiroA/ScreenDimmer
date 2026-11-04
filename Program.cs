using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ScreenDimmer.Core;
using ScreenDimmer.Forms;
using ScreenDimmer.Models;
using ScreenDimmer.Services;

namespace ScreenDimmer
{
    internal static class Program
    {
        private const string MutexName = "Global\\ScreenDimmer_SingleInstance_Mutex_v1";

        [STAThread]
        private static void Main(string[] args)
        {
            // 1. Garantizar una única instancia de la aplicación
            using var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "ScreenDimmer ya se encuentra en ejecución en segundo plano.\nPuede acceder a sus controles desde la bandeja del sistema (junto al reloj).",
                    "ScreenDimmer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            // 2. Inicialización de la configuración DPI y estilos visuales nativos
            ApplicationConfiguration.Initialize();

            // 3. Servicios y lógica de persistencia
            var settingsService = new SettingsService();
            var settings = settingsService.Load();
            var startupService = new StartupService();

            // 4. Componentes principales del sistema
            ScreenOverlay? overlay = null;
            try
            {
                overlay = new ScreenOverlay();

                var controller = new BrightnessController(
                    overlay,
                    settings.DimmingPercentage,
                    settings.IsEnabled
                );

                var profileManager = new ProfileManager(
                    settings.SelectedProfile,
                    settings.CustomDimmingPercentage
                );

                var hotkeyManager = new HotkeyManager();

                // Conectar atajos de teclado globales con el controlador
                hotkeyManager.DimmingReduced += () =>
                {
                    controller.Decrease(settings.StepDelta);
                    profileManager.MatchOrSetCustom(controller.DimmingLevel);
                };

                hotkeyManager.DimmingIncreased += () =>
                {
                    controller.Increase(settings.StepDelta);
                    profileManager.MatchOrSetCustom(controller.DimmingLevel);
                };

                hotkeyManager.DimmingDeactivated += () =>
                {
                    controller.SetEnabled(false);
                };

                hotkeyManager.DimmingToggled += () =>
                {
                    controller.Toggle();
                };

                // Mecanismo de seguridad ante excepciones no controladas:
                // Restaura inmediatamente la pantalla original para evitar que el usuario quede a oscuras.
                Application.ThreadException += (s, e) =>
                {
                    try { overlay?.Dispose(); } catch { }
                };

                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    try { overlay?.Dispose(); } catch { }
                };

                // Crear formulario principal
                var mainForm = new MainForm(
                    controller,
                    profileManager,
                    hotkeyManager,
                    settingsService,
                    startupService,
                    settings
                );

                // Servicio de System Tray
                using var trayService = new SystemTrayService(controller, profileManager, startupService);

                trayService.ShowMainWindowRequested = () =>
                {
                    if (mainForm.IsDisposed) return;
                    mainForm.Show();
                    mainForm.WindowState = FormWindowState.Normal;
                    mainForm.BringToFront();
                    mainForm.Activate();
                    mainForm.TopMost = true;
                };

                trayService.ExitApplicationRequested = () =>
                {
                    mainForm.ExitApplication();
                };

                // Verificar si se inicia minimizado (argumento --minimized o preferencia guardada)
                bool startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                                                    a.Equals("/minimized", StringComparison.OrdinalIgnoreCase))
                                      || settings.StartMinimized;

                if (!startMinimized)
                {
                    mainForm.Show();
                }

                // Iniciar ciclo de mensajes de Windows
                Application.Run();
            }
            finally
            {
                // Garantizar la liberación total de recursos y restauración del estado visual
                overlay?.Dispose();
            }
        }
    }
}
