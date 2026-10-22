using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace AlertasXIV;

[Serializable]
public class ScheduledFete
{
    public DateTime StartUtc { get; set; }

    // Posición dentro del ciclo (1-12). 0 = desconocida.
    public int Number { get; set; }

    public bool Notified { get; set; }
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    public bool FeteAlertEnabled { get; set; } = true;
    public bool OnlyOnWorld { get; set; } = true;
    public string WorldName { get; set; } = "Ragnarok";

    // Avisar cuando falten estos minutos para cada Fête (0 = avisar justo al empezar).
    public int PreAlertMinutes { get; set; } = 10;
    public bool AlertAllFetes { get; set; } = true;
    public bool AlertOnActive { get; set; } = true;
    public bool AlertEachSession { get; set; } = true;

    // Datos de la wiki: 12 Fêtes por ciclo, una cada 2 horas.
    public int FetesPerCycle { get; set; } = 12;
    public int FeteIntervalMinutes { get; set; } = 120;

    // Horario proyectado a partir de la cuenta atrás del indicador.
    public List<ScheduledFete> Schedule { get; set; } = new();

    public bool ShowChatMessage { get; set; } = true;
    public bool ShowToast { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
