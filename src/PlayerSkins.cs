using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace CultCraft
{
    // Public Mojang profiles only; never asks for credentials or changes an account.
    internal sealed class PlayerSkins : IDisposable
    {
        internal sealed class Choice
        {
            public string Nickname = "";
            public string StatusKey;
            public string Status => StatusKey == null ? Nickname : Text(StatusKey);
            public int Arms; // 0 auto, 1 classic, 2 slim
            public Texture2D Texture;
            public bool SourceSlim;
            public int Revision, Request;
            internal ConfigEntry<string> NickConfig;
            internal ConfigEntry<int> ArmsConfig;
            public bool Slim { get { return Arms == 2 || Arms == 0 && SourceSlim; } }
        }
        private readonly MonoBehaviour host;
        private readonly ManualLogSource logger;
        private readonly Choice[] choices = new Choice[2];
        private readonly Dictionary<string, Tuple<Texture2D,bool,string>> cache = new Dictionary<string, Tuple<Texture2D,bool,string>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Texture2D> owned = new List<Texture2D>();
        private readonly List<UnityWebRequest> requests = new List<UnityWebRequest>();
        private readonly MinecraftAssets assets;
        private bool disposed;
        public PlayerSkins(MonoBehaviour host, MinecraftAssets assets, ConfigFile config, ManualLogSource logger)
        {
            this.host=host;this.assets=assets;this.logger=logger;
            for(int i=0;i<2;i++)
            {
                Choice c=choices[i]=new Choice();
                string role=i==0?"Lamb skin":"Goat skin";
                c.NickConfig=config.Bind(role,"Nickname","","Public Minecraft Java nickname; empty uses Steve/Alex.");
                c.ArmsConfig=config.Bind(role,"Arms",0,new ConfigDescription("0 Auto, 1 Classic (4 pixels), 2 Slim (3 pixels).",new AcceptableValueRange<int>(0,2)));
                c.Arms=c.ArmsConfig.Value;Default(i);
                string saved=c.NickConfig.Value;
                if(!String.IsNullOrEmpty(saved)) Apply(i,saved);
            }
        }
        public Choice Get(int role){return choices[Mathf.Clamp(role,0,1)];}
        public static int Role(PlayerFarming player){return player!=null&&(player.IsGoat||!player.isLamb||player.playerID!=0)?1:0;}
        private void Default(int role)
        {
            Choice c=Get(role);c.Texture=role==0?assets.PlayerSkin:assets.Texture("assets/minecraft/textures/entity/player/slim/alex.png");
            if(c.Texture==null)throw new InvalidOperationException("Installed Minecraft Alex skin is unavailable.");
            c.SourceSlim=role==1;c.Nickname="";c.StatusKey=role==0?"Steve":"Alex";c.Revision++;
        }
        public void SetArms(int role,int value){Choice c=Get(role);c.Arms=Mathf.Clamp(value,0,2);c.ArmsConfig.Value=c.Arms;c.Revision++;}
        public void Reset(int role){Choice c=Get(role);c.Request++;Default(role);c.NickConfig.Value="";c.Arms=0;c.ArmsConfig.Value=0;}
        public void Apply(int role,string nickname)
        {
            Choice c=Get(role);nickname=(nickname??"").Trim();
            if(!Regex.IsMatch(nickname,"^[A-Za-z0-9_]{1,16}$")){c.StatusKey="invalid";return;}
            int request=++c.Request;c.StatusKey="loading";
            host.StartCoroutine(Load(role,nickname,request));
        }
        private bool Current(Choice c,int request){return !disposed&&c.Request==request;}
        private IEnumerator Load(int role,string nickname,int sequence)
        {
            Choice c=Get(role);Tuple<Texture2D,bool,string> ready;
            if(!cache.TryGetValue(nickname,out ready))
            {
                string id=null,error=null;
                using(UnityWebRequest web=Open("https://api.minecraftservices.com/minecraft/profile/lookup/name/"+nickname))
                {
                    yield return web.SendWebRequest();requests.Remove(web);
                    if(!Current(c,sequence))yield break;
                    try {Check(web);id=(string)JObject.Parse(web.downloadHandler.text)["id"];if(id==null||!Regex.IsMatch(id,"^[a-fA-F0-9]{32}$"))throw new Exception("Invalid profile.");}
                    catch(Exception ex){error=ex.Message;}
                }
                if(error!=null){Fail(c,error);yield break;}
                string url=null;bool slim=false;
                using(UnityWebRequest web=Open("https://sessionserver.mojang.com/session/minecraft/profile/"+id))
                {
                    yield return web.SendWebRequest();requests.Remove(web);
                    if(!Current(c,sequence))yield break;
                    try
                    {
                        Check(web);JObject profile=JObject.Parse(web.downloadHandler.text);string encoded=null;
                        foreach(JToken property in profile["properties"]??new JArray())if((string)property["name"]=="textures")encoded=(string)property["value"];
                        JObject data=JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded??"")));
                        JToken skin=data["textures"]?["SKIN"];url=(string)skin?["url"];slim=(string)skin?["metadata"]?["model"]=="slim";
                        Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Host!="textures.minecraft.net"||!Regex.IsMatch(uri.AbsolutePath,"^/texture/[a-fA-F0-9]{32,64}$"))throw new Exception("Profile has no supported skin texture.");
                        url="https://textures.minecraft.net"+uri.AbsolutePath;
                    }
                    catch(Exception ex){error=ex.Message;}
                }
                if(error!=null){Fail(c,error);yield break;}
                Texture2D texture=null;
                using(UnityWebRequest web=Open(url))
                {
                    yield return web.SendWebRequest();requests.Remove(web);
                    if(!Current(c,sequence))yield break;
                    try
                    {
                        Check(web);byte[] png=web.downloadHandler.data;
                        // Check dimensions before handing data to Unity's image decoder.
                        if(png.Length<24||png.Length>1048576||png[0]!=137||png[1]!=80||png[2]!=78||png[3]!=71||ReadInt(png,16)!=64||(ReadInt(png,20)!=64&&ReadInt(png,20)!=32))throw new Exception("Unsupported skin image.");
                        texture=new Texture2D(64,64,TextureFormat.RGBA32,false);
                        if(!ImageConversion.LoadImage(texture,png,false))throw new Exception("Skin PNG could not be decoded.");
                        texture=Normalize(texture);texture.name="CultCraft public skin "+nickname;texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;owned.Add(texture);
                    }
                    catch(Exception ex){if(texture!=null)UnityEngine.Object.Destroy(texture);error=ex.Message;}
                }
                if(error!=null){Fail(c,error);yield break;}
                ready=Tuple.Create(texture,slim,nickname);cache[nickname]=ready;
            }
            if(!Current(c,sequence))yield break;
            c.Texture=ready.Item1;c.SourceSlim=ready.Item2;c.Nickname=ready.Item3;c.StatusKey=null;c.NickConfig.Value=c.Nickname;c.Revision++;
            logger.LogInfo("Public Minecraft skin applied: role="+role+", nickname="+c.Nickname+", sourceSlim="+c.SourceSlim+", arms="+c.Arms);
        }
        private UnityWebRequest Open(string url){var web=UnityWebRequest.Get(url);web.timeout=15;requests.Add(web);return web;}
        private static void Check(UnityWebRequest web){if(web.result!=UnityWebRequest.Result.Success)throw new Exception("HTTP "+web.responseCode+": "+web.error);if(web.downloadedBytes>1048576)throw new Exception("Oversized response.");}
        private void Fail(Choice c,string error){c.StatusKey="failed";logger.LogWarning("Public skin lookup failed; keeping previous skin: "+error);}
        private static int ReadInt(byte[] b,int i){return b[i]<<24|b[i+1]<<16|b[i+2]<<8|b[i+3];}
        private static Texture2D Normalize(Texture2D source)
        {
            if(source.height==64)return source;
            var target=new Texture2D(64,64,TextureFormat.RGBA32,false);target.SetPixels32(new Color32[4096]);
            target.SetPixels(0,32,64,32,source.GetPixels());
            // Legacy right limbs are reflected into modern independent left-limb UV islands.
            Copy(target,4,16,20,48,4,4);Copy(target,8,16,24,48,4,4);
            Copy(target,0,20,24,52,4,12);Copy(target,4,20,20,52,4,12);Copy(target,8,20,16,52,4,12);Copy(target,12,20,28,52,4,12);
            Copy(target,44,16,36,48,4,4);Copy(target,48,16,40,48,4,4);
            Copy(target,40,20,40,52,4,12);Copy(target,44,20,36,52,4,12);Copy(target,48,20,32,52,4,12);Copy(target,52,20,44,52,4,12);
            target.Apply();UnityEngine.Object.Destroy(source);return target;
        }
        private static void Copy(Texture2D t,int sx,int sy,int dx,int dy,int w,int h){for(int y=0;y<h;y++)for(int x=0;x<w;x++)t.SetPixel(dx+x,63-dy-y,t.GetPixel(sx+w-1-x,63-sy-y));}
        public static string Text(string key)
        {
            string lang=MinecraftLocalization.UiLanguageCode.Split('_')[0];
            string[] keys={"role","lamb","goat","nickname","apply","reset","arms","auto","classic","slim","loading","failed","invalid"};
            string[] en={"Skin","Lamb","Goat","Minecraft nickname","Apply","Reset skin","Arms","Auto","Classic","Slim","Loading…","Could not load skin","Use 1–16 letters, digits or _"};
            string[] ru={"Скин","Ягнёнок","Козёл","Ник Minecraft","Применить","Сбросить скин","Руки","Авто","Обычные","Тонкие","Загрузка…","Не удалось загрузить скин","1–16 латинских букв, цифр или _"};
            string[] uk={"Скін","Ягня","Козел","Нік Minecraft","Застосувати","Скинути скін","Руки","Авто","Звичайні","Тонкі","Завантаження…","Не вдалося завантажити скін","1–16 латинських літер, цифр або _"};
            int i=Array.IndexOf(keys,key);return i<0?key:MinecraftLocalization.TranslateOriginal((lang=="ru"?ru:lang=="uk"?uk:en)[i]);
        }
        public void Dispose(){disposed=true;foreach(var web in requests){web.Abort();web.Dispose();}requests.Clear();foreach(var t in owned)UnityEngine.Object.Destroy(t);owned.Clear();}
    }
}
