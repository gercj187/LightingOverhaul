// Settings.cs
using UnityEngine;
using UnityModManagerNet;

namespace LightingOverhaul
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        // Visual tweaks
        public bool enableDynamicShadows = true;
        public bool enableWarmWhiteTint = true;

        // Player-based selection (always enabled)
        public float maxDistanceMeters = 150f;
        public int   maxLightsCount    = 50;

        // Tunnel darkness
        public bool enableTunnelDarkness = true;
        // 0.0 = keine Änderung, 1.0 = maximale Dunkelheit (90% Reduktion)
        public float tunnelLightingIntensity = 0.75f;
        // Blend/Smooth über Zeit (Sekunden)
        public float tunnelBlendTime = 5.0f;

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

        public void Draw()
        {
            GUILayout.Label("Lighting Overhaul Settings", UnityModManager.UI.bold);

            // Visual toggles
            enableDynamicShadows = GUILayout.Toggle(enableDynamicShadows, "Enable dynamic shadows");
            enableWarmWhiteTint  = GUILayout.Toggle(enableWarmWhiteTint,  "Enable warm white tint");
            GUILayout.Space(8);

            GUILayout.Label("Selection around player");
            GUILayout.Label($"Max distance (m): {maxDistanceMeters:0}");
            maxDistanceMeters = Mathf.Round(GUILayout.HorizontalSlider(Mathf.Clamp(maxDistanceMeters, 10f, 500f), 10f, 500f));
            GUILayout.Label($"Max lights (N): {maxLightsCount}");
            maxLightsCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(Mathf.Clamp(maxLightsCount, 5, 200), 5, 200));

            GUILayout.Space(8);
            GUILayout.Label("Tunnel Darkness", UnityModManager.UI.bold);
            enableTunnelDarkness = GUILayout.Toggle(enableTunnelDarkness, "Enable Tunnel Darkness");
            if (enableTunnelDarkness)
            {
                GUILayout.Label($"Tunnel Lighting Intensity: {tunnelLightingIntensity:0.00}  (0 = no change, 1 = max darkness)");
                tunnelLightingIntensity = Mathf.Round(
                    GUILayout.HorizontalSlider(Mathf.Clamp(tunnelLightingIntensity, 0.0f, 1.0f), 0.0f, 1.0f) * 100f
                ) / 100f;

                //GUILayout.Label($"Blend Time (s): {tunnelBlendTime:0.00}");
                //tunnelBlendTime = Mathf.Round(
                //    GUILayout.HorizontalSlider(Mathf.Clamp(tunnelBlendTime, 0.00f, 5.00f), 0.00f, 5.00f) * 100f
                //) / 100f;
            }			
            GUILayout.Space(12);
            if (GUILayout.Button("Refresh", GUILayout.Width(100f), GUILayout.Height(25f)))
            {
                LightingOverhaul.RequestRefresh();
            }
        }

        public void OnChange() { }
    }
}
