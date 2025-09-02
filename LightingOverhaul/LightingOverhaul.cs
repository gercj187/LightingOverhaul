// LightingOverhaul.cs
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityModManagerNet;

namespace LightingOverhaul
{
    static class LightingOverhaul
    {
        public static Settings? settings;
        private static GameObject? helperGO;
        private static SceneLightProcessor? processor;

        // Referenz auf den Tunnel-Controller
        private static LightingTunnel? tunnel;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            Debug.Log("[LightingOverhaul] Load()");
            settings = Settings.Load<Settings>(modEntry);
            modEntry.OnGUI     = _ => settings?.Draw();
            modEntry.OnSaveGUI = _ => settings?.Save(modEntry);

            helperGO = new GameObject("LightingOverhaulHelper");
            Object.DontDestroyOnLoad(helperGO);

            processor = helperGO.AddComponent<SceneLightProcessor>();
            tunnel    = helperGO.AddComponent<LightingTunnel>();
            Debug.Log("[LightingOverhaul] LightingTunnel component attached.");

            SceneManager.sceneLoaded += OnSceneLoaded;
            if (SceneManager.GetActiveScene().isLoaded)
                ProcessScene(SceneManager.GetActiveScene());

            modEntry.OnUnload = Unload;
            return true;
        }

