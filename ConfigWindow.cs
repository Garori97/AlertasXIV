using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AlertasXIV;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin) : base("Alertas XIV – Configuración")
    {
        this.plugin = plugin;
        Size = new Vector2(420, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var cfg = plugin.Config;
        var changed = false;

        ImGui.TextUnformatted("Fêtes de Ishgardian Restoration");
        ImGui.Separator();

        var enabled = cfg.FeteAlertEnabled;
        if (ImGui.Checkbox("Activada", ref enabled)) { cfg.FeteAlertEnabled = enabled; changed = true; }

        var onlyWorld = cfg.OnlyOnWorld;
        if (ImGui.Checkbox("Solo en un mundo concreto", ref onlyWorld)) { cfg.OnlyOnWorld = onlyWorld; changed = true; }

        if (cfg.OnlyOnWorld)
        {
            var world = cfg.WorldName;
            if (ImGui.InputText("Mundo", ref world, 32)) { cfg.WorldName = world; changed = true; }
        }

        var pre = cfg.PreAlertMinutes;
        if (ImGui.SliderInt("Avisar antes (min, 0 = al empezar)", ref pre, 0, 60)) { cfg.PreAlertMinutes = pre; changed = true; }

        var all = cfg.AlertAllFetes;
        if (ImGui.Checkbox("Avisar de cada una de las 12 Fêtes (si no, solo de la primera)", ref all)) { cfg.AlertAllFetes = all; changed = true; }

        var active = cfg.AlertOnActive;
        if (ImGui.Checkbox("Avisar también al detectar una Fête en curso", ref active)) { cfg.AlertOnActive = active; changed = true; }

        var each = cfg.AlertEachSession;
        if (ImGui.Checkbox("Avisar en cada nueva sesión detectada", ref each)) { cfg.AlertEachSession = each; changed = true; }

        ImGui.Spacing();
        ImGui.TextUnformatted("Cómo avisar");
        ImGui.Separator();

        var chat = cfg.ShowChatMessage;
        if (ImGui.Checkbox("Mensaje en el chat", ref chat)) { cfg.ShowChatMessage = chat; changed = true; }

        var toast = cfg.ShowToast;
        if (ImGui.Checkbox("Aviso en pantalla", ref toast)) { cfg.ShowToast = toast; changed = true; }

        ImGui.Spacing();
        ImGui.TextUnformatted("Próximas Fêtes guardadas (hora de tu PC)");
        ImGui.Separator();

        var next = cfg.Schedule.Where(f => f.StartUtc > DateTime.UtcNow).OrderBy(f => f.StartUtc).Take(4).ToList();
        if (next.Count == 0)
        {
            ImGui.TextWrapped("Ninguna todavía. Se rellena sola cuando el indicador de Ishgardian Restoration muestre la cuenta atrás.");
        }
        else
        {
            foreach (var f in next)
            {
                var label = f.Number > 0 ? $"Fête {f.Number}" : "Fête";
                ImGui.TextUnformatted($"{label}: {f.StartUtc.ToLocalTime():dd/MM HH:mm}");
            }
        }

        if (ImGui.Button("Borrar horario guardado"))
        {
            cfg.Schedule.Clear();
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextWrapped("/alerta test: aviso de prueba. /alerta dump: muestra lo que lee el plugin y el horario.");

        if (changed) cfg.Save();
    }
}
