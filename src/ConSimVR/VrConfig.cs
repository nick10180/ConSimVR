using BepInEx.Configuration;
using UnityEngine;

namespace ConSimVR
{
    internal enum CullingMode
    {
        /// <summary>One culling pass with a frustum enclosing both eyes (cheaper on CPU).</summary>
        Shared,
        /// <summary>Cull each eye separately (exact, costs a second culling pass).</summary>
        PerEye,
    }

    internal enum UiPanelMode
    {
        /// <summary>Desktop UI shown on a floating panel in VR; the desktop window shows the UI over black.</summary>
        Panel,
        /// <summary>No UI panel in VR; the desktop window mirrors the left eye.</summary>
        Off,
    }

    internal enum WindowModeOption
    {
        /// <summary>Force a normal window while VR runs (exclusive fullscreen breaks the headset image).</summary>
        Windowed,
        /// <summary>Force borderless fullscreen while VR runs.</summary>
        Borderless,
        /// <summary>Leave the game's display mode alone.</summary>
        Unchanged,
    }

    internal static class VrConfig
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> AutoStart;
        public static ConfigEntry<WindowModeOption> WindowMode;
        public static ConfigEntry<float> RenderScale;
        public static ConfigEntry<int> MaxEyeDimension;
        public static ConfigEntry<bool> Diagnostics;
        public static ConfigEntry<KeyCode> DumpKey;
        public static ConfigEntry<CullingMode> Culling;
        public static ConfigEntry<float> WorldScale;

        public static ConfigEntry<bool> DisableUncomfortableEffects;
        public static ConfigEntry<bool> ReduceTaaGhosting;
        public static ConfigEntry<bool> DisableSsrAccumulation;
        public static ConfigEntry<bool> ForceFirstPerson;
        public static ConfigEntry<bool> BlockMouseLook;
        public static ConfigEntry<bool> DisableHeadBob;
        public static ConfigEntry<bool> FollowVehicleTilt;
        public static ConfigEntry<float> SeatOffsetRight;
        public static ConfigEntry<float> SeatOffsetUp;
        public static ConfigEntry<float> SeatOffsetForward;

        public static ConfigEntry<UiPanelMode> UiPanel;
        public static ConfigEntry<float> UiDistance;
        public static ConfigEntry<float> UiWidth;
        public static ConfigEntry<float> UiHeightOffset;
        public static ConfigEntry<bool> ShowCursor;
        public static ConfigEntry<bool> WorldMarkersOnPanel;
        public static ConfigEntry<bool> FlatUiBackgrounds;
        public static ConfigEntry<string> UiBackgroundColorHex;

        /// <summary>UI.BackgroundColor parsed from #RRGGBB or #RRGGBBAA.</summary>
        public static class UiBackgroundColor
        {
            public static Color Value
            {
                get
                {
                    string hex = (UiBackgroundColorHex.Value ?? "").Trim().TrimStart('#');
                    if ((hex.Length == 6 || hex.Length == 8) && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint v))
                    {
                        if (hex.Length == 6)
                            v = (v << 8) | 0xFF;
                        return new Color(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
                    }
                    return new Color(0.08f, 0.09f, 0.11f, 1f);
                }
            }
        }

