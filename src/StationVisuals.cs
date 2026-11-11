using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CultCraft
{
    // The actual build-menu interaction is not the obsolete BUILDER structure brain.
    public sealed class StationVisuals : IDisposable
    {
        private sealed class Station
        {
            public Interaction_PlacementRegion Native;
            public GameObject Visual;
            public readonly Dictionary<Renderer, bool> Hidden = new Dictionary<Renderer, bool>();
            public readonly List<Material> Materials = new List<Material>();
            public readonly List<Mesh> Meshes = new List<Mesh>();
        }
        private sealed class BedBinding
        {
            public StructuresData Data;
            public Dwelling Dwelling;
            public Transform Head;
        }
        private static StationVisuals current;
        private static readonly Dictionary<Structure, BedBinding> bedBindings = new Dictionary<Structure, BedBinding>();
        private readonly MinecraftAssets assets;
        private readonly ManualLogSource logger;
        private readonly Harmony harmony = new Harmony("cultcraft.station.visuals");
        private readonly Dictionary<Interaction_PlacementRegion, Station> stations = new Dictionary<Interaction_PlacementRegion, Station>();
        private readonly List<Station> ordered = new List<Station>();
        private readonly HashSet<Interaction_PlacementRegion> pending = new HashSet<Interaction_PlacementRegion>();
        private readonly List<Interaction_PlacementRegion> queue = new List<Interaction_PlacementRegion>();
        private bool disposed;
        public int Count { get { return ordered.Count; } }

        public StationVisuals(MinecraftAssets assets, ManualLogSource logger)
        {
            this.assets = assets; this.logger = logger; current = this;
            try
            {
                harmony.Patch(AccessTools.Method(typeof(Interaction_PlacementRegion), "Start"), postfix: new HarmonyMethod(typeof(StationVisuals), "RegisterPostfix"));
                harmony.Patch(AccessTools.Method(typeof(Interaction_PlacementRegion), "OnEnableInteraction"), postfix: new HarmonyMethod(typeof(StationVisuals), "RegisterPostfix"));
                harmony.Patch(AccessTools.Method(typeof(Interaction_PlacementRegion), "OnDisableInteraction"), postfix: new HarmonyMethod(typeof(StationVisuals), "DisablePostfix"));
                // The native lifecycle maintains this registry. No per-frame scene searches.
                foreach (Interaction interaction in Interaction.interactions)
                    if (interaction is Interaction_PlacementRegion) pending.Add((Interaction_PlacementRegion)interaction);
                SceneManager.sceneLoaded += SceneLoaded;
            }
            catch { harmony.UnpatchSelf(); if (current == this) current = null; throw; }
        }

        private static void RegisterPostfix(Interaction_PlacementRegion __instance)
        { if (current != null && !current.disposed) current.pending.Add(__instance); }

        private static void DisablePostfix(Interaction_PlacementRegion __instance)
        { if (current != null && !current.disposed) current.Remove(__instance); }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) { bedBindings.Clear(); }

        public void Tick()
        {
            if (disposed) return;
            queue.Clear(); queue.AddRange(pending); pending.Clear();
            foreach (Interaction_PlacementRegion native in queue)
            {
                if (native == null || !native.gameObject.scene.IsValid() || !native.isActiveAndEnabled) continue;
                try { Register(native); }
                catch (Exception error) { logger.LogWarning("Minecraft construction table kept native art: " + error.Message); }
            }
        }

        private void Register(Interaction_PlacementRegion native)
        {
            Station station;
            if (stations.TryGetValue(native, out station)) { Capture(station); return; }
            if (native.ActiveObject == null) return;
            station = new Station { Native = native };
            try
            {
                station.Visual = new GameObject("CultCraft actual construction table");
                station.Visual.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                new BlockModels(assets, station.Visual.transform, station.Materials, station.Meshes).Build(Spec.Building("building_workbench"));
                Capture(station);
                stations.Add(native, station); ordered.Add(station);
            }
            catch { Release(station); throw; }
        }

        private static bool Protected(Interaction_PlacementRegion native, Renderer renderer)
        {
            Transform transform = renderer.transform;
            return renderer is ParticleSystemRenderer || renderer.GetComponentInParent<Canvas>() != null ||
                native.NewBuildingAvailableObject != null && transform.IsChildOf(native.NewBuildingAvailableObject.transform) ||
                native.TutorialObject != null && transform.IsChildOf(native.TutorialObject.transform);
        }

        private static void Capture(Station station)
        {
            foreach (GameObject art in new[] { station.Native.ActiveObject, station.Native.InactiveObject })
                if (art != null) foreach (Renderer renderer in art.GetComponentsInChildren<Renderer>(true))
                    if (!Protected(station.Native, renderer) && !station.Hidden.ContainsKey(renderer)) station.Hidden.Add(renderer, renderer.forceRenderingOff);
        }

        public void LateTick()
        {
            if (disposed) return;
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                Station station = ordered[i];
                if (station.Native == null || !station.Native.gameObject.scene.IsValid())
                { stations.Remove(station.Native); ordered.RemoveAt(i); Release(station); continue; }
                bool active = station.Native.isActiveAndEnabled &&
                    (station.Native.ActiveObject != null && station.Native.ActiveObject.activeInHierarchy || station.Native.InactiveObject != null && station.Native.InactiveObject.activeInHierarchy);
                if(station.Visual.activeSelf!=active)station.Visual.SetActive(active);
                Transform art = station.Native.ActiveObject != null ? station.Native.ActiveObject.transform : station.Native.transform;
                Vector3 position = art.position; position.z = station.Native.transform.position.z;
                if(station.Visual.transform.position!=position)station.Visual.transform.position = position;
                foreach (var pair in station.Hidden)
                {
                    bool hidden=pair.Value||active;
                    if(pair.Key!=null&&pair.Key.forceRenderingOff!=hidden)pair.Key.forceRenderingOff=hidden;
                }
            }
        }

        public GameObject NativeVisual(Interaction_PlacementRegion native)
        { Station station; return native != null && stations.TryGetValue(native, out station) ? station.Visual : null; }

        private void Remove(Interaction_PlacementRegion native)
        {
            pending.Remove(native); Station station;
            if (!stations.TryGetValue(native, out station)) return;
            stations.Remove(native); ordered.Remove(station); Release(station);
        }

        private static void Release(Station station)
        {
            foreach (var pair in station.Hidden) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            if (station.Visual != null) UnityEngine.Object.Destroy(station.Visual);
            foreach (Mesh mesh in station.Meshes) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            foreach (Material material in station.Materials) if (material != null) UnityEngine.Object.Destroy(material);
        }

        public static bool TryBedPose(Structure native, BuildingRow design, out Vector3 position, out Quaternion rotation)
        {
            position = native != null ? native.transform.position : Vector3.zero;
            rotation = Quaternion.Euler(-90f, 0f, 0f);
            if (native == null || (design.Model != "bed" && design.Model != "straw_bed")) return false;
            BedBinding binding;
            if (!bedBindings.TryGetValue(native, out binding) || binding.Data != native.Structure_Info)
            {
                binding = new BedBinding { Data = native.Structure_Info, Dwelling = native.GetComponentInChildren<Dwelling>(true) };
                bedBindings[native] = binding;
            }
            if (binding.Head == null && binding.Dwelling != null && binding.Dwelling.Positions.Count > 0 && binding.Dwelling.Positions[0].BedOccupied != null)
                binding.Head = binding.Dwelling.Positions[0].BedOccupied.transform;
            if (binding.Head == null) return false;
            // Rotation is a native flip/variant counter, not an angle. The slot's
            // serialized world position already includes native placement/direction.
            Vector3 pillow = rotation * BlockModels.BedPillow(design);
            Vector3 ground = native.Structure_Info != null ? native.Structure_Info.Position : native.transform.position;
            position = new Vector3(binding.Head.position.x - pillow.x, binding.Head.position.y - pillow.y, ground.z);
            return true;
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            harmony.UnpatchSelf(); SceneManager.sceneLoaded -= SceneLoaded;
            foreach (Station station in ordered) Release(station);
            stations.Clear(); ordered.Clear(); pending.Clear(); queue.Clear(); bedBindings.Clear();
            if (current == this) current = null;
        }
    }
}
