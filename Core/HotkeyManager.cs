using System;
using System.Collections.Generic;
using System.Windows.Forms;
using ScreenDimmer.Interop;

namespace ScreenDimmer.Core
{
    /// <summary>
    /// Administra el registro y procesamiento de combinaciones de teclas globales en Windows (Win32 HotKeys).
    /// Permite controlar la atenuación incluso cuando otra aplicación tiene el foco.
    /// </summary>
    public sealed class HotkeyManager : IDisposable
    {
        public const int HOTKEY_ID_DIM_DOWN = 1001;   // Ctrl + Alt + Up (Reducir atenuación / Más brillo)
        public const int HOTKEY_ID_DIM_UP = 1002;     // Ctrl + Alt + Down (Aumentar atenuación / Menos brillo)
        public const int HOTKEY_ID_DEACTIVATE = 1003; // Ctrl + Alt + 0 (Desactivar)
        public const int HOTKEY_ID_TOGGLE = 1004;     // Ctrl + Alt + 1 (Alternar activación)

        private const uint VK_UP = 0x26;
        private const uint VK_DOWN = 0x28;
        private const uint VK_0 = 0x30;
        private const uint VK_1 = 0x31;

        private IntPtr _windowHandle = IntPtr.Zero;
        private readonly HashSet<int> _registeredHotkeys = new HashSet<int>();
        private bool _isDisposed = false;

        public event Action? DimmingReduced;
        public event Action? DimmingIncreased;
        public event Action? DimmingDeactivated;
        public event Action? DimmingToggled;

        public bool AreHotkeysRegistered => _registeredHotkeys.Count > 0;

        /// <summary>
        /// Registra las combinaciones globales de teclas asociadas a la ventana indicada.
        /// </summary>
        public bool RegisterHotkeys(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            UnregisterHotkeys();

            _windowHandle = hWnd;
            uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;

            bool allSuccess = true;

            // Ctrl + Alt + Up
            if (NativeMethods.RegisterHotKey(hWnd, HOTKEY_ID_DIM_DOWN, modifiers, VK_UP))
            {
                _registeredHotkeys.Add(HOTKEY_ID_DIM_DOWN);
            }
            else
            {
                allSuccess = false;
            }

            // Ctrl + Alt + Down
            if (NativeMethods.RegisterHotKey(hWnd, HOTKEY_ID_DIM_UP, modifiers, VK_DOWN))
            {
                _registeredHotkeys.Add(HOTKEY_ID_DIM_UP);
            }
            else
            {
                allSuccess = false;
            }

            // Ctrl + Alt + 0
            if (NativeMethods.RegisterHotKey(hWnd, HOTKEY_ID_DEACTIVATE, modifiers, VK_0))
            {
                _registeredHotkeys.Add(HOTKEY_ID_DEACTIVATE);
            }
            else
            {
                allSuccess = false;
            }

            // Ctrl + Alt + 1
            if (NativeMethods.RegisterHotKey(hWnd, HOTKEY_ID_TOGGLE, modifiers, VK_1))
            {
                _registeredHotkeys.Add(HOTKEY_ID_TOGGLE);
            }
            else
            {
                allSuccess = false;
            }

            return allSuccess;
        }

        /// <summary>
        /// Libera todas las teclas de acceso rápido registradas en el sistema.
        /// </summary>
        public void UnregisterHotkeys()
        {
            if (_windowHandle != IntPtr.Zero)
            {
                foreach (int id in _registeredHotkeys)
                {
                    NativeMethods.UnregisterHotKey(_windowHandle, id);
                }
                _registeredHotkeys.Clear();
                _windowHandle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Procesa los mensajes WM_HOTKEY recibidos en el WndProc de la ventana.
        /// </summary>
        public bool ProcessMessage(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();
                switch (hotkeyId)
                {
                    case HOTKEY_ID_DIM_DOWN:
                        DimmingReduced?.Invoke();
                        return true;

                    case HOTKEY_ID_DIM_UP:
                        DimmingIncreased?.Invoke();
                        return true;

                    case HOTKEY_ID_DEACTIVATE:
                        DimmingDeactivated?.Invoke();
                        return true;

                    case HOTKEY_ID_TOGGLE:
                        DimmingToggled?.Invoke();
                        return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                UnregisterHotkeys();
                _isDisposed = true;
            }
        }
    }
}