        public static ConfigEntry<KeyCode> RecenterKey;
        public static ConfigEntry<KeyCode> ToggleVrKey;
        public static ConfigEntry<KeyCode> ToggleUiKey;

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true, "Master switch for the VR mod.");
            WindowMode = config.Bind("General", "WindowMode", WindowModeOption.Windowed,
                "Display mode forced while VR runs. Exclusive fullscreen turns the headset image white; the game's own mode is restored when VR stops.");
            AutoStart = config.Bind("General", "AutoStart", true, "Start the OpenXR session automatically once the game is up. Otherwise press ToggleVrKey.");
            RenderScale = config.Bind("Rendering", "RenderScale", 1.0f,
                new ConfigDescription("Multiplier on the headset's recommended per-eye resolution.", new AcceptableValueRange<float>(0.5f, 2.0f)));
            MaxEyeDimension = config.Bind("Rendering", "MaxEyeDimension", 0,
                "Upper limit for the per-eye render width/height after RenderScale (0 = no limit). The runtime's recommendation includes its own supersampling and can be far above the panel resolution.");
            Diagnostics = config.Bind("Debug", "Diagnostics", false, "Log frame statistics every 2 seconds and texture descriptions once.");
            DumpKey = config.Bind("Debug", "DumpKey", KeyCode.F11, "Save the left eye texture, its swapchain image and the UI as PNGs in the plugin folder.");
            Culling = config.Bind("Rendering", "Culling", CullingMode.Shared, "Shared = one combined culling frustum for both eyes; PerEye = exact per-eye culling.");
            WorldScale = config.Bind("Rendering", "WorldScale", 1.0f,
                new ConfigDescription("Scales head movement and eye separation. Above 1 makes the world feel smaller.", new AcceptableValueRange<float>(0.5f, 2.0f)));

            ReduceTaaGhosting = config.Bind("Rendering", "ReduceTaaGhosting", true,
                "When the game uses TAA: High quality TAA with full motion vector rejection and anti-ringing, anti-flicker off. Reduces ghost trails on bright edges in VR and flat play.");
            DisableSsrAccumulation = config.Bind("Rendering", "DisableSsrAccumulation", true,
                "Switch screen-space reflections from PBR Accumulation (averages over frames, smears when moving fast) to Approximation. Applies in VR and flat play.");
            DisableUncomfortableEffects = config.Bind("Rendering", "DisableUncomfortableEffects", true,
                "Turn off motion blur, depth of field, lens distortion, chromatic aberration and panini projection while in VR.");
            ForceFirstPerson = config.Bind("Camera", "ForceFirstPerson", true,
                "Replace third-person cameras with the first-person/cab camera whenever the current pawn or machine mode has one.");
            BlockMouseLook = config.Bind("Camera", "BlockMouseLook", true, "Ignore mouse/stick camera look while in a first-person view (your head does the looking).");
            DisableHeadBob = config.Bind("Camera", "DisableHeadBob", true, "Disable the on-foot first-person camera noise/head bob.");
            FollowVehicleTilt = config.Bind("Camera", "FollowVehicleTilt", true,
                "In a cab, tilt the view with the machine (realistic). Off keeps the horizon level (more comfortable).");
            SeatOffsetRight = config.Bind("Camera", "SeatOffsetRight", 0f, "Extra eye offset to the right of the game camera, in metres.");
            SeatOffsetUp = config.Bind("Camera", "SeatOffsetUp", 0f, "Extra eye offset above the game camera, in metres.");
            SeatOffsetForward = config.Bind("Camera", "SeatOffsetForward", 0f, "Extra eye offset in front of the game camera, in metres.");

            UiPanel = config.Bind("UI", "Mode", UiPanelMode.Panel, "How the game's screen-space UI is shown in VR.");
            UiDistance = config.Bind("UI", "Distance", 1.4f, "Distance of the UI panel from the recentred head position, in metres.");
            UiWidth = config.Bind("UI", "Width", 1.6f, "Width of the UI panel in metres.");
            UiHeightOffset = config.Bind("UI", "HeightOffset", -0.15f, "Vertical offset of the UI panel centre relative to eye level, in metres.");

            WorldMarkersOnPanel = config.Bind("UI", "AlignWorldMarkers", true,
                "Place navigation markers (destination dot and distance) on the UI panel where the target actually is from your head.");
            ShowCursor = config.Bind("UI", "ShowCursor", true, "Draw a mouse cursor into the UI panel (the Windows cursor is not part of the captured image).");
            FlatUiBackgrounds = config.Bind("UI", "FlatBackgrounds", true,
                "Replace the blurred-world background behind HUD panels with a flat colour while in VR (the blur shows the world from the wrong angle).");
            UiBackgroundColorHex = config.Bind("UI", "BackgroundColor", "#161A1F", "Colour used for HUD panel backgrounds when FlatBackgrounds is on (#RRGGBB).");

            RecenterKey = config.Bind("Keys", "Recenter", KeyCode.F8, "Recenter the seated position on your current head pose.");
            ToggleVrKey = config.Bind("Keys", "ToggleVr", KeyCode.F9, "Start or stop VR.");
            ToggleUiKey = config.Bind("Keys", "ToggleUiPanel", KeyCode.F10, "Show or hide the VR UI panel.");
        }
    }
}
