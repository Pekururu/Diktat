using System.Numerics;
using Diktat;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var expectedColors = new Dictionary<string, uint>
{
    ["Background"] = 0x1A1B26, ["DarkBackground"] = 0x13141C, ["DarkerBackground"] = 0x0E0E14,
    ["Raised"] = 0x24283B, ["Foreground"] = 0xA9B1D6, ["DarkForeground"] = 0x8C92AE,
    ["BrightForeground"] = 0xC0CAF5, ["Accent"] = 0x7AA2F7, ["Selection"] = 0x292E42,
    ["Muted"] = 0x414868, ["Danger"] = 0xF7768E, ["Warning"] = 0xE0AF68,
    ["Success"] = 0x9ECE6A, ["Info"] = 0x449DAB, ["Special"] = 0xAD8EE6, ["Orange"] = 0xEB927B,
};
foreach (var property in typeof(UiColors).GetProperties())
{
    var actual = (Vector4)property.GetValue(TokyoNight.Theme.Colors)!;
    var rgb = expectedColors[property.Name];
    var expected = new Vector4(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
    Check(actual == expected, $"Palette mismatch: {property.Name}");
}
Check(typeof(UiColors).GetProperties().Length == 16, "Palette must expose all sixteen reference tokens.");
var original = TokyoNight.Theme;
var variant = original with { Metrics = original.Metrics with { RowHeight = 40 } };
Check(original.Metrics.RowHeight == 28 && variant.Metrics.RowHeight == 40, "Customizing a theme must not mutate the shared preset.");
Check(original.Metrics.WindowPadding == new Vector2(18) && original.Metrics.PopupPadding == new Vector2(14), "Padding must match the brief.");
Check(original.Color(UiTone.Warning) == original.Colors.Warning && original.Color(UiTone.Success) == original.Colors.Success, "Semantic tones must preserve meaning.");
Check(UiWidgets.ProgressFraction(4, 10) == .4f && UiWidgets.ProgressFraction(-2, 10) == 0
    && UiWidgets.ProgressFraction(20, 10) == 1 && UiWidgets.ProgressFraction(0, 0) == 0, "Progress must be bounded, including empty totals.");
Console.WriteLine("PASS: palette, immutable themes, metrics, semantic colors, and progress bounds.");
ThemeTests.Run();

if (args.Contains("--native")) NativeTests.Run();
else Console.WriteLine("Native ImGui checks not requested. Use --native on Windows (or Wine).");
Console.WriteLine("Diktat tests passed.");
