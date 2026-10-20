using System.Numerics;
using System.Text.Json;
using Dalamud.Interface.Style;
using Diktat;

internal static class ThemeTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        Check(ThemePresets.All.Select(p => p.Id).SequenceEqual(new[] { "tokyo-night", "dalamud-default", "ffxiv-menu" }), "Only the requested presets must be available.");
        var standard = StyleModelV1.DalamudStandard;
        var dalamud = ThemePresets.Find("dalamud-default").Theme.Colors;
        Check(dalamud.Background == ThemePalette.Validate(standard.Colors["WindowBg"])
            && dalamud.Accent == ThemePalette.Validate(standard.Colors["CheckMark"])
            && dalamud.Selection == ThemePalette.Validate(standard.Colors["TabActive"]),
            "Dalamud surfaces and accent must map installed built-in defaults, not active plugin styling.");
        var ffxiv = ThemePresets.Find("ffxiv-menu").Theme.Colors;
        Check(ffxiv.Background == ThemePalette.Rgb(0x313131) && ffxiv.Raised == ThemePalette.Rgb(0x494949)
            && ffxiv.Accent == ThemePalette.Rgb(0xD6B56C), "FFXIV palette must retain charcoal surfaces and gold emphasis.");
        foreach (var preset in ThemePresets.All)
        {
            Check(new ThemeEditor(new ThemePreferences { PresetId = preset.Id }).Warnings.Count == 0,
                $"Built-in preset {preset.Name} must pass all readability checks.");
            Check(preset.Theme.Metrics == TokyoNight.Theme.Metrics, "Presets must change only colors.");
            foreach (var slot in ThemePalette.Slots)
            {
                var color = slot.Get(preset.Theme.Colors);
                Check(color.W == 1 && color == ThemePalette.Validate(color), "All preset colors must be opaque RGB bytes.");
            }
        }
        Check(ThemePalette.Slots.Select(slot => slot.Key).Order().SequenceEqual(typeof(UiColors).GetProperties().Select(p => p.Name).Order()), "Editor must cover every palette token exactly once.");

        var editor = new ThemeEditor();
        Check(editor.PreviewTheme == TokyoNight.Theme && !editor.IsDirty && !editor.IsCustom, "New preferences must preserve the existing appearance.");
        editor.SelectPreset("dalamud-default");
        Check(editor.IsDirty && editor.PreviewTheme == ThemePresets.Find("dalamud-default").Theme, "Preset changes must preview immediately.");
        editor.Cancel();
        Check(!editor.IsDirty && editor.PreviewTheme == TokyoNight.Theme, "Cancel must undo preset selection.");

        editor.Customize();
        editor.SetColor("Accent", new Vector4(.2f, .4f, .6f, 0));
        Check(editor.IsCustom && editor.IsDirty && editor.PreviewTheme.Colors.Accent == new Vector4(.2f, .4f, .6f, 1), "Custom colors must remain opaque.");
        Check(TokyoNight.Theme.Colors.Accent != editor.PreviewTheme.Colors.Accent, "Editing must not mutate a built-in preset.");
        Check(new ThemeEditor().PreviewTheme == TokyoNight.Theme, "Editors must not share mutable plugin state.");
        var exported = editor.CreatePreferences();
        Check(exported.CustomColors!["Accent"] == "#336699", "Saved colors must be portable RGB hex.");
        exported.CustomColors["Accent"] = "#FFFFFF";
        Check(editor.PreviewTheme.Colors.Accent.X == .2f, "Exported preferences must be detached from the draft.");
        editor.ResetColor("Accent");
        Check(editor.PreviewTheme.Colors.Accent == TokyoNight.Theme.Colors.Accent && editor.IsCustom, "Per-color reset must retain custom mode.");
        editor.SetColor("Accent", new Vector4(.2f, .4f, .6f, 1));

        string? json = null;
        editor.Apply(preferences => json = JsonSerializer.Serialize(preferences));
        Check(!editor.IsDirty, "Apply must accept a successfully saved draft.");
        var reloaded = new ThemeEditor(JsonSerializer.Deserialize<ThemePreferences>(json!));
        Check(reloaded.PreviewTheme == editor.PreviewTheme && reloaded.IsCustom && reloaded.LoadNotice == null, "Saved custom colors must survive reload exactly.");
        editor.SelectPreset("ffxiv-menu");
        var failed = false;
        try { editor.Apply(_ => throw new IOException("Simulated save failure")); }
        catch (IOException) { failed = true; }
        Check(failed && editor.IsDirty, "Save failure must preserve the pending draft.");
        editor.Cancel();
        Check(editor.PreviewTheme == reloaded.PreviewTheme && !editor.IsDirty, "Cancel after failed save must restore the last successful Apply.");
        editor.ResetToPreset();
        Check(!editor.IsCustom && editor.PreviewTheme == TokyoNight.Theme && editor.IsDirty, "Reset to preset must be reversible until Apply.");
        editor.Cancel();
        Check(editor.IsCustom, "Cancel must recover custom colors after reset.");
        editor.SelectPreset("ffxiv-menu");
        editor.Apply(preferences => json = JsonSerializer.Serialize(preferences));
        reloaded = new ThemeEditor(JsonSerializer.Deserialize<ThemePreferences>(json!));
        Check(!reloaded.IsCustom && reloaded.PresetId == "ffxiv-menu", "Built-in selection must survive reload without custom overrides.");
        foreach (var removedId in new[] { "nord", "catppuccin-mocha", "gruvbox-dark" })
        {
            var removed = new ThemeEditor(new ThemePreferences { PresetId = removedId });
            Check(removed.PreviewTheme == TokyoNight.Theme && removed.LoadNotice != null, "Removed presets must fall back safely with a notice.");
            var savedCustom = editor.CreatePreferences();
            savedCustom.PresetId = removedId;
            editor.Customize();
            editor.SetColor("Accent", ThemePalette.Rgb(0x123456));
            savedCustom.CustomColors = editor.CreatePreferences().CustomColors;
            var retained = new ThemeEditor(savedCustom);
            Check(retained.IsCustom && retained.PreviewTheme.Colors == editor.PreviewTheme.Colors,
                "Removing a source preset must preserve every saved custom color.");
            Check(savedCustom.PresetId == removedId, "Loading old preferences must not mutate or overwrite the saved input.");
        }

        var legacyColors = ThemePalette.Slots.ToDictionary(slot => slot.Key, slot => ThemePalette.Hex(slot.Get(TokyoNight.Theme.Colors)));
        legacyColors["DarkForeground"] = "#565F89";
        var legacyCustom = new ThemeEditor(new ThemePreferences { CustomColors = legacyColors });
        Check(legacyCustom.PreviewTheme.Colors.DarkForeground == ThemePalette.Rgb(0x565F89)
            && legacyCustom.Warnings.Any(w => w.StartsWith("Secondary text:")),
            "Preset updates must preserve saved custom colors and continue warning about low contrast.");

        var repaired = new ThemeEditor(new ThemePreferences
        {
            PresetId = "missing-preset",
            CustomColors = new() { ["Accent"] = "#336699", ["Background"] = "#GG0000", ["Raised"] = "#00000000", ["Unknown"] = "#FFFFFF" },
        });
        Check(repaired.PresetId == ThemePresets.DefaultId && repaired.LoadNotice != null, "Unknown presets and malformed colors must report safe fallback.");
        Check(repaired.PreviewTheme.Colors.Background == TokyoNight.Theme.Colors.Background && repaired.PreviewTheme.Colors.Accent.X == .2f, "Fallback must preserve valid saved colors.");
        repaired.Apply(_ => { });
        Check(repaired.LoadNotice == null, "Saving repaired preferences must clear the load notice.");
        foreach (var bad in new[] { "#00000000", "#GGGGGG", "#00000", "000000", "# 00000", "#0000000", "", null })
            Check(!ThemePalette.TryParse(bad, out _), "Invalid saved RGB input must be rejected.");
        var rejected = false;
        try { repaired.SetColor("Accent", new Vector4(float.NaN, 0, 0, 1)); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Non-finite editor input must not enter the theme.");
        Check(Math.Abs(ThemePalette.Contrast(Vector4.Zero, Vector4.One) - 21) < .001f, "Contrast must use linearized sRGB luminance.");
        repaired.SetColor("Foreground", repaired.PreviewTheme.Colors.Background);
        repaired.SetColor("Success", repaired.PreviewTheme.Colors.Danger);
        Check(repaired.Warnings.Any(w => w.StartsWith("Body text:")) && repaired.Warnings.Any(w => w.Contains("Failure and Success")), "Unreadable text and matching state colors must warn.");
        Console.WriteLine("PASS: presets, custom editing/reset, Apply/Cancel, failed saves, JSON reload, malformed preferences, opacity, and readability warnings.");
    }
}
