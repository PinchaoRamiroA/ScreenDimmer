# ScreenDimmer — Advanced Windows Screen Dimmer

**ScreenDimmer** es una aplicación de escritorio nativa para **Windows 11** desarrollada en C# y .NET 8 (Windows Forms). Permite reducir el brillo de la pantalla de un ordenador o portátil por debajo del umbral mínimo permitido por los controles de hardware convencionales de Windows, mediante una capa de superposición negra semitransparente regulable (0 % a 85 %), con soporte para *click-through*, perfiles de iluminación y atajos de teclado globales.

---

## 🚀 Características principales

- **Atenuación continua de 0 % a 85 %:**
  - `0 %`: Pantalla original, sin oscurecimiento.
  - `20 %`: Oscurecimiento ligero.
  - `40 %`: Oscurecimiento moderado.
  - `60 %`: Oscurecimiento fuerte.
  - `85 %`: Oscurecimiento muy intenso.
- **Capa Overlay Transparente (Click-through):**
  - Implementada con la API Win32 (`WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`).
  - Todos los clics y movimientos del ratón atraviesan la capa sin interferir con las ventanas de trabajo.
  - No intercepta pulsaciones de teclado ni roba el foco de la ventana activa.
- **Interfaz moderna con tema oscuro Windows 11:**
  - Barra de título inmersiva oscura mediante DWM (`DwmSetWindowAttribute`).
  - Indicador destacado del porcentaje actual y etiqueta interpretativa.
  - Selector rápido de perfiles: **Día (15 %)**, **Tarde (30 %)**, **Noche (50 %)**, **Lectura (65 %)** y **Personalizado**.
  - La ventana de control se mantiene independiente y nítida por encima del efecto sin quedar oscurecida.
- **Atajos de teclado globales (Win32 HotKeys):**
  - Funcionan en segundo plano con cualquier aplicación en foco.
  - `Ctrl + Alt + ↑`: Reducir atenuación (-5 %).
  - `Ctrl + Alt + ↓`: Aumentar atenuación (+5 %).
  - `Ctrl + Alt + 0`: Desactivar atenuación.
  - `Ctrl + Alt + 1`: Alternar activación (On/Off).
- **Bandeja del sistema (System Tray):**
  - Al cerrar la ventana principal con la 'X', la aplicación se minimiza silenciosamente a la bandeja sin cerrarse.
  - Menú contextual rápido para alternar la atenuación, cambiar de perfil, configurar inicio con Windows o salir.
- **Inicio automático con Windows:**
  - Implementado mediante el acceso directo nativo en `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup` sin alterar de forma innecesaria el registro de Windows.
- **Persistencia en JSON:**
  - Almacena automáticamente los niveles, perfil activo y preferencias en `%APPDATA%\ScreenDimmer\settings.json`.
- **Seguridad y recuperación:**
  - Instancia única protegida por `Mutex`.
  - Mecanismo de recuperación que restaura inmediatamente la pantalla original ante cualquier excepción no controlada.

---

## 📂 Arquitectura del Proyecto

```
ScreenDimmer/
├── Program.cs                     # Punto de entrada, Mutex de instancia única, manejo de excepciones
├── ScreenDimmer.csproj            # Configuración de compilación .NET 8 WinForms x64
├── app.manifest                   # Compatibilidad Windows 11 y soporte DPI Per-Monitor V2
├── build.ps1                      # Script automatizado de compilación y publicación
├── Forms/
│   ├── MainForm.cs                # Lógica de la UI (Dark Theme, eventos de controles, hotkeys)
│   └── MainForm.Designer.cs       # Declaración de componentes gráficos
├── Core/
│   ├── ScreenOverlay.cs           # Ventana de superposición borderless y click-through (Win32)
│   ├── BrightnessController.cs    # Controlador del nivel de atenuación y notificaciones
│   ├── HotkeyManager.cs           # Registro y despacho de atajos globales con RegisterHotKey
│   └── ProfileManager.cs          # Gestión de perfiles predefinidos y personalizados
├── Models/
│   ├── AppSettings.cs             # Modelo serializable para configuración
│   └── BrightnessProfile.cs       # Entidad de perfil de atenuación
├── Services/
│   ├── SettingsService.cs         # Carga y guardado atómico en settings.json
│   ├── StartupService.cs          # Creación/eliminación de acceso en la carpeta Inicio
│   └── SystemTrayService.cs       # Icono de notificación y menú contextual en la bandeja
├── Interop/
│   └── NativeMethods.cs           # P/Invoke para user32.dll, shell32.dll y dwmapi.dll
└── Resources/
    └── app.ico                    # Icono de la aplicación
```

---

## 🛠️ Requisitos de Compilación y Ejecución

- **Sistema Operativo:** Windows 11 (o Windows 10 versión 1809+).
- **Entorno de desarrollo:** .NET 8.0 SDK o superior.

### Compilar y publicar:
```powershell
# Ejecutar el script automatizado
.\build.ps1

# O manualmente con dotnet CLI:
dotnet build -c Release
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o publish\
```

El ejecutable final se genera en `publish\ScreenDimmer.exe`.
