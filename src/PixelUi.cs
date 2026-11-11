using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CultCraft
{
    /// <summary>Draws the owner's Minecraft bitmap glyphs without smoothing or an OS font substitution.</summary>
    public sealed class PixelUi : IDisposable
    {
        private static PixelUi shared;
        private readonly MinecraftAssets assets;
        private readonly Dictionary<int,Glyph> glyphs=new Dictionary<int,Glyph>();
        private readonly Dictionary<int,string> unicodeHex=new Dictionary<int,string>();
        private readonly List<Texture2D> generated=new List<Texture2D>();
        private readonly List<int> unicodeGlyphCodes=new List<int>();
        private bool unicodeIndexed;
        private bool japaneseUnicode;
        private GUIStyle fallback;
        public bool BitmapFontReady { get { return glyphs.ContainsKey('A')&&glyphs.ContainsKey('0'); } }
        public static float V(string id){return Spec.Ui(id).Value;}
        public const int DefaultScaleMode = 0;
        public static int ScaleMode { get; internal set; }
        public static bool ShowHints { get; internal set; } = true;
        public static float Scale { get { return ResolveScale(ScaleMode, Screen.width, Screen.height); } }

        internal static float ResolveScale(int mode, int width, int height)
        {
            float original = Mathf.Clamp(Mathf.Floor(height / V("scale_screen_divisor")), V("scale_min"), V("scale_max"));
            if (mode == 1) return original + 1f;
            if (mode == 2) return original;
            if (mode == 3) return Mathf.Max(1f, original - 1f);
            return Mathf.Clamp(Mathf.Floor(Mathf.Min(width / 400f, height / 250f)), 1f, V("scale_max"));
        }
        private sealed class Glyph { public Texture2D Texture; public Rect Uv; public float Width,Height,Advance,Ascent; }

        private PixelUi(MinecraftAssets source)
        {
            assets=source;
            JObject data=JObject.Parse(assets.ReadText(Spec.Asset("mc_font_default_data").Entry));
            foreach(JToken provider in data["providers"])
            {
                if((string)provider["type"]!="bitmap")continue;
                string file=(string)provider["file"];
                Texture2D texture=assets.Texture("assets/minecraft/textures/"+file.Replace("minecraft:",""));
                JArray rows=provider["chars"] as JArray;
                if(texture==null||rows==null||rows.Count==0)continue;
                var cells=new List<int[]>();int columns=0;
                foreach(JToken row in rows){int[] points=CodePoints((string)row);cells.Add(points);columns=Math.Max(columns,points.Length);}
                int cellWidth=texture.width/columns,cellHeight=texture.height/rows.Count;
                float logicalHeight=(float?)provider["height"]??8f;
                float ascent=(float?)provider["ascent"]??7f;
                Color32[] pixels=texture.GetPixels32();
                for(int y=0;y<cells.Count;y++)for(int x=0;x<cells[y].Length;x++)
                {
                    int code=cells[y][x];if(code==0||glyphs.ContainsKey(code))continue;
                    int right=0;
                    for(int px=0;px<cellWidth;px++)for(int py=0;py<cellHeight;py++)
                        if(pixels[(texture.height-(y+1)*cellHeight+py)*texture.width+x*cellWidth+px].a>16)right=Math.Max(right,px+1);
                    float ratio=logicalHeight/cellHeight;
                    glyphs.Add(code,new Glyph{Texture=texture,Uv=new Rect((float)(x*cellWidth)/texture.width,1f-(float)((y+1)*cellHeight)/texture.height,(float)Math.Max(right,1)/texture.width,(float)cellHeight/texture.height),Width=Math.Max(right,1)*ratio,Height=logicalHeight,Advance=right==0?4f:(float)Math.Floor(right*ratio+0.5f)+1f,Ascent=ascent});
                }
            }
        }

        public static PixelUi Shared(MinecraftAssets source)
        {
            if(shared==null||!ReferenceEquals(shared.assets,source)){if(shared!=null)shared.Dispose();shared=new PixelUi(source);}
            return shared;
        }

        private static int[] CodePoints(string text)
        {
            var points=new List<int>();if(text==null)return points.ToArray();
            for(int i=0;i<text.Length;i++)
            {
                int code=text[i];if(Char.IsHighSurrogate(text[i])&&i+1<text.Length&&Char.IsLowSurrogate(text[i+1]))code=Char.ConvertToUtf32(text[i],text[++i]);
                points.Add(code);
            }
            return points.ToArray();
        }

        private Glyph Get(int code)
        {
            Glyph glyph;if(glyphs.TryGetValue(code,out glyph))return glyph;
            if(!unicodeIndexed)IndexUnicode();
            string hex;
            if(!unicodeHex.TryGetValue(code,out hex))return null;
            int width=hex.Length/16*4; //16 rows; each hex digit encodes four pixels.
            if(width!=8&&width!=16&&width!=24&&width!=32)return glyphs['?'];
            var texture=new Texture2D(width,16,TextureFormat.RGBA32,false){name="CultCraft local Minecraft unicode "+code.ToString("X"),filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color32[width*16];int left=width,right=-1;
            for(int y=0;y<16;y++)for(int x=0;x<width;x++)
            {
                int nibble=Int32.Parse(hex.Substring(y*(width/4)+x/4,1),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
                bool opaque=(nibble&(1<<(3-x%4)))!=0;
                pixels[(15-y)*width+x]=new Color32(255,255,255,opaque?(byte)255:(byte)0);
                if(opaque){left=Math.Min(left,x);right=Math.Max(right,x);}
            }
            if(right<left){left=0;right=7;}
            //Native Minecraft's unihex provider uses full-width boxes for CJK/Hangul.
            if(code>=0x3000&&code<=0xD7FF||code>=0xF900&&code<=0xFAFF){left=0;right=Math.Min(15,width-1);}
            texture.SetPixels32(pixels);texture.Apply();generated.Add(texture);unicodeGlyphCodes.Add(code);
            glyph=new Glyph{Texture=texture,Uv=new Rect((float)left/width,0f,(float)(right-left+1)/width,1f),Width=(right-left+1)*0.5f,Height=8f,Advance=(right-left+1)*0.5f+1f,Ascent=7f};glyphs[code]=glyph;
            return glyph;
        }

        private void IndexUnicode()
        {
            unicodeIndexed=true;
            japaneseUnicode=MinecraftLocalization.LanguageCode.StartsWith("ja",StringComparison.Ordinal);
            try
            {
                // The Japanese archive is an override, not a complete font: kana and
                // most symbols remain in the base archive, exactly as the native providers.
                ReadUnicodeArchive(assets.FindLocalIndexedObject("minecraft/font/unifont.zip", 16 * 1024 * 1024));
                if(japaneseUnicode)ReadUnicodeArchive(assets.FindLocalIndexedObject("minecraft/font/unifont_jp.zip", 16 * 1024 * 1024));
            }
            catch(IOException){}catch(UnauthorizedAccessException){}catch(InvalidDataException){}
        }

        private void ReadUnicodeArchive(string path)
        {
                if(path==null)return;
                using(var file=File.OpenRead(path))using(var zip=new ZipArchive(file,ZipArchiveMode.Read))
                    foreach(ZipArchiveEntry item in zip.Entries)
                    {
                        if(!item.FullName.EndsWith(".hex",StringComparison.Ordinal)||item.Length>16*1024*1024)continue;
                        using(var reader=new StreamReader(item.Open()))
                        {
                            string line;
                            while((line=reader.ReadLine())!=null)
                            {
                                int colon=line.IndexOf(':');int point;
                                if(colon<4||!Int32.TryParse(line.Substring(0,colon),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out point))continue;
                                string bits=line.Substring(colon+1).Trim();
                                if((bits.Length==32||bits.Length==64||bits.Length==96||bits.Length==128)&&System.Text.RegularExpressions.Regex.IsMatch(bits,"\\A[0-9A-Fa-f]+\\z"))unicodeHex[point]=bits;
                            }
                        }
                    }
        }

        public float Measure(string text,float scale)
        {
            if(unicodeIndexed&&japaneseUnicode!=MinecraftLocalization.LanguageCode.StartsWith("ja",StringComparison.Ordinal))
            {
                foreach(int code in unicodeGlyphCodes)glyphs.Remove(code);
                foreach(Texture2D texture in generated)if(texture!=null)UnityEngine.Object.Destroy(texture);
                unicodeGlyphCodes.Clear();generated.Clear();unicodeHex.Clear();unicodeIndexed=false;
            }
            float width=0f;foreach(int code in CodePoints(text)){Glyph glyph=Get(code);width+=(glyph==null?8f:glyph.Advance)*scale;}return Math.Max(0f,width-scale);
        }

        public void Text(Rect area,string text,float scale,Color color,TextAnchor anchor=TextAnchor.MiddleCenter,bool shadow=true)
        {
            if(String.IsNullOrEmpty(text))return;
            int[] points=CodePoints(text);float width=Measure(text,scale);
            float x=anchor==TextAnchor.MiddleRight?area.xMax-width:anchor==TextAnchor.MiddleLeft?area.x:area.center.x-width*0.5f;
            float baseline=area.center.y+3f*scale;
            Color previous=GUI.color;
            foreach(int code in points)
            {
                Glyph glyph=Get(code);
                if(glyph==null)
                {
                    // An incomplete owner's asset installation must remain readable.
                    if(fallback==null)fallback=new GUIStyle(GUI.skin.label){padding=new RectOffset(0,0,0,0),alignment=TextAnchor.MiddleLeft};
                    fallback.font=MinecraftLocalization.UiFont;fallback.fontSize=Mathf.RoundToInt(8f*scale);fallback.normal.textColor=color;
                    GUI.Label(new Rect(x,area.center.y-5f*scale,8f*scale,11f*scale),Char.ConvertFromUtf32(code),fallback);
                    x+=8f*scale;continue;
                }
                Rect rect=new Rect(Mathf.Round(x),Mathf.Round(baseline-glyph.Ascent*scale),glyph.Width*scale,glyph.Height*scale);
                if(shadow){GUI.color=new Color(0f,0f,0f,color.a*0.85f);GUI.DrawTextureWithTexCoords(new Rect(rect.x+scale,rect.y+scale,rect.width,rect.height),glyph.Texture,glyph.Uv,true);}
                GUI.color=color;GUI.DrawTextureWithTexCoords(rect,glyph.Texture,glyph.Uv,true);x+=glyph.Advance*scale;
            }
            GUI.color=previous;
        }

        public void Dispose()
        {
            foreach(Texture2D texture in generated)if(texture!=null)UnityEngine.Object.Destroy(texture);
            generated.Clear();unicodeGlyphCodes.Clear();glyphs.Clear();unicodeHex.Clear();if(ReferenceEquals(shared,this))shared=null;
        }
    }
}
