using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BepInEx.Logging;
using UnityEngine.Networking;

namespace CultCraft
{
    /// <summary>Verified official client archives stored separately from the installed game.</summary>
    internal sealed class MinecraftClientSources : IDisposable
    {
        private sealed class Source : IDisposable
        {
            public string Version, Hash, Url, Path, Status="pending";
            public long Size;
            public readonly List<string> Entries=new List<string>();
            public FileStream File;
            public ZipArchive Archive;
            public bool LoggedFailure;
            public void Dispose(){if(Archive!=null)Archive.Dispose();Archive=null;if(File!=null)File.Dispose();File=null;}
        }
        private readonly Dictionary<string,Source> sources=new Dictionary<string,Source>(StringComparer.Ordinal);
        private readonly List<string> roots;
        private readonly bool primaryOnly;
        private readonly string cacheDirectory;
        private UnityWebRequest activeRequest;
        private string activePartial;
        private bool started,disposed;
        public int Revision { get; private set; }
        public string Status
        {
            get {var values=new List<string>();foreach(Source source in sources.Values)values.Add(source.Version+"="+source.Status);return String.Join(", ",values.ToArray());}
        }

        public MinecraftClientSources(IEnumerable<string> ownerRoots, bool primaryOnly = false, string cacheDirectory = null)
        {
            roots=new List<string>(ownerRoots);
            this.primaryOnly = primaryOnly;
            this.cacheDirectory = cacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CultCraft", "minecraft-assets");
            foreach(AssetRow row in Spec.AssetRows)
            {
                bool primary = String.IsNullOrEmpty(row.SourceJarVersion) || row.SourceJarVersion == "primary";
                if (primary != primaryOnly) continue;
                string version = primary ? Spec.Source("minecraft_java").PreferredVersion : row.SourceJarVersion;
                Source source;
                if(!sources.TryGetValue(version,out source))
                {
                    source=new Source{Version=version};
                    if (primary && version == "1.21")
                    {
                        source.Hash = "0e9a07b9bb3390602f977073aa12884a4ce12431";
                        source.Size = 26836080L;
                        source.Url = "https://piston-data.mojang.com/v1/objects/" + source.Hash + "/client.jar";
                    }
                    else if(source.Version=="26.3")
                    {
                        source.Hash="e877b6a07acd633fb3bb475002175cec036e7b87";
                        source.Size=41483720L;
                        source.Url="https://piston-data.mojang.com/v1/objects/"+source.Hash+"/client.jar";
                    }
                    else if(source.Version=="1.21.2")
                    {
                        source.Hash="c7ac2d0d86f4ca416cab9064ff8a281852ad0c7b";
                        source.Size=27842947L;
                        source.Url="https://piston-data.mojang.com/v1/objects/"+source.Hash+"/client.jar";
                    }
                    else if(source.Version=="1.21.9")
                    {
                        source.Hash="ce92fd8d1b2460c41ceda07ae7b3fe863a80d045";
                        source.Size=30591861L;
                        source.Url="https://piston-data.mojang.com/v1/objects/"+source.Hash+"/client.jar";
                    }
                    sources.Add(source.Version,source);
                }
                source.Entries.Add(row.Entry);
            }
        }

        public ZipArchive Archive(string version){Source source;return !disposed&&sources.TryGetValue(version,out source)?source.Archive:null;}
        public string PreparedPath(string version)
        {
            Source source;
            return !disposed && sources.TryGetValue(version, out source) && source.Archive != null ? source.Path : null;
        }

        private IEnumerable<string> Candidates(Source source)
        {
            string cache=Path.Combine(cacheDirectory,source.Version+"-client.jar");
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            paths.Add(cache);yield return cache;
            foreach(string root in roots)
            {
                foreach(string path in new[]{Path.Combine(root,"versions",source.Version,source.Version+".jar"),Path.Combine(root,source.Version,source.Version+".jar"),Path.Combine(root,source.Version+".jar")})
                    if(paths.Add(path))yield return path;
            }
        }

        private static bool Verified(string path,Source source)
        {
            try
            {
                using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                using(var hash=SHA1.Create())
                    return file.Length==source.Size&&BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").Equals(source.Hash,StringComparison.OrdinalIgnoreCase);
            }
            catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
        }

