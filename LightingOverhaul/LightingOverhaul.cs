using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace LightingOverhaul
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        public bool enableStaticShadows = true;
        public bool enableGadgetShadows = false;
        public bool enableWarmTint = true;

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

        public void Draw()
        {
            GUILayout.Label("Lighting Overhaul Settings", UnityModManager.UI.bold);
            enableStaticShadows = GUILayout.Toggle(enableStaticShadows, "Dynamic Shadows");
            //enableGadgetShadows = GUILayout.Toggle(enableGadgetShadows, "Aktiviere dynamische Schatten für Gadget Lampen");
            enableWarmTint = GUILayout.Toggle(enableWarmTint, "Warmwhite");
        }

        public void OnChange() { }
    }

    [EnableReloading]
    static class LightingOverhaul
    {
        public static Harmony harmony = null!;
        public static GameObject? loggerObject;
        public static LightScanner? scanner;
        public static Settings? settings;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            Debug.Log("[LightingOverhaul] Load() called");

            settings = Settings.Load<Settings>(modEntry);
            modEntry.OnGUI = (_) => settings?.Draw();
            modEntry.OnSaveGUI = (_) => settings?.Save(modEntry);

            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            loggerObject = new GameObject("LightingOverhaulScanner");
            GameObject.DontDestroyOnLoad(loggerObject);
            scanner = loggerObject.AddComponent<LightScanner>();

            //Debug.Log("[LightingOverhaul] Scanner started.");
            return true;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            if (scanner == null) return false;
            scanner.enabled = value;
            return true;
        }
    }

    public class LightScanner : MonoBehaviour
    {
        private readonly float interval = 10f;
        private readonly HashSet<Light> modifiedLights = new();

        private readonly HashSet<string> knownGadgetLights = new()
        {
            "lighter", "lantern", "beaconred", "swivellight", "flashlight",
            "eotlantern", "beaconamber", "lightbarcyan", "beaconblue",
            "lightbarorange", "modernheadlightr", "modernheadlightl",
            "lightbaryellow", "lightbarpurple", "lightbargreen",
            "lightbarred", "lightbarwhite", "headlight", "lightbarblue",
            "gadgetlight"
        };

        void Start()
        {
            StartCoroutine(ScanLoop());
        }

        IEnumerator ScanLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(interval);
                int modifiedCount = 0;

                foreach (Light light in GameObject.FindObjectsOfType<Light>())
                {
                    if (light == null || modifiedLights.Contains(light)) continue;

                    string rawRootName = light.transform.root.name.ToLowerInvariant();
                    string rootName = rawRootName.Replace("(clone)", "").Trim();

                    // 1. Check rootName
                    bool isGadgetLight = false;
                    foreach (var known in knownGadgetLights)
                    {
                        if (rootName.StartsWith(known))
                        {
                            isGadgetLight = true;
                            break;
                        }
                    }

                    // 2. Check via component type
                    if (!isGadgetLight)
                    {
                        Transform current = light.transform;
                        while (current != null && !isGadgetLight)
                        {
                            foreach (var b in current.GetComponents<MonoBehaviour>())
                            {
                                string t = b.GetType().Name.ToLowerInvariant();
                                if (knownGadgetLights.Contains(t))
                                {
                                    isGadgetLight = true;
                                    break;
                                }
                            }
                            current = current.parent;
                        }
                    }

                    Color c = light.color;
                    bool colorMatchA = Mathf.Abs(c.r - 0.910f) < 0.01f && Mathf.Abs(c.g - 0.930f) < 0.01f && Mathf.Abs(c.b - 0.880f) < 0.01f;
                    bool colorMatchB = Mathf.Abs(c.r - 1.000f) < 0.01f && Mathf.Abs(c.g - 0.900f) < 0.01f && Mathf.Abs(c.b - 0.750f) < 0.01f;
                    bool colorMatchC = Mathf.Abs(c.r - 1.000f) < 0.01f && Mathf.Abs(c.g - 1.000f) < 0.01f && Mathf.Abs(c.b - 1.000f) < 0.01f;
                    bool colorMatch = colorMatchA || colorMatchB || colorMatchC;

                    bool doModifyShadow = false;

                    if (LightingOverhaul.settings?.enableStaticShadows == true && colorMatch && !isGadgetLight)
                        doModifyShadow = true;

                    if (LightingOverhaul.settings?.enableGadgetShadows == true && isGadgetLight)
                        doModifyShadow = true;

                    if (doModifyShadow)
                    {
                        light.shadows = LightShadows.Soft;
                        light.shadowStrength = 1f;
                        light.shadowBias = 0.001f;
                        modifiedCount++;
                    }

                    if (LightingOverhaul.settings?.enableWarmTint == true && colorMatch && !isGadgetLight)
                    {
                        light.color = new Color(1.0f, 0.85f, 0.65f, 1.0f);
                        light.bounceIntensity = 1.0f;
                    }

                    if (doModifyShadow || (LightingOverhaul.settings?.enableWarmTint == true && colorMatch && !isGadgetLight))
					{
						modifiedLights.Add(light);
					}
                }

                if (modifiedCount > 0)
                {
                    Debug.Log($"[LightingOverhaul] Lights modified this pass: {modifiedCount}");
                }
            }
        }
    }
}
