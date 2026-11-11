using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CultCraft
{
    /// <summary>Pixel-aligned camera options using the owner's real Minecraft widget sprites.</summary>
    internal sealed partial class MinecraftSettingsUi : IDisposable
    {
        private readonly MinecraftAssets assets;
        private PixelUi pixels;
        private Texture2D tiledBackground;
        private Widget button,slider,handle;
        private readonly Rect[] sliders=new Rect[2];
        private float scale;
        private int focus,dragging=-1,pressedButton=-1;
        private int inputFrame=-1,pointerEventFrame=-1,keyEventFrame=-1,renderFrame=-1;
        private int controlId;
        private bool close;
        private bool testInput;
        private Vector2 pointer;
        private string styleError;
        internal PlayerSkins Skins;
        private int skinRole;
        internal int SelectedSkinRole { get { return skinRole; } }
        private string nickname="";
        private Rect roleRect,nicknameRect,applyRect,skinResetRect,armsRect,statusRect,languageRect;
        private const int LanguageControl = 11;
        private static readonly int[] FocusOrder = { 0, 1, 6, 10, 7, 8, 9, LanguageControl, 2, 3, 4, 5 };
        private static readonly int[] FocusOrderWithoutSkins = { 0, 1, LanguageControl, 2, 3, 4, 5 };
        private bool Editing,selectAll;
        private Keyboard textKeyboard;
        internal Rect NicknameRect { get { return nicknameRect; } }
        internal Rect ApplySkinRect { get { return applyRect; } }
        internal Rect SkinRoleRect { get { return roleRect; } }
        internal Rect SkinResetRect { get { return skinResetRect; } }
        internal Rect SkinArmsRect { get { return armsRect; } }
        private void Append(char c){if(!Editing||Char.IsControl(c))return;if(selectAll){nickname="";selectAll=false;}if(nickname.Length<16)nickname+=c;}
        public Rect WindowRect { get; private set; }
        public Rect[] SliderRects { get { return sliders; } }
        public Rect ResetRect { get; private set; }
        public Rect CloseRect { get; private set; }
        public Rect ScaleRect { get; private set; }
        public Rect HintsRect { get; private set; }
        public float Sensitivity { get; private set; }
        public float Fov { get; private set; }
        public int RenderFrameCount { get; private set; }
        public int SliderChangeCount { get; private set; }
        public bool PixelStyleReady { get { return pixels!=null&&pixels.BitmapFontReady&&tiledBackground!=null&&button!=null&&slider!=null&&handle!=null; } }
        public string StyleError { get { return styleError; } }

        private sealed class Widget
        {
            private readonly GUIStyle style=new GUIStyle();
            private readonly int left,right,top,bottom;
            public Widget(MinecraftAssets assets,string id,string highlighted,string metadata)
            {
                Texture2D normal=assets.Texture(id),hover=assets.Texture(highlighted);
                if(normal==null||hover==null)throw new InvalidOperationException("Minecraft settings sprite is unavailable: "+id);
                JToken data=JObject.Parse(assets.ReadText(Spec.Asset(metadata).Entry))["gui"]?["scaling"];
                if(data==null||(string)data["type"]!="nine_slice")throw new InvalidOperationException("Minecraft widget nine-slice metadata is unavailable: "+id);
                JToken border=data["border"];
                if(border.Type==JTokenType.Integer)left=right=top=bottom=(int)border;
                else {left=(int)border["left"];right=(int)border["right"];top=(int)border["top"];bottom=(int)border["bottom"];}
                style.normal.background=normal;style.hover.background=hover;style.active.background=hover;style.focused.background=hover;
                style.border=new RectOffset(left,right,top,bottom);
                style.padding=new RectOffset();style.margin=new RectOffset();
            }
            public void Draw(Rect rect,float scale,bool hover,bool active,bool focused)
            {
                // Preserve the source texture's exact border texels, then scale
                // the whole widget in integer pixels. Input rectangles stay in screen space.
                Matrix4x4 original=GUI.matrix;
                GUI.matrix=original*Matrix4x4.TRS(new Vector3(rect.x,rect.y,0f),Quaternion.identity,new Vector3(scale,scale,1f));
                style.Draw(new Rect(0f,0f,rect.width/scale,rect.height/scale),GUIContent.none,hover,active,false,focused);
                GUI.matrix=original;
            }
        }

        private readonly MinecraftLocalization language;
        public MinecraftSettingsUi(MinecraftAssets source){assets=source;language=new MinecraftLocalization(source);}
        private static float V(string id){return PixelUi.V(id);}

        public void Open(float sensitivity,float fov)
        {
            Sensitivity=sensitivity;Fov=fov;focus=0;dragging=pressedButton=-1;close=false;CloseLanguageMenu();
            inputFrame=pointerEventFrame=keyEventFrame=-1;Layout();
            if(Skins!=null)nickname=Skins.Get(skinRole).Nickname;
            Editing=selectAll=false;
            if(textKeyboard!=null)textKeyboard.onTextInput-=Append;
            textKeyboard=Keyboard.current;if(textKeyboard!=null)textKeyboard.onTextInput+=Append;
        }
        public void Release(){if(textKeyboard!=null)textKeyboard.onTextInput-=Append;textKeyboard=null;Editing=false;if(controlId!=0&&GUIUtility.hotControl==controlId)GUIUtility.hotControl=0;dragging=pressedButton=-1;close=false;CloseLanguageMenu();}

        private void Layout()
        {
            float width=V("settings_width"),height=V("settings_height"),margin=V("settings_screen_margin");
            float fit=Mathf.Floor(Mathf.Min((Screen.width-margin*2f)/width,(Screen.height-margin*2f)/height));
            scale=Mathf.Max(1f,Mathf.Min(PixelUi.Scale,fit));
            WindowRect=new Rect(Mathf.Round((Screen.width-width*scale)*0.5f),Mathf.Round((Screen.height-height*scale)*0.5f),width*scale,height*scale);
            float padding=V("settings_padding"),controlWidth=width-padding*2f,heightControl=V("settings_control_height");
            for(int i=0;i<2;i++)sliders[i]=At(padding,V("settings_first_slider_y")+i*V("settings_slider_gap"),controlWidth,heightControl);
            float buttonY=V("settings_button_y");
            languageRect=At(padding,buttonY-62f,controlWidth,heightControl);
            ScaleRect=At(padding,buttonY-38f,controlWidth,heightControl);
            HintsRect=At(padding,buttonY-14f,controlWidth,heightControl);
            float gap=V("settings_button_gap"),buttonWidth=(controlWidth-gap)*0.5f;
            ResetRect=At(padding,buttonY+10f,buttonWidth,heightControl);
            CloseRect=At(padding+buttonWidth+gap,buttonY+10f,buttonWidth,heightControl);
            roleRect=At(padding,126f,controlWidth,heightControl);
            nicknameRect=At(padding,164f,controlWidth-90f,heightControl);
            applyRect=At(padding+controlWidth-84f,164f,84f,heightControl);
            skinResetRect=At(padding,190f,controlWidth,heightControl);
            armsRect=At(padding,216f,controlWidth,heightControl);
            statusRect=At(padding,242f,controlWidth,12f);
            if (languageMenuOpen) LayoutLanguageMenu();
        }
        private Rect At(float x,float y,float width,float height){return new Rect(WindowRect.x+x*scale,WindowRect.y+y*scale,width*scale,height*scale);}

        private bool EnsureStyle()
        {
            if(PixelStyleReady)return true;
            if(assets==null||!assets.Ready)return false;
            try
            {
                pixels=PixelUi.Shared(assets);
                button=new Widget(assets,"gui_settings_button","gui_settings_button_highlighted","gui_settings_button_meta");
                slider=new Widget(assets,"gui_settings_slider","gui_settings_slider_highlighted","gui_settings_slider_meta");
                handle=new Widget(assets,"gui_settings_handle","gui_settings_handle_highlighted","gui_settings_handle_meta");
                Texture2D source=assets.Texture("gui_settings_background");
                if(source==null)throw new InvalidOperationException("Minecraft menu background is unavailable.");
                tiledBackground=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false){name="CultCraft RAM Minecraft menu tiles",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
                tiledBackground.SetPixels32(source.GetPixels32());tiledBackground.Apply();styleError=null;
                return PixelStyleReady;
            }
            catch(Exception error){styleError=error.Message;return false;}
        }

        public bool Draw(float defaultSensitivity,float defaultFov,bool testAllowed)
        {
            testInput = testAllowed;
            Layout();Event ev=Event.current;if(ev==null)return false;
            controlId=GUIUtility.GetControlID(0x435243,FocusType.Passive,WindowRect);
            if(ev.type==EventType.Repaint&&inputFrame!=Time.frameCount)
            {
                inputFrame=Time.frameCount;
                PollDevices(defaultSensitivity,defaultFov,testAllowed);
            }
            if(close)return true;
            HandleEvent(ev,defaultSensitivity,defaultFov,testAllowed);
            if(close)return true;
            if(ev.type!=EventType.Repaint||!EnsureStyle())return false;
            if(renderFrame!=Time.frameCount){renderFrame=Time.frameCount;RenderFrameCount++;}
            Color original=GUI.color;GUI.color=Color.white;
            Fill(new Rect(0f,0f,Screen.width,Screen.height),new Color(0f,0f,0f,0.65f));
            Fill(new Rect(WindowRect.x+4f*scale,WindowRect.y+4f*scale,WindowRect.width,WindowRect.height),new Color(0f,0f,0f,0.65f));
            Fill(new Rect(WindowRect.x-scale,WindowRect.y-scale,WindowRect.width+scale*2f,WindowRect.height+scale*2f),new Color(0.07f,0.07f,0.07f,1f));
            GUI.DrawTextureWithTexCoords(WindowRect,tiledBackground,new Rect(0f,0f,WindowRect.width/(tiledBackground.width*scale),WindowRect.height/(tiledBackground.height*scale)),true);
            Fill(At(0f,0f,V("settings_width"),31f),new Color(0f,0f,0f,0.3f));
            if (languageMenuOpen)
            {
                DrawLanguageMenu();
                GUI.color=original;
                return false;
            }
            pixels.Text(At(8f,V("settings_title_y"),V("settings_width")-16f,12f),MinecraftLocalization.Ui("settings"),scale,Color.white);
            DrawSlider(0,MinecraftLocalization.Ui("mouse_sensitivity")+": "+Sensitivity.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),"0.02","1.00",(Sensitivity-0.02f)/0.98f);
            DrawSlider(1,MinecraftLocalization.Ui("field_of_view")+": "+Mathf.RoundToInt(Fov)+"°","55°","110°",(Fov-55f)/55f);
            string selectedLanguage = CultLanguages.CurrentLabel();
            string languageLabel = language.Translation("options.language", "Language") + " · " + selectedLanguage;
            if (pixels.Measure(languageLabel, scale) + 8f * scale > languageRect.width)
                languageLabel = selectedLanguage;
            DrawButton(languageRect, LanguageControl, languageLabel);
            string scaleLabel = PixelUi.ScaleMode == 0 ? language.Translation("options.guiScale.auto","Auto") : PixelUi.ScaleMode.ToString();
            DrawButton(ScaleRect,2,language.Translation("options.guiScale","Interface scale")+": "+scaleLabel);
            string hintsValue = PixelUi.ShowHints ? language.Translation("options.on","On") : language.Translation("options.off","Off");
            DrawButton(HintsRect,3,MinecraftLocalization.Ui("show_hints")+": "+hintsValue);
            DrawButton(ResetRect,4,MinecraftLocalization.Ui("reset_defaults"));
            DrawButton(CloseRect,5,MinecraftLocalization.Ui("close"));
            if(Skins!=null)
            {
                var choice=Skins.Get(skinRole);
                DrawButton(roleRect,6,PlayerSkins.Text("role")+": "+PlayerSkins.Text(skinRole==0?"lamb":"goat"));
                pixels.Text(At(V("settings_padding"),150f,V("settings_width")-32f,10f),PlayerSkins.Text("nickname"),scale,Color.white,TextAnchor.MiddleLeft);
                Fill(nicknameRect,new Color(0.03f,0.03f,0.03f,1f));
                pixels.Text(new Rect(nicknameRect.x+4f*scale,nicknameRect.y,nicknameRect.width-8f*scale,nicknameRect.height),nickname+(Editing&&Time.unscaledTime%1f<0.5f?"_":""),scale,Color.white,TextAnchor.MiddleLeft);
                DrawButton(applyRect,7,PlayerSkins.Text("apply"));
                DrawButton(skinResetRect,8,PlayerSkins.Text("reset"));
                DrawButton(armsRect,9,PlayerSkins.Text("arms")+": "+PlayerSkins.Text(choice.Arms==0?"auto":choice.Arms==1?"classic":"slim"));
                pixels.Text(statusRect,choice.Status,scale,new Color(0.75f,0.85f,0.75f));
            }
            pixels.Text(At(8f,V("settings_hint_y"),V("settings_width")-16f,10f),MinecraftLocalization.Ui("settings_controls"),scale,new Color(0.75f,0.75f,0.75f));
            GUI.color=original;return false;
        }

        private void DrawSlider(int index,string label,string minimum,string maximum,float t)
        {
            Rect rect=sliders[index];bool hover=rect.Contains(pointer),selected=focus==index;
            slider.Draw(rect,scale,hover,dragging==index,selected);
            float handleWidth=V("settings_handle_width")*scale;
            Rect thumb=new Rect(Mathf.Round(rect.x+Mathf.Clamp01(t)*(rect.width-handleWidth)),rect.y,handleWidth,rect.height);
            handle.Draw(thumb,scale,hover,dragging==index,selected);
            pixels.Text(rect,label,scale,Color.white);
            pixels.Text(new Rect(rect.x,rect.yMax+3f*scale,rect.width*0.5f,10f*scale),minimum,scale,new Color(0.65f,0.65f,0.65f),TextAnchor.MiddleLeft);
            pixels.Text(new Rect(rect.center.x,rect.yMax+3f*scale,rect.width*0.5f,10f*scale),maximum,scale,new Color(0.65f,0.65f,0.65f),TextAnchor.MiddleRight);
        }
        private void DrawButton(Rect rect,int index,string label)
        {
            button.Draw(rect,scale,rect.Contains(pointer),pressedButton==index,focus==index);
            pixels.Text(rect,label,scale,Color.white);
        }
        private static void Fill(Rect rect,Color color){Color old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;}

        private void PointerDown(Vector2 position)
        {
            pointer=position;dragging=pressedButton=-1;
            if (languageMenuOpen) { LanguagePointerDown(position); return; }
            if(Skins!=null)
            {
                if(nicknameRect.Contains(position)){focus=10;Editing=true;return;}
                Editing=false;
                if(roleRect.Contains(position))focus=pressedButton=6;
                else if(applyRect.Contains(position))focus=pressedButton=7;
                else if(skinResetRect.Contains(position))focus=pressedButton=8;
                else if(armsRect.Contains(position))focus=pressedButton=9;
                if(pressedButton>=6){GUIUtility.hotControl=controlId;return;}
            }
            for(int i=0;i<2;i++)if(sliders[i].Contains(position)){focus=dragging=i;GUIUtility.hotControl=controlId;SetFromPointer(i,position);return;}
            if(languageRect.Contains(position))focus=pressedButton=LanguageControl;
            else if(ScaleRect.Contains(position))focus=pressedButton=2;
            else if(HintsRect.Contains(position))focus=pressedButton=3;
            else if(ResetRect.Contains(position))focus=pressedButton=4;
            else if(CloseRect.Contains(position))focus=pressedButton=5;
            if(pressedButton>=0)GUIUtility.hotControl=controlId;
        }
        private void PointerUp(Vector2 position,float defaultSensitivity,float defaultFov)
        {
            pointer=position;
            if (languageMenuOpen) { LanguagePointerUp(position); return; }
            if(pressedButton>=6&&pressedButton<=9)
            {
                Rect target=pressedButton==6?roleRect:pressedButton==7?applyRect:pressedButton==8?skinResetRect:armsRect;
                if(target.Contains(position))Activate(defaultSensitivity,defaultFov);
            }
            if(dragging>=0)SetFromPointer(dragging,position);
            if(pressedButton==LanguageControl&&languageRect.Contains(position))Activate(defaultSensitivity,defaultFov);
            else if(pressedButton==2&&ScaleRect.Contains(position))Activate(defaultSensitivity,defaultFov);
            else if(pressedButton==3&&HintsRect.Contains(position))Activate(defaultSensitivity,defaultFov);
            else if(pressedButton==4&&ResetRect.Contains(position))Activate(defaultSensitivity,defaultFov);
            else if(pressedButton==5&&CloseRect.Contains(position))Activate(defaultSensitivity,defaultFov);
            dragging=pressedButton=-1;
            if(GUIUtility.hotControl==controlId)GUIUtility.hotControl=0;
        }
        private void SetFromPointer(int index,Vector2 position)
        {
            Rect rect=sliders[index];float half=V("settings_handle_width")*scale*0.5f;
            float t=Mathf.InverseLerp(rect.x+half,rect.xMax-half,position.x);
            SetValue(index,index==0?Mathf.Lerp(0.02f,1f,t):Mathf.Lerp(55f,110f,t));
        }
        private void SetValue(int index,float value)
        {
            if(index==0)
            {
                value=Mathf.Clamp(Mathf.Round(value*100f)/100f,0.02f,1f);
                if(Mathf.Abs(value-Sensitivity)>0.00001f){Sensitivity=value;SliderChangeCount++;}
            }
            else
            {
                value=Mathf.Clamp(Mathf.Round(value),55f,110f);
                if(Mathf.Abs(value-Fov)>0.00001f){Fov=value;SliderChangeCount++;}
            }
        }
        private void MoveFocus(int direction)
        {
            int[] order=Skins==null?FocusOrderWithoutSkins:FocusOrder;
            int current=Array.IndexOf(order,focus);
            focus=order[(current+direction+order.Length)%order.Length];
            Editing=focus==10;
        }
        private void Adjust(int direction)
        {
            if(focus<2)SetValue(focus,(focus==0?Sensitivity:Fov)+direction*(focus==0?0.01f:1f));
            else if(focus==2)CycleScale(direction);
            else if(focus==3)PixelUi.ShowHints=!PixelUi.ShowHints;
            else if(focus==LanguageControl)OpenLanguageMenu();
            else MoveFocus(direction);
        }
        private static void CycleScale(int direction){PixelUi.ScaleMode=(PixelUi.ScaleMode+direction+4)%4;}
        private void Activate(float defaultSensitivity,float defaultFov)
        {
            if(focus==2)CycleScale(1);
            else if(focus==3)PixelUi.ShowHints=!PixelUi.ShowHints;
            else if(focus==4){SetValue(0,defaultSensitivity);SetValue(1,defaultFov);PixelUi.ScaleMode=PixelUi.DefaultScaleMode;PixelUi.ShowHints=true;}
            else if(focus==5)close=true;
            else if(focus==LanguageControl)OpenLanguageMenu();
            else if(Skins!=null&&focus==6){skinRole=1-skinRole;nickname=Skins.Get(skinRole).Nickname;}
            else if(Skins!=null&&(focus==7||focus==10))Skins.Apply(skinRole,nickname);
            else if(Skins!=null&&focus==8){Skins.Reset(skinRole);nickname="";}
            else if(Skins!=null&&focus==9)Skins.SetArms(skinRole,(Skins.Get(skinRole).Arms+1)%3);
        }

        private void PollDevices(float defaultSensitivity,float defaultFov,bool testAllowed)
        {
            Mouse mouse=Mouse.current;
            if(testAllowed&&mouse!=null)
            {
                Vector2 p=mouse.position.ReadValue();pointer=new Vector2(p.x,Screen.height-p.y);
                if(pointerEventFrame!=Time.frameCount)
                {
                    if(mouse.leftButton.wasPressedThisFrame)PointerDown(pointer);
                    if(mouse.leftButton.isPressed&&dragging>=0)SetFromPointer(dragging,pointer);
                    if(mouse.leftButton.isPressed&&languageScrollDrag)DragLanguageScroll(pointer);
                    if(mouse.leftButton.wasReleasedThisFrame)PointerUp(pointer,defaultSensitivity,defaultFov);
                }
                if (languageMenuOpen && wheelEventFrame != Time.frameCount)
                    ScrollLanguages(pointer, mouse.scroll.ReadValue().y);
            }
            Keyboard keyboard=Keyboard.current;
            if (languageMenuOpen)
            {
                PollLanguageKeys(testAllowed && keyEventFrame != Time.frameCount ? keyboard : null, Gamepad.current);
                return;
            }
            if(Editing&&keyboard!=null)
            {
                if(keyboard.escapeKey.wasPressedThisFrame)close=true;
                if(keyboard.tabKey.wasPressedThisFrame)MoveFocus(keyboard.shiftKey.isPressed?-1:1);
                if(keyboard.enterKey.wasPressedThisFrame)Skins.Apply(skinRole,nickname);
                bool ctrl=keyboard.ctrlKey.isPressed;
                if(ctrl&&keyboard.aKey.wasPressedThisFrame)selectAll=true;
                if(ctrl&&keyboard.vKey.wasPressedThisFrame){string paste=GUIUtility.systemCopyBuffer??"";if(selectAll){nickname="";selectAll=false;}nickname=(nickname+paste).Substring(0,Math.Min(16,nickname.Length+paste.Length));}
                if(keyboard.backspaceKey.wasPressedThisFrame||keyboard.deleteKey.wasPressedThisFrame){nickname=selectAll?"":nickname.Length>0?nickname.Substring(0,nickname.Length-1):"";selectAll=false;}
                return;
            }
            if(testAllowed&&keyboard!=null&&keyEventFrame!=Time.frameCount)
            {
                if(keyboard.escapeKey.wasPressedThisFrame)close=true;
                if(keyboard.tabKey.wasPressedThisFrame)MoveFocus(keyboard.shiftKey.isPressed?-1:1);
                if(keyboard.upArrowKey.wasPressedThisFrame)MoveFocus(-1);
                if(keyboard.downArrowKey.wasPressedThisFrame)MoveFocus(1);
                if(keyboard.leftArrowKey.wasPressedThisFrame)Adjust(-1);
                if(keyboard.rightArrowKey.wasPressedThisFrame)Adjust(1);
                if(keyboard.enterKey.wasPressedThisFrame||keyboard.spaceKey.wasPressedThisFrame)Activate(defaultSensitivity,defaultFov);
            }
            Gamepad pad=Gamepad.current;
            if(pad!=null)
            {
                if(pad.dpad.up.wasPressedThisFrame)MoveFocus(-1);
                if(pad.dpad.down.wasPressedThisFrame)MoveFocus(1);
                if(pad.dpad.left.wasPressedThisFrame)Adjust(-1);
                if(pad.dpad.right.wasPressedThisFrame)Adjust(1);
                if(pad.buttonSouth.wasPressedThisFrame)Activate(defaultSensitivity,defaultFov);
                if(pad.buttonEast.wasPressedThisFrame)close=true;
            }
        }
        private void HandleEvent(Event ev,float defaultSensitivity,float defaultFov,bool testAllowed)
        {
            if(ev.isMouse||!testAllowed&&ev.type==EventType.Repaint)pointer=ev.mousePosition;
            if(ev.type==EventType.MouseDown&&ev.button==0){PointerDown(ev.mousePosition);pointerEventFrame=Time.frameCount;ev.Use();}
            else if(ev.type==EventType.MouseDrag&&dragging>=0){SetFromPointer(dragging,ev.mousePosition);pointerEventFrame=Time.frameCount;ev.Use();}
            else if(ev.type==EventType.MouseDrag&&languageScrollDrag){DragLanguageScroll(ev.mousePosition);pointerEventFrame=Time.frameCount;ev.Use();}
            else if(ev.type==EventType.MouseUp&&ev.button==0){PointerUp(ev.mousePosition,defaultSensitivity,defaultFov);pointerEventFrame=Time.frameCount;ev.Use();}
            else if(ev.type==EventType.ScrollWheel&&languageMenuOpen)
            {
                if (wheelEventFrame != Time.frameCount) ScrollLanguages(ev.mousePosition, -ev.delta.y);
                ev.Use();
            }
            else if(ev.type==EventType.KeyDown)
            {
                if(Editing){ev.Use();return;}
                if (languageMenuOpen)
                {
                    if (HandleLanguageKey(ev.keyCode, ev.shift)) { keyEventFrame=Time.frameCount;ev.Use(); }
                    return;
                }
                bool used=true;
                switch(ev.keyCode)
                {
                    case KeyCode.Escape:close=true;break;
                    case KeyCode.Tab:MoveFocus(ev.shift?-1:1);break;
                    case KeyCode.UpArrow:MoveFocus(-1);break;
                    case KeyCode.DownArrow:MoveFocus(1);break;
                    case KeyCode.LeftArrow:Adjust(-1);break;
                    case KeyCode.RightArrow:Adjust(1);break;
                    case KeyCode.Return:case KeyCode.KeypadEnter:case KeyCode.Space:Activate(defaultSensitivity,defaultFov);break;
                    default:used=false;break;
                }
                if(used){keyEventFrame=Time.frameCount;ev.Use();}
            }
            if(ev.type==EventType.MouseDown||ev.type==EventType.MouseDrag||ev.type==EventType.MouseUp||ev.type==EventType.ScrollWheel)ev.Use();
        }

        public void Dispose(){if(tiledBackground!=null)UnityEngine.Object.Destroy(tiledBackground);tiledBackground=null;Release();}
    }
}