        private bool Open(Source source,string path)
        {
            try
            {
                source.File=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read | FileShare.Delete);
                source.Archive=new ZipArchive(source.File,ZipArchiveMode.Read,false);
                foreach(string entry in source.Entries)if(source.Archive.GetEntry(entry)==null)throw new InvalidDataException("Declared Minecraft entry is missing: "+entry);
                source.Path=path;source.Status="ready";Revision++;return true;
            }
            catch(Exception){source.Dispose();return false;}
        }

        private void Fail(Source source,ManualLogSource logger,string reason)
        {
            source.Status="unavailable";
            if(!source.LoggedFailure&&logger!=null)logger.LogWarning("Minecraft "+source.Version+(primaryOnly?" resource fallback":" optional resources")+" unavailable: "+reason);
            source.LoggedFailure=true;
        }

        public IEnumerator Prepare(ManualLogSource logger)
        {
            if(disposed||started)yield break;
            started=true;
            // Run only from Plugin's coroutine after its save/test guards exist.
            yield return null;
            foreach(Source source in sources.Values)
            {
                if(disposed)yield break;
                if(String.IsNullOrEmpty(source.Url)){Fail(source,logger,"no verified official source is declared");continue;}
                bool ready=false;
                source.Status="checking local client";
                foreach(string path in Candidates(source))
                {
                    if(!System.IO.File.Exists(path))continue;
                    string candidate=path;
                    Task<bool> check=System.Threading.Tasks.Task.Run(()=>Verified(candidate,source));
                    while(!disposed&&!check.IsCompleted)yield return null;
                    if(disposed)yield break;
                    if(!check.IsFaulted&&check.Result&&Open(source,path)){ready=true;break;}
                }
                if(ready)continue;
                string cache=Path.Combine(cacheDirectory,source.Version+"-client.jar");
                string setupError=null;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cache));
                    activePartial=cache+"."+Guid.NewGuid().ToString("N")+".download";
                    var handler=new DownloadHandlerFile(activePartial){removeFileOnAbort=true};
                    activeRequest=new UnityWebRequest(source.Url,"GET"){downloadHandler=handler,timeout=120,redirectLimit=4};
                    activeRequest.SendWebRequest();source.Status="downloading official client";
                }
                catch(Exception error){setupError=error.Message;}
                if(setupError!=null){ClearPending();Fail(source,logger,setupError);continue;}
                while(!disposed&&!activeRequest.isDone)yield return null;
                if(disposed){ClearPending();yield break;}
                if(activeRequest.result!=UnityWebRequest.Result.Success)
                {
                    string error=activeRequest.error;ClearPending();Fail(source,logger,error);continue;
                }
                activeRequest.Dispose();activeRequest=null;
                source.Status="verifying official client";
                string partial=activePartial;
                Task<bool> verify=System.Threading.Tasks.Task.Run(()=>Verified(partial,source));
                while(!disposed&&!verify.IsCompleted)yield return null;
                if(disposed){ClearPending();yield break;}
                if(verify.IsFaulted||!verify.Result){ClearPending();Fail(source,logger,"official client size/SHA-1 verification failed");continue;}
                string commitError=null;
                try
                {
                    // Same-directory move/replace commits only a fully verified file.
                    if(System.IO.File.Exists(cache))System.IO.File.Replace(partial,cache,null);
                    else System.IO.File.Move(partial,cache);
                    activePartial=null;
                }
                catch(Exception error){commitError=error.Message;}
                if(commitError!=null)
                {
                    ClearPending();
                    // Another game instance can populate the shared cache during this download.
                    Task<bool> check=System.Threading.Tasks.Task.Run(()=>Verified(cache,source));
                    while(!disposed&&!check.IsCompleted)yield return null;
                    if(disposed)yield break;
                    if(check.IsFaulted||!check.Result){Fail(source,logger,commitError);continue;}
                }
                if(!Open(source,cache))Fail(source,logger,"verified client does not contain its declared entries");
                else if(logger!=null)logger.LogInfo("Minecraft "+source.Version+" assets ready from verified CultCraft cache.");
            }
        }

        private void ClearPending()
        {
            if(activeRequest!=null){activeRequest.Abort();activeRequest.Dispose();activeRequest=null;}
            if(activePartial!=null){try{System.IO.File.Delete(activePartial);}catch(IOException){}catch(UnauthorizedAccessException){}activePartial=null;}
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;ClearPending();foreach(Source source in sources.Values)source.Dispose();sources.Clear();
        }
    }
}
