using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace LightningOverhaul
{
    [EnableReloading]
    static class LightningOverhaul
    {
        public static Harmony harmony = null!;
        public static GameObject? loggerObject;
        public static LightScanner? scanner;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            Debug.Log("[LightningOverhaul] Load() called");

            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            loggerObject = new GameObject("LightningOverhaulScanner");
            GameObject.DontDestroyOnLoad(loggerObject);
            scanner = loggerObject.AddComponent<LightScanner>();

            Debug.Log("[LightningOverhaul] Scanner started.");
            return true;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            if (scanner == null) return false;
            if (value)
            {
                harmony.PatchAll();
                scanner.enabled = true;
            }
            else
            {
                harmony.UnpatchAll(harmony.Id);
                scanner.enabled = false;
            }
            return true;
        }
    }

    public class LightScanner : MonoBehaviour
    {
        private readonly float interval = 10f;
        private readonly HashSet<Light> modifiedLights = new();

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

                    string lightName = light.name.ToLowerInvariant();
                    bool isSpotLight = lightName.Contains("spot light");
                    bool nameOk = lightName.Contains("light") || isSpotLight;

                    Color c = light.color;
                    string owner = light.transform.root.name;
                    string colorInfo = $"RGBA({c.r:F3}, {c.g:F3}, {c.b:F3}, {c.a:F3})";

                    bool colorMatchA = Mathf.Abs(c.r - 0.910f) < 0.01f && Mathf.Abs(c.g - 0.930f) < 0.01f && Mathf.Abs(c.b - 0.880f) < 0.01f;
                    bool colorMatchB = Mathf.Abs(c.r - 1.000f) < 0.01f && Mathf.Abs(c.g - 0.900f) < 0.01f && Mathf.Abs(c.b - 0.750f) < 0.01f;
                    bool colorMatchC = Mathf.Abs(c.r - 1.000f) < 0.01f && Mathf.Abs(c.g - 1.000f) < 0.01f && Mathf.Abs(c.b - 1.000f) < 0.01f;

                    if (nameOk && (colorMatchA || colorMatchB || colorMatchC))
                    {
                        light.shadows = LightShadows.Soft;
                        light.shadowStrength = 1f;
                        light.shadowBias = 0.001f;
                        light.color = new Color(1.0f, 0.85f, 0.2f, 1.0f);
                        light.bounceIntensity = 1.0f;

                        modifiedLights.Add(light);
                        //Debug.Log($"[LightningOverhaul] MODIFIED: {light.name} (owner: {owner}) | color: {colorInfo} -> warm yellow");
                        modifiedCount++;
                    }
                }

                if (modifiedCount > 0)
                {
                    Debug.Log($"[LightningOverhaul] Lights modified this pass: {modifiedCount}");
                }
            }
        }
    }
}
