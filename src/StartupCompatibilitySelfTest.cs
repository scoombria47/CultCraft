using System;
using System.Collections;
using System.IO;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;

namespace CultCraft
{
    internal static class StartupCompatibilitySelfTest
    {
        internal const string Id = "minecraft-startup-fallback";
        private const string FontEntry = "minecraft/font/unifont.zip";
        private const long FontLimit = 16 * 1024 * 1024;

        internal static IEnumerator Run(MinecraftAssets activeAssets, ManualLogSource logger)
        {
            string source = activeAssets.VerifiedOptionalJar("26.3");
            if (source == null)
            {
                MeltyTest.Report(Id, false, "The verified Minecraft 26.3 client is unavailable for the fixture.");
                yield break;
            }

            string sandbox = Path.Combine(Path.GetTempPath(), "CultCraft-startup-" + Guid.NewGuid().ToString("N"));
            string clientRoot = Path.Combine(sandbox, "client");
            string client = Path.Combine(clientRoot, "versions", "26.3", "26.3.jar");
            string cache = Path.Combine(sandbox, "cache");
            string cachedJar = Path.Combine(cache, Spec.Source("minecraft_java").PreferredVersion + "-client.jar");
            string indexPath = Path.Combine(clientRoot, "assets", "indexes", "fixture.json");
            string font = activeAssets.FindLocalIndexedObject(FontEntry, FontLimit);
            string stagedFont = font == null ? null : Path.Combine(clientRoot, "assets", "objects",
                Path.GetFileName(font).Substring(0, 2), Path.GetFileName(font));

            try
            {
                string setupError = null;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(client));
                    File.Copy(source, client);
                    if (stagedFont != null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(stagedFont));
                        File.Copy(font, stagedFont);
                        Directory.CreateDirectory(Path.GetDirectoryName(indexPath));
                        File.WriteAllText(indexPath, new JObject { ["objects"] = new JObject
                        {
                            [FontEntry] = new JObject { ["hash"] = Path.GetFileName(font) }
                        }}.ToString());
                    }
                }
                catch (IOException error) { setupError = error.Message; }
                catch (UnauthorizedAccessException error) { setupError = error.Message; }
                if (setupError != null)
                {
                    MeltyTest.Report(Id, false, "Cannot stage the isolated client: " + setupError);
                    yield break;
                }

                // Use the production loader with only the staged 26.3 client and an empty private cache.
                using (var cold = new MinecraftAssets(clientRoot, useInstalledFolders: false, cacheDirectory: cache))
                {
                    if (cold.Ready || !cold.CanPreparePrimary || File.Exists(cachedJar))
                    {
                        MeltyTest.Report(Id, false, "The isolated 26.3 installation did not enter the cold fallback path: " + cold.Error);
                        yield break;
                    }
                    yield return cold.PreparePrimary(logger);
                    if (!MatchesFallback(cold, client, cachedJar) ||
                        (stagedFont != null && cold.FindLocalIndexedObject(FontEntry, FontLimit) != stagedFont))
                    {
                        MeltyTest.Report(Id, false, "Cold fallback did not load the verified primary archive or the installed font index: " + cold.Error);
                        yield break;
                    }
                }

                DirectoryInfo versions = Directory.GetParent(activeAssets.InstalledClientJar)?.Parent;
                if (versions?.Name == "versions")
                {
                    foreach (string version in new[] { "1.12.2", "1.20.1" })
                    {
                        string legacyClient = Path.Combine(versions.FullName, version, version + ".jar");
                        if (!File.Exists(legacyClient)) continue;
                        using (var legacy = new MinecraftAssets(legacyClient, useInstalledFolders: false, cacheDirectory: cache))
                        {
                            yield return legacy.PreparePrimary(logger);
                            if (!MatchesFallback(legacy, legacyClient, cachedJar))
                            {
                                MeltyTest.Report(Id, false, "Verified warm fallback failed for Minecraft " + version + ": " + legacy.Error);
                                yield break;
                            }
                        }
                        logger.LogInfo("Minecraft startup compatibility: " + version + " reused the verified fallback.");
                    }
                }

                DateTime cachedWrite = File.GetLastWriteTimeUtc(cachedJar);
                using (var warm = new MinecraftAssets(client, useInstalledFolders: false, cacheDirectory: cache))
                {
                    yield return warm.PreparePrimary(logger);
                    if (!MatchesFallback(warm, client, cachedJar) || File.GetLastWriteTimeUtc(cachedJar) != cachedWrite)
                    {
                        MeltyTest.Report(Id, false, "Warm fallback did not reuse the verified cache: " + warm.Error);
                        yield break;
                    }
                }

                logger.LogInfo("Minecraft startup compatibility: 26.3-only cold official download and warm verified reuse passed" +
                    (stagedFont == null ? "." : "; the Unicode font resolved through the installed client's index."));
                MeltyTest.Report(Id, true, null);
            }
            finally
            {
                DeleteFixtureFile(stagedFont, sandbox);
                DeleteFixtureFile(indexPath, sandbox);
                DeleteFixtureFile(client, sandbox);
                DeleteFixtureFile(cachedJar, sandbox);
                if (Directory.Exists(sandbox) && Directory.GetFileSystemEntries(sandbox).Length == 0) Directory.Delete(sandbox);
            }
        }

        private static bool MatchesFallback(MinecraftAssets assets, string installed, string cached) =>
            assets.Ready && assets.InstalledClientJar == installed && assets.ClientJar == cached &&
            assets.PlayerSkin != null && assets.PlayerSkin.width == 64 && assets.PlayerSkin.height == 64 &&
            assets.Texture("wooden_sword") != null && assets.Texture("red_bed") != null;

        private static void DeleteFixtureFile(string path, string sandbox)
        {
            if (path == null) return;
            try
            {
                File.Delete(path);
                string directory = Path.GetDirectoryName(path);
                while (directory != sandbox && Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
                {
                    Directory.Delete(directory);
                    directory = Path.GetDirectoryName(directory);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
