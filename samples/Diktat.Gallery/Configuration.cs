using Dalamud.Configuration;

namespace Diktat.Gallery;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public ThemePreferences Theme { get; set; } = new();
}
