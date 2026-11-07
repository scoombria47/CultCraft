using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace CultCraft
{
    /// <summary>Read-only, in-memory access to the owner's installed Java client assets.</summary>
    public sealed class MinecraftAssets : IDisposable
    {
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.Ordinal);
        private ZipArchive archive;
        private FileStream clientFile;
        private bool disposed;
        private InstalledSounds sounds;
        private MinecraftClientSources optionalSources;
        private readonly Dictionary<string,string> entrySources=new Dictionary<string,string>(StringComparer.Ordinal);

        public bool Ready { get; private set; }
        public string Version { get; private set; }
        public string ClientJar { get; private set; }
        public string Error { get; private set; }
        public Texture2D PlayerSkin { get; private set; }
        public int SourceRevision { get { return optionalSources==null?0:optionalSources.Revision; } }
        public string OptionalSourceStatus { get { return optionalSources==null?"not prepared":optionalSources.Status; } }

        public MinecraftAssets(string mcFolder)
        {
            Version = "unavailable";
            try
            {
                SourceRow source = Spec.Source("minecraft_java");
                foreach(AssetRow row in Spec.AssetRows)entrySources[row.Entry]=String.IsNullOrEmpty(row.SourceJarVersion)?"primary":row.SourceJarVersion;
                var folders = new List<string>();
                if (!String.IsNullOrWhiteSpace(mcFolder)) folders.Add(mcFolder);
                string companion = Environment.GetEnvironmentVariable(source.CompanionEnvironment);
                if (!String.IsNullOrWhiteSpace(companion)) folders.Add(companion);
                // Melty can identify the launcher's program folder rather than its separate data folder.
                folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"));
                var candidates = new List<string>();
                var rejections = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string folder in folders)
                {
                    try { AddClientCandidates(Path.GetFullPath(folder), source.PreferredVersion, candidates, seen); }
                    catch (Exception ex) { rejections.Add(folder + ": " + ex.Message); }
                }
                foreach (string candidate in candidates)
                {
                    try
                    {
                        clientFile = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        archive = new ZipArchive(clientFile, ZipArchiveMode.Read, false);
                        // Compatibility means the whole declared asset set, including late-game materials.
                        foreach (AssetRow row in Spec.AssetRows)
                        {
                            if(!String.IsNullOrEmpty(row.SourceJarVersion)&&row.SourceJarVersion!="primary")continue;
                            if (archive.GetEntry(row.Entry) == null) throw new InvalidDataException("Required Minecraft asset is missing: " + row.Id + " (" + row.Entry + ").");
                            if (row.Entry.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && Texture(row.Entry) == null) throw new InvalidDataException("Required Minecraft texture could not be decoded: " + row.Id + ".");
                        }
                        string skin = Spec.Asset("steve").Entry;
                        // Require the modern 64x64 wide skin: the UV layout below is exact for this format.
                        PlayerSkin = Texture(skin);
                        int skinSize = (int)Spec.Visual("skin_size").Value;
                        if (PlayerSkin == null || PlayerSkin.width != skinSize || PlayerSkin.height != skinSize) throw new InvalidDataException("The installed Steve texture does not match the declared " + skinSize + "x" + skinSize + " wide-player UV layout.");
                        ClientJar = candidate;
                        Version = Path.GetFileNameWithoutExtension(candidate);
                        DirectoryInfo versionFolder=Directory.GetParent(candidate);
                        if(versionFolder!=null&&versionFolder.Parent!=null&&versionFolder.Parent.Name=="versions"&&versionFolder.Parent.Parent!=null)folders.Add(versionFolder.Parent.Parent.FullName);
                        optionalSources=new MinecraftClientSources(folders);
                        try { sounds = new InstalledSounds(candidate); } catch { sounds = null; }
                        Error = null;
                        Ready = true;
                        return;
                    }
                    catch (Exception ex)
                    {
                        rejections.Add(candidate + ": " + ex.Message);
                        CloseArchive();
                        ClearTextures();
                    }
                }
                Error = "No compatible installed Minecraft Java client was found. Searched: " + String.Join("; ", folders.ToArray()) + ".";
                if (rejections.Count > 0) Error += " Rejected clients: " + String.Join("; ", rejections.ToArray());
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                CloseArchive();
                ClearTextures();
            }
        }

        private static void AddClientCandidates(string folder, string preferredVersion, List<string> candidates, HashSet<string> seen)
        {
            if (File.Exists(folder) && String.Equals(Path.GetExtension(folder), ".jar", StringComparison.OrdinalIgnoreCase))
            {
                AddCandidate(folder, candidates, seen);
                return;
            }
            if (!Directory.Exists(folder)) return;
            AddCandidate(Path.Combine(folder, Path.GetFileName(folder) + ".jar"), candidates, seen);
            string versions = Path.Combine(folder, "versions");
            if (!Directory.Exists(versions)) return;
            AddCandidate(Path.Combine(versions, preferredVersion, preferredVersion + ".jar"), candidates, seen);
            foreach (string versionFolder in Directory.GetDirectories(versions).OrderByDescending(VersionRank).ThenByDescending(x => x, StringComparer.Ordinal))
            {
                AddCandidate(Path.Combine(versionFolder, Path.GetFileName(versionFolder) + ".jar"), candidates, seen);
            }
        }

        private static void AddCandidate(string candidate, List<string> candidates, HashSet<string> seen)
        {
            if (File.Exists(candidate) && seen.Add(candidate)) candidates.Add(candidate);
        }

        private static long VersionRank(string path)
        {
            string[] components = Path.GetFileName(path).Split('.');
            int major, minor, patch;
            if (components.Length < 2 || !Int32.TryParse(components[0], out major) || !Int32.TryParse(components[1], out minor)) return 0;
            patch = 0;
            if (components.Length > 2) Int32.TryParse(components[2], out patch);
            return major * 1000000L + minor * 1000L + patch;
        }

        public Texture2D Texture(string entry)
        {
            if (disposed || archive == null || String.IsNullOrWhiteSpace(entry)) return null;
            if (entry == "grass") return TintedTexture("grass_raw", new Color(0.48f, 0.74f, 0.29f, 1f));
            if (entry == "chorus_lime") return RecolouredResource("chorus_lime", "chorus_fruit");
            if (entry == "spider_eye_red") return RecolouredResource("spider_eye_red", "spider_eye");
            if (entry == "rainbow_dye") return RecolouredResource("rainbow_dye", "lime_dye");
            if (entry == "black_shulker_box") return BlackShulkerIcon();
            if (!entry.StartsWith("assets/", StringComparison.Ordinal))
            {
                // The design sheet owns asset identifiers; paths remain supported for callers reading client data.
                foreach (AssetRow row in Spec.AssetRows)
                {
                    if (row.Id == entry) { entry = row.Entry; break; }
                }
            }
            if (!entry.StartsWith("assets/", StringComparison.Ordinal))
            {
                entry = entry.Replace("minecraft:", "");
                entry = "assets/minecraft/textures/" + entry;
                if (!entry.EndsWith(".png", StringComparison.Ordinal)) entry += ".png";
            }
            Texture2D result;
            if (textures.TryGetValue(entry, out result)) return result;
            byte[] png = ReadBytes(entry);
            if (png == null) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.name = "CultCraft local Minecraft: " + entry;
            if (!ImageConversion.LoadImage(texture, png, false) || texture.width > 4096 || texture.height > 4096)
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            textures.Add(entry, texture);
            return texture;
        }

        public string ReadText(string entry)
        {
            if (disposed || archive == null || String.IsNullOrWhiteSpace(entry)) return null;
            string result;
            if (texts.TryGetValue(entry, out result)) return result;
            byte[] bytes = ReadBytes(entry);
            if (bytes == null) return null;
            result = System.Text.Encoding.UTF8.GetString(bytes);
            texts.Add(entry, result);
            return result;
        }

        public bool IsSourceReady(string version){return !disposed&&(version=="primary"?Ready:optionalSources!=null&&optionalSources.Archive(version)!=null);}
        public bool IsAssetReady(string assetId)
        {
            if(disposed||archive==null)return false;
            string entry=assetId;
            foreach(AssetRow row in Spec.AssetRows)if(row.Id==assetId){entry=row.Entry;break;}
            ZipArchive source=ArchiveForEntry(entry);
            return source!=null&&source.GetEntry(entry)!=null;
        }
        public IEnumerator PrepareOptionalSources(ManualLogSource logger)
        {
            if(!disposed&&Ready&&optionalSources!=null)yield return optionalSources.Prepare(logger);
        }

        public AudioClip Sound(string id) { return sounds == null ? null : sounds.Get(id); }
        public bool TryPlaySound(string id,float volume) { return sounds!=null && sounds.Play(id,volume); }
        public bool TryPlaySound(string id,float volume,float pitch) { return sounds!=null && sounds.Play(id,volume,pitch); }
        public float LastSoundPitch { get { return sounds==null?1f:sounds.LastPitch; } }
        internal PreparedSoundPlayback PrepareSoundPlayback(string id) { return sounds==null?null:sounds.Prepare(id); }
        public int SoundsPlayed { get { return sounds==null?0:sounds.PlayedCount; } }
        public string LastSoundPlayback { get { return sounds==null?"unavailable":sounds.LastPlayback; } }
        public string SoundStatus { get { return sounds == null ? "unavailable" : sounds.Status; } }
        public void TickSounds() { if (sounds != null) sounds.Tick(); }

        internal Texture2D BroomTexture(BroomVisual broom)
        {
            if (broom.BundleSource == null) return Texture(broom.Id);
            Texture2D result;
            if (textures.TryGetValue(broom.Id, out result)) return result;
            Texture2D stem = Texture(broom.BundleSource);
            Texture2D stringTexture = Texture("broom_string_source");
            if (stem == null || stringTexture == null) return null;
            result = broom.Bundle(stem, stringTexture);
            textures.Add(broom.Id, result);
            return result;
        }

        private Texture2D RecolouredResource(string id,string sourceId)
        {
            string key="cultcraft-resource-recolour:"+id;Texture2D result;
            if(textures.TryGetValue(key,out result))return result;
            Texture2D source=Texture(sourceId);if(source==null)return null;
            Color[] pixels=source.GetPixels();
            int diagonalMin=Int32.MaxValue,diagonalMax=Int32.MinValue;
            for(int y=0;y<source.height;y++)for(int x=0;x<source.width;x++)if(pixels[y*source.width+x].a>0f)
            {int diagonal=x+source.height-1-y;diagonalMin=Math.Min(diagonalMin,diagonal);diagonalMax=Math.Max(diagonalMax,diagonal);}
            Color[] rainbow={new Color(0.1f,0.85f,0.15f),new Color(0.1f,0.85f,1f),new Color(0.15f,0.25f,1f),new Color(0.6f,0.15f,0.9f),new Color(1f,0.3f,0.7f),new Color(1f,0.1f,0.08f),new Color(1f,0.5f,0.05f),new Color(1f,0.9f,0.1f)};
            for(int y=0;y<source.height;y++)for(int x=0;x<source.width;x++)
            {
                int index=y*source.width+x;Color original=pixels[index];if(original.a==0f)continue;
                float hue,saturation,value;Color.RGBToHSV(original,out hue,out saturation,out value);
                if(id=="chorus_lime")pixels[index]=Color.HSVToRGB(0.25f,Mathf.Max(0.5f,saturation),Mathf.Clamp01(value*1.15f));
                else if(id=="spider_eye_red")pixels[index]=Color.HSVToRGB(0f,Mathf.Max(0.8f,saturation),Mathf.Clamp01(value*1.4f));
                else
                {
                    // Unity pixels start at bottom-left; creator's diagonal starts at top-left.
                    // Span the visible sprite, not its transparent canvas corners,
                    // otherwise green/cyan disappear entirely from the dye silhouette.
                    float t=(x+source.height-1-y-diagonalMin)/(float)Math.Max(1,diagonalMax-diagonalMin);
                    int band=Mathf.Min(rainbow.Length-1,Mathf.FloorToInt(t*rainbow.Length));
                    pixels[index]=rainbow[band]*Mathf.Lerp(0.3f,1f,value);
                }
                pixels[index].a=original.a;
            }
            result=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false){name="CultCraft recoloured "+id,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            result.SetPixels(pixels);result.Apply();textures.Add(key,result);return result;
        }

        public Texture2D TintedTexture(string id, Color tint)
        {
            string key="cultcraft-tint:"+id; Texture2D result;
            if(textures.TryGetValue(key,out result))return result;
            Texture2D source=Texture(id);if(source==null)return null;
            result=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false);
            Color[] pixels=source.GetPixels();for(int i=0;i<pixels.Length;i++)pixels[i]*=tint;
            result.SetPixels(pixels);result.Apply();result.filterMode=FilterMode.Point;
            textures.Add(key,result);return result;
        }

        internal Texture2D GrayTexture(string id)
        {
            string key="cultcraft-grey:"+id;
            Texture2D result;
            if(textures.TryGetValue(key,out result))return result;
            Texture2D source=Texture(id);
            if(source==null)return null;
            Color32[] pixels=source.GetPixels32();
            for(int i=0;i<pixels.Length;i++)
            {
                Color32 c=pixels[i];
                byte brightness=(byte)((54*c.r+183*c.g+19*c.b)/256);
                pixels[i]=new Color32(brightness,brightness,brightness,c.a);
            }
            result=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            result.SetPixels32(pixels);result.Apply();textures.Add(key,result);
            return result;
        }

        public Texture2D OpaqueFoliage(string id, Color tint)
        {
            string key="cultcraft-solid-foliage:"+id; Texture2D result;
            if(textures.TryGetValue(key,out result))return result;
            Texture2D source=TintedTexture(id,tint);if(source==null)return null;
            Color[] pixels=source.GetPixels();Color fill=Color.black;int visible=0;
            foreach(Color pixel in pixels)if(pixel.a>0.2f){fill+=pixel;visible++;}
            if(visible>0)fill/=visible;fill*=0.6f;fill.a=1f;
            for(int i=0;i<pixels.Length;i++){if(pixels[i].a<=0.2f)pixels[i]=fill;else pixels[i].a=1f;}
            result=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false){name="CultCraft fast opaque Minecraft foliage",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            result.SetPixels(pixels);result.Apply();textures.Add(key,result);return result;
        }

        public Texture2D HeldBlockTexture(string id)
        {
            if(id=="black_shulker_box")return BlackShulkerAtlas();
            string key="cultcraft-held-block:"+id;Texture2D result;
            if(textures.TryGetValue(key,out result))return result;
            Texture2D side=Texture(id),top=(id.EndsWith("_log",StringComparison.Ordinal)||id=="pumpkin"||id=="melon")?Texture(id+"_top"):side;
            if(side==null||top==null||side.width!=top.width||side.height!=top.height)throw new InvalidDataException("Installed held block faces are unavailable: "+id);
            result=new Texture2D(side.width,side.height*3,TextureFormat.RGBA32,false){name="CultCraft RAM Minecraft held block "+id,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            result.SetPixels(0,0,side.width,side.height,side.GetPixels());
            result.SetPixels(0,side.height,side.width,side.height,top.GetPixels());
            result.SetPixels(0,side.height*2,side.width,side.height,top.GetPixels());
            result.Apply();textures.Add(key,result);return result;
        }

        private Texture2D BlackShulkerAtlas()
        {
            const string key="cultcraft-held-block:black_shulker_box";Texture2D result;
            if(textures.TryGetValue(key,out result))return result;
            Texture2D entity=Texture("black_shulker_entity"),top=Texture("black_shulker_top");
            if(entity==null||top==null)return null;
            // Actual 1.21 ShulkerModel.createBodyLayer: lid16x12x16 UV0,0;
            // base16x8x16 UV0,28. Closed lid hides the upper four base rows.
            result=new Texture2D(32,96,TextureFormat.RGBA32,false){name="CultCraft closed black shulker faces",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                int sx=x/2,sy=y/2,sideRow=sy<4?51-sy:31-sy;
                result.SetPixel(x,y,entity.GetPixel(16+sx,63-sideRow));
                result.SetPixel(x,y+32,top.GetPixel(sx,sy));
                result.SetPixel(x,y+64,entity.GetPixel(32+sx,20+sy));
            }
            result.Apply();textures.Add(key,result);return result;
        }

        private Texture2D BlackShulkerIcon()
        {
            const string key="cultcraft-isometric:black_shulker_box";Texture2D result;
            if(textures.TryGetValue(key,out result))return result;
            Texture2D atlas=BlackShulkerAtlas();if(atlas==null)return null;
            result=new Texture2D(32,32,TextureFormat.RGBA32,false){name="CultCraft black shulker cube icon",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            result.SetPixels(new Color[32*32]);
            IconFace(result,atlas,new Vector2(16,2),new Vector2(14,7),new Vector2(-14,7),1,1f);
            IconFace(result,atlas,new Vector2(2,23),new Vector2(14,7),new Vector2(0,-14),0,0.7f);
            IconFace(result,atlas,new Vector2(16,30),new Vector2(14,-7),new Vector2(0,-14),0,0.9f);
            result.Apply();textures.Add(key,result);return result;
        }
        private static void IconFace(Texture2D icon,Texture2D atlas,Vector2 origin,Vector2 axisU,Vector2 axisV,int tile,float shade)
        {
            float determinant=axisU.x*axisV.y-axisU.y*axisV.x;
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                Vector2 p=new Vector2(x+0.5f,y+0.5f)-origin;
                float u=(p.x*axisV.y-p.y*axisV.x)/determinant,v=(axisU.x*p.y-axisU.y*p.x)/determinant;
                if(u<0f||u>=1f||v<0f||v>=1f)continue;
                Color c=atlas.GetPixel(Mathf.Min(31,(int)(u*32f)),tile*32+Mathf.Min(31,(int)(v*32f)));
                c.r*=shade;c.g*=shade;c.b*=shade;icon.SetPixel(x,31-y,c);
            }
        }

        private byte[] ReadBytes(string entry)
        {
            if (entry.IndexOf("..", StringComparison.Ordinal) >= 0) return null;
            ZipArchive source=ArchiveForEntry(entry);
            if(source==null)return null;
            ZipArchiveEntry file = source.GetEntry(entry);
            if (file == null || file.Length > 8 * 1024 * 1024) return null;
            using (Stream input = file.Open())
            using (var output = new MemoryStream((int)file.Length))
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }

        private ZipArchive ArchiveForEntry(string entry)
        {
            string version;
            if(!entrySources.TryGetValue(entry,out version)||version=="primary")return archive;
            return optionalSources==null?null:optionalSources.Archive(version);
        }

        private void CloseArchive()
        {
            if (archive != null) archive.Dispose();
            archive = null;
            if (clientFile != null) clientFile.Dispose();
            clientFile = null;
        }

        private void ClearTextures()
        {
            foreach (Texture2D texture in textures.Values) if (texture != null) UnityEngine.Object.Destroy(texture);
            textures.Clear();
            texts.Clear();
            PlayerSkin = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Ready = false;
            if(optionalSources!=null){optionalSources.Dispose();optionalSources=null;}
            if(sounds!=null){sounds.Dispose();sounds=null;}
            CloseArchive();
            ClearTextures();
        }
    }
}
