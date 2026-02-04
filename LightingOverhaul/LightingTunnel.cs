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
        private const float SamplerOffsetY = 0.4f;
        private const float PresenceSmoothTime = 0.45f;
        
        private ConeSampler? sampler;

        private const float EnterPresence = 0.20f;
        private const float ExitPresence = 0.08f;
        private const float MinStateHoldSeconds = 0.50f;
        private bool inTunnel = false;
        private float lastStateChangeT = -999f;

        private const float FallbackUpRayLength = 20f;
        private const int FallbackUpRayCount = 8;
        private int allLayersMask = ~0;
        private const float SphereCastRadius = 0.10f;

        private Light? sun;
        private float origSunIntensity = -1f;
        private AmbientMode ambientMode;
        private float origAmbientIntensity = -1f;
        private Color origAmbientLight;
        private Color origAmbientSkyColor;
        private Color origAmbientEquatorColor;
        private Color origAmbientGroundColor;
        
        private float lastBaselineRefreshT = -999f;
        private const float BaselineRefreshCooldown = 0.5f;
        
        private float presenceSmoothed = 0f;
        private float presenceVel = 0f;
        private float darkCurrent = 0f;

        private const float MaxDarknessFraction = 1.00f; 

        private bool isBlending = false;
        private float blendStartT = 0f;
        private float blendDuration = 0f;
        private float blendStartValue = 0f;
        private float blendTargetValue = 0f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            var go = new GameObject("lighting tunnel sampler");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.up * SamplerOffsetY;

            sampler = go.AddComponent<ConeSampler>();
            if (sampler != null)
            {
                sampler.useCustomHitWeightFunction = true;
                sampler.weightFunction = (hit) => (hit.collider.name ?? "").EndsWith(TunnelNameSuffix, StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
                sampler.coneAngle = 70f;
                sampler.maxDistance = 50f;
                sampler.sampleLayers = allLayersMask;
                sampler.timingMode = ConeSampler.TimingMode.OneSampleEveryNFrames;
                sampler.timingRate = 3;
                sampler.sampleBufferSize = 30;
                sampler.enabled = true;
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
            if (s == null) return;

            Vector3 sampleBasePos;
            if (PlayerRef.TryGetPlayerPosition(out var ppos))
            {
                sampleBasePos = ppos;
                TrainCar currentCar = PlayerManager.Car;
                if (currentCar != null)
                {
                    sampleBasePos = currentCar.transform.position + Vector3.up * 4.5f;
                }
            }
            else return;

            transform.position = sampleBasePos;

            if (!s.enableTunnelDarkness) {
                if (inTunnel || darkCurrent > 0) ResetToNormal();
                return;
            }

            float rawPresence = Mathf.Max(sampler != null ? sampler.average : 0f, ComputeUpRayPresence(sampleBasePos));
            presenceSmoothed = Mathf.SmoothDamp(presenceSmoothed, rawPresence, ref presenceVel, PresenceSmoothTime);

            bool wantInTunnel = inTunnel ? (presenceSmoothed >= ExitPresence) : (presenceSmoothed >= EnterPresence);

            if (wantInTunnel != inTunnel && Time.unscaledTime - lastStateChangeT >= MinStateHoldSeconds)
            {
                inTunnel = wantInTunnel;
                lastStateChangeT = Time.unscaledTime;
                
                isBlending = true;
                blendStartT = Time.unscaledTime;
                
                float baseTime = Mathf.Max(0.01f, s.tunnelBlendTime);
                blendDuration = inTunnel ? baseTime : baseTime * 0.6f; 
                
                blendStartValue = darkCurrent;
                blendTargetValue = inTunnel ? 1.0f : 0.0f;

                if (inTunnel) CaptureWorldAsBaseline(); 
            }

            if (isBlending)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - blendStartT) / blendDuration);
                float k = t * t * (3f - 2f * t);
                darkCurrent = Mathf.Lerp(blendStartValue, blendTargetValue, k);

                if (t >= 1f)
                {
                    isBlending = false;
                    if (!inTunnel) RestoreBaselines();
                }
            }

            TryRefreshBaselinesDynamic(s);

            if (darkCurrent > 0.0001f) ApplyLighting(darkCurrent, s);
        }

        private void LateUpdate()
        {
            var s = LightingOverhaul.settings;
            if (s != null && darkCurrent > 0.0001f) ApplyLighting(darkCurrent, s);
        }

        private void OnBeforeRender()
        {
            var s = LightingOverhaul.settings;
            if (s != null && darkCurrent > 0.0001f) ApplyLighting(darkCurrent, s);
        }

        private void ResetToNormal()
        {
            inTunnel = false;
            isBlending = false;
            darkCurrent = 0f;
            RestoreBaselines();
        }

        private void TryRefreshBaselinesDynamic(Settings s)
        {
            if (Time.unscaledTime - lastBaselineRefreshT < BaselineRefreshCooldown) return;
            
            float factor = 1f - (MaxDarknessFraction * darkCurrent * s.tunnelLightingIntensity);
            
            // Wenn der Faktor zu klein wird (fast Schwarz), machen wir keinen Refresh mehr,
            // da die Division durch extrem kleine Zahlen zu Flackern führt.
            if (factor > 0.05f)
            {
                if (RenderSettings.sun != null) origSunIntensity = RenderSettings.sun.intensity / factor;
                origAmbientIntensity = RenderSettings.ambientIntensity / factor;
                origAmbientLight = RenderSettings.ambientLight / factor;
                origAmbientSkyColor = RenderSettings.ambientSkyColor / factor;
                origAmbientEquatorColor = RenderSettings.ambientEquatorColor / factor;
                origAmbientGroundColor = RenderSettings.ambientGroundColor / factor;
            }
            else if (darkCurrent < 0.01f)
            {
                CaptureWorldAsBaseline();
            }
            
            lastBaselineRefreshT = Time.unscaledTime;
        }

        private void CaptureWorldAsBaseline()
        {
            sun = RenderSettings.sun ?? FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
            if (sun != null) origSunIntensity = sun.intensity;
            ambientMode = RenderSettings.ambientMode;
            origAmbientIntensity = RenderSettings.ambientIntensity;
            origAmbientLight = RenderSettings.ambientLight;
            origAmbientSkyColor = RenderSettings.ambientSkyColor;
            origAmbientEquatorColor = RenderSettings.ambientEquatorColor;
            origAmbientGroundColor = RenderSettings.ambientGroundColor;
            lastBaselineRefreshT = Time.unscaledTime;
        }

        private void RestoreBaselines()
        {
            if (sun != null && origSunIntensity >= 0f) sun.intensity = origSunIntensity;
            RenderSettings.ambientLight = origAmbientLight;
            RenderSettings.ambientIntensity = origAmbientIntensity;
            RenderSettings.ambientSkyColor = origAmbientSkyColor;
            RenderSettings.ambientEquatorColor = origAmbientEquatorColor;
            RenderSettings.ambientGroundColor = origAmbientGroundColor;
        }

        private void ApplyLighting(float dark01, Settings s)
        {
            float factor = 1f - (MaxDarknessFraction * dark01 * s.tunnelLightingIntensity);

            if (s.debugEnableSunDarkening && sun != null && origSunIntensity >= 0f)
            {
                float target = Mathf.Max(0f, origSunIntensity * factor);
                if (Math.Abs(sun.intensity - target) > 0.0005f) sun.intensity = target;
            }

            switch (ambientMode)
            {
                case AmbientMode.Flat:
                    if (!s.debugEnableAmbientFlat) break;
                    Color tFlat = Scale(origAmbientLight, factor);
                    if (ColorDiffSqr(RenderSettings.ambientLight, tFlat) > 0.000001f) RenderSettings.ambientLight = tFlat;
                    break;
                case AmbientMode.Skybox:
                    if (!s.debugEnableAmbientSkybox) break;
                    float tSky = Mathf.Max(0f, origAmbientIntensity * factor);
                    if (Math.Abs(RenderSettings.ambientIntensity - tSky) > 0.0005f) RenderSettings.ambientIntensity = tSky;
                    break;
                case AmbientMode.Trilight:
                    if (!s.debugEnableAmbientTrilight) break;
                    Color sky = Scale(origAmbientSkyColor, factor);
                    Color equator = Scale(origAmbientEquatorColor, factor);
                    Color ground = Scale(origAmbientGroundColor, factor);
                    if (ColorDiffSqr(RenderSettings.ambientSkyColor, sky) > 0.000001f) {
                        RenderSettings.ambientSkyColor = sky;
                        RenderSettings.ambientEquatorColor = equator;
                        RenderSettings.ambientGroundColor = ground;
                    }
                    break;
            }
        }

        private float ComputeUpRayPresence(Vector3 origin)
        {
            int hits = 0;
            for (int i = 0; i < FallbackUpRayCount; i++)
            {
                float ang = (i / (float)FallbackUpRayCount) * Mathf.PI * 2f;
                Vector3 dir = (Vector3.up + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.35f).normalized;
                if (Physics.SphereCast(origin, SphereCastRadius, dir, out var hit, FallbackUpRayLength, allLayersMask))
                    if ((hit.collider.name ?? "").EndsWith(TunnelNameSuffix, StringComparison.OrdinalIgnoreCase)) hits++;
            }
            return (float)hits / FallbackUpRayCount;
        }

        private static Color Scale(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a);
        private static float ColorDiffSqr(Color a, Color b) => (a.r-b.r)*(a.r-b.r) + (a.g-b.g)*(a.g-b.g) + (a.b-b.b)*(a.b-b.b);
    }
}