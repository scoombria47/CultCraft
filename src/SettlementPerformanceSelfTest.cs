using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using UnityEngine;

namespace CultCraft
{
    // Only created in the marked, memory-only Melty settlement.
    internal sealed class SettlementPerformanceSelfTest : IDisposable
    {
        public const string Id = "minecraft-settlement-spawns";
        private const int WaveSize = 12;
        private const int Waves = 6;
        private const int RockSamples = 12;
        private static readonly InventoryItem.ITEM_TYPE[] Loot =
            { InventoryItem.ITEM_TYPE.LOG, InventoryItem.ITEM_TYPE.STONE, InventoryItem.ITEM_TYPE.BERRY };
        private readonly MinecraftAssets assets;
        private readonly ManualLogSource logger;
        private readonly Action<string, bool, string> report;
        private readonly List<PickUp> pickups = new List<PickUp>();
        private readonly List<double> spawnTimes = new List<double>();
        private readonly List<double> spawnFrames = new List<double>();
        private readonly List<double> rockTimes = new List<double>();
        private readonly HashSet<int> dropModels = new HashSet<int>();
        private float start = -1f;
        private float waveTime;
        private int frameWindow;
        private int rockBuilds;
        private int rockTriangles;
        private int waves;
        private int verified;
        private int spawnFrame;
        public bool Complete { get; private set; }

        public SettlementPerformanceSelfTest(MinecraftAssets assets, ManualLogSource logger, Action<string, bool, string> report)
        { this.assets = assets; this.logger = logger; this.report = report; }

        public void Tick(PlayerFarming player, bool ready)
        {
            if (Complete || !ready) return;
            float now = Time.unscaledTime;
            if (start < 0f) start = now;
            if (now - start < 2f) return;
            if (frameWindow > 0)
            {
                spawnFrames.Add(Time.unscaledDeltaTime * 1000.0);
                frameWindow--;
            }
            if (pickups.Count > 0)
            {
                if (now - waveTime < 0.45f) return;
                int drawn = 0;
                foreach (PickUp pickup in pickups)
                {
                    if (pickup == null) continue;
                    var probe = pickup.GetComponentInChildren<PickupFirstDrawProbe>(true);
                    if (probe == null) continue;
                    var volume = PickupVisuals.Current.DroppedFor(probe.GetComponent<SpriteRenderer>());
                    if (probe.FirstGameDrawHadMinecraft && volume != null && volume.HasVolume && volume.NativeArtSuppressed)
                    {
                        drawn++;
                        dropModels.Add(volume.GetInstanceID());
                    }
                }
                if (drawn < WaveSize && now - waveTime < 2f) return;
                verified += drawn;
                ReleaseWave();
            }
            if (waves < Waves)
            {
                if (waves == 0 || now - waveTime >= 0.7f) SpawnWave(player, now);
                return;
            }
            if (rockBuilds < RockSamples + 2)
            {
                BuildRock();
                return;
            }
            Finish();
        }

        private void SpawnWave(PlayerFarming player, float now)
        {
            waveTime = now;
            spawnFrame = Time.frameCount;
            frameWindow = 3;
            waves++;
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < WaveSize; i++)
            {
                Vector3 position = player.transform.position + new Vector3((i % 6 - 2.5f) * 0.43f, 1.2f + i / 6 * 0.5f, 0f);
                InventoryItem.Spawn(Loot[i % Loot.Length], 1, position, 1f, pickup =>
                {
                    if (pickup == null) return;
                    pickup.MagnetToPlayer = false;
                    pickup.CanBePickedUp = false;
                    pickups.Add(pickup);
                });
            }
            spawnTimes.Add(MillisecondsSince(started));
        }

        private void BuildRock()
        {
            BuildingRow design = Spec.Building("rubble_big");
            design.Width = 4.5f;
            design.Depth = 3.5f;
            design.Height = 2.8f;
            var host = new GameObject("CultCraft test stone pile");
            var materials = new List<Material>();
            var meshes = new List<Mesh>();
            try
            {
                long started = Stopwatch.GetTimestamp();
                new BlockModels(assets, host.transform, materials, meshes).Build(design);
                double elapsed = MillisecondsSince(started);
                if (rockBuilds++ >= 2) rockTimes.Add(elapsed);
                rockTriangles = 0;
                foreach (Mesh mesh in meshes)
                    for (int i = 0; i < mesh.subMeshCount; i++) rockTriangles += (int)mesh.GetIndexCount(i) / 3;
            }
            finally
            {
                host.SetActive(false);
                UnityEngine.Object.Destroy(host);
                foreach (Mesh mesh in meshes) UnityEngine.Object.Destroy(mesh);
                foreach (Material material in materials) UnityEngine.Object.Destroy(material);
            }
        }

        private static double MillisecondsSince(long start)
        { return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency; }

        private static double Percentile(List<double> samples, double percentile)
        {
            samples.Sort();
            return samples[Mathf.Clamp((int)Math.Ceiling(samples.Count * percentile) - 1, 0, samples.Count - 1)];
        }

        private void Finish()
        {
            Complete = true;
            double spawnMedian = Percentile(spawnTimes, 0.5);
            double spawnPeak = Percentile(spawnTimes, 1.0);
            double frameP95 = Percentile(spawnFrames, 0.95);
            double rockMedian = Percentile(rockTimes, 0.5);
            MeltyPlay.Report("cultcraft.performance.spawn_batch_median_ms", spawnMedian);
            MeltyPlay.Report("cultcraft.performance.spawn_batch_peak_ms", spawnPeak);
            MeltyPlay.Report("cultcraft.performance.spawn_frame_p95_ms", frameP95);
            MeltyPlay.Report("cultcraft.performance.rock_build_median_ms", rockMedian);
            MeltyPlay.Report("cultcraft.performance.rock_triangles", rockTriangles);
            int fittedRocks = 0;
            if (Structure.Structures != null)
                foreach (Structure native in Structure.Structures)
                {
                    if (native == null || (native.Type != StructureBrain.TYPES.ROCK && native.Type != StructureBrain.TYPES.RUBBLE &&
                        native.Type != StructureBrain.TYPES.RUBBLE_BIG)) continue;
                    Transform facade = Gameplay.Current.NativeStructureVisual(native);
                    if (facade != null && facade.gameObject.activeInHierarchy && RockPileFit.Aligned(facade, native.transform, out _)) fittedRocks++;
                }
            string detail = FormattableString.Invariant(
                $"native loot first draws {verified}/{Waves * WaveSize} using {dropModels.Count} model instances; spawn batch median {spawnMedian:F2} ms, peak {spawnPeak:F2} ms; surrounding frame p95 {frameP95:F2} ms; rock build median {rockMedian:F2} ms, triangles {rockTriangles}, native fits {fittedRocks}; last spawn frame {spawnFrame}");
            logger.LogInfo("CultCraft settlement performance: " + detail);
            report(Id, verified == Waves * WaveSize && dropModels.Count < verified && rockTriangles > 0 && fittedRocks > 0, detail);
        }

        private void ReleaseWave()
        {
            foreach (PickUp pickup in pickups) if (pickup != null && pickup.gameObject.activeInHierarchy) ObjectPool.Recycle(pickup.gameObject);
            pickups.Clear();
        }

        public void Dispose() { ReleaseWave(); }
    }
}
