using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Lamb.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace CultCraft
{
    // Apply at the native pool/initialization boundaries, before the first draw.
    // Native identity, quantity, motion, colliders and collection callbacks stay intact.
    public sealed class PickupVisuals : IDisposable
    {
        private sealed class SpriteState
        {
            public Sprite Native;
            public Sprite Replacement;
        }
        private sealed class ImageState { public Sprite Native; public Sprite Replacement; }
        private static PickupVisuals current;
        private readonly Harmony harmony=new Harmony("cultcraft.pickup.visuals");
        private readonly Func<string,Sprite> sprite;
        private readonly MinecraftAssets assets;
        private readonly Dictionary<(Sprite,Texture2D),Sprite> rewardPreviews=new Dictionary<(Sprite,Texture2D),Sprite>();
        private readonly Func<EquipmentType,int,Sprite> weaponSprite;
        private readonly ManualLogSource logger;
        private readonly Dictionary<SpriteRenderer,SpriteState> renderers=new Dictionary<SpriteRenderer,SpriteState>();
        private readonly Dictionary<Image,ImageState> images=new Dictionary<Image,ImageState>();
        private readonly HashSet<PickUp> waiting=new HashSet<PickUp>();
        private readonly DroppedItemVisuals dropped;
        private bool disposed;
        private int initializationCount,firstDraws,firstDrawFailures,reuses;
        public static PickupVisuals Current { get { return current; } }
        public int InitializationCount { get { return initializationCount; } }
        public int FirstGameDraws { get { return firstDraws; } }
        public int FirstGameDrawFailures { get { return firstDrawFailures; } }
        public int PoolReuseCount { get { return reuses; } }
        public int PendingAssetCount { get { return waiting.Count; } }
        public Sprite NativeSpriteFor(SpriteRenderer renderer)
        {SpriteState state;return renderer!=null&&renderers.TryGetValue(renderer,out state)?state.Native:renderer==null?null:renderer.sprite;}
        internal bool Active { get { return !disposed; } }
        public DroppedItemBinding DroppedFor(SpriteRenderer renderer){return dropped.Get(renderer);}

        public PickupVisuals(Func<string,Sprite> sprite,Func<EquipmentType,int,Sprite> weaponSprite,ManualLogSource logger,MinecraftAssets assets)
        {
            dropped=new DroppedItemVisuals(assets);
            this.sprite=sprite;this.weaponSprite=weaponSprite;this.logger=logger;this.assets=assets;current=this;
            try
            {
                Hook(typeof(PickUp),"OnEnable",Type.EmptyTypes,"PickupRestorePrefix","PickupEnablePostfix");
                Hook(typeof(PickUp),"OnDisable",Type.EmptyTypes,"PickupRestorePrefix",null);
                Hook(typeof(PickUp),"Start",Type.EmptyTypes,null,"PickupRefreshPostfix");
                Hook(typeof(PickUp),"SetImage",new[]{typeof(InventoryItem.ITEM_TYPE)},null,"PickupImagePostfix");
                Hook(typeof(FoundItemPickUp),"OnEnableInteraction",Type.EmptyTypes,null,"FoundPostfix");
                Hook(typeof(FoundItemPickUp),"Start",Type.EmptyTypes,null,"FoundPostfix");
                Hook(typeof(Interaction_WeaponPickUp),"SetWeapon",new[]{typeof(EquipmentType),typeof(int),typeof(Interaction_WeaponPickUp.Types)},null,"WeaponPostfix");
                Hook(typeof(Interaction_WeaponPickUp),"OnEnableInteraction",Type.EmptyTypes,null,"WeaponPostfix");
                Hook(typeof(InventoryItemDisplay),"SetImage",new[]{typeof(InventoryItem.ITEM_TYPE),typeof(bool)},"DisplayRestorePrefix","DisplayPostfix");
                Hook(typeof(InventoryItemDisplay),"SetImage",new[]{typeof(Sprite),typeof(bool)},"DisplayRestorePrefix",null);
                Hook(typeof(InventoryItemDisplay),"GetImage",new[]{typeof(InventoryItem.ITEM_TYPE)},null,"DisplayIconPostfix");
                Hook(typeof(ResourceCustomTarget),"OnRecycled",Type.EmptyTypes,"FlyingResourceRestorePrefix",null);
                Hook(typeof(InventoryItem),"Spawn",new[]{typeof(InventoryItem.ITEM_TYPE),typeof(int),typeof(Vector3),typeof(float),typeof(Action<PickUp>)},null,"SpawnPostfix");
                foreach(PickUp pickup in new List<PickUp>(PickUp.PickUps))if(pickup!=null)Apply(pickup,false);
                foreach(Interaction interaction in new List<Interaction>(Interaction.interactions))
                    if(interaction is Interaction_WeaponPickUp)ApplyWeapon((Interaction_WeaponPickUp)interaction);
                SceneManager.sceneLoaded+=SceneLoaded;
            }
            catch{harmony.UnpatchSelf();dropped.Dispose();if(current==this)current=null;throw;}
        }
        private void Hook(Type type,string name,Type[] args,string prefix,string postfix)
        {
            var method=AccessTools.Method(type,name,args);
            if(method==null)throw new MissingMethodException(type.FullName,name);
            harmony.Patch(method,prefix:prefix==null?null:new HarmonyMethod(typeof(PickupVisuals),prefix),postfix:postfix==null?null:new HarmonyMethod(typeof(PickupVisuals),postfix));
        }
        public static string ResourceTexture(InventoryItem.ITEM_TYPE type)
        {foreach(ResourceRow row in Spec.ResourceRows)if(row.NativeId==(int)type)return row.Texture;return null;}
        public static InventoryItem.ITEM_TYPE SemanticType(PickUp pickup)
        {
            if(pickup==null)return InventoryItem.ITEM_TYPE.NONE;
            FoundItemPickUp found=pickup.GetComponent<FoundItemPickUp>();
            // Both actual scroll prefabs serialize PickUp.type=NONE. The native
            // card discriminant, not a guessed common blueprint ID, is authoritative.
            if(found!=null)
            {
                if(found.typeOfCard==UINewItemOverlayController.TypeOfCard.FollowerSkin)return InventoryItem.ITEM_TYPE.FOUND_ITEM_FOLLOWERSKIN;
                if(found.typeOfCard==UINewItemOverlayController.TypeOfCard.Decoration)return InventoryItem.ITEM_TYPE.FOUND_ITEM_DECORATION;
            }
            return pickup.type;
        }
        private static void PickupRestorePrefix(PickUp __instance,MethodBase __originalMethod)
        {
            if(current==null)return;
            if(__originalMethod.Name=="OnDisable")foreach(PickupFirstDrawProbe probe in __instance.GetComponentsInChildren<PickupFirstDrawProbe>(true))probe.MarkDisabled();
            current.RestorePickup(__instance);
        }
        private static void PickupEnablePostfix(PickUp __instance){if(current!=null)current.Apply(__instance,true);}
        private static void PickupRefreshPostfix(PickUp __instance){if(current!=null)current.Apply(__instance,false);}
        private static void PickupImagePostfix(PickUp __instance,InventoryItem.ITEM_TYPE __0){if(current!=null)current.Apply(__instance,false,__0);}
        private static void FoundPostfix(FoundItemPickUp __instance){if(current!=null)current.Apply(__instance.GetComponent<PickUp>(),false);}
        private static void WeaponPostfix(Interaction_WeaponPickUp __instance){if(current!=null)current.ApplyWeapon(__instance);}
        private static void SpawnPostfix(PickUp __result){if(current!=null&&__result!=null)current.Apply(__result,false);}
        private static void DisplayRestorePrefix(InventoryItemDisplay __instance){if(current!=null)current.RestoreDisplay(__instance);}
        private static void FlyingResourceRestorePrefix(ResourceCustomTarget __instance){if(current!=null)current.RestoreDisplay(__instance.inventoryItemDisplay);}
        private static void DisplayPostfix(InventoryItemDisplay __instance,InventoryItem.ITEM_TYPE __0){if(current!=null)current.ApplyDisplay(__instance,__0);}
        private static void DisplayIconPostfix(InventoryItem.ITEM_TYPE __0,ref Sprite __result)
        {if(current!=null&&!current.disposed){string id=ResourceTexture(__0);if(id!=null)__result=current.sprite(id)??__result;}}

        private static SpriteRenderer Art(PickUp pickup)
        {
            FoundItemPickUp found=pickup.GetComponent<FoundItemPickUp>();
            if(found!=null&&found.itemSprite!=null)return found.itemSprite;
            if(pickup.inventoryItemDisplay!=null)
            {
                SpriteRenderer image=pickup.inventoryItemDisplay.spriteRenderer;
                if(image==null)image=pickup.inventoryItemDisplay.GetComponent<SpriteRenderer>();
                if(image!=null)return image;
            }
            if(pickup.child!=null)
                foreach(SpriteRenderer candidate in pickup.child.GetComponentsInChildren<SpriteRenderer>(true))
                    if(!candidate.name.ToLowerInvariant().Contains("shadow")&&candidate.GetComponentInParent<Canvas>()==null)return candidate;
            return pickup.GetComponent<SpriteRenderer>();
        }
        private void Apply(PickUp pickup,bool newLifetime,InventoryItem.ITEM_TYPE? requested=null)
        {
            if(disposed||pickup==null)return;
            Interaction_WeaponPickUp weapon=pickup.GetComponent<Interaction_WeaponPickUp>();
            if(weapon!=null){ApplyWeapon(weapon);waiting.Remove(pickup);return;}
            InventoryItem.ITEM_TYPE type=requested??SemanticType(pickup);
            string id=ResourceTexture(type);SpriteRenderer renderer=Art(pickup);
            if(renderer==null)return;
            PickupFirstDrawProbe probe=renderer.GetComponent<PickupFirstDrawProbe>();
            if(newLifetime||probe==null||!probe.BelongsTo(this))
            {
                if(probe==null)probe=renderer.gameObject.AddComponent<PickupFirstDrawProbe>();
                bool recycled=probe.Lifetimes>0&&probe.WasDisabled;
                if(recycled)reuses++;
                probe.Begin(this,pickup,type,probe.Lifetimes==0||recycled);initializationCount++;
            }
            if(id==null){Restore(renderer);probe.Expect(null,type);waiting.Remove(pickup);return;}
            Sprite replacement=sprite(id);
            if(replacement==null){Restore(renderer);probe.Expect(null,type);waiting.Add(pickup);return;}
            Set(renderer,replacement);probe.Expect(replacement,type);dropped.Apply(renderer,replacement,id);waiting.Remove(pickup);
        }
        private void ApplyWeapon(Interaction_WeaponPickUp pickup)
        {
            if(disposed||pickup==null||pickup.IconSpriteRenderer==null)return;
            if(pickup.Type!=Interaction_WeaponPickUp.Types.Weapon){Restore(pickup.IconSpriteRenderer);return;}
            Sprite replacement=weaponSprite(pickup.TypeOfWeapon,Math.Max(1,pickup.WeaponLevel));
            Set(pickup.IconSpriteRenderer,replacement);dropped.Apply(pickup.IconSpriteRenderer,replacement);
        }
        private void ApplyDisplay(InventoryItemDisplay display,InventoryItem.ITEM_TYPE type)
        {
            if(disposed||display==null)return;string id=ResourceTexture(type);if(id==null)return;
            SpriteRenderer renderer=display.spriteRenderer!=null?display.spriteRenderer:display.GetComponent<SpriteRenderer>();
            bool reward=display.GetComponentInParent<SingleChoiceRewardOption>(true)!=null;
            if(reward&&type==InventoryItem.ITEM_TYPE.GIFT_MEDIUM)
            {
                if(renderer!=null)Set(renderer,GiftPreview(renderer.sprite));
                if(display.image!=null)Set(display.image,GiftPreview(display.image.sprite));
                if(display.outline!=null)Set(display.outline,GiftPreview(display.outline.sprite));
                return;
            }
            Sprite replacement=sprite(id);if(replacement==null)return;
            if(reward&&renderer!=null&&renderer.sprite!=null&&MinecraftAvatar.IsBlockItem(id))
            {
                Bounds nativeBounds=renderer.sprite.bounds;
                Set(renderer,replacement);
                dropped.Apply(renderer,replacement,id,nativeBounds);
                return;
            }
            if(reward)
            {
                if(renderer!=null)Set(renderer,FitRewardPreview(renderer.sprite,replacement.texture,replacement.rect,replacement.name));
                if(display.image!=null)Set(display.image,FitRewardPreview(display.image.sprite,replacement.texture,replacement.rect,replacement.name));
                if(display.outline!=null)Set(display.outline,FitRewardPreview(display.outline.sprite,replacement.texture,replacement.rect,replacement.name));
                return;
            }
            Set(renderer,replacement);Set(display.image,replacement);Set(display.outline,replacement);
            PickUp pickup=display.GetComponentInParent<PickUp>();if(pickup!=null)Apply(pickup,false,type);
        }

        private Sprite GiftPreview(Sprite native)
        {
            Texture2D texture=assets.Texture("black_shulker_reward");
            return texture==null?null:FitRewardPreview(native,texture,new Rect(0,0,texture.width,texture.height),
                "CultCraft fitted black shulker reward");
        }

        private Sprite FitRewardPreview(Sprite native,Texture2D texture,Rect rect,string name)
        {
            if(native==null||texture==null)return null;
            var key=(native,texture);
            if(rewardPreviews.TryGetValue(key,out Sprite preview))return preview;
            Vector2 nativeSize=native.rect.size/native.pixelsPerUnit;
            if(nativeSize.x<=0f||nativeSize.y<=0f)return null;
            float ppu=Mathf.Max(rect.width/nativeSize.x,rect.height/nativeSize.y);
            Vector2 center=(native.rect.size*0.5f-native.pivot)/native.pixelsPerUnit;
            Vector2 pivot=Vector2.one*0.5f-new Vector2(center.x*ppu/rect.width,center.y*ppu/rect.height);
            preview=Sprite.Create(texture,rect,pivot,ppu,0,SpriteMeshType.FullRect);
            preview.name=name;
            rewardPreviews.Add(key,preview);
            return preview;
        }
        private void Set(SpriteRenderer renderer,Sprite replacement)
        {
            if(renderer==null||replacement==null)return;SpriteState state;
            if(!renderers.TryGetValue(renderer,out state)){state=new SpriteState{Native=renderer.sprite};renderers.Add(renderer,state);}
            else if(renderer.sprite!=state.Replacement)state.Native=renderer.sprite;
            state.Replacement=replacement;renderer.sprite=replacement;
        }
        private void Set(Image image,Sprite replacement)
        {
            if(image==null||replacement==null)return;ImageState state;
            if(!images.TryGetValue(image,out state)){state=new ImageState{Native=image.sprite};images.Add(image,state);}
            else if(image.sprite!=state.Replacement)state.Native=image.sprite;
            state.Replacement=replacement;image.sprite=replacement;
        }
        private void Restore(SpriteRenderer renderer)
        {
            if(renderer==null)return;SpriteState state;
            dropped.Restore(renderer);
            if(renderers.TryGetValue(renderer,out state)){if(renderer.sprite==state.Replacement)renderer.sprite=state.Native;renderers.Remove(renderer);}
        }
        private void Restore(Image image)
        {
            if(image==null)return;ImageState state;
            if(images.TryGetValue(image,out state)){if(image.sprite==state.Replacement)image.sprite=state.Native;images.Remove(image);}
        }
        private void RestoreDisplay(InventoryItemDisplay display)
        {if(display!=null){Restore(display.spriteRenderer!=null?display.spriteRenderer:display.GetComponent<SpriteRenderer>());Restore(display.image);Restore(display.outline);}}
        private void RestorePickup(PickUp pickup)
        {
            if(disposed||pickup==null)return;
            foreach(SpriteRenderer renderer in pickup.GetComponentsInChildren<SpriteRenderer>(true))Restore(renderer);
            foreach(InventoryItemDisplay display in pickup.GetComponentsInChildren<InventoryItemDisplay>(true))RestoreDisplay(display);
            waiting.Remove(pickup);
        }
        public void Tick()
        {
            if(disposed||waiting.Count==0)return;
            foreach(PickUp pickup in new List<PickUp>(waiting))
                if(pickup==null||!pickup.gameObject.activeInHierarchy)waiting.Remove(pickup);else Apply(pickup,false);
        }
        private void SceneLoaded(Scene scene,LoadSceneMode mode)
        {
            foreach(SpriteRenderer renderer in new List<SpriteRenderer>(renderers.Keys))if(renderer==null)renderers.Remove(renderer);
            foreach(Image image in new List<Image>(images.Keys))if(image==null)images.Remove(image);
            waiting.RemoveWhere(pickup=>pickup==null);
        }
        internal void Observed(PickupFirstDrawProbe probe,bool correct)
        {
            firstDraws++;if(!correct)firstDrawFailures++;
            if(logger!=null)logger.LogInfo("Native pickup first game-camera draw: semantic="+probe.NativeType+", pool life="+probe.Lifetimes+", applied frame="+probe.AppliedFrame+", draw frame="+probe.FirstGameDrawFrame+", authentic sprite before render="+correct+", asset pending at first draw="+probe.AssetPendingAtFirstGameDraw);
        }
        public void Dispose()
        {
            if(disposed)return;harmony.UnpatchSelf();SceneManager.sceneLoaded-=SceneLoaded;
            foreach(SpriteRenderer renderer in new List<SpriteRenderer>(renderers.Keys))Restore(renderer);
            foreach(Image image in new List<Image>(images.Keys))Restore(image);
            foreach(Sprite preview in rewardPreviews.Values)UnityEngine.Object.Destroy(preview);
            waiting.Clear();dropped.Dispose();disposed=true;if(current==this)current=null;
        }
    }

    public sealed class PickupFirstDrawProbe : MonoBehaviour
    {
        private PickupVisuals owner;
        private SpriteRenderer renderer;
        private Sprite expected;
        public PickUp NativePickup { get; private set; }
        public InventoryItem.ITEM_TYPE NativeType { get; private set; }
        public int Lifetimes { get; private set; }
        public int AppliedFrame { get; private set; }=-1;
        public int FirstGameDrawFrame { get; private set; }=-1;
        public bool FirstGameDrawHadMinecraft { get; private set; }
        public Sprite FirstGameDrawSprite { get; private set; }
        public bool Mapped { get; private set; }
        public bool AssetPendingAtFirstGameDraw { get; private set; }
        internal bool WasDisabled { get; private set; }
        internal bool BelongsTo(PickupVisuals value){return owner==value;}
        internal void MarkDisabled(){WasDisabled=true;}
        internal void Begin(PickupVisuals owner,PickUp pickup,InventoryItem.ITEM_TYPE type,bool advance)
        {this.owner=owner;NativePickup=pickup;NativeType=type;renderer=GetComponent<SpriteRenderer>();expected=null;if(advance)Lifetimes++;WasDisabled=false;AppliedFrame=-1;FirstGameDrawFrame=-1;FirstGameDrawHadMinecraft=false;FirstGameDrawSprite=null;AssetPendingAtFirstGameDraw=false;Mapped=PickupVisuals.ResourceTexture(type)!=null;}
        internal void Expect(Sprite sprite,InventoryItem.ITEM_TYPE type){expected=sprite;NativeType=type;Mapped=PickupVisuals.ResourceTexture(type)!=null;if(sprite!=null)AppliedFrame=Time.frameCount;}
        private void OnWillRenderObject()
        {
            ObserveMeshDraw(Camera.current,renderer==null?null:renderer.sprite,renderer==null||renderer.sprite==null?null:renderer.sprite.texture);
        }
        public void ObserveMeshDraw(Camera camera,Sprite drawn,Texture actualTexture)
        {
            if(owner==null||!owner.Active||FirstGameDrawFrame>=0||camera==null||camera.cameraType!=CameraType.Game)return;
            FirstGameDrawFrame=Time.frameCount;AssetPendingAtFirstGameDraw=Mapped&&expected==null;
            FirstGameDrawSprite=drawn;
            DroppedItemBinding volume=owner.DroppedFor(renderer);
            bool correctTexture=expected!=null&&(actualTexture==expected.texture || volume!=null&&volume.CubeAtlasMatches(expected.texture,actualTexture));
            FirstGameDrawHadMinecraft=expected!=null&&drawn==expected&&correctTexture&&renderer!=null&&renderer.sprite==expected&&AppliedFrame>=0&&AppliedFrame<=FirstGameDrawFrame;
            if(Mapped)owner.Observed(this,FirstGameDrawHadMinecraft);
        }
    }
}