        static bool Unload(UnityModManager.ModEntry _)
        {
            try { SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
            try { if (processor != null) processor.StopAllCoroutines(); } catch { }
            try { if (helperGO != null) Object.Destroy(helperGO); } catch { }

            settings  = null;
            processor = null;
            tunnel    = null;
            helperGO  = null;

            Debug.Log("[LightingOverhaul] Unloaded.");
            return true;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ProcessScene(scene);

        private static void ProcessScene(Scene scene)
        {
            if (processor == null) return;
            processor.StartProcessing(scene);
        }

        public static void RequestRefresh()
        {
            if (processor == null) return;
            processor.RefreshNow();
        }

        public static bool IsInTunnel => LightingTunnel.IsPlayerInTunnel;
    }

    // ------- Lokale Light-Tweak-Helfer bleiben unverändert (dienen als Debug über enableDynamicShadows/enableWarmWhiteTint) -------

    internal sealed class LightTweakState : MonoBehaviour
    {
        public bool  hadEnabled;
        public Color origColor;
        public float origBounceIntensity;
        public LightShadows origShadows;
        public float origShadowStrength;
        public float origShadowBias;
        public bool aestheticApplied;

        public void CaptureFrom(Light l)
        {
            hadEnabled          = l.enabled;
            origColor           = l.color;
            origBounceIntensity = l.bounceIntensity;
            origShadows         = l.shadows;
            origShadowStrength  = l.shadowStrength;
            origShadowBias      = l.shadowBias;
        }

        public void RestoreTo(Light l)
        {
            l.enabled          = hadEnabled;
            l.color            = origColor;
            l.bounceIntensity  = origBounceIntensity;
            l.shadows          = origShadows;
            l.shadowStrength   = origShadowStrength;
            l.shadowBias       = origShadowBias;
        }
    }

    internal static class PlayerRef
    {
        public static bool TryGetPlayerPosition(out Vector3 pos)
        {
            if (Camera.main != null)
            {
                pos = Camera.main.transform.position;
                return true;
            }
            var go = GameObject.FindWithTag("Player");
            if (go != null)
            {
                pos = go.transform.position;
                return true;
            }
            var any = Object.FindObjectsOfType<Camera>().FirstOrDefault(c => c.isActiveAndEnabled);
            if (any != null)
            {
                pos = any.transform.position;
                return true;
            }
            pos = Vector3.zero;
            return false;
        }
    }

    internal static class LightTweakHelper
    {
        private static readonly HashSet<string> knownGadgetLights = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "lighter","lantern","beaconred","swivellight","flashlight","eotlantern",
            "beaconamber","lightbarcyan","beaconblue","lightbarorange","modernheadlightr",
            "modernheadlightl","lightbaryellow","lightbarpurple","lightbargreen",
            "lightbarred","lightbarwhite","headlight","lightbarblue","gadgetlight"
        };

        private static readonly Vector3[] warmTargets = new[]
        {
            new Vector3(0.910f, 0.930f, 0.880f),
            new Vector3(1.000f, 0.900f, 0.750f),
            new Vector3(1.000f, 1.000f, 1.000f)
        };

        public static bool MatchesWarmColor(Color c)
        {
            Vector3 v = new(c.r, c.g, c.b);
            return warmTargets.Any(tc => Vector3.Distance(v, tc) < 0.02f);
        }

        public static bool IsGadgetLight(Transform t)
        {
            if (t == null) return false;
            string rootName = t.root?.name?.Replace("(Clone)", "").Trim() ?? string.Empty;
            foreach (string prefix in knownGadgetLights)
                if (rootName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    return true;

            Transform current = t;
            while (current != null)
            {
                var comps = current.GetComponents<Component>();
                foreach (var comp in comps)
                {
                    if (comp == null) continue;
                    string typeName = comp.GetType().Name.ToLowerInvariant();
                    if (knownGadgetLights.Contains(typeName))
                        return true;
                }
                current = current.parent;
            }
            return false;
        }

        public static bool ApplyAesthetic(Light l, Settings s)
        {
            bool colorMatch = MatchesWarmColor(l.color);
            if (!colorMatch) return false;

            var state = l.GetComponent<LightTweakState>();
            if (state == null)
            {
                state = l.gameObject.AddComponent<LightTweakState>();
                state.CaptureFrom(l);
            }

            bool changed = false;

            if (s.enableDynamicShadows)
            {
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 1f;
                l.shadowBias = 0.001f;
                changed = true;
            }

            if (s.enableWarmWhiteTint)
            {
                l.color = new Color(1.0f, 0.85f, 0.65f, 1.0f);
                l.bounceIntensity = 1.0f;
                changed = true;
            }

            state.aestheticApplied = state.aestheticApplied || changed;
            return changed;
        }

        public static void RevertAesthetic(Light l)
        {
            var st = l ? l.GetComponent<LightTweakState>() : null;
            if (st == null) return;
            if (!st.aestheticApplied) return;

            st.RestoreTo(l);
            st.aestheticApplied = false;
        }
    }

    internal sealed class SceneLightProcessor : MonoBehaviour
    {
        private const float UpdateIntervalSeconds = 0.75f;
        private const float MinMoveMetersForRefresh = 8f;
        private const bool  RevertOutsideBubble = true;

        private Coroutine? loopRoutine;
        private readonly HashSet<Light> activeTweaked = new();
        private Vector3 lastPlayerPos = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        private int lastActiveCount = -1;

        public void StartProcessing(Scene scene)
        {
            StopRunning();
            activeTweaked.Clear();
            lastActiveCount = -1;
            lastPlayerPos = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            StartCoroutine(InitialBurst());
            loopRoutine = StartCoroutine(ProximityLoop());
        }

        public void RefreshNow()
        {
            try
            {
                foreach (var l in activeTweaked.ToList())
                {
                    if (l == null) continue;
                    LightTweakHelper.RevertAesthetic(l);
                }
            }
            catch { }
            activeTweaked.Clear();
            lastActiveCount = -1;
            lastPlayerPos = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            StopRunning();
            StartCoroutine(InitialBurst());
            loopRoutine = StartCoroutine(ProximityLoop());
            Debug.Log("[LightingOverhaul] Refresh applied.");
        }

        private void StopRunning()
        {
            if (loopRoutine != null)
            {
                StopCoroutine(loopRoutine);
                loopRoutine = null;
            }
            StopAllCoroutines();
        }

        private System.Collections.IEnumerator InitialBurst()
        {
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForEndOfFrame();
                ProximityRefresh(force: true);
            }
        }

        private System.Collections.IEnumerator ProximityLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(UpdateIntervalSeconds);
                if (!HasPlayerMovedEnough(MinMoveMetersForRefresh))
                    continue;
                ProximityRefresh(force: false);
            }
        }

        private bool HasPlayerMovedEnough(float minMoveMeters)
        {
            if (!PlayerRef.TryGetPlayerPosition(out var pos))
                return false;
            if (lastPlayerPos.x == float.MinValue)
            {
                lastPlayerPos = pos;
                return true;
            }
            float dist2 = (pos - lastPlayerPos).sqrMagnitude;
            if (dist2 >= minMoveMeters * minMoveMeters)
            {
                lastPlayerPos = pos;
                return true;
            }
            return false;
        }

        private void ProximityRefresh(bool force)
        {
            var s = LightingOverhaul.settings;
            if (s == null) return;

            Vector3 playerPos;
            bool havePlayer = PlayerRef.TryGetPlayerPosition(out playerPos);

            var all = Object.FindObjectsOfType<Light>();
            if (all == null || all.Length == 0) return;

            IEnumerable<(Light l, float d2)> seq = all
                .Where(l => l != null && l.isActiveAndEnabled && l.gameObject.activeInHierarchy)
                .Where(l => !LightTweakHelper.IsGadgetLight(l.transform))
                .Select(l => (l, havePlayer ? (l.transform.position - playerPos).sqrMagnitude : 0f));

            if (havePlayer)
            {
                float maxD2 = s.maxDistanceMeters * s.maxDistanceMeters;
                seq = seq.Where(t => t.d2 <= maxD2)
                         .OrderBy(t => t.d2)
                         .Take(Mathf.Max(1, s.maxLightsCount));
            }
            else
            {
                seq = seq.Take(Mathf.Max(1, s.maxLightsCount));
            }

            var targetSet = new HashSet<Light>(seq.Select(t => t.l));

            if (RevertOutsideBubble && activeTweaked.Count > 0)
            {
                activeTweaked.RemoveWhere(x => x == null);
                foreach (var old in activeTweaked.Where(l => !targetSet.Contains(l)).ToList())
                {
                    LightTweakHelper.RevertAesthetic(old);
                    activeTweaked.Remove(old);
                }
            }

            foreach (var l in targetSet)
            {
                if (l == null) continue;
                if (activeTweaked.Contains(l)) continue;
                if (LightTweakHelper.ApplyAesthetic(l, s))
                    activeTweaked.Add(l);
            }

            if (activeTweaked.Count != lastActiveCount)
            {
                //Debug.Log($"[LightingOverhaul] Active tweaked: {activeTweaked.Count}");
                lastActiveCount = activeTweaked.Count;
            }
        }
    }
}
