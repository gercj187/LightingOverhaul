// Settings.cs
using UnityEngine;
using UnityModManagerNet;

namespace LightingOverhaul
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        // Visual tweaks (lokale Lichter, dienen auch als Debug-Toggle)
        public bool enableDynamicShadows = true;
        public bool enableWarmWhiteTint  = true;

        // Player-based selection (lokale Lichter-Auswahl)
        public float maxDistanceMeters = 150f;
        public int   maxLightsCount    = 50;

        // Tunnel darkness (global, nur im Tunnel)
        public bool  enableTunnelDarkness     = true;
        // 0.0 = keine Änderung, 1.0 = maximale Dunkelheit (90% Reduktion)
        public float tunnelLightingIntensity  = 0.75f;
        // Blend/Smooth über Zeit (Sekunden)
        public float tunnelBlendTime          = 5.0f;

        // ---- DEBUG: feingranulare Toggles für globale Effekte ----
        public bool debugEnableTunnelSystem   = true;  // Master für Tunnelpfad
        public bool debugEnableSunDarkening   = false; // Sonne reduzieren
        public bool debugEnableAmbientFlat    = true;  // ambientLight
        public bool debugEnableAmbientSkybox  = true;  // ambientIntensity
        public bool debugEnableAmbientTrilight= true;  // sky/equator/ground
        public bool debugVerboseTunnelLogs    = true;  // zusätzliche Logs

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

        public void Draw()
        {
            GUILayout.Label("Lighting Overhaul Settings", UnityModManager.UI.bold);

            // Lokale Visual toggles (Bubble)
            enableDynamicShadows = GUILayout.Toggle(enableDynamicShadows, "Enable dynamic shadows (local lights)");
            enableWarmWhiteTint  = GUILayout.Toggle(enableWarmWhiteTint,  "Enable warm white tint (local lights)");
            GUILayout.Space(6);

            GUILayout.Label("Selection around player (local lights)");
            GUILayout.Label($"Max distance (m): {maxDistanceMeters:0}");
            maxDistanceMeters = Mathf.Round(GUILayout.HorizontalSlider(Mathf.Clamp(maxDistanceMeters, 10f, 500f), 10f, 500f));
            GUILayout.Label($"Max lights (N): {maxLightsCount}");
            maxLightsCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(Mathf.Clamp(maxLightsCount, 5, 200), 5, 200));

            GUILayout.Space(8);
            GUILayout.Label("Tunnel Darkness (global)", UnityModManager.UI.bold);
            enableTunnelDarkness = GUILayout.Toggle(enableTunnelDarkness, "Enable Tunnel Darkness (master enable)");
            if (enableTunnelDarkness)
            {
                GUILayout.Label($"Tunnel Lighting Intensity: {tunnelLightingIntensity:0.00}  (0 = no change, 1 = max darkness)");
                tunnelLightingIntensity = Mathf.Round(
                    GUILayout.HorizontalSlider(Mathf.Clamp(tunnelLightingIntensity, 0.0f, 1.0f), 0.0f, 1.0f) * 100f
                ) / 100f;

                // Optional im UI auskommentiert belassen, Wert wird dennoch genutzt:
                //GUILayout.Label($"Blend Time (s): {tunnelBlendTime:0.00}");
                //tunnelBlendTime = Mathf.Round(
                //    GUILayout.HorizontalSlider(Mathf.Clamp(tunnelBlendTime, 0.00f, 5.00f), 0.00f, 5.00f) * 100f
                //) / 100f;
            }

			/*
            GUILayout.Space(10);
            GUILayout.Label("Debug (global tunnel effects)", UnityModManager.UI.bold);
            debugEnableTunnelSystem    = GUILayout.Toggle(debugEnableTunnelSystem,    "Enable tunnel system (master for global darkening path)");
            debugEnableSunDarkening    = GUILayout.Toggle(debugEnableSunDarkening,    "Affect Sun intensity");
            debugEnableAmbientFlat     = GUILayout.Toggle(debugEnableAmbientFlat,     "Affect Ambient FLAT (ambientLight)");
            debugEnableAmbientSkybox   = GUILayout.Toggle(debugEnableAmbientSkybox,   "Affect Ambient SKYBOX (ambientIntensity)");
            debugEnableAmbientTrilight = GUILayout.Toggle(debugEnableAmbientTrilight, "Affect Ambient TRILIGHT (sky/equator/ground)");
            debugVerboseTunnelLogs     = GUILayout.Toggle(debugVerboseTunnelLogs,     "Verbose tunnel logs");
			*/
            GUILayout.Space(12);
            if (GUILayout.Button("Refresh", GUILayout.Width(100f), GUILayout.Height(25f)))
            {
                LightingOverhaul.RequestRefresh();
            }
        }

        public void OnChange() { }
    }
}
