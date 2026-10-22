using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AlertasXIV;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IToastGui Toast { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/alerta";
    private const string TodoAddonName = "_ToDoList";
    private const string EventTitle = "Skyrise Celebration";

    private enum FeteState { None, Pending, Active, Ended }

    private sealed record FeteInfo(FeteState State, int? Session, int? MinutesToStart);

    public Configuration Config { get; }
    public readonly WindowSystem WindowSystem = new("AlertasXIV");
    private ConfigWindow ConfigWindow { get; }

    private DateTime nextCheck = DateTime.MinValue;
    private FeteState lastState = FeteState.None;
    private int? lastSession;
    private int? lastCountdownMinutes;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "/alerta abre la configuración. /alerta test: aviso de prueba. /alerta dump: muestra lo que lee el plugin y el horario guardado."
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ConfigWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += ConfigWindow.Toggle;

        Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ConfigWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= ConfigWindow.Toggle;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "test":
                Notify("Aviso de prueba: ¡hay Fêtes activas!");
                break;
            case "dump":
                Dump();
                break;
            default:
                ConfigWindow.Toggle();
                break;
        }
    }

    private void OnUpdate(IFramework framework)
    {
        // Una comprobación por segundo, no en cada frame.
        var now = DateTime.UtcNow;
        if (now < nextCheck) return;
        nextCheck = now.AddSeconds(1);

        if (!Config.FeteAlertEnabled || !WorldMatches()) return;

        // El indicador solo existe en The Firmament; el horario guardado funciona en cualquier sitio.
        var lines = ReadTodoLines();
        if (lines != null) Handle(Analyze(lines), now);

        CheckSchedule(now);
    }

    private void Handle(FeteInfo info, DateTime now)
    {
        // Si el indicador no está visible, no cambiamos nada para no repetir avisos.
        if (info.State == FeteState.None)
        {
            lastCountdownMinutes = null;
            return;
        }

        switch (info.State)
        {
            case FeteState.Pending:
                if (info.MinutesToStart is int minutes)
                {
                    // Cuando los minutos cambian, la cuenta atrás acaba de pasar por ese valor:
                    // es el momento en que la hora calculada es más precisa.
                    var precise = lastCountdownMinutes != null && lastCountdownMinutes != minutes;
                    Calibrate(minutes, precise, now);
                    lastCountdownMinutes = minutes;
                }
                else
                {
                    lastCountdownMinutes = null;
                }
                lastSession = null;
                break;

            case FeteState.Active:
                if (Config.AlertOnActive)
                {
                    if (lastState != FeteState.Active)
                        Notify(info.Session is int s ? $"¡Fêtes activas! Sesión {s}." : "¡Fêtes activas!");
                    else if (Config.AlertEachSession && info.Session != null && info.Session != lastSession)
                        Notify($"Nueva sesión de Fêtes: {info.Session}.");
                }
                lastSession = info.Session;
                lastCountdownMinutes = null;
                break;

            case FeteState.Ended:
                lastSession = null;
                lastCountdownMinutes = null;
                break;
        }

        lastState = info.State;
    }

    // Convierte la cuenta atrás del indicador en una hora de inicio guardada.
    // - Más de 2 h restantes: es el descanso entre ciclos, así que será la Fête 1 de un ciclo nuevo
    //   y se proyectan las 12 Fêtes (una cada 2 h).
    // - Menos de 2 h: no sabemos qué número de Fête es; se corrige el horario si ya coincide con
    //   una Fête guardada, o se añade como Fête suelta.
    private void Calibrate(int minutes, bool precise, DateTime now)
    {
        Config.Schedule.RemoveAll(f => f.StartUtc < now.AddHours(-3));

        var estimate = now.AddMinutes(minutes);
        var match = Config.Schedule.FirstOrDefault(f => Math.Abs((f.StartUtc - estimate).TotalMinutes) <= 5);

        if (match != null)
        {
            if (precise) Shift(match, estimate);
            return;
        }

        if (minutes > Config.FeteIntervalMinutes)
            Config.Schedule = CreateCycle(estimate);
        else
            Config.Schedule.Add(new ScheduledFete { Number = 0, StartUtc = estimate });

        Config.Save();
    }

    private List<ScheduledFete> CreateCycle(DateTime firstStart)
    {
        var list = new List<ScheduledFete>();
        for (var i = 0; i < Config.FetesPerCycle; i++)
        {
            list.Add(new ScheduledFete
            {
                Number = i + 1,
                StartUtc = firstStart.AddMinutes(i * Config.FeteIntervalMinutes),
            });
        }
        return list;
    }

    // Mueve la Fête indicada y todas las siguientes (corrige el desfase acumulado).
    private void Shift(ScheduledFete anchor, DateTime newStart)
    {
        var delta = newStart - anchor.StartUtc;
        if (Math.Abs(delta.TotalSeconds) < 20) return;

        var from = anchor.StartUtc;
        foreach (var f in Config.Schedule.Where(f => f.StartUtc >= from).ToList())
            f.StartUtc += delta;

        Config.Save();
    }

    private void CheckSchedule(DateTime now)
    {
        var changed = false;

        foreach (var f in Config.Schedule)
        {
            if (f.Notified) continue;
            if (!Config.AlertAllFetes && f.Number > 1) continue;

            var remaining = (f.StartUtc - now).TotalMinutes;
            if (remaining > Config.PreAlertMinutes) continue;

            f.Notified = true;
            changed = true;

            // Si el juego estuvo cerrado y ya pasó, no avisamos tarde.
            if (remaining <= -5) continue;

            var label = f.Number > 0 ? $"Fête {f.Number}/{Config.FetesPerCycle}" : "Fête";
            Notify(remaining <= 0.5
                ? $"¡{label} ahora!"
                : $"{label} empieza en {Math.Ceiling(remaining)} min.");
        }

        if (changed) Config.Save();
    }

    // Interpreta el texto del indicador de Ishgardian Restoration, que cambia así:
    //   "Skyrise Celebration: Session Pending" + "Fêtes commencing in 1d 16h 59m"
    //   "Skyrise Celebration: Session 12/12"   + "The current fête session has ended."
    // El texto de una sesión en curso no lo hemos visto todavía: se asume "Session n/12"
    // sin la frase "has ended".
    private static FeteInfo Analyze(List<string> lines)
    {
        var title = lines.FirstOrDefault(l => l.Contains(EventTitle, StringComparison.OrdinalIgnoreCase));
        if (title == null) return new FeteInfo(FeteState.None, null, null);

        var m = Regex.Match(title, @"Session\s+(\d+)\s*/\s*(\d+)");
        int? session = m.Success ? int.Parse(m.Groups[1].Value) : null;

        if (title.Contains("Pending", StringComparison.OrdinalIgnoreCase))
        {
            var countdown = lines.FirstOrDefault(l => l.Contains("commencing", StringComparison.OrdinalIgnoreCase));
            return new FeteInfo(FeteState.Pending, null, countdown != null ? ParseMinutes(countdown) : null);
        }

        if (lines.Any(l => l.Contains("has ended", StringComparison.OrdinalIgnoreCase)))
            return new FeteInfo(FeteState.Ended, session, null);

        return new FeteInfo(FeteState.Active, session, null);
    }

    // "Fêtes commencing in 1d 16h 59m" -> 2459
    private static int? ParseMinutes(string text)
    {
        var d = Regex.Match(text, @"(\d+)\s*d");
        var h = Regex.Match(text, @"(\d+)\s*h");
        var mi = Regex.Match(text, @"(\d+)\s*m");
        var s = Regex.Match(text, @"(\d+)\s*s");
        if (!d.Success && !h.Success && !mi.Success && !s.Success) return null;

        var total = 0;
        if (d.Success) total += int.Parse(d.Groups[1].Value) * 1440;
        if (h.Success) total += int.Parse(h.Groups[1].Value) * 60;
        if (mi.Success) total += int.Parse(mi.Groups[1].Value);
        return total;
    }

    private unsafe List<string>? ReadTodoLines()
    {
        var ptr = GameGui.GetAddonByName(TodoAddonName);
        var addon = (AtkUnitBase*)ptr.Address;
        if (addon == null || !addon->IsVisible) return null;

        var lines = new List<string>();
        CollectText(&addon->UldManager, lines, 0);
        return lines;
    }

    // Recorre los nodos de la ventana (y los de sus componentes) y junta el texto visible.
    private static unsafe void CollectText(AtkUldManager* uld, List<string> lines, int depth)
    {
        if (depth > 8) return;

        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null || !node->IsVisible()) continue;

            var type = (ushort)node->Type;
            if (type == (ushort)NodeType.Text)
            {
                var text = ((AtkTextNode*)node)->NodeText.ToString();
                if (!string.IsNullOrWhiteSpace(text)) lines.Add(text.Trim());
            }
            else if (type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null) CollectText(&component->UldManager, lines, depth + 1);
            }
        }
    }

    private void Dump()
    {
        var lines = ReadTodoLines();
        if (lines == null)
        {
            Chat.Print($"No encuentro la ventana {TodoAddonName} (o no está visible).");
        }
        else
        {
            var info = Analyze(lines);
            Chat.Print($"Estado detectado: {info.State} | sesión: {info.Session?.ToString() ?? "-"} | min. para empezar: {info.MinutesToStart?.ToString() ?? "-"}");

            var relevant = lines
                .Where(l => Regex.IsMatch(l, "skyrise|f[eê]te|session|restoration", RegexOptions.IgnoreCase))
                .Distinct()
                .ToList();

            if (relevant.Count == 0) Chat.Print("No hay líneas relacionadas con las Fêtes en la lista de tareas.");
            foreach (var line in relevant) Chat.Print($"  · {line}");
        }

        var upcoming = Config.Schedule.OrderBy(f => f.StartUtc).ToList();
        if (upcoming.Count == 0)
        {
            Chat.Print("Horario guardado: vacío (se rellena cuando el indicador muestre la cuenta atrás).");
            return;
        }

        Chat.Print($"Horario guardado ({upcoming.Count} Fêtes, hora de tu PC):");
        foreach (var f in upcoming)
        {
            var label = f.Number > 0 ? $"Fête {f.Number}" : "Fête";
            var state = f.Notified ? " (avisada)" : "";
            Chat.Print($"  · {label}: {f.StartUtc.ToLocalTime():dd/MM HH:mm}{state}");
        }
    }

    private bool WorldMatches()
    {
        if (!Config.OnlyOnWorld) return true;

        var player = Objects.LocalPlayer;
        if (player == null) return false;

        var world = player.CurrentWorld.Value.Name.ToString();
        return world.Equals(Config.WorldName, StringComparison.OrdinalIgnoreCase);
    }

    private void Notify(string text)
    {
        if (Config.ShowChatMessage) Chat.Print(text);
        if (Config.ShowToast) Toast.ShowNormal(text);
    }
}
