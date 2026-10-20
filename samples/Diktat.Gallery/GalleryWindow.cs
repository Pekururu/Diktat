using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace Diktat.Gallery;

internal sealed class GalleryWindow : ThemedWindow
{
    private readonly ITextureProvider textures;
    private bool running;
    private int completed = 3;
    private bool option = true;
    private bool selected = true;
    private bool ordinarySelected;
    private bool modalOpen;
    private string query = string.Empty;
    private string lastAction = "Ready";

    public GalleryWindow(ITextureProvider textures) : base("Diktat Gallery###diktat-gallery")
    {
        this.textures = textures;
        this.Size = new Vector2(720, 650);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 320), MaximumSize = new Vector2(float.MaxValue) };
    }

    public Action ShowControl { get; set; } = () => { };
    public Action ShowAppearance { get; set; } = () => { };

    private int SelectedCount => (this.selected ? 1 : 0) + (this.ordinarySelected ? 1 : 0);

    private string ActivityHint => this.running
        ? "Running: selection and edits are locked. Stop to make changes."
        : this.SelectedCount == 0 ? "Select at least one row to enable Start." : "Ready. Start runs only this local demonstration.";

    protected override void DrawContent()
    {
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        // Reserve the longest possible message so activity changes never move the buttons.
        var hintHeight = new[]
        {
            "Running: selection and edits are locked. Stop to make changes.",
            "Select at least one row to enable Start.",
            "Ready. Start runs only this local demonstration.",
        }.Max(text => ImGui.CalcTextSize(text, false, width).Y);
        var actionHeight = Math.Max(this.Theme.Metrics.ControlHeight * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeight());
        var footerHeight = actionHeight * 2 + hintHeight + ImGui.GetTextLineHeight() + spacing * 3;
        UiWidgets.BodyWithFooter(this.Theme, "gallery", footerHeight, () =>
        {
            UiWidgets.Title(this.Theme, "Diktat / COMPONENT GALLERY", "Square geometry. Scoped styles. Local demonstration data.");
            UiWidgets.HelpText(this.Theme, $"Dalamud scale: {ImGuiHelpers.GlobalScale:0.00}x. Font follows Dalamud.");
            if (UiWidgets.Button(this.Theme, "control", "Open unthemed control window", UiTone.Neutral)) this.ShowControl();
            if (UiWidgets.Button(this.Theme, "appearance", "Appearance", UiTone.Neutral)) this.ShowAppearance();
            ImGui.Separator();

            using var tabs = ImRaii.TabBar("gallery-tabs");
            if (!tabs) return;
            using (var tab = ImRaii.TabItem("Components")) { if (tab) this.Components(); }
            using (var tab = ImRaii.TabItem("States & interaction")) { if (tab) this.States(); }
            using (var tab = ImRaii.TabItem("Palette")) { if (tab) this.Palette(); }
        }, this.Actions);

        UiWidgets.Modal(this.Theme, "gallery-confirm", "Confirm example", ref this.modalOpen, () =>
        {
            UiWidgets.Status(this.Theme, "Confirmation required", UiTone.Warning);
            UiWidgets.HelpText(this.Theme, "This changes only the gallery's local status text.");
            if (UiWidgets.Button(this.Theme, "accept", "Confirm", UiTone.Primary))
            {
                this.lastAction = "Confirmed";
                this.modalOpen = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (UiWidgets.Button(this.Theme, "cancel", "Cancel", UiTone.Neutral))
            {
                this.modalOpen = false;
                ImGui.CloseCurrentPopup();
            }
        });
    }

    private void Components()
    {
        var theme = this.Theme;
        if (UiWidgets.Section(theme, "summary", "Surface & progress", "3 metrics"))
            UiWidgets.Surface(theme, "summary-card", () =>
            {
                UiWidgets.Status(theme, this.running ? "Running" : "Idle", this.running ? UiTone.Primary : UiTone.Neutral);
                UiWidgets.Metrics(theme, "overview",
                    new UiMetric("Selected", this.SelectedCount.ToString(), UiTone.Primary),
                    new UiMetric("Protected", "7", UiTone.Info),
                    new UiMetric("Free slots", "20"));
                ImGui.TextUnformatted("Demonstration progress");
                UiWidgets.Progress(theme, this.completed, 10);
            });

        if (UiWidgets.Section(theme, "rows", "Rows & game icons", "3 rows"))
        {
            var icon = this.textures.GetFromGameIcon(new GameIconLookup(60001)).GetWrapOrDefault();
            var shown = 0;
            if (this.MatchesFilter("Selectable item"))
            {
                shown++;
                if (UiWidgets.SelectableRow(theme, "selected", "Selectable item", this.selected, this.selected ? "Selected" : "Available", !this.running,
                    "Stop the demonstration before changing selection.", icon)) this.selected = !this.selected;
            }
            if (this.MatchesFilter("Another selectable item with a longer label"))
            {
                shown++;
                if (UiWidgets.SelectableRow(theme, "ordinary", "Another selectable item with a longer label", this.ordinarySelected, this.ordinarySelected ? "Selected" : "Available", !this.running,
                    "Stop the demonstration before changing selection.", icon)) this.ordinarySelected = !this.ordinarySelected;
            }
            if (this.MatchesFilter("Protected item"))
            {
                shown++;
                UiWidgets.SelectableRow(theme, "disabled", "Protected item", false, "Protected", false,
                    "Protected items cannot be selected in this demonstration.", icon);
            }
            if (shown == 0) UiWidgets.HelpText(theme, "No matching rows. Clear or change the filter below.");
            UiWidgets.HelpText(theme, "Protected items cannot be selected. Hover a row to read its full label and details.");
        }

        if (UiWidgets.Section(theme, "form", "Native controls"))
            UiWidgets.Surface(theme, "form-card", () =>
            {
                ImGui.TextUnformatted("Filter rows");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##query", "Type an item name...", ref this.query, 128);
                UiWidgets.HelpText(theme, "Filtering does not change selection. Clear this field to show all rows.");
                using (ImRaii.Disabled(this.running))
                {
                    ImGui.Checkbox("Example option", ref this.option);
                    ImGui.TextUnformatted("Demonstration progress");
                    ImGui.SetNextItemWidth(-1);
                    ImGui.SliderInt("##progress", ref this.completed, 0, 10, "%d / 10");
                }
                UiWidgets.HelpText(theme, this.running ? "Stop before changing the option or progress." : "The option and progress affect local preview data only.");
            });
    }

    private bool MatchesFilter(string label) => label.Contains(this.query.Trim(), StringComparison.OrdinalIgnoreCase);

    private void Actions()
    {
        var theme = this.Theme;
        if (UiWidgets.Button(theme, "start", "Start", UiTone.Primary, FontAwesomeIcon.Play, !this.running && this.SelectedCount > 0,
            this.running ? "The demonstration is already running." : "Select at least one row first."))
        {
            this.running = true;
            this.lastAction = "Started";
        }
        ImGui.SameLine();
        if (UiWidgets.Button(theme, "stop", "Stop", UiTone.Warning, FontAwesomeIcon.Stop, this.running, "Nothing is running."))
        {
            this.running = false;
            this.lastAction = "Stopped";
        }
        if (UiWidgets.Button(theme, "reset", "Reset", UiTone.Neutral, FontAwesomeIcon.Undo, !this.running, "Stop before resetting progress."))
        {
            this.completed = 0;
            this.lastAction = "Progress reset";
        }
        ImGui.SameLine();
        if (UiWidgets.Button(theme, "confirm", "Review", UiTone.Neutral, enabled: !this.running, disabledReason: "Stop before opening confirmation."))
        {
            this.modalOpen = true;
            ImGui.OpenPopup("Confirm example###gallery-confirm");
        }
        UiWidgets.HelpText(theme, this.ActivityHint);
        ImGui.TextUnformatted($"Last action: {this.lastAction}");
    }

    private void States()
    {
        var theme = this.Theme;
        UiWidgets.Surface(theme, "interaction-help", () => UiWidgets.HelpText(theme, "Hover a button, hold the mouse button, or use keyboard navigation to inspect native hover, pressed, and focus states. Disabled samples stay visible and explain why on hover. The footer demonstrates caller-owned activity rules on every tab."));
        foreach (var (label, tone) in new[] { ("Idle", UiTone.Neutral), ("Running", UiTone.Primary), ("Paused / stopped", UiTone.Warning), ("Completed / healthy", UiTone.Success), ("Failed / blocked", UiTone.Danger), ("Informational / protected", UiTone.Info) })
        {
            UiWidgets.Status(theme, label, tone);
            UiWidgets.Button(theme, $"state-{tone}", label, tone);
            UiWidgets.Button(theme, $"disabled-{tone}", "Disabled", tone, enabled: false, disabledReason: "This is a disabled-state sample; it has no action.");
        }
        UiWidgets.IconButton(theme, "icon-sample", FontAwesomeIcon.Undo, "Reset icon sample", enabled: false,
            disabledReason: "This is a disabled-state sample; use Reset in the footer for the demo.");
        UiWidgets.Progress(theme, 0, 0, UiTone.Neutral);
        UiWidgets.Progress(theme, 4, 10, UiTone.Warning);
        UiWidgets.Progress(theme, 10, 10, UiTone.Success);
    }

    private void Palette()
    {
        foreach (var property in typeof(UiColors).GetProperties())
        {
            var color = (Vector4)property.GetValue(this.Theme.Colors)!;
            ImGui.ColorButton($"##{property.Name}", color, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(28, 20) * ImGuiHelpers.GlobalScale);
            ImGui.SameLine();
            ImGui.TextUnformatted($"{property.Name,-18} #{(int)MathF.Round(color.X * 255):X2}{(int)MathF.Round(color.Y * 255):X2}{(int)MathF.Round(color.Z * 255):X2}");
        }
    }
}
