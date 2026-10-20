using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Diktat.Gallery;

internal sealed class AppearanceWindow : ThemedWindow
{
    private readonly ThemeEditor editor;
    private readonly Action<ThemePreferences> save;

    public AppearanceWindow(ThemeEditor editor, Action<ThemePreferences> save) : base("Diktat Appearance###diktat-appearance")
    {
        this.editor = editor;
        this.save = save;
        this.Size = new Vector2(500, 650);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 320), MaximumSize = new Vector2(float.MaxValue) };
    }

    protected override void DrawContent() => this.editor.Draw(this.Theme, "appearance", this.save);

    public override void OnClose() => this.editor.Cancel();
}
