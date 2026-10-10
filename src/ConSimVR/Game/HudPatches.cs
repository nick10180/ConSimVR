using System;
using HarmonyLib;
using UnityEngine;
using Wb.UI.HUD;

namespace ConSimVR
{
    /// <summary>
    /// Navigation markers (the destination dot with its distance) are screen-space HUD elements placed with the game
    /// camera's WorldToScreenPoint. In VR the HUD is a floating panel and the game camera is not where the player's head
    /// is, so the dot floated in front of the player. Before the game lays a marker out, its screen position is replaced
    /// by where the line from the player's head to the target crosses the UI panel; the game's own on/off-screen logic
    /// (edge arrows, distance text) then works from that position.
    /// </summary>
    [HarmonyPatch]
    internal static class HudPatches
    {
        private static int s_failures;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WbUIHUDNavigationIconsSystem), nameof(WbUIHUDNavigationIconsSystem.UpdateIndicatorElement))]
        private static void NavigationIcon(WbUIHUDNavigationIconSettings element, ref Vector3 screenPos)
        {
            var session = VrSession.Instance;
            if (session == null || element == null || !VrConfig.WorldMarkersOnPanel.Value)
                return;
            try
            {
                if (session.TryProjectToUiPanel(element.GetPosition(), out var projected))
                    screenPos = projected;
            }
            catch (Exception e)
            {
                if (s_failures++ < 3)
                    Plugin.Log.LogWarning($"Navigation marker placement failed: {e.Message}");
            }
        }
    }
}
