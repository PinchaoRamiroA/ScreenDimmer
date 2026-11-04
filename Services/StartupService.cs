using System;
using System.Diagnostics;
using System.IO;

namespace ScreenDimmer.Services
{
    /// <summary>
    /// Administra la configuración de inicio automático de la aplicación con Windows.
    /// Utiliza el mecanismo nativo de la carpeta de inicio de usuario (%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup),
    /// cumpliendo con el requisito de evitar modificaciones innecesarias en el registro del sistema.
    /// </summary>
    public class StartupService
    {
        private const string ShortcutFileName = "ScreenDimmer.lnk";
        private readonly string _startupFolderPath;
        private readonly string _shortcutPath;

        public StartupService()
        {
            _startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            _shortcutPath = Path.Combine(_startupFolderPath, ShortcutFileName);
        }

        public string ShortcutPath => _shortcutPath;

        /// <summary>
        /// Comprueba si la aplicación está configurada para iniciar con Windows.
        /// </summary>
        public bool IsStartupEnabled()
        {
            return File.Exists(_shortcutPath);
        }

        /// <summary>
        /// Habilita o deshabilita el inicio automático creando o eliminando el acceso directo en la carpeta Inicio.
        /// </summary>
        public bool SetStartup(bool enable, bool startMinimized = true)
        {
            try
            {
                if (enable)
                {
                    string? currentExe = Process.GetCurrentProcess().MainModule?.FileName;
                    if (string.IsNullOrEmpty(currentExe))
                    {
                        currentExe = Environment.ProcessPath;
                    }

                    if (string.IsNullOrEmpty(currentExe)) return false;

                    // Crear acceso directo utilizando WScript.Shell (mecanismo nativo de Windows)
                    Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType == null) return false;

                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell == null) return false;

                    dynamic shortcut = shell.CreateShortcut(_shortcutPath);
                    shortcut.TargetPath = currentExe;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(currentExe);
                    shortcut.Description = "ScreenDimmer - Advanced Windows Screen Dimmer";
                    if (startMinimized)
                    {
                        shortcut.Arguments = "--minimized";
                    }
                    shortcut.Save();
                    return true;
                }
                else
                {
                    if (File.Exists(_shortcutPath))
                    {
                        File.Delete(_shortcutPath);
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupService] Error al modificar inicio automático: {ex.Message}");
                return false;
            }
        }
    }
}
