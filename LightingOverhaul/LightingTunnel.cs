// LightingTunnel.cs — smoothed-basierte Dunkelheit
// OUT->IN: weicher Blend über settings.tunnelBlendTime
// IN->OUT: sofort hell (ohne Blend)
// Außerhalb Tunnel: KEINE RenderSettings-Änderung
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightingOverhaul
{
    internal sealed class LightingTunnel : MonoBehaviour
    {
        public static LightingTunnel? Instance { get; private set; }
        public static bool IsPlayerInTunnel => Instance != null && Instance.inTunnel;

        private const string TunnelNameSuffix = "tunnel";

        private const float SamplerOffsetY      = 0.4f;
        private const float PresenceSmoothTime  = 0.45f;
        private const float DarkSmoothTime      = 0.90f;
        private ConeSampler? sampler;

        private const float EnterPresence = 0.20f;
        private const float ExitPresence  = 0.08f;
        private const float MinStateHoldSeconds = 0.50f;
        private bool  inTunnel = false;
        private float lastStateChangeT = -999f;

        private const float FallbackUpRayLength = 20f;
        private const int   FallbackUpRayCount  = 8;
        private int allLayersMask = ~0;

        private static bool UseSphereCastCap = true;
        private const float SphereCastRadius  = 0.10f;

        private const float DebugPresenceInterval = 1.0f;
        private float dbgNextPrintT = 0f;

        // Baselines (außen)
        private Light? sun;
        private float origSunIntensity = -1f;
        private AmbientMode ambientMode;
        private float origAmbientIntensity = -1f;
        private Color origAmbientLight;
        private Color origAmbientSkyColor;
        private Color origAmbientEquatorColor;
        private Color origAmbientGroundColor;
        private float lastBaselineRefreshT = -999f;
        private const float BaselineRefreshCooldown = 0.75f;

        // Runtime
        private float presenceSmoothed = 0f;
        private float presenceVel      = 0f;
        private float presenceForDark  = 0f;
        private float presenceDarkVel  = 0f;
        private float darkCurrent      = 0f;

        private const float MaxDarknessFraction = 0.90f;

        private bool  isBlendingIn = false;
        private float blendStartT  = 0f;
        private float blendDur     = 0f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
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
            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Application.onBeforeRender -= OnBeforeRender;
            RestoreBaselines();
        }

        private void Update()
        {
            var s = LightingOverhaul.settings;
            if (!PlayerRef.TryGetPlayerPosition(out var ppos))
            {
                if (inTunnel)
                {
                    inTunnel = false;
                    CancelBlendAndSetBright_Once();
                }
                return;
            }
            transform.position = ppos;

            // Master enables
            if (s == null || !s.enableTunnelDarkness || !s.debugEnableTunnelSystem)
            {
                if (inTunnel)
                {
                    inTunnel = false;
                    CancelBlendAndSetBright_Once();
                }
                TryRefreshBaselinesOutside();
                return;
            }

            float samplerPresence  = Mathf.Clamp01(sampler != null ? sampler.average : 0f);
            float fallbackPresence = ComputeUpRayPresence(ppos);
            float rawPresence      = Mathf.Max(samplerPresence, fallbackPresence);

            presenceSmoothed = Smooth01(presenceSmoothed, rawPresence, ref presenceVel, PresenceSmoothTime);

            bool wantInTunnel = inTunnel
                ? (presenceSmoothed >= ExitPresence)
                : (presenceSmoothed >= EnterPresence);

            if (wantInTunnel != inTunnel && Time.unscaledTime - lastStateChangeT >= MinStateHoldSeconds)
            {
                bool wasIn = inTunnel;
                inTunnel = wantInTunnel;
                lastStateChangeT = Time.unscaledTime;

                if (!wasIn && inTunnel)
                {
                    blendDur    = Mathf.Max(0f, s.tunnelBlendTime);
                    blendStartT = Time.unscaledTime;
                    isBlendingIn = blendDur > 0.0001f;
                    darkCurrent = 0f;
                    if (s.debugVerboseTunnelLogs)
                        Debug.Log("[LightingOverhaul] Inside Tunnel");
                }
                else if (wasIn && !inTunnel)
                {
                    CancelBlendAndSetBright_Once();
                    if (s.debugVerboseTunnelLogs)
                        Debug.Log("[LightingOverhaul] Outside Tunnel");
                }
            }

            if (s.debugVerboseTunnelLogs && Time.unscaledTime >= dbgNextPrintT)
            {
                //Debug.Log($"[LightingOverhaul] Tunnel presence: sampler={samplerPresence:0.00} fallback={fallbackPresence:0.00} smoothed={presenceSmoothed:0.00} state={(inTunnel ? "IN" : "OUT")} blend={(isBlendingIn ? "ON" : "OFF")}");
                dbgNextPrintT = Time.unscaledTime + DebugPresenceInterval;
            }

            if (!inTunnel)
            {
                TryRefreshBaselinesOutside();
                return;
            }

            presenceForDark = Smooth01(presenceForDark, rawPresence, ref presenceDarkVel, DarkSmoothTime);

            float userIntensity = Mathf.Clamp01(s.tunnelLightingIntensity);
            float immediateTargetDark = Mathf.Min(2.0f, presenceForDark * 2.0f * userIntensity);

            float portalCap = Mathf.Lerp(0.65f, 2.0f, fallbackPresence);
            immediateTargetDark = Mathf.Min(immediateTargetDark, portalCap);

            if (isBlendingIn)
            {
                float t = blendDur <= 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - blendStartT) / blendDur);
                float k = t * t * (3f - 2f * t);
                darkCurrent = Mathf.Lerp(0f, immediateTargetDark, k);
                if (t >= 1f) isBlendingIn = false;
            }
            else
            {
                darkCurrent = immediateTargetDark;
            }

            ApplyLighting(darkCurrent, s);
        }

        private void LateUpdate()
        {
            var s = LightingOverhaul.settings;
            if (inTunnel && s != null && s.enableTunnelDarkness && s.debugEnableTunnelSystem)
                ApplyLighting(darkCurrent, s);
        }

        private void OnBeforeRender()
        {
            var s = LightingOverhaul.settings;
            if (inTunnel && s != null && s.enableTunnelDarkness && s.debugEnableTunnelSystem)
                ApplyLighting(darkCurrent, s);
        }

        private void CancelBlendAndSetBright_Once()
        {
            isBlendingIn   = false;
            blendDur       = 0f;
            blendStartT    = 0f;

            presenceSmoothed = 0f;
            presenceForDark  = 0f;
            presenceVel      = 0f;
            presenceDarkVel  = 0f;

            darkCurrent = 0f;
            RestoreBaselines();
        }

        private float WeightTunnelOnly(RaycastHit hit)
        {
            var n = hit.collider.name;
            return (!string.IsNullOrEmpty(n) && n.EndsWith(TunnelNameSuffix, StringComparison.OrdinalIgnoreCase)) ? 1f : 0f;
        }

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

        // Baselines
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

        // Apply (nur im Tunnel), mit Debug-Gates
        private void ApplyLighting(float dark01, Settings s)
        {
            float core       = Mathf.Clamp01(dark01);
            float extra      = Mathf.Clamp01(dark01 - 1f);
            float factorCore = 1f - MaxDarknessFraction * core; // 1 -> 0.10
            float factor     = Mathf.Lerp(factorCore, 0f, extra); // bis 0.0 (schwarz)

            if (s.debugEnableSunDarkening && sun != null && origSunIntensity >= 0f)
            {
                float targetIntensity = Mathf.Max(0f, origSunIntensity * factor);
                if (Math.Abs(sun.intensity - targetIntensity) > 0.0005f)
                    sun.intensity = targetIntensity;
            }

            switch (ambientMode)
            {
                case AmbientMode.Flat:
                {
                    if (!s.debugEnableAmbientFlat) break;
                    Color target = new Color(origAmbientLight.r * factor, origAmbientLight.g * factor, origAmbientLight.b * factor, origAmbientLight.a);
                    if (ColorDiffSqr(RenderSettings.ambientLight, target) > 0.000001f)
                        RenderSettings.ambientLight = target;
                    break;
                }
                case AmbientMode.Skybox:
                {
                    if (!s.debugEnableAmbientSkybox) break;
                    float target = Mathf.Max(0f, origAmbientIntensity * factor);
                    if (Math.Abs(RenderSettings.ambientIntensity - target) > 0.0005f)
                        RenderSettings.ambientIntensity = target;
                    break;
                }
                case AmbientMode.Trilight:
                {
                    if (!s.debugEnableAmbientTrilight) break;
                    Color sky     = Scale(origAmbientSkyColor,     factor);
                    Color equator = Scale(origAmbientEquatorColor, factor);
                    Color ground  = Scale(origAmbientGroundColor,  factor);

                    bool apply =
                        ColorDiffSqr(RenderSettings.ambientSkyColor, sky)        > 0.000001f ||
                        ColorDiffSqr(RenderSettings.ambientEquatorColor, equator) > 0.000001f ||
                        ColorDiffSqr(RenderSettings.ambientGroundColor, ground)   > 0.000001f;

                    if (apply)
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
