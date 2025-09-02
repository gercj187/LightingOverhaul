// LightingTunnel.cs — smoothed-basierte Dunkelheit
// OUT->IN: weicher Blend über settings.tunnelBlendTime
// IN->OUT: sofort hell (ohne Blend)
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace LightingOverhaul
{
    internal sealed class LightingTunnel : MonoBehaviour
    {
        // ===== Singleton =====
        public static LightingTunnel? Instance { get; private set; }

        // ===== Basics =====
        private const string TunnelNameSuffix = "tunnel";

        // Präsenz-Messung (ConeSampler)
        private const float SamplerOffsetY      = 0.4f;
        private const float PresenceSmoothTime  = 0.45f; // Glättung für State/Hysterese
        private const float DarkSmoothTime      = 0.90f; // trägere Glättung nur für Dunkelheit
        private ConeSampler? sampler;

        // IN/OUT via Hysterese
        private const float EnterPresence = 0.20f; // ab hier IN
        private const float ExitPresence  = 0.08f; // darunter OUT
        private const float MinStateHoldSeconds = 0.50f;
        private bool inTunnel = false;
        private float lastStateChangeT = -999f;

        // Fallback-Deckenerkennung
        private const float FallbackUpRayLength = 20f;
        private const int   FallbackUpRayCount  = 8;
        private int allLayersMask = ~0;

        // Optional: SphereCast-Kappe
        private static bool UseSphereCastCap = true;
        private const float SphereCastRadius  = 0.10f;

        // Debug
        private const float DebugPresenceInterval = 1.0f;
        private float dbgNextPrintT = 0f;

        // ===== Baselines (Originalhelligkeit draußen) =====
        private Light? sun;
        private float origSunIntensity = -1f;
        private AmbientMode ambientMode;
        private float origAmbientIntensity = -1f; // Skybox
        private Color origAmbientLight;           // Flat
        private Color origAmbientSkyColor;        // Trilight
        private Color origAmbientEquatorColor;    // Trilight
        private Color origAmbientGroundColor;     // Trilight
        private float lastBaselineRefreshT = -999f;
        private const float BaselineRefreshCooldown = 0.75f;

        // ===== Runtime =====
        private float presenceSmoothed = 0f;      // für State/Hysterese
        private float presenceVel      = 0f;

        private float presenceForDark  = 0f;      // träge Glättung für Dunkelheitsberechnung
        private float presenceDarkVel  = 0f;

        // 0..2 (1.0 = ~90% Abdunklung, 2.0 = schwarz)
        private float darkCurrent = 0f;

        // Mapping-Parameter
        private const float MaxDarknessFraction = 0.90f; // 90% Reduktion bei dark=1.0

        // ===== Eingang-Blend (nur OUT->IN) =====
        private bool  isBlendingIn = false;
        private float blendStartT  = 0f;
        private float blendDur     = 0f; // Sekunden

        // ===== Unity =====
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.Log("[LightingOverhaul] LightingTunnel: Duplicate instance -> destroying this one.");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            var go = new GameObject("lighting tunnel sampler");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.up * SamplerOffsetY;
            go.transform.localRotation = Quaternion.identity;

            sampler = go.AddComponent<ConeSampler>();
            if (sampler != null)
            {
                sampler.useCustomHitWeightFunction = true;
                sampler.weightFunction   = WeightTunnelOnly;
                sampler.coneAngle        = 85f;
                sampler.maxDistance      = 12f;
                sampler.sampleLayers     = allLayersMask;
                sampler.timingMode       = ConeSampler.TimingMode.OneSampleEveryNFrames;
                sampler.timingRate       = 3;
                sampler.sampleBufferSize = 30;
                sampler.enabled          = true;
            }

            CaptureWorldAsBaseline();
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            Application.onBeforeRender -= OnBeforeRender;
            RestoreBaselines();
        }

        private void OnActiveSceneChanged(Scene a, Scene b)
        {
            CaptureWorldAsBaseline();
        }

        // ===== Core Logic =====
        private void Update()
        {
            if (!PlayerRef.TryGetPlayerPosition(out var ppos))
            {
                inTunnel = false;
                presenceSmoothed = 0f;
                presenceForDark  = 0f;
                CancelBlendAndSetBright();
                return;
            }
            transform.position = ppos;

            var s = LightingOverhaul.settings;
            if (s == null || !s.enableTunnelDarkness)
            {
                inTunnel = false;
                CancelBlendAndSetBright();
                return;
            }

            // Präsenz messen
            float samplerPresence  = Mathf.Clamp01(sampler != null ? sampler.average : 0f);
            float fallbackPresence = ComputeUpRayPresence(ppos); // Deckenabdeckung
            float rawPresence      = Mathf.Max(samplerPresence, fallbackPresence);

            // Glätten für State/Hysterese
            presenceSmoothed = Smooth01(presenceSmoothed, rawPresence, ref presenceVel, PresenceSmoothTime);

            // IN/OUT (Hysterese + Haltezeit)
            bool wantInTunnel = inTunnel
                ? (presenceSmoothed >= ExitPresence)
                : (presenceSmoothed >= EnterPresence);

            // Übergangserkennung
            if (wantInTunnel != inTunnel && Time.unscaledTime - lastStateChangeT >= MinStateHoldSeconds)
            {
                bool wasIn = inTunnel;
                inTunnel = wantInTunnel;
                lastStateChangeT = Time.unscaledTime;

                if (!wasIn && inTunnel)
                {
                    // OUT -> IN: Blend starten
                    blendDur    = Mathf.Max(0f, s.tunnelBlendTime); // Sek.
                    blendStartT = Time.unscaledTime;
                    isBlendingIn = blendDur > 0.0001f;
                    // Sofort auf 0 setzen, dann hochblenden
                    darkCurrent = 0f;
                    Debug.Log("[LightingOverhaul] State=IN (blend in)");
                }
                else if (wasIn && !inTunnel)
				{
					CancelBlendAndSetBright(); // ← der neue Hard-Reset
					Debug.Log("[LightingOverhaul] State=OUT (instant)");
				}
            }

            if (!inTunnel)
            {
                // sicherheitshalber Baselines wiederherstellen
                RestoreBaselines();
            }

            // Debug alle 1s
            if (Time.unscaledTime >= dbgNextPrintT)
            {
                Debug.Log($"[LightingOverhaul] Tunnel presence: sampler={samplerPresence:0.00} fallback={fallbackPresence:0.00} smoothed={presenceSmoothed:0.00} state={(inTunnel ? "IN" : "OUT")} blend={(isBlendingIn ? "ON" : "OFF")}");
                dbgNextPrintT = Time.unscaledTime + DebugPresenceInterval;
            }

            // ===== smoothed als Referenz für Dunkelheit (ohne Schwellen) =====
            // separate träge Glättung gegen Feinzittern
            presenceForDark = Smooth01(presenceForDark, rawPresence, ref presenceDarkVel, DarkSmoothTime);

            // Ziel-Dunkelheit: linear 0..2 anhand Präsenz, gewichtet durch User-Intensität (Multiplikation)
            float userIntensity = Mathf.Clamp01(s.tunnelLightingIntensity); // 0..1
            float immediateTargetDark = Mathf.Min(2.0f, presenceForDark * 2.0f * userIntensity);

            float portalCap = Mathf.Lerp(0.65f, 2.0f, fallbackPresence); // weiche Kappe nahe Portalen
            immediateTargetDark = Mathf.Min(immediateTargetDark, portalCap);

            // Blend nur bei OUT->IN aktiv:
            if (inTunnel)
            {
                if (isBlendingIn)
                {
                    float t = blendDur <= 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - blendStartT) / blendDur);
                    // SmoothStep für besonders weichen Anstieg
                    float k = t * t * (3f - 2f * t);
                    darkCurrent = Mathf.Lerp(0f, immediateTargetDark, k);

                    if (t >= 1f)
                    {
                        isBlendingIn = false; // fertig geblendet
                    }
                }
                else
                {
                    // wenn bereits IN: einfach dem Ziel folgen (keine extra Slew-Logik nötig)
                    darkCurrent = immediateTargetDark;
                }
            }

            // Anwenden (außerhalb wird in CancelBlendAndSetBright hell gesetzt)
            ApplyLighting(darkCurrent, force: true);
        }

        private void LateUpdate()
        {
            ApplyLighting(darkCurrent, force: true);
        }

        private void OnBeforeRender()
        {
            ApplyLighting(darkCurrent, force: true);
        }

        private void CancelBlendAndSetBright()
		{
			isBlendingIn   = false;
			blendDur       = 0f;
			blendStartT    = 0f;

			// Alle Präsenz-States hard resetten, damit nichts "nachfedert"
			presenceSmoothed = 0f;
			presenceForDark  = 0f;
			presenceVel      = 0f;
			presenceDarkVel  = 0f;

			// Hart auf hell
			darkCurrent = 0f;

			// Baselines SOFORT zurück
			RestoreBaselines();

			// Und direkt anwenden
			ApplyLighting(0f, force: true);
		}

        // ===== Helpers =====
        private float WeightTunnelOnly(RaycastHit hit)
        {
            var n = hit.collider.name;
            return (!string.IsNullOrEmpty(n) && n.EndsWith(TunnelNameSuffix, StringComparison.OrdinalIgnoreCase)) ? 1f : 0f;
        }

        /// <summary>
        /// Prüft pro Richtung ALLE Treffer entlang des Strahls.
        /// Zählt als Treffer, sobald irgendein Collider am Strahl ein "tunnel" ist.
        /// Gibt Anteil [0..1] der Richtungen zurück, die Tunnel sehen (Abdeckung).
        /// </summary>
        private float ComputeUpRayPresence(Vector3 origin)
        {
            int hitsThatAreTunnel = 0;

            for (int i = 0; i < FallbackUpRayCount; i++)
            {
                float ang = (i / (float)FallbackUpRayCount) * Mathf.PI * 2f;
                Vector3 dir = (Vector3.up + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.35f).normalized;
                Vector3 start = origin + Vector3.up * 0.2f;

                bool foundTunnel = false;

                if (UseSphereCastCap)
                {
                    var hits = Physics.SphereCastAll(start, SphereCastRadius, dir, FallbackUpRayLength, allLayersMask, QueryTriggerInteraction.Collide);
                    if (hits != null && hits.Length > 0)
                    {
                        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                        for (int h = 0; h < hits.Length; h++)
                        {
                            var col = hits[h].collider;
                            string hn = col != null ? col.name : "";
                            if (!string.IsNullOrEmpty(hn) && hn.EndsWith(TunnelNameSuffix, StringComparison.OrdinalIgnoreCase))
                            {
                                foundTunnel = true;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    var hits = Physics.RaycastAll(start, dir, FallbackUpRayLength, allLayersMask, QueryTriggerInteraction.Collide);
                    if (hits != null && hits.Length > 0)
                    {
                        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                        for (int h = 0; h < hits.Length; h++)
                        {
                            var col = hits[h].collider;
                            string hn = col != null ? col.name : "";
                            if (!string.IsNullOrEmpty(hn) && hn.EndsWith(TunnelNameSuffix, StringComparison.OrdinalIgnoreCase))
                            {
                                foundTunnel = true;
                                break;
                            }
                        }
                    }
                }

                if (foundTunnel) hitsThatAreTunnel++;
            }

            return (FallbackUpRayCount > 0)
                ? Mathf.Clamp01(hitsThatAreTunnel / (float)FallbackUpRayCount)
                : 0f;
        }

        private static float Smooth01(float current, float target, ref float vel, float smoothTime)
        {
            if (smoothTime <= 0.0001f) return Mathf.Clamp01(target);
            float v = Mathf.SmoothDamp(current, target, ref vel, smoothTime);
            return Mathf.Clamp01(v);
        }

        private void SetDarkImmediate(float dark01_0to2)
        {
            darkCurrent = Mathf.Clamp(dark01_0to2, 0f, 2f);
            ApplyLighting(darkCurrent, force: true);
        }

        // ===== Baselines =====
        private void CaptureWorldAsBaseline()
        {
            sun = RenderSettings.sun != null
                ? RenderSettings.sun
                : FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
            origSunIntensity = (sun != null) ? sun.intensity : -1f;

            ambientMode             = RenderSettings.ambientMode;
            origAmbientIntensity    = RenderSettings.ambientIntensity;
            origAmbientLight        = RenderSettings.ambientLight;
            origAmbientSkyColor     = RenderSettings.ambientSkyColor;
            origAmbientEquatorColor = RenderSettings.ambientEquatorColor;
            origAmbientGroundColor  = RenderSettings.ambientGroundColor;

            lastBaselineRefreshT = Time.unscaledTime;
        }

        private void TryRefreshBaselinesOutside()
        {
            if (inTunnel) return;
            if (Time.unscaledTime - lastBaselineRefreshT < BaselineRefreshCooldown) return;
            CaptureWorldAsBaseline();
        }

        private void RestoreBaselines()
        {
            if (sun != null && origSunIntensity >= 0f)
                sun.intensity = origSunIntensity;

            switch (ambientMode)
            {
                case AmbientMode.Flat:
                    RenderSettings.ambientLight = origAmbientLight;
                    break;
                case AmbientMode.Skybox:
                    RenderSettings.ambientIntensity = origAmbientIntensity;
                    break;
                case AmbientMode.Trilight:
                    RenderSettings.ambientSkyColor     = origAmbientSkyColor;
                    RenderSettings.ambientEquatorColor = origAmbientEquatorColor;
                    RenderSettings.ambientGroundColor  = origAmbientGroundColor;
                    break;
            }
        }

        // ===== Apply =====
        private void ApplyLighting(float dark01, bool force)
        {
            if (!inTunnel)
			{
				RestoreBaselines();

				// Sicherheits-Reset: falls jemand darkCurrent verändert hat.
				if (darkCurrent != 0f) darkCurrent = 0f;

				// Nichts mehr tun – garantiert 100% hell.
				return;
			}

            // dark01 in 0..2 -> Faktor 1..0
            float core       = Mathf.Clamp01(dark01);        // 0..1
            float extra      = Mathf.Clamp01(dark01 - 1f);   // 0..1
            float factorCore = 1f - MaxDarknessFraction * core; // 1 → 0.10
            float factor     = Mathf.Lerp(factorCore, 0f, extra); // bei 2.0 → 0.0 (schwarz)

            // Wenn praktisch kein Effekt und nicht forced → skip
            if (factor >= 0.999f && !force)
                return;

            // Sonne
            if (sun != null && origSunIntensity >= 0f)
            {
                float targetIntensity = Mathf.Max(0f, origSunIntensity * factor);
                if (force || Math.Abs(sun.intensity - targetIntensity) > 0.0005f)
                    sun.intensity = targetIntensity;
            }

            // Ambient
            switch (ambientMode)
            {
                case AmbientMode.Flat:
                {
                    Color target = new Color(origAmbientLight.r * factor, origAmbientLight.g * factor, origAmbientLight.b * factor, origAmbientLight.a);
                    if (force || ColorDiffSqr(RenderSettings.ambientLight, target) > 0.000001f)
                        RenderSettings.ambientLight = target;
                    break;
                }
                case AmbientMode.Skybox:
                {
                    float target = Mathf.Max(0f, origAmbientIntensity * factor);
                    if (force || Math.Abs(RenderSettings.ambientIntensity - target) > 0.0005f)
                        RenderSettings.ambientIntensity = target;
                    break;
                }
                case AmbientMode.Trilight:
                {
                    Color sky     = Scale(origAmbientSkyColor,     factor);
                    Color equator = Scale(origAmbientEquatorColor, factor);
                    Color ground  = Scale(origAmbientGroundColor,  factor);

                    bool apply =
                        ColorDiffSqr(RenderSettings.ambientSkyColor, sky)        > 0.000001f ||
                        ColorDiffSqr(RenderSettings.ambientEquatorColor, equator) > 0.000001f ||
                        ColorDiffSqr(RenderSettings.ambientGroundColor, ground)   > 0.000001f;

                    if (force || apply)
                    {
                        RenderSettings.ambientSkyColor     = sky;
                        RenderSettings.ambientEquatorColor = equator;
                        RenderSettings.ambientGroundColor  = ground;
                    }
                    break;
                }
            }
        }

        private static Color Scale(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a);

        private static float ColorDiffSqr(Color a, Color b)
        {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            float da = a.a - b.a;
            return dr*dr + dg*dg + db*db + da*da;
        }
    }
}
