using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Diktat;

internal static class NativeTests
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static unsafe void Run()
    {
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.DisplaySize = new Vector2(1280, 1600);
            io.DeltaTime = 1f / 60;
            io.Fonts.AddFontDefault();
            Check(io.Fonts.Build(), "Font atlas must build.");
            io.Fonts.SetTexID(0, new ImTextureID(1));
            foreach (var scale in new[] { 1f, 1.5f, 2f })
            {
                io.FontGlobalScale = scale;
                TestScopes(scale);
                TestWindowLifecycle();
                TestWidgets();
                TestInteraction();
                TestRowMarker(scale);
                TestFooter(scale);
                TestMetricLayout(scale);
                TestThemeSwitch();
                TestThemeEditor(scale);
            }
            Console.WriteLine("PASS: native scope restoration, exception cleanup, window lifecycle, widget clicks/disabled states, fixed footer, adaptive metrics, selection markers, runtime theme switching, editor actions, and 1x/1.5x/2x scaling.");
        }
        finally { ImGui.DestroyContext(context); }
    }

    private static void TestScopes(float scale)
    {
        var before = Capture();
        var scope = new UiStyleScope(TokyoNight.Theme);
        try
        {
            var style = ImGui.GetStyle();
            Check(style.WindowRounding == 0 && style.ChildRounding == 0 && style.FrameRounding == 0
                && style.PopupRounding == 0 && style.GrabRounding == 0 && style.TabRounding == 0 && style.ScrollbarRounding == 0,
                "Every corner radius must be zero.");
            Check(style.WindowPadding == new Vector2(18 * scale) && style.FramePadding == new Vector2(10, 6) * scale,
                "Theme geometry must follow GlobalScale exactly once.");
            Check(style.Colors[(int)ImGuiCol.WindowBg].W == 1 && style.Colors[(int)ImGuiCol.PopupBg].W == 1, "Surfaces must be opaque.");
            Check(style.Colors[(int)ImGuiCol.Button] != style.Colors[(int)ImGuiCol.ButtonHovered]
                && style.Colors[(int)ImGuiCol.ButtonHovered] != style.Colors[(int)ImGuiCol.ButtonActive], "Interaction fills must be distinct.");
            var themed = Capture();
            var custom = TokyoNight.Theme with { Colors = TokyoNight.Theme.Colors with { Accent = new Vector4(1, 0, 0, 1) } };
            try
            {
                using var nested = new UiStyleScope(custom);
                throw new TestException();
            }
            catch (TestException) { }
            AssertRestored(themed);
        }
        finally { scope.Dispose(); }
        scope.Dispose();
        AssertRestored(before);
    }

    private static void TestWindowLifecycle()
    {
        var before = Capture();
        var window = new TestWindow();
        ImGui.NewFrame();
        window.PreDraw();
        ImGui.SetNextWindowSize(new Vector2(400, 250), ImGuiCond.Always);
        ImGui.Begin("exception-window");
        try
        {
            try { window.Draw(); }
            catch (TestException) { }
        }
        finally { ImGui.End(); window.PostDraw(); }
        ImGui.EndFrame();
        AssertRestored(before);

        // Collapsed windows get PreDraw/PostDraw but do not execute Draw.
        window.PreDraw();
        window.PostDraw();
        AssertRestored(before);
    }

    private static void TestWidgets()
    {
        var before = Capture();
        ImGui.NewFrame();
        using (var theme = new UiStyleScope(TokyoNight.Theme))
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(600, 1450), ImGuiCond.Always);
            ImGui.Begin("widgets");
            try
            {
                var inside = Capture();
                try { UiWidgets.Surface(TokyoNight.Theme, "throws", () => throw new TestException()); }
                catch (TestException) { }
                AssertRestored(inside);
                UiWidgets.Surface(TokyoNight.Theme, "empty", () => { });
                UiWidgets.Title(TokyoNight.Theme, "Gallery", "Wrapped description following the active font.");
                UiWidgets.Section(TokyoNight.Theme, "section", "Title", "4 items");
                UiWidgets.Surface(TokyoNight.Theme, "body", () =>
                {
                    UiWidgets.Status(TokyoNight.Theme, "Running", UiTone.Primary);
                    UiWidgets.Progress(TokyoNight.Theme, 4, 10);
                    var draw = ImGui.GetWindowDrawList();
                    var start = draw.VtxBuffer.Size;
                    UiWidgets.SelectableRow(TokyoNight.Theme, "unselected", "Item", false, "Metadata");
                    var unselectedVertices = draw.VtxBuffer.Size - start;
                    start = draw.VtxBuffer.Size;
                    UiWidgets.SelectableRow(TokyoNight.Theme, "selected", "Item", true, "Metadata");
                    // The selection background alone adds four vertices; the checkmark must add more.
                    Check(draw.VtxBuffer.Size - start > unselectedVertices + 4, "Selected rows must draw an additional non-color marker.");
                    Check(!UiWidgets.SelectableRow(TokyoNight.Theme, "disabled-row", "Disabled", false, enabled: false, disabledReason: "Protected item."), "Disabled rows must not activate.");
                    Check(!UiWidgets.Button(TokyoNight.Theme, "disabled-button", "Disabled", enabled: false), "Disabled buttons must not activate.");
                });

                var open = true;
                ImGui.OpenPopup("Modal###modal");
                try
                {
                    UiWidgets.Modal(TokyoNight.Theme, "modal", "Modal", ref open, () =>
                    {
                        ImGui.CloseCurrentPopup();
                        throw new TestException();
                    });
                }
                catch (TestException) { }
                AssertRestored(inside);
                ImGui.Button("Unthemed widget after nested scopes");
            }
            finally { ImGui.End(); }
        }
        ImGui.Render();
        Check(ImGui.GetDrawData().TotalVtxCount > 0, "Widgets must produce draw data.");
        AssertRestored(before);
    }

    private static void TestInteraction()
    {
        var layout = InteractionFrame(new Vector2(-100), false);
        for (var index = 0; index < layout.Centers.Length; index++)
        {
            InteractionFrame(layout.Centers[index], true);
            var released = InteractionFrame(layout.Centers[index], false);
            Check(released.Clicks[index] == (index % 2 == 0), $"Control {index} did not respect its enabled state during a mouse click.");
        }
    }

    private static void TestRowMarker(float scale)
    {
        var mouseTarget = new Vector2(-100);
        foreach (var selected in new[] { false, true })
        foreach (var hovered in new[] { false, true })
        {
            ImGui.GetIO().MousePos = hovered ? mouseTarget : new Vector2(-100);
            ImGui.NewFrame();
            using (var style = new UiStyleScope(TokyoNight.Theme))
            {
                ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(420, 320) * scale, ImGuiCond.Always);
                ImGui.Begin("row-marker");
                try
                {
                    var draw = ImGui.GetWindowDrawList();
                    var start = draw.VtxBuffer.Size;
                    UiWidgets.SelectableRow(TokyoNight.Theme, "item", "Item", selected);
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    mouseTarget = (min + max) / 2;
                    if (hovered) Check(ImGui.IsItemHovered(), "Marker test must exercise the hovered row.");
                    var backingColor = ImGui.GetColorU32(TokyoNight.Theme.Colors.DarkBackground);
                    var backing = new List<Vector2>();
                    for (var i = start; i < draw.VtxBuffer.Size; i++)
                    {
                        var vertex = draw.VtxBuffer[i];
                        if (vertex.Col == backingColor) backing.Add(vertex.Pos);
                    }
                    Check(backing.Count == 4, "Row checkbox must have an opaque backing independent of selected/hover fill.");
                    Check(backing.Min(point => point.X) >= min.X + 6 * scale,
                        "Row checkbox must be inset from the highlight edge.");
                }
                finally { ImGui.End(); }
            }
            ImGui.Render();
        }
    }

    private static (bool[] Clicks, Vector2[] Centers) InteractionFrame(Vector2 mouse, bool down)
    {
        var io = ImGui.GetIO();
        io.MousePos = mouse;
        io.MouseDown[0] = down;
        ImGui.NewFrame();
        var clicks = new bool[4];
        var centers = new Vector2[4];
        using (var style = new UiStyleScope(TokyoNight.Theme))
        {
            ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.Always);
            ImGui.Begin("interaction");
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    clicks[i] = i < 2
                        ? UiWidgets.Button(TokyoNight.Theme, $"button-{i}", "Test button", enabled: i % 2 == 0, disabledReason: "Stop before editing.")
                        : UiWidgets.SelectableRow(TokyoNight.Theme, $"row-{i}", "Test row", false, enabled: i % 2 == 0, disabledReason: "Protected item.");
                    centers[i] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                }
            }
            finally { ImGui.End(); }
        }
        ImGui.Render();
        return (clicks, centers);
    }

    private static void TestFooter(float scale)
    {
        var before = Capture();
        Vector2? footerPosition = null;
        for (var frame = 0; frame < 3; frame++)
        {
            ImGui.NewFrame();
            using (var style = new UiStyleScope(TokyoNight.Theme))
            {
                ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(420, 320) * scale, ImGuiCond.Always);
                ImGui.Begin("fixed-footer");
                try
                {
                    var parentPosition = ImGui.GetWindowPos();
                    var footerHeight = Math.Max(TokyoNight.Theme.Metrics.ControlHeight * scale, ImGui.GetFrameHeight());
                    var bottom = ImGui.GetCursorScreenPos().Y + ImGui.GetContentRegionAvail().Y;
                    var inside = Capture();
                    if (frame == 0)
                    {
                        var cursor = ImGui.GetCursorPos();
                        try { UiWidgets.BodyWithFooter(TokyoNight.Theme, "throws", footerHeight, () => throw new TestException(), () => { }); }
                        catch (TestException) { }
                        Check(ImGui.GetWindowPos() == parentPosition, "Throwing body must restore its parent window.");
                        AssertRestored(inside);
                        ImGui.SetCursorPos(cursor);
                    }
                    UiWidgets.BodyWithFooter(TokyoNight.Theme, "layout", footerHeight, () =>
                    {
                        for (var i = 0; i < 60; i++) ImGui.TextUnformatted($"Scrollable row {i}");
                        if (frame == 1) ImGui.SetScrollY(200 * scale);
                        if (frame == 2) Check(ImGui.GetScrollY() > 0, "Body must scroll independently.");
                    }, () =>
                    {
                        Check(ImGui.GetWindowPos() == parentPosition, "Footer must belong to the parent window.");
                        var position = ImGui.GetCursorScreenPos();
                        if (footerPosition.HasValue) Check(Vector2.Distance(position, footerPosition.Value) < 1, "Footer must not move when the body scrolls.");
                        footerPosition = position;
                        UiWidgets.Button(TokyoNight.Theme, "stop", "Stop", UiTone.Warning);
                        Check(ImGui.GetItemRectMax().Y <= bottom + 1, "Footer must remain inside the visible content region.");
                    });
                    AssertRestored(inside);
                }
                finally { ImGui.End(); }
            }
            ImGui.Render();
        }
        AssertRestored(before);
    }

    private static void TestMetricLayout(float scale)
    {
        var heights = new List<float>();
        foreach (var width in new[] { 420, 170 })
        {
            ImGui.NewFrame();
            using (var style = new UiStyleScope(TokyoNight.Theme))
            {
                ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(width, 320) * scale, ImGuiCond.Always);
                ImGui.Begin($"metrics-{width}");
                try
                {
                    var y = ImGui.GetCursorPosY();
                    UiWidgets.Metrics(TokyoNight.Theme, "summary",
                        new UiMetric("Selected", "2", UiTone.Primary),
                        new UiMetric("Protected", "7", UiTone.Info),
                        new UiMetric("Free slots", "20"));
                    heights.Add(ImGui.GetCursorPosY() - y);
                    Check(ImGui.GetScrollMaxX() == 0, "Metrics must not require horizontal scrolling.");
                }
                finally { ImGui.End(); }
            }
            ImGui.Render();
        }
        Check(heights[1] > heights[0], "Narrow summaries must wrap metrics onto additional rows.");
    }

    private static void TestThemeSwitch()
    {
        var before = Capture();
        var window = new TestWindow();
        var dalamud = ThemePresets.Find("dalamud-default").Theme;
        var ffxiv = ThemePresets.Find("ffxiv-menu").Theme;
        window.SetTheme(dalamud);
        Check(window.Theme == TokyoNight.Theme, "Queued colors must not change the active snapshot before PreDraw.");
        window.PreDraw();
        try
        {
            Check(window.Theme == dalamud, "PreDraw must adopt the pending theme.");
            window.SetTheme(ffxiv);
            Check(window.Theme == dalamud && Vector4.Distance(ImGui.GetStyle().Colors[(int)ImGuiCol.Text], dalamud.Colors.Foreground) < .008f,
                "Changes during drawing must not mix palette snapshots.");
        }
        finally { window.PostDraw(); }
        AssertRestored(before);
        window.PreDraw();
        try { Check(window.Theme == ffxiv, "Queued changes must become visible on the following draw."); }
        finally { window.PostDraw(); }
        AssertRestored(before);
    }

    private static void TestThemeEditor(float scale)
    {
        var before = Capture();
        var editor = new ThemeEditor();
        editor.SelectPreset("dalamud-default");
        var saveCount = 0;
        var failSave = false;
        var targets = EditorFrame(new Vector2(-100), false);
        Click(targets.Apply);
        Check(saveCount == 1 && !editor.IsDirty, "The editor's Apply button must save its current draft.");
        editor.Customize();
        editor.SetColor("Accent", new Vector4(1, 0, 0, 1));
        Click(targets.Cancel);
        Check(editor.PresetId == "dalamud-default" && !editor.IsCustom && !editor.IsDirty, "The editor's Cancel button must restore the applied preset.");
        editor.Customize();
        failSave = true;
        Click(targets.Apply);
        Check(editor.IsDirty && saveCount == 1, "Save errors in the editor must preserve the previous accepted state.");
        Click(targets.Cancel);
        Check(!editor.IsDirty, "Cancel must remain usable after a save error.");
        AssertRestored(before);

        void Click(Vector2 target)
        {
            EditorFrame(target, true);
            EditorFrame(target, false);
        }

        (Vector2 Apply, Vector2 Cancel) EditorFrame(Vector2 mouse, bool down)
        {
            ImGui.GetIO().MousePos = mouse;
            ImGui.GetIO().MouseDown[0] = down;
            ImGui.NewFrame();
            Vector2 apply;
            Vector2 cancel;
            using (var style = new UiStyleScope(TokyoNight.Theme))
            {
                ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(420, 480) * scale, ImGuiCond.Always);
                ImGui.Begin("theme-editor");
                try
                {
                    var start = ImGui.GetCursorScreenPos();
                    var bottom = start.Y + ImGui.GetContentRegionAvail().Y;
                    var inside = Capture();
                    editor.Draw(TokyoNight.Theme, "test-editor", _ =>
                    {
                        if (failSave) throw new IOException("Test save failure");
                        saveCount++;
                    });
                    // The final item is the status beneath the fixed action row.
                    var y = ImGui.GetItemRectMin().Y - ImGui.GetStyle().ItemSpacing.Y
                        - Math.Max(TokyoNight.Theme.Metrics.ControlHeight * scale, ImGui.GetFrameHeight()) / 2;
                    var applyWidth = ImGui.CalcTextSize("Apply").X + ImGui.GetStyle().FramePadding.X * 2;
                    var cancelWidth = ImGui.CalcTextSize("Cancel").X + ImGui.GetStyle().FramePadding.X * 2;
                    apply = new Vector2(start.X + applyWidth / 2, y);
                    cancel = new Vector2(start.X + applyWidth + ImGui.GetStyle().ItemSpacing.X + cancelWidth / 2, y);
                    Check(ImGui.GetItemRectMax().Y <= bottom + 1, "Editor actions and status must fit the visible window.");
                    AssertRestored(inside);
                }
                finally { ImGui.End(); }
            }
            ImGui.Render();
            return (apply, cancel);
        }
    }

    private static (Vector4[] Colors, float[] Floats, Vector2[] Vectors) Capture()
    {
        var s = ImGui.GetStyle();
        var colors = new Vector4[(int)ImGuiCol.Count];
        for (var i = 0; i < colors.Length; i++) colors[i] = s.Colors[i];
        return (colors,
            [s.Alpha, s.DisabledAlpha, s.WindowRounding, s.ChildRounding, s.FrameRounding, s.PopupRounding,
                s.GrabRounding, s.TabRounding, s.ScrollbarRounding, s.WindowBorderSize, s.ChildBorderSize, s.FrameBorderSize, s.PopupBorderSize],
            [s.WindowPadding, s.FramePadding, s.ItemSpacing, s.ItemInnerSpacing, s.CellPadding]);
    }

    private static void AssertRestored((Vector4[] Colors, float[] Floats, Vector2[] Vectors) expected)
    {
        var actual = Capture();
        for (var i = 0; i < expected.Colors.Length; i++)
            Check(Vector4.Distance(expected.Colors[i], actual.Colors[i]) < .008f, $"Color stack leaked at {(ImGuiCol)i}.");
        Check(expected.Floats.SequenceEqual(actual.Floats) && expected.Vectors.SequenceEqual(actual.Vectors), "Style stack leaked.");
    }

    private sealed class TestException : Exception;
    private sealed class TestWindow() : ThemedWindow("test")
    {
        protected override void DrawContent() => throw new TestException();
    }
}
