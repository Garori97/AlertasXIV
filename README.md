# Alertas XIV

Plugin de Dalamud (API 15, Dalamud.NET.Sdk 15.0.0) que avisa de las Fêtes de la Restauración de Ishgardiense (*Skyrise Celebration*) en The Firmament.

## Cómo funciona
Esas Fêtes no son FATEs del juego. El plugin lee el texto del indicador "Ishgardian Restoration" de la lista de tareas (la ventana `_ToDoList`) cada segundo, y hace dos cosas:

1. **Aprende el horario.** Cuando el indicador muestra la cuenta atrás (`Session Pending` + `Fêtes commencing in 1d 16h 59m`), calcula la hora de inicio y la guarda. Si faltan más de 2 horas, la toma como la primera Fête de un ciclo y proyecta las 12 (una cada 2 horas, según la wiki). Cada vez que vuelves a ver la cuenta atrás, corrige el desfase.
2. **Avisa.** Con el horario guardado avisa unos minutos antes de cada Fête (configurable) desde cualquier zona del juego, mientras el juego esté abierto. Además, si estás en The Firmament y detecta una sesión en curso, avisa también.

| Texto del indicador | Estado |
|---|---|
| `Session Pending` + `Fêtes commencing in 1d 16h 59m` | Pendiente (se lee la cuenta atrás) |
| `Session n/12` sin la frase `has ended` | Activa (**suposición, no verificada**) |
| `Session 12/12` + `The current fête session has ended.` | Terminada (verificado) |

Por defecto solo actúa si tu personaje está en Ragnarok.

## Comandos
- `/alerta`: abre la configuración.
- `/alerta test`: lanza un aviso de prueba.
- `/alerta dump`: escribe en el chat el estado detectado, las líneas que lee del indicador y el horario guardado (en la hora de tu PC).

## Limitaciones
- Entre ciclos hay 1–2 días de descanso impredecibles. El horario del ciclo siguiente solo se conoce cuando el indicador muestra la cuenta atrás en The Firmament; hay que pasar por allí para que el plugin lo aprenda.
- La cuenta atrás va en minutos, así que la hora tiene un margen de aproximadamente 1 minuto.
- Se asume que las 2 horas cuentan de inicio a inicio entre Fêtes y que el ciclo tiene 12. Si no es así, se ajusta en `FeteIntervalMinutes` y `FetesPerCycle` (`Configuration.cs`).
- El texto está en inglés. Con el cliente en otro idioma, hay que adaptar `EventTitle` y las frases de `Analyze` en `Plugin.cs`.
- Si el juego estaba cerrado cuando tocaba una Fête, no se avisa tarde.

## Requisitos
- Windows, .NET SDK (el que pida la versión actual de Dalamud) y Visual Studio 2022 / Rider.
- XIVLauncher con el juego ejecutado al menos una vez con Dalamud.

## Compilar y probar
1. En la carpeta que contiene `AlertasXIV.csproj`: `dotnet build -c Debug`
2. En el juego: `/xlsettings` → Experimental → Dev Plugin Locations → añade la ruta completa a `AlertasXIV.dll` (en `bin/x64/Debug/`).
3. Abre `/xlplugins` → Dev Tools → Installed Dev Plugins y activa **Alertas XIV**.
4. En The Firmament, escribe `/alerta dump` y comprueba que el estado coincide con lo que ves en pantalla.

## Notas
- La API de Dalamud y FFXIVClientStructs cambian con cada parche. Si algo no compila, revisa la versión del SDK en `AlertasXIV.csproj` y https://dalamud.dev.
- Cambia `Author` y `RepoUrl` en `AlertasXIV.json` antes de publicar.
