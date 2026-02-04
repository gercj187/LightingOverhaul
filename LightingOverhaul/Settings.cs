// Settings.cs
using UnityEngine;
using UnityModManagerNet;

namespace LightingOverhaul
{	
    public enum LightColorPreset
    {
        ColdWhite,
        WarmWhite,
        Yellow
    }
	
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        public bool enableDynamicShadows = true;		
        public LightColorPreset lightColorPreset = LightColorPreset.ColdWhite;

        public float maxDistanceMeters = 150f;
        public int   maxLightsCount    = 50;

        public bool  enableTunnelDarkness     = true;
        public float tunnelLightingIntensity  = 0.75f;
        public float tunnelBlendTime          = 5.0f;

        public bool debugEnableTunnelSystem   = false;
        public bool debugEnableSunDarkening   = false;
        public bool debugEnableAmbientFlat    = false;
        public bool debugEnableAmbientSkybox  = false;
        public bool debugEnableAmbientTrilight= false;
        public bool debugVerboseTunnelLogs    = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
		
        public void Draw()
        {
            GUILayout.Label("Lighting Overhaul Settings", UnityModManager.UI.bold);
            GUILayout.Space(5);
			GUILayout.BeginVertical(GUI.skin.box);
            enableDynamicShadows = GUILayout.Toggle(enableDynamicShadows, "Enable dynamic shadows (local lights)");
            GUILayout.Space(5);
			if (enableDynamicShadows)
            {
				GUILayout.Label("Choose light color preset");

				GUILayout.BeginHorizontal(GUILayout.Width(500f));
				if (GUILayout.Toggle(lightColorPreset == LightColorPreset.ColdWhite, "Cold White", GUI.skin.button, GUILayout.Width(166f)))
					lightColorPreset = LightColorPreset.ColdWhite;
				if (GUILayout.Toggle(lightColorPreset == LightColorPreset.WarmWhite, "Warm White", GUI.skin.button, GUILayout.Width(166f)))
					lightColorPreset = LightColorPreset.WarmWhite;
				if (GUILayout.Toggle(lightColorPreset == LightColorPreset.Yellow, "Yellowish", GUI.skin.button, GUILayout.Width(166f)))
					lightColorPreset = LightColorPreset.Yellow;
				GUILayout.EndHorizontal();
				GUILayout.Space(5);
				GUILayout.Label("Selection around player (local lights)");
				GUILayout.Label($"Max distance (m): {maxDistanceMeters:0}");
				maxDistanceMeters = Mathf.Round(
					GUILayout.HorizontalSlider(
						Mathf.Clamp(maxDistanceMeters, 10f, 500f),
						10f,
						500f,
						GUILayout.Width(500f)
					)
				);
				GUILayout.Space(5);
				GUILayout.Label($"Max lights (N): {maxLightsCount}");
				maxLightsCount = Mathf.RoundToInt(
					GUILayout.HorizontalSlider(
						Mathf.Clamp(maxLightsCount, 5, 200),
						5,
						200,
						GUILayout.Width(500f)
					)
				);
            }
			GUILayout.EndVertical();	
			GUILayout.Space(5);
			GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Tunnel Darkness (global)", UnityModManager.UI.bold);
            enableTunnelDarkness = GUILayout.Toggle(enableTunnelDarkness, "Enable Tunnel Darkness (master enable)");
			GUILayout.Space(5);
            if (enableTunnelDarkness)
            {
                GUILayout.Label($"Tunnel Lighting Intensity: {tunnelLightingIntensity:0.00}  (0 = no change, 1 = max darkness)");
                tunnelLightingIntensity =
				Mathf.Round(
					GUILayout.HorizontalSlider(
						Mathf.Clamp(tunnelLightingIntensity, 0.0f, 1.0f),
						0.0f,
						1.0f,
						GUILayout.Width(500f)
					) * 100f
				) / 100f;
            }
			GUILayout.EndVertical();	
			GUILayout.Space(5);
            if (GUILayout.Button("Refresh", GUILayout.Width(500f), GUILayout.Height(25f)))
            {
                LightingOverhaul.RequestRefresh();
            }
        }

        public void OnChange() { }
    }
}
