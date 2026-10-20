using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Diktat.Gallery;

// Intentionally uses the host theme. Drawn after the gallery to make leaks visible.
internal sealed class ControlWindow : Window
{
    private bool example;

    public ControlWindow() : base("Unthemed control###diktat-control")
    {
        this.Size = new Vector2(380, 220);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.TextWrapped("This window should retain your normal Dalamud colors, padding, and corner rounding while the gallery is open.");
        ImGui.Button("Standard button");
        ImGui.Checkbox("Standard checkbox", ref this.example);
        ImGui.ProgressBar(.5f);
    }
}
