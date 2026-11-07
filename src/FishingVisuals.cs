using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CultCraft
{
    internal sealed class FishingVisuals : IDisposable
    {
        private static readonly FieldInfo lineField = AccessTools.Field(typeof(Interaction_Fishing), "fishingLines");
        private readonly MinecraftAssets assets;
        private readonly Dictionary<Renderer, bool> hidden = new Dictionary<Renderer, bool>();
        private readonly Vector3[] points = new Vector3[17];
        private Interaction_Fishing native;
        private Transform nativeHook, nativeLine;
        private Renderer[] hookRenderers = new Renderer[0], lineRenderers = new Renderer[0];
        private GameObject root, bobber;
        private MeshRenderer bobberRenderer;
        private LineRenderer line;
        private Material hookMaterial, lineMaterial;
        private Texture2D lineTexture;
        private Mesh hookMesh;
        private int lastDrawFrame = -1, lastLineDrawFrame = -1;
        public int BobberDraws { get; private set; }
        public int LineDraws { get; private set; }
        public bool Cast { get; private set; }
        public bool NativeArtHidden { get; private set; }
        public float LineTipError { get; private set; }

        public FishingVisuals(MinecraftAssets assets) { this.assets = assets; }

        public void Bind(PlayerFarming player, bool fishing)
        {
            Interaction_Fishing next = Interaction_Fishing.Instance;
            int index = player == null ? -1 : PlayerFarming.players.IndexOf(player);
            Transform hook = next != null && next.FishingHooks != null && index >= 0 && index < next.FishingHooks.Length
                ? next.FishingHooks[index] : null;
            if (native != next || nativeHook != hook)
            {
                Restore();
                native = next; nativeHook = hook;
                Transform[] lines = next == null ? null : lineField?.GetValue(next) as Transform[];
                nativeLine = lines != null && index >= 0 && index < lines.Length ? lines[index] : null;
                hookRenderers = hook == null ? new Renderer[0] : hook.GetComponentsInChildren<Renderer>(true);
                lineRenderers = nativeLine == null ? new Renderer[0] : nativeLine.GetComponentsInChildren<Renderer>(true);
            }
            Cast = fishing && nativeLine != null && nativeLine.gameObject.activeInHierarchy;
            if (!fishing || nativeHook == null || nativeLine == null)
            {
                if (root != null) root.SetActive(false);
                RestoreArt();
            }
        }

        public void Tick(bool fishing, Vector3 tip, Camera camera)
        {
            if (!fishing || nativeHook == null || nativeLine == null || !Cast)
            {
                if (root != null) root.SetActive(false);
                RestoreArt();
                return;
            }
            if (root == null) Create();
            if (root == null) return;
            root.SetActive(true);
            bool hookVisible = nativeHook.gameObject.activeInHierarchy;
            bobber.SetActive(hookVisible);
            bobber.transform.position = nativeHook.position;
            // The vanilla hook entity is a camera-facing 0.5-block sprite. Keep
            // that original atlas, add pixel thickness and use the game's hook position.
            if (camera != null) bobber.transform.rotation = camera.transform.rotation;
            Vector3 end = nativeHook.position + Vector3.back * 0.07f;
            for (int i = 0; i < points.Length; i++)
            {
                float t = i / (points.Length - 1f);
                points[i] = Vector3.Lerp(tip, end, t) + Vector3.forward * (Mathf.Sin(t * Mathf.PI) * Mathf.Min(0.3f, Vector3.Distance(tip, end) * 0.035f));
            }
            line.SetPositions(points);
            LineTipError = Vector3.Distance(line.GetPosition(0), tip);
            // Hide presentation only after valid replacement resources exist.
            // Native hook transforms, fish AI, curves, minigame and colliders run untouched.
            Hide(hookRenderers); Hide(lineRenderers);
            NativeArtHidden = hidden.Count > 0;
        }

        private void Create()
        {
            Texture2D texture = assets.Texture("fishing_hook");
            Shader shader = Shader.Find("Unlit/Texture");
            if (texture == null || shader == null || !shader.isSupported) return;
            if (hookMesh == null) hookMesh = MinecraftAvatar.GenerateItemMesh(texture, "fishing hook entity", 0.30f, 0.01875f);
            if (hookMaterial == null) hookMaterial = new Material(shader) { name = "CultCraft Minecraft fishing-hook atlas", mainTexture = texture };
            if (lineMaterial == null)
            {
                lineTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "CultCraft fishing line", filterMode = FilterMode.Point };
                lineTexture.SetPixel(0, 0, new Color(0.06f, 0.045f, 0.035f, 1f)); lineTexture.Apply();
                lineMaterial = new Material(shader) { name = "CultCraft fishing line", mainTexture = lineTexture };
            }
            root = new GameObject("CultCraft native fishing presentation");
            bobber = new GameObject("Minecraft fishing-hook entity"); bobber.transform.SetParent(root.transform, false);
            bobber.AddComponent<MeshFilter>().sharedMesh = hookMesh;
            bobberRenderer = bobber.AddComponent<MeshRenderer>(); bobberRenderer.sharedMaterial = hookMaterial;
            bobberRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; bobberRenderer.receiveShadows = false;
            bobber.AddComponent<BookRenderProbe>().Observed = camera =>
            {
                if (IsGameCamera(camera) && lastDrawFrame != Time.frameCount && bobberRenderer.enabled && bobber.activeInHierarchy)
                { lastDrawFrame = Time.frameCount; BobberDraws++; }
            };
            var lineObject = new GameObject("Minecraft rod-tip fishing line"); lineObject.transform.SetParent(root.transform, false);
            line = lineObject.AddComponent<LineRenderer>(); line.sharedMaterial = lineMaterial;
            line.useWorldSpace = true; line.positionCount = points.Length; line.startWidth = line.endWidth = 0.012f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            lineObject.AddComponent<BookRenderProbe>().Observed = camera =>
            {
                if (IsGameCamera(camera) && lastLineDrawFrame != Time.frameCount && line.enabled && root.activeInHierarchy)
                { lastLineDrawFrame = Time.frameCount; LineDraws++; }
            };
        }

        private static bool IsGameCamera(Camera camera) { return camera != null && camera == Camera.main && camera.cameraType == CameraType.Game; }
        private void Hide(Renderer[] renderers)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                if (!hidden.ContainsKey(renderer)) hidden[renderer] = renderer.enabled;
                renderer.enabled = false;
            }
        }
        private void RestoreArt()
        {
            foreach (var item in hidden) if (item.Key != null) item.Key.enabled = item.Value;
            hidden.Clear(); NativeArtHidden = false;
        }
        private void Restore()
        {
            RestoreArt();
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null; bobber = null; line = null;
        }
        public void Dispose()
        {
            Restore();
            if (hookMesh != null) UnityEngine.Object.Destroy(hookMesh);
            if (hookMaterial != null) UnityEngine.Object.Destroy(hookMaterial);
            if (lineMaterial != null) UnityEngine.Object.Destroy(lineMaterial);
            if (lineTexture != null) UnityEngine.Object.Destroy(lineTexture);
        }
    }
}
