using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace CultCraft
{
    internal sealed class MinecraftAssetIndex
    {
        private static readonly Regex Hash = new Regex(@"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> SoundEntries = new HashSet<string>(Spec.SoundSourceRows.Select(row => row.Entry), StringComparer.Ordinal);
        private readonly List<string> roots;
        private readonly string clientJar;
        private Dictionary<string, List<string>> objects;

        internal MinecraftAssetIndex(string clientJar, IEnumerable<string> folders)
        {
            this.clientJar = clientJar;
            DirectoryInfo version = Directory.GetParent(clientJar);
            string clientRoot = version?.Parent?.Name == "versions" ? version.Parent.Parent?.FullName : version?.FullName;
            roots = new[] { clientRoot }.Concat(folders).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal string Find(string entry, long maxSize = Int64.MaxValue)
        {
            if (objects == null) LoadIndexes();
            if (!objects.TryGetValue(entry, out List<string> paths)) return null;
            foreach (string path in paths)
            {
                try
                {
                    var file = new FileInfo(path);
                    if (file.Exists && file.Length <= maxSize) return path;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return null;
        }

        internal string FindLegacy(string path) => roots.Select(root => Path.Combine(root, path)).FirstOrDefault(File.Exists);

        private void LoadIndexes()
        {
            objects = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            string id = null;
            try
            {
                var metadata = JObject.Parse(File.ReadAllText(Path.ChangeExtension(clientJar, ".json")));
                id = ((metadata["assetIndex"] as JObject)?["id"] as JValue)?.Value as string;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (Newtonsoft.Json.JsonException) { }
            foreach (string root in roots)
            {
                string folder = Path.Combine(root, "assets", "indexes");
                if (!Directory.Exists(folder)) continue;
                string preferred = !String.IsNullOrEmpty(id) && Path.GetFileName(id) == id ? Path.Combine(folder, id + ".json") : null;
                try
                {
                    foreach (string file in Directory.GetFiles(folder, "*.json").OrderByDescending(path => path == preferred).ThenBy(path => path, StringComparer.Ordinal))
                    {
                        try
                        {
                            if (new FileInfo(file).Length > 16 * 1024 * 1024) continue;
                            if (!(JObject.Parse(File.ReadAllText(file))["objects"] is JObject entries)) continue;
                            foreach (JProperty entry in entries.Properties())
                            {
                                // Other Minecraft assets are read from the verified client archive.
                                if (!entry.Name.StartsWith("minecraft/lang/", StringComparison.Ordinal) &&
                                    !entry.Name.StartsWith("minecraft/font/", StringComparison.Ordinal) && !SoundEntries.Contains(entry.Name)) continue;
                                string hash = ((entry.Value as JObject)?["hash"] as JValue)?.Value as string;
                                if (hash == null || !Hash.IsMatch(hash)) continue;
                                if (!objects.TryGetValue(entry.Name, out List<string> paths)) objects.Add(entry.Name, paths = new List<string>());
                                string path = Path.Combine(root, "assets", "objects", hash.Substring(0, 2), hash);
                                if (!paths.Contains(path)) paths.Add(path);
                            }
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                        catch (Newtonsoft.Json.JsonException) { }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
