using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Diktat.Gallery;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windows = new("Diktat.Gallery");
    private readonly GalleryWindow gallery = new(Textures);
    private readonly ControlWindow control = new();
    private readonly ThemeEditor themeEditor;
    private readonly AppearanceWindow appearance;
    private Configuration configuration;
    private bool canSaveConfiguration = true;

    public Plugin()
    {
        try
        {
            var loaded = PluginInterface.GetPluginConfig();
            this.configuration = loaded switch
            {
                null => new Configuration(),
                Configuration config => config,
                _ => throw new InvalidOperationException("Unexpected configuration type."),
            };
            if (this.configuration.Version != 1)
            {
                this.canSaveConfiguration = false;
                Log.Warning("Unknown Diktat configuration version; saving is disabled to preserve it.");
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not load Diktat preferences. Using defaults without overwriting the existing configuration.");
            this.configuration = new Configuration();
            this.canSaveConfiguration = false;
        }
        this.themeEditor = new ThemeEditor(this.configuration.Theme);
        this.appearance = new AppearanceWindow(this.themeEditor, this.SaveTheme);
        this.windows.AddWindow(this.gallery);
        this.windows.AddWindow(this.control);
        this.windows.AddWindow(this.appearance);
        this.gallery.ShowControl = () => this.control.IsOpen = true;
        this.gallery.ShowAppearance = this.OpenAppearance;
        Commands.AddHandler("/diktat", new CommandInfo(this.OnCommand) { HelpMessage = "Open the Diktat component gallery." });
        PluginInterface.UiBuilder.Draw += this.Draw;
        PluginInterface.UiBuilder.OpenMainUi += this.Open;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenAppearance;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenAppearance;
        PluginInterface.UiBuilder.OpenMainUi -= this.Open;
        PluginInterface.UiBuilder.Draw -= this.Draw;
        Commands.RemoveHandler("/diktat");
        this.windows.RemoveAllWindows();
    }

    private void Open() => this.gallery.IsOpen = true;
    private void OpenAppearance()
    {
        this.gallery.IsOpen = true;
        this.appearance.IsOpen = true;
    }

    private void Draw()
    {
        // All consuming windows receive the same snapshot before WindowSystem starts drawing.
        this.gallery.SetTheme(this.themeEditor.PreviewTheme);
        this.windows.Draw();
    }

    private void SaveTheme(ThemePreferences preferences)
    {
        try
        {
            if (!this.canSaveConfiguration) throw new InvalidOperationException("The existing configuration could not be loaded safely. Back it up and repair it before saving.");
            var next = new Configuration { Theme = preferences };
            PluginInterface.SavePluginConfig(next);
            this.configuration = next;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not save Diktat theme preferences.");
            throw;
        }
    }

    private void OnCommand(string command, string arguments) => this.Open();
}
