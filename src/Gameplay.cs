using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Lamb.UI.Assets;
using MMTools;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace CultCraft
{
    /// <summary>Native combat and gathering remain responsible for every gameplay consequence.</summary>
    public sealed class Gameplay : IDisposable
    {
        private const string HarmonyId = "cultcraft.gameplay";
        internal const string NativePromptPlaceholder = "asdasdasdasdasdasdasdasddfdsfdsfdsf";
        private readonly MinecraftAssets assets;
        private readonly ManualLogSource logger;
        private readonly Harmony harmony;
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly PickupVisuals pickupVisuals;
        private readonly List<BlockVisual> blocks = new List<BlockVisual>();
        private readonly HashSet<Structure> blockIndex = new HashSet<Structure>();
        private readonly List<Arrow> arrows = new List<Arrow>();
        private Mesh arrowEntityMesh;
        private Material arrowEntityMaterial;
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private PlayerFarming player;
        private static Gameplay current;
        public static Gameplay Current { get { return current; } }
        private float nextVisualScan;
        private float nextShot;
        private const int CrossbowCapacity = 3;
        private const float CrossbowReloadSeconds = 0.8f;
        private int crossbowAmmo=CrossbowCapacity;
        private float nextCrossbowShot,crossbowReloadAt;
        private bool homeEquipPending;
        private bool disposed;
        private int lastLevel = -1;
        private WeaponRow row;
        private readonly CombatRow combat = Spec.Combat("bow");
        private readonly Enchantments enchantments;
        private CameraRig viewRig;
        private bool testInputAllowed;
        private readonly Hotbar hotbar;
        private readonly Dictionary<PlayerFarming,ConstructionRecipe> construction=new Dictionary<PlayerFarming,ConstructionRecipe>();
        private InventoryIconMapping inventoryIcons;
        private sealed class ConstructionRecipe
        {
            public Component Plot;
            public string[] Items;
        }
        private bool readingPrimary;
        private float eatingTime;
        private float eatingHealth;
        private float nextFoodUse;
        private int eatingFoodId;

        public string WeaponId { get { return CombatLocation&&NativeRangedEquipped?CrossbowModel:WeaponTexture(row.Id??"wooden_sword",CombatLocation&&player!=null?player.currentWeapon:EquipmentType.Sword); } }
        public bool NativeRangedEquipped { get { return player != null && IsRangedWeapon(player.currentWeapon); } }
        public int Tier { get; private set; }
        public bool BowHeld { get; private set; }
        public bool IsDungeon { get; private set; }
        internal bool CombatLocation {get{return IsDungeon||(player!=null&&player.playerWeapon!=null&&player.playerWeapon.ForceWeapons);}}
        internal int CrossbowAmmo {get{return crossbowAmmo;}}
        internal float CrossbowCooldownRemaining {get{return Mathf.Max(0f,Mathf.Max(nextCrossbowShot,crossbowReloadAt)-Time.time);}}
        internal float CrossbowCooldownTotal {get{return crossbowReloadAt>0f?CrossbowReloadSeconds:BowCooldownTotal;}}
        private string CrossbowModel
        {
            get
            {
                if(crossbowAmmo>0)return "crossbow_arrow";
                float progress=1f-(crossbowReloadAt-Time.time)/CrossbowReloadSeconds;
                return progress<0.33f?"crossbow_pulling_0":progress<0.66f?"crossbow_pulling_1":"crossbow_pulling_2";
            }
        }
        internal static bool IsCrossbowItem(string id){return id!=null&&id.StartsWith("crossbow_",StringComparison.Ordinal);}
        public int ShotsFired { get; private set; }
        public float BowCooldownReadyAt { get { return nextShot; } }
        public float BowCooldownRemaining { get { return Mathf.Max(0f,nextShot-Time.time); } }
        public float BowCooldownTotal { get { return Math.Max(combat.MinimumCooldown,row.Cooldown); } }
        public int ArrowDamageHits { get; private set; }
        internal int ArrowEntityDrawFrames { get; private set; }
        public int BlockCount { get { return blocks.Count; } }
        public int EnchantmentLevel { get { return CombatLocation&&player!=null&&(NativeRangedEquipped||IsHammer(player.currentWeapon)) ? 0 : enchantments.SharpnessLevel(lastLevel); } }
        public int BowEnchantmentLevel { get { return enchantments.PowerLevel(lastLevel); } }
        public int DisplayedEnchantmentLevel { get { return SelectedItemId == "bow" ? BowEnchantmentLevel : hotbar.SelectedSlot == 0&&SelectedItemId.Length>0 ? EnchantmentLevel : 0; } }
        public string EnchantmentLabel { get { return enchantments.Label(lastLevel); } }
        public string SelectedItemId { get { return BowHeld ? "bow" : hotbar.SelectedId; } }
        public int SelectedSlot { get { return hotbar.SelectedSlot; } }
        public static bool IsWeaponPresentation(PlayerFarming owner,string animation)
        {
            if(owner==null||owner.state==null||owner.state.CURRENT_STATE!=StateMachine.State.CustomAnimation||owner.currentWeapon==EquipmentType.None||String.IsNullOrEmpty(animation))return false;
            EquipmentData equipment=EquipmentManager.GetEquipmentData(owner.currentWeapon);
            return equipment!=null&&!String.IsNullOrEmpty(equipment.PickupAnimationKey)&&animation==equipment.PickupAnimationKey;
        }
        public static string PresentedWeapon(PlayerFarming owner)
        {return current!=null&&current.player==owner?current.WeaponId:"";}
        public int FoodCount { get { return CountFood(hotbar.Food); } }
        internal int SelectedFoodId { get { return hotbar.Food.NativeId; } }
        internal int PreviewFoodId { get { return hotbar.PreviewFood.NativeId; } }
        internal bool FoodPickerOpen { get { return hotbar.FoodPickerOpen; } }
        internal bool HandPickerOpen {get{return hotbar.HandPickerOpen;}}
        internal static int CountFood(ConsumableRow food)
        {
            if (DataManager.Instance == null || Inventory.items == null) return 0;
            InventoryItem item = Inventory.GetItemByType(food.NativeId);
            return item == null ? 0 : Math.Max(0, item.UnreservedQuantity);
        }
        public int FoodsEaten { get; private set; }
        public float LastFoodHealing { get; private set; }
        public float EatingProgress { get { return Mathf.Clamp01(eatingTime / hotbar.Food.UseSeconds); } }
        public void DrawHotbar() { hotbar.Draw(player, CanUseHotbar()); }
        public void SelectSlot(int slot) { hotbar.Select(slot); }
        public void SetCamera(CameraRig rig) { viewRig = rig; }
        public void AllowTestInput(bool allowed) { testInputAllowed = allowed; }
        public bool TestShootAt(PlayerFarming owner, Vector2 target)
        {
            string marker = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Melty", "test", "cultcraft.json");
            if (!System.IO.File.Exists(marker) || owner != player || !IsDungeon || !CanAct(owner) || viewRig == null || !viewRig.TestLookAt(target)) return false;
            return Shoot(owner);
        }

        private sealed class Arrow
        {
            public GameObject Object;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Height;
            public float Life;
            public float Damage;
            public PlayerFarming Owner;
        }

        private sealed class BlockVisual
        {
            public Structure Native;
            public int NativeType;
            public StructuresData NativeData;
            public BuildingRow Design;
            public string VegetationId;
            public float VegetationScale = 1f;
            public CropController Crop;
            public Interaction_Berries Berries;
            public Vector3 AnchorOffset;
            public int Revisions;
            public bool RockFitted;
            public float RockReadyAt;
            public GameObject Object;
            public readonly Dictionary<Renderer, bool> Renderers = new Dictionary<Renderer, bool>();
            public readonly List<Material> Materials = new List<Material>();
            public readonly List<Mesh> Meshes = new List<Mesh>();
            public readonly List<ColliderSnapshot> Colliders = new List<ColliderSnapshot>();
        }

        private sealed class ColliderSnapshot
        {
            public Collider2D Collider;
            public bool Enabled;
            public bool IsTrigger;
        }

        public bool NativeStructurePreserved(Structure native)
        {
            if (native == null || native.Brain == null || native.Structure_Info == null || native.Brain.Data != native.Structure_Info || native.Structure_Info.Type != native.Type) return false;
            foreach (BlockVisual block in blocks)
            {
                if (block.Native != native || block.Object == null || !block.Object.activeInHierarchy) continue;
                foreach (ColliderSnapshot snapshot in block.Colliders)
                    if (snapshot.Collider == null || snapshot.Collider.enabled != snapshot.Enabled || snapshot.Collider.isTrigger != snapshot.IsTrigger) return false;
                return true;
            }
            return false;
        }

        public Transform NativeStructureVisual(Structure native)
        {
            foreach (BlockVisual block in blocks)
                if (block.Native == native && block.Object != null) return block.Object.transform;
            return null;
        }

        public string NativeVegetationId(Structure native)
        {
            foreach (BlockVisual block in blocks)
                if (block.Native == native && block.Object != null) return block.VegetationId;
            return null;
        }

        public int NativeVegetationRevision(Structure native)
        {
            foreach (BlockVisual block in blocks)
                if (block.Native == native && block.Object != null) return block.Revisions;
            return 0;
        }

        public float NativeVegetationScale(Structure native)
        {
            foreach (BlockVisual block in blocks)
                if (block.Native == native && block.Object != null) return block.VegetationScale;
            return 1f;
        }

        public Gameplay(MinecraftAssets assets, ManualLogSource logger)
        {
            this.assets = assets;
            this.logger = logger;
            enchantments = new Enchantments(assets);
            if (!enchantments.Ready) throw new InvalidOperationException("Minecraft enchantment data: " + enchantments.Error);
            row = RowForLevel(1, out int tier);
            Tier = tier;
            hotbar = new Hotbar(assets, this);
            current = this;
            harmony = new Harmony(HarmonyId);
            Patch(typeof(InventoryIconMapping), "GetImage", new[] { typeof(InventoryItem.ITEM_TYPE) }, "ResourceIconPostfix");
            Patch(typeof(PlayerWeapon), "GetCurrentIcon", new[] { typeof(EquipmentType) }, "WeaponIconPostfix");
            Patch(typeof(PlayerWeapon), "GetSprite", new[] { typeof(EquipmentType) }, "WeaponIconPostfix");
            Patch(typeof(PlayerWeapon), "GetDamage", new[] { typeof(float), typeof(int), typeof(PlayerFarming) }, "DamagePostfix");
            Patch(typeof(RewiredGameplayInputSource), "GetAttackButtonDown", new[] { typeof(PlayerFarming) }, "PrimaryInputPostfix");
            Patch(typeof(RewiredGameplayInputSource), "GetAttackButtonHeld", new[] { typeof(PlayerFarming) }, "PrimaryInputPostfix");
            Patch(typeof(RewiredGameplayInputSource), "GetAttackButtonUp", new[] { typeof(PlayerFarming) }, "PrimaryInputPostfix");
            Patch(typeof(RewiredGameplayInputSource), "GetHeavyAttackButtonDown", new[] { typeof(PlayerFarming) }, "PrimaryInputPostfix");
            Patch(typeof(RewiredGameplayInputSource), "GetHeavyAttackButtonHeld", new[] { typeof(PlayerFarming) }, "PrimaryInputPostfix");
            MethodInfo firearm = AccessTools.Method(typeof(PlayerWeapon), "DoBlunderbussAttack", new[] { typeof(float), typeof(Vector3), typeof(Health.AttackTypes), typeof(bool) });
            if (firearm == null) throw new MissingMethodException(typeof(PlayerWeapon).FullName, "DoBlunderbussAttack");
            harmony.Patch(firearm, prefix: new HarmonyMethod(typeof(Gameplay), "NativeFirearmPrefix"));
            harmony.Patch(AccessTools.Method(typeof(Indicator), nameof(Indicator.Reset)),
                prefix: new HarmonyMethod(typeof(Gameplay), nameof(ClearIndicatorPlaceholder)));
            Patch(typeof(Indicator), "LateUpdate", Type.EmptyTypes, nameof(LiftCarryPrompt));
            Patch(typeof(PlayerWeapon),"SetWeapon",new[]{typeof(EquipmentType),typeof(int)},nameof(WeaponChangedPostfix));
            Patch(typeof(BiomeBaseManager),"PlacePlayer",Type.EmptyTypes,nameof(PlacedInBasePostfix));
            Patch(typeof(Interaction_BaseTeleporter),"TeleportIn",Type.EmptyTypes,nameof(PlacedInBasePostfix));
            Patch(typeof(Interaction_PlayerBuild),"DoBuild",new[]{typeof(PlayerFarming)},nameof(BuildStartedPostfix));
            Patch(typeof(Interaction_PlayerBuildProject),"DoBuild",new[]{typeof(PlayerFarming)},nameof(ProjectStartedPostfix));
            pickupVisuals = new PickupVisuals(Sprite, PickupWeaponSprite, logger,assets);
        }

        private void Patch(Type type, string name, Type[] args, string postfix)
        {
            MethodInfo method = AccessTools.Method(type, name, args);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(Gameplay), postfix));
        }

        private static void ClearIndicatorPlaceholder(Indicator __instance)
        {
            // These fields start with sample text in the native prefab. The
            // carry branch sets Drop and clears the secondary label only.
            if (__instance.Thirdtext != null && __instance.Thirdtext.text == NativePromptPlaceholder)
                __instance.Thirdtext.text = "";
            if (__instance.Fourthtext != null && __instance.Fourthtext.text == NativePromptPlaceholder)
                __instance.Fourthtext.text = "";
        }

        private static void LiftCarryPrompt(Indicator __instance, RectTransform ____container)
        {
            PlayerFarming owner = __instance.playerFarming;
            if (owner == null || owner.CarryingDeadFollowerID < 0 || !__instance.UpdatePosition ||
                ____container == null || !(____container.parent is RectTransform parent) ||
                (owner.state.CURRENT_STATE != StateMachine.State.Idle_CarryingBody &&
                 owner.state.CURRENT_STATE != StateMachine.State.Moving_CarryingBody))
                return;
            RectTransform label = __instance.text.rectTransform;
            Canvas canvas = label.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 bottom = RectTransformUtility.WorldToScreenPoint(camera,
                label.TransformPoint(new Vector3(label.rect.center.x, label.rect.yMin)));
            Rect bar = Hotbar.ScreenRect;
            float minimum = Screen.height - bar.y + bar.height;
            if (bottom.y >= minimum) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, bottom, camera, out Vector2 from) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(bottom.x, minimum), camera, out Vector2 to))
                ____container.anchoredPosition += to - from;
        }

        public void Tick(PlayerFarming value)
        {
            if (disposed || assets == null || !assets.Ready) return;
            pickupVisuals.Tick();
            if (player != value)
            {
                ClearArrows();
                player = value;
                lastLevel = -1;
                nextShot = Time.time;
                ResetCrossbow();
                construction.Clear();
                eatingTime = 0f;
            }
            FollowerLocation location=PlayerFarming.Location;
            IsDungeon=value!=null&&(GameManager.IsDungeon(location)||LocationManager.LocationIsDungeon(location)||location==FollowerLocation.Endless);
            BowHeld = false;
            if (value == null)
            {
                hotbar.TickInput(false);
                ClearArrows();
                RestoreBlocks();
                return;
            }
            if (IsDungeon && value.playerWeapon != null && CanAct(value))
            {
                // Give an unarmed entrance a starter sword. A real native pickup,
                // especially an axe, must keep its own family, variant and attacks.
                if (value.currentWeapon == EquipmentType.None)
                {
                    int level = Math.Max(1, Math.Max(value.currentWeaponLevel, value.playerWeapon.CurrentWeaponLevel));
                    value.playerWeapon.SetWeapon(EquipmentType.Sword, level);
                }
            }
            int nativeLevel = Math.Max(1, value.currentWeaponLevel);
            if (nativeLevel != lastLevel)
            {
                row = RowForLevel(nativeLevel, out int tier);
                Tier = tier;
                lastLevel = nativeLevel;
                logger.LogInfo("Minecraft weapon: " + row.Id + ", native level " + nativeLevel);
            }
            TickCrossbow(value);
            if(homeEquipPending&&PlayerFarming.Location==FollowerLocation.Base&&value.gameObject.activeInHierarchy&&value.health!=null&&value.health.CurrentHP>0f)
            {
                homeEquipPending=false;
                hotbar.ResetMainHand();
            }
            else hotbar.TickInput(CanUseHotbar());
            var nativeTrack=value.Spine==null||value.Spine.AnimationState==null?null:value.Spine.AnimationState.GetCurrent(0);
            if(nativeTrack!=null&&nativeTrack.Animation!=null&&IsWeaponPresentation(value,nativeTrack.Animation.Name))hotbar.Select(0);
            bool legacyBow = BowKeyHeld();
            BowHeld = CombatLocation && (hotbar.SelectedSlot == 1 || legacyBow);
            bool primaryHeld = PrimaryHeld();
            bool canShoot=CanAct(value)&&CanUseHotbar()&&!hotbar.FoodPickerOpen&&!hotbar.HandPickerOpen&&!hotbar.PointerOverBar;
            if (BowHeld && canShoot && (primaryHeld || legacyBow) && Time.time >= nextShot)
            {
                if(Shoot(value))nextShot=Time.time+BowCooldownTotal;
            }
            else if(CombatLocation&&NativeRangedEquipped&&hotbar.SelectedSlot==0&&!BowHeld&&canShoot&&primaryHeld&&crossbowAmmo>0&&Time.time>=nextCrossbowShot)
            {
                if(Shoot(value,true))
                {
                    crossbowAmmo--;
                    nextCrossbowShot=Time.time+BowCooldownTotal;
                    if(crossbowAmmo==0)crossbowReloadAt=Time.time+CrossbowReloadSeconds;
                }
            }
            TickFood(value, primaryHeld);
            TickArrows();
            if (!CombatLocation) ClearArrows();
            if (Time.unscaledTime >= nextVisualScan)
            {
                nextVisualScan = Time.unscaledTime + combat.ScanInterval;
                ScanVisuals();
            }
            TickBlocks();
        }

        private static void PlacedInBasePostfix(){if(current!=null)current.homeEquipPending=true;}

        private static void BuildStartedPostfix(Interaction_PlayerBuild __instance,PlayerFarming __0)
        {
            var plot=__instance.buildSitePlot;
            if(plot!=null)current?.BindConstruction(__0,plot,plot.StructureInfo);
        }

        private static void ProjectStartedPostfix(Interaction_PlayerBuildProject __instance,PlayerFarming __0)
        {
            var plot=__instance.buildSitePlot;
            if(plot!=null)current?.BindConstruction(__0,plot,plot.StructureInfo);
        }

        private void BindConstruction(PlayerFarming owner,Component plot,StructuresData data)
        {
            if(owner==null||data==null)return;
            var cost=StructuresData.GetCost(data.ToBuildType);
            if(cost==null||cost.Count==0){construction.Remove(owner);return;}
            string[] items=new string[cost.Count];
            for(int i=0;i<items.Length;i++)
            {
                int id=(int)cost[i].CostItem;
                string texture=ResourceTextureId(id);
                items[i]=texture!=null&&assets.IsAssetReady(texture)?texture:"native_material_"+id;
            }
            construction[owner]=new ConstructionRecipe {Plot=plot,Items=items};
        }

        internal string ConstructionItem(PlayerFarming owner,float nativeTime)
        {
            ConstructionRecipe recipe;
            if(!construction.TryGetValue(owner,out recipe)||recipe.Plot==null||!recipe.Plot.gameObject.activeInHierarchy)return "";
            float interval=Mathf.Min(Spec.Visual("build_item_cycle").Value,0.95f/recipe.Items.Length);
            int index=Mathf.FloorToInt(Mathf.Max(0f,nativeTime)/interval)%recipe.Items.Length;
            return recipe.Items[index];
        }

        internal Sprite ConstructionMaterialIcon(int id)
        {
            if(inventoryIcons==null)
            {
                var maps=Resources.FindObjectsOfTypeAll<InventoryIconMapping>();
                if(maps.Length>0)inventoryIcons=maps[0];
            }
            return inventoryIcons==null?null:inventoryIcons.GetImage((InventoryItem.ITEM_TYPE)id);
        }

        private static void WeaponChangedPostfix(PlayerWeapon __instance,EquipmentType __0)
        {
            if(current!=null&&__instance.playerFarming==current.player&&IsRangedWeapon(__0))
                current.ResetCrossbow();
        }

        private void ResetCrossbow()
        {
            crossbowAmmo=CrossbowCapacity;
            nextCrossbowShot=crossbowReloadAt=0f;
        }

        private void TickCrossbow(PlayerFarming owner)
        {
            if(!CombatLocation||!NativeRangedEquipped)return;
            if(crossbowReloadAt>0f&&Time.time>=crossbowReloadAt)
            {
                crossbowAmmo=CrossbowCapacity;
                crossbowReloadAt=0f;
            }
            var ammo=owner.playerWeapon?.blunderAmmo;
            if(ammo!=null&&ammo.gameObject.activeSelf)
            {
                // The native gun HUD shows bullets even when slot two is selected.
                // This replacement shows its actual arrow count in slot one.
                ammo.gameObject.SetActive(false);
                Lamb.UI.UIManager.Instance?.SetCurrentCursor(0);
            }
        }

        private static bool CanAct(PlayerFarming p)
        {
            if (p == null || !p.gameObject.activeInHierarchy || p.state == null || p.IsKnockedOut || p.GoToAndStopping || Time.timeScale <= 0f || LetterBox.IsPlaying || MMConversation.isPlaying) return false;
            if (Lamb.UI.UIManager.Instance != null && Lamb.UI.UIManager.Instance.MenusBlocked) return false;
            var state = p.state.CURRENT_STATE;
            return state == StateMachine.State.Idle || state == StateMachine.State.Moving || state == StateMachine.State.Idle_Winter || state == StateMachine.State.Moving_Winter;
        }

        private bool BowKeyHeld()
        {
            if (!Application.isFocused && !testInputAllowed) return false;
            // Additional input: no native Rewired action or curse binding is intercepted.
            try { if (Input.GetKey(KeyCode.R)) return true; } catch (InvalidOperationException) { }
            return Keyboard.current != null && Keyboard.current.rKey.isPressed;
        }

        private bool CanUseHotbar()
        {
            return player != null && player.enabled && player.state != null && player.gameObject.activeInHierarchy && !player.IsKnockedOut && !player.GoToAndStopping &&
                (Application.isFocused || testInputAllowed) && Time.timeScale > 0f && !LetterBox.IsPlaying && !MMConversation.isPlaying &&
                (viewRig == null || !viewRig.SettingsOpen) &&
                (Lamb.UI.UIManager.Instance == null || !Lamb.UI.UIManager.Instance.MenusBlocked);
        }

        private bool PrimaryHeld()
        {
            if (!CanUseHotbar()) return false;
            if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;
            readingPrimary = true;
            try { return InputManager.Gameplay != null && InputManager.Gameplay.GetAttackButtonHeld(player); }
            finally { readingPrimary = false; }
        }

        private static void PrimaryInputPostfix(PlayerFarming playerFarming, ref bool __result)
        {
            Gameplay instance = current;
            if (instance == null || instance.disposed || instance.readingPrimary) return;
            PlayerFarming owner = playerFarming ?? PlayerFarming.Instance;
            if (owner == null || owner != instance.player) return;
            // Suppress only gameplay attack actions. Native right-click curses and UI input remain native.
            if ((instance.viewRig != null && instance.viewRig.SettingsOpen) || instance.hotbar.PointerOverBar ||
                instance.hotbar.SelectedSlot != 0 || instance.hotbar.EmptyHand || IsRangedWeapon(owner.currentWeapon) || (instance.CombatLocation && instance.BowKeyHeld())) __result = false;
        }

        private static bool NativeFirearmPrefix(PlayerWeapon __instance, ref bool __result)
        {
            Gameplay instance = current;
            PlayerFarming owner = __instance == null ? null : __instance.playerFarming;
            if (instance == null || instance.disposed || owner == null || owner != instance.player || !IsRangedWeapon(owner.currentWeapon)) return true;
            // Native normal and heavy blunderbuss attacks both enter this routine.
            // Do not consume firearm ammo or emit a second projectile beside our arrow.
            __result = false;
            return false;
        }

        private void TickFood(PlayerFarming owner, bool primaryHeld)
        {
            ConsumableRow food = hotbar.Food;
            HealthPlayer health = owner == null ? null : owner.health;
            bool canEat = hotbar.SelectedSlot == 2 && !hotbar.FoodPickerOpen && !BowKeyHeld() && CanUseHotbar() && CanAct(owner) && !hotbar.PointerOverBar && primaryHeld &&
                Time.time >= nextFoodUse && health != null && health.CurrentHP > 0f &&
                (food.Heart != "red" || health.HP < health.totalHP) && FoodCount > 0;
            if (!canEat) { eatingTime = 0f; return; }
            if (eatingFoodId != food.NativeId) { eatingFoodId = food.NativeId; eatingTime = 0f; }
            if (eatingTime <= 0f) eatingHealth = health.CurrentHP;
            else if (health.CurrentHP < eatingHealth) { eatingTime = 0f; nextFoodUse = Time.time + 0.3f; return; }
            eatingTime += Time.deltaTime;
            if (eatingTime < food.UseSeconds) return;
            eatingTime = 0f;
            if (CountFood(food) == 0) return;
            float before = health.CurrentHP;
            switch (food.Heart)
            {
                case "blue": health.BlueHearts += food.Healing; break;
                case "diseased": health.BlackHearts += food.Healing; break;
                default: health.Heal(food.Healing); break;
            }
            LastFoodHealing = health.CurrentHP - before;
            if (LastFoodHealing <= 0f) return;
            // Dungeon loot is a subset of the global inventory. Consume it too, so exit/death
            // reconciliation cannot restore eaten food or subtract it for a second time.
            InventoryItem dungeonFood = Inventory.GetDungeonItemByType(food.NativeId);
            if (dungeonFood != null && dungeonFood.quantity > 0)
            {
                dungeonFood.quantity--;
                if (dungeonFood.quantity == 0) Inventory.itemsDungeon.Remove(dungeonFood);
            }
            Inventory.ChangeItemQuantity(food.NativeId, -1);
            FoodsEaten++;
            nextFoodUse = Time.time + food.Cooldown;
        }

        private static WeaponRow RowForLevel(int level, out int tier)
        {
            WeaponRow selected = Spec.WeaponRows[0];
            tier = 0;
            for (int i = 0; i < Spec.WeaponRows.Length; i++)
            {
                WeaponRow candidate = Spec.WeaponRows[i];
                if (candidate.MinLevel <= level && candidate.MinLevel >= selected.MinLevel)
                {
                    selected = candidate;
                    tier = i;
                }
            }
            return selected;
        }

        private Texture2D Texture(string id)
        {
            return assets.Texture(id);
        }

        private static bool IsAxe(EquipmentType type)
        {
            switch(type)
            {
                case EquipmentType.Axe: case EquipmentType.Axe_Poison: case EquipmentType.Axe_Critical: case EquipmentType.Axe_Healing:
                case EquipmentType.Axe_Fervour: case EquipmentType.Axe_Godly: case EquipmentType.Axe_Nercomancy: case EquipmentType.Axe_Legendary: return true;
                default:return false;
            }
        }
        private static bool IsHammer(EquipmentType type)
        {
            switch(type)
            {
                case EquipmentType.Hammer: case EquipmentType.Hammer_Poison: case EquipmentType.Hammer_Critical: case EquipmentType.Hammer_Healing:
                case EquipmentType.Hammer_Fervour: case EquipmentType.Hammer_Godly: case EquipmentType.Hammer_Nercomancy: case EquipmentType.Hammer_Legendary: return true;
                default:return false;
            }
        }
        public static bool IsRangedWeapon(EquipmentType type)
        {
            switch(type)
            {
                case EquipmentType.Blunderbuss: case EquipmentType.Blunderbuss_Poison: case EquipmentType.Blunderbuss_Critical: case EquipmentType.Blunderbuss_Healing:
                case EquipmentType.Blunderbuss_Fervour: case EquipmentType.Blunderbuss_Godly: case EquipmentType.Blunderbuss_Nercomancy: case EquipmentType.Blunderbuss_Legendary: return true;
                default:return false;
            }
        }
        private static bool IsSword(EquipmentType type)
        {
            switch(type)
            {
                case EquipmentType.Sword: case EquipmentType.Sword_Poison: case EquipmentType.Sword_Critical: case EquipmentType.Sword_Healing:
                case EquipmentType.Sword_Fervour: case EquipmentType.Sword_Godly: case EquipmentType.Sword_Nercomancy: case EquipmentType.Sword_Legendary: case EquipmentType.Sword_Ratau: return true;
                default:return false;
            }
        }
        internal static string PlayerWeaponTexture(PlayerFarming player)
        {
            if(player==null||player.currentWeapon==EquipmentType.None)return "";
            WeaponRow row=RowForLevel(Math.Max(1,player.currentWeaponLevel),out int tier);
            return WeaponTexture(row.Texture,player.currentWeapon);
        }
        private static string WeaponTexture(string swordId, EquipmentType nativeType)
        {
            if(IsRangedWeapon(nativeType))return "crossbow_arrow";
            if(IsHammer(nativeType))return "mace";
            return IsAxe(nativeType)?swordId.Replace("_sword","_axe"):swordId;
        }
        public static string ResourceTextureId(int nativeId)
        {
            foreach(ResourceRow resource in Spec.ResourceRows)if(resource.NativeId==nativeId)return resource.Texture;
            return null;
        }

        private Sprite Sprite(string id)
        {
            Sprite result;
            if (sprites.TryGetValue(id, out result)) return result;
            Texture2D texture = Texture(id);
            if (texture == null) return null;
            result = UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f, combat.SpritePixelsPerUnit);
            result.name = "CultCraft local Minecraft sprite: " + id;
            sprites.Add(id, result);
            return result;
        }

        private static void ResourceIconPostfix(InventoryItem.ITEM_TYPE type, ref Sprite __result)
        {
            if (current == null || current.disposed || !current.assets.Ready) return;
            foreach (ResourceRow resource in Spec.ResourceRows)
                if (resource.NativeId == (int)type) { __result = current.Sprite(resource.Texture) ?? __result; break; }
        }

        private static void WeaponIconPostfix(EquipmentType __0, ref Sprite __result)
        {
            if (current != null && !current.disposed && current.assets.Ready && current.CombatLocation && (IsSword(__0) || IsAxe(__0) || IsHammer(__0) || IsRangedWeapon(__0)))
                __result = current.Sprite(WeaponTexture(current.row.Texture, __0)) ?? __result;
        }

        private static void DamagePostfix(PlayerFarming playerFarming, ref float __result)
        {
            if (current != null && !current.disposed && current.IsDungeon && current.player == playerFarming &&
                !IsRangedWeapon(playerFarming.currentWeapon) && !IsHammer(playerFarming.currentWeapon))
                __result = __result * current.row.DamageMultiplier + current.enchantments.MeleeBonus(current.lastLevel);
        }

        private bool Shoot(PlayerFarming owner,bool crossbow=false)
        {
            Vector2 direction = Aim(owner);
            if (direction.sqrMagnitude < 0.5f) return false;
            owner.state.facingAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            if (arrowEntityMesh == null)
            {
                Texture2D atlas = assets.Texture("arrow_entity");
                arrowEntityMesh = ArrowEntityVisual.Build(atlas);
                meshes.Add(arrowEntityMesh);
                arrowEntityMaterial = new Material(Shader.Find("Unlit/Texture")) { mainTexture = atlas };
                materials.Add(arrowEntityMaterial);
            }
            var go = new GameObject("CultCraft Minecraft arrow");
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = arrowEntityMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = arrowEntityMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 10;
            if (testInputAllowed)
            {
                go.AddComponent<BookRenderProbe>().Observed = camera =>
                {
                    if (camera == Camera.main && filter.sharedMesh == arrowEntityMesh &&
                        renderer.sharedMaterial.mainTexture == assets.Texture("arrow_entity") &&
                        filter.sharedMesh.bounds.size.z > 0.05f && go.GetComponent<SpriteRenderer>() == null)
                        ArrowEntityDrawFrames++;
                };
            }
            go.transform.localScale = Vector3.one * combat.VisualScale;
            Vector2 position = (Vector2)owner.transform.position + direction * combat.SpawnOffset;
            float height = owner.transform.position.z - combat.Height;
            // Native collisions run on the XY plane. Point local X along the
            // velocity and keep the fletching in world space from either view.
            go.transform.SetPositionAndRotation(new Vector3(position.x, position.y, height),
                Quaternion.LookRotation(Vector3.forward, Vector3.Cross(Vector3.forward, direction)));
            arrows.Add(new Arrow {
                Object = go, Owner = owner, Position = position,
                Velocity = direction * combat.Speed, Height = height,
                Life = combat.Lifetime, Damage = row.BowDamage + enchantments.BowBonus(lastLevel, crossbow)
            });
            ShotsFired++;
            return true;
        }

        private Vector2 Aim(PlayerFarming owner)
        {
            if (viewRig != null && viewRig.Active)
            {
                float yaw = viewRig.Yaw * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
            }
            Camera camera = Camera.main;
            if (camera != null)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Vector2 forward = camera.transform.forward;
                    if (forward.sqrMagnitude > 0.001f) return forward.normalized;
                }
                Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2)Input.mousePosition;
                Ray ray = camera.ScreenPointToRay(mouse);
                var floor = new Plane(Vector3.forward, new Vector3(0f, 0f, owner.transform.position.z));
                if (floor.Raycast(ray, out float distance))
                {
                    Vector2 dir = (Vector2)(ray.GetPoint(distance) - owner.transform.position);
                    if (dir.sqrMagnitude > 0.001f) return dir.normalized;
                }
            }
            float angle = owner.state.facingAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private void TickArrows()
        {
            if (Time.deltaTime <= 0f) return;
            int obstructionMask = LayerMask.GetMask("Obstacles", "Island");
            for (int i = arrows.Count - 1; i >= 0; i--)
            {
                Arrow arrow = arrows[i];
                bool remove = arrow.Object == null || arrow.Owner == null || !CombatLocation;
                if (!remove)
                {
                    float step = arrow.Velocity.magnitude * Time.deltaTime;
                    Vector2 direction = arrow.Velocity.normalized;
                    RaycastHit2D[] hits = Physics2D.CircleCastAll(arrow.Position, combat.HitRadius, direction, step);
                    Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                    foreach (RaycastHit2D hit in hits)
                    {
                        if (hit.collider == null || hit.collider.transform.IsChildOf(arrow.Owner.transform)) continue;
                        // Native projectiles resolve Health on the collider object itself;
                        // child proximity/attack triggers must not act as larger damage hitboxes.
                        Health health = hit.collider.GetComponent<Health>();
                        if (health != null && health.enabled && health.gameObject.activeInHierarchy && health.team == Health.Team.Team2 &&
                            !health.IsCharmedEnemy && !health.untouchable && !health.invincible && !health.IgnoreProjectiles &&
                            !health.CompareTag("ProjectileIgnore") && health.CurrentHP > 0f &&
                            (health.state == null || health.state.CURRENT_STATE != StateMachine.State.Dodging))
                        {
                            float before = health.CurrentHP;
                            bool damaged = health.DealDamage(arrow.Damage, arrow.Owner.gameObject, new Vector3(hit.point.x, hit.point.y, arrow.Height), false, Health.AttackTypes.Projectile, true, (Health.AttackFlags)0);
                            if (damaged && health.CurrentHP < before) ArrowDamageHits++;
                            remove = true;
                            break;
                        }
                        if (health == null && !hit.collider.isTrigger && ((1 << hit.collider.gameObject.layer) & obstructionMask) != 0)
                        {
                            remove = true;
                            break;
                        }
                    }
                    arrow.Position += arrow.Velocity * Time.deltaTime;
                    arrow.Life -= Time.deltaTime;
                    remove |= arrow.Life <= 0f;
                    if (!remove)
                        arrow.Object.transform.position = new Vector3(arrow.Position.x, arrow.Position.y, arrow.Height);
                }
                if (remove)
                {
                    if (arrow.Object != null) UnityEngine.Object.Destroy(arrow.Object);
                    arrows.RemoveAt(i);
                }
            }
        }

        private void ScanVisuals()
        {
            RefreshBlockStates();
            // Pickups are styled synchronously by their real pool, SetImage and
            // factory hooks. This maintained structure list is not an item scan.
            if (Structure.Structures == null) return;
            foreach (Structure structure in Structure.Structures)
            {
                if (structure == null || !structure.gameObject.activeInHierarchy || structure.Brain == null || structure.Structure_Info == null || HasBlock(structure)) continue;
                if (VegetationState.IsVegetation(structure) && VegetationState.Resolve(structure) == null) continue;
                if ((structure.Type == StructureBrain.TYPES.RUBBLE || structure.Type == StructureBrain.TYPES.RUBBLE_BIG) && structure.Structure_Info.LootToDrop != InventoryItem.ITEM_TYPE.STONE) continue;
                foreach (BuildingRow building in Spec.BuildingRows)
                    if ((int)structure.Type == building.NativeType)
                    {
                        if(building.Model=="straw_bed" && (!assets.IsAssetReady("straw_bed") || !assets.IsAssetReady("straw_bed_head_model") || !assets.IsAssetReady("straw_bed_foot_model"))) break;
                        AddBlock(structure, building); break;
                    }
            }
        }

        private Sprite PickupWeaponSprite(EquipmentType type,int level)
        {WeaponRow pickupRow=RowForLevel(Math.Max(1,level),out int ignored);return Sprite(WeaponTexture(pickupRow.Texture,type));}

        private bool HasBlock(Structure native)
        {
            return blockIndex.Contains(native);
        }

        private void AddBlock(Structure native, BuildingRow design)
        {
            var record = new BlockVisual { Native = native, NativeType = (int)native.Type, NativeData = native.Structure_Info, Design = design, VegetationId = VegetationState.Resolve(native), VegetationScale = VegetationState.Scale(native) };
            CaptureBlockSources(record);
            record.AnchorOffset = WorldResourceVisuals.NativeVisualAnchor(native) - native.transform.position;
            record.Berries = native.GetComponentInChildren<Interaction_Berries>(true);
            record.RockReadyAt = Time.unscaledTime + 0.4f;
            BuildBlockObject(record);
            blocks.Add(record);
            blockIndex.Add(native);
        }

        private void CaptureBlockSources(BlockVisual record)
        {
            Structure native = record.Native;
            record.Colliders.Clear();
            var collisionSources = new HashSet<Collider2D>(native.GetComponentsInChildren<Collider2D>(true));
            if (native.ObstructionColldiers != null) foreach (Collider2D collider in native.ObstructionColldiers) if (collider != null) collisionSources.Add(collider);
            foreach (Collider2D collider in collisionSources)
                if (collider != null) record.Colliders.Add(new ColliderSnapshot { Collider = collider, Enabled = collider.enabled, IsTrigger = collider.isTrigger });
            CaptureBlockRenderers(record);
        }

        private void CaptureBlockRenderers(BlockVisual record)
        {
            Structure native = record.Native;
            bool cropOnly = native.Type == StructureBrain.TYPES.FARM_PLOT;
            if (cropOnly)
            {
                // Native planting can instantiate the active CropController after
                // the first data-stage scan, and can replace it on replanting.
                record.Crop = null;
                foreach (CropController controller in native.GetComponentsInChildren<CropController>(true))
                    if (controller.SeedType == InventoryItem.ITEM_TYPE.SEED && controller.gameObject.activeInHierarchy) { record.Crop = controller; break; }
            }
            // A farm keeps its soil, tile, UI and colliders: hide only the berry crop art.
            foreach (Renderer renderer in native.GetComponentsInChildren<Renderer>(true))
                if (!(renderer is ParticleSystemRenderer) && renderer.GetComponentInParent<Canvas>() == null && !IsInteractionVisual(renderer.name) &&
                    (!cropOnly || (renderer.GetComponentInParent<CropController>() != null && renderer.GetComponentInParent<CropController>().SeedType == InventoryItem.ITEM_TYPE.SEED)) && !record.Renderers.ContainsKey(renderer))
                    record.Renderers.Add(renderer, renderer.enabled);
        }

        private void BuildBlockObject(BlockVisual record)
        {
            Structure native = record.Native;
            int materialStart = materials.Count;
            int meshStart = meshes.Count;
            record.Object = new GameObject("CultCraft Minecraft blocks: " + record.Design.Id + (record.VegetationId == null ? "" : "/" + record.VegetationId));
            // Native trees/structures may carry negative Spine scale or a tilted art transform.
            // Follow world position only; preserve the native collider/animation hierarchy unchanged.
            record.Object.transform.position = record.Crop != null ? record.Crop.transform.position : native.transform.position + record.AnchorOffset;
            record.Object.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            ApplyBedPose(record);
            try
            {
                if (record.Design.Model == "rock_pile")
                {
                    record.Design = RockPileFit.Layout(native.transform, record.Renderers.Keys, record.Design);
                    RockPileFit.Align(record.Object.transform, native.transform, record.Renderers.Keys);
                }
                new BlockModels(assets, record.Object.transform, materials, meshes).Build(record.Design, record.VegetationId, record.VegetationScale,
                    record.Design.Model == "rock_pile" ? RockPileFit.BlockingFootprint(native.transform) : null);
                if (record.Design.Model == "rock_pile" && RockPileFit.Align(record.Object.transform, native.transform, record.Renderers.Keys))
                    record.AnchorOffset = record.Object.transform.position - native.transform.position;
            }
            catch
            {
                // A missing/unsupported runtime resource must leave native art
                // recoverable and must not leak each partially generated model.
                while (materials.Count > materialStart)
                {
                    Material partial = materials[materials.Count - 1];
                    materials.RemoveAt(materials.Count - 1);
                    if (partial != null) UnityEngine.Object.Destroy(partial);
                }
                while (meshes.Count > meshStart)
                {
                    Mesh partial = meshes[meshes.Count - 1];
                    meshes.RemoveAt(meshes.Count - 1);
                    if (partial != null) UnityEngine.Object.Destroy(partial);
                }
                record.Object.SetActive(false);
                UnityEngine.Object.Destroy(record.Object);
                record.Object = null;
                throw;
            }
            record.Materials.AddRange(materials.GetRange(materialStart, materials.Count-materialStart));
            record.Meshes.AddRange(meshes.GetRange(meshStart, meshes.Count-meshStart));
            record.Revisions++;
        }

        private void RefreshBlockStates()
        {
            // Stage discovery is throttled with the ordinary 1s visual scan. Meshes
            // rebuild only when native growth/picking actually changes the profile.
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                BlockVisual record = blocks[i];
                if (record.Native == null || record.Native.Brain == null || record.Native.Structure_Info != record.NativeData) continue;
                if (record.Design.Model == "rock_pile")
                {
                    int count = record.Renderers.Count;
                    CaptureBlockRenderers(record);
                    if (Time.unscaledTime >= record.RockReadyAt && (!record.RockFitted || count != record.Renderers.Count))
                    {
                        ReleaseBlockObject(record);
                        record.AnchorOffset = WorldResourceVisuals.NativeVisualAnchor(record.Native) - record.Native.transform.position;
                        BuildBlockObject(record); record.RockFitted = true;
                        RockPileFit.Aligned(record.Object.transform, record.Native.transform, out string detail);
                        logger.LogInfo("Native stone hill assembled with fixed0.4 cubes: " + record.Native.Type + "; " + detail);
                    }
                    continue;
                }
                if (!VegetationState.IsVegetation(record.Native)) continue;
                // Refresh late-created crop art even if the growth profile has not
                // changed. The collider baseline is untouched on these scans.
                if (record.Native.Type == StructureBrain.TYPES.FARM_PLOT) CaptureBlockRenderers(record);
                string next = VegetationState.Resolve(record.Native);
                float nextScale = VegetationState.Scale(record.Native);
                if (next == null)
                {
                    RestoreBlock(record);
                    blocks.RemoveAt(i);
                    continue;
                }
                if (next == record.VegetationId && nextScale == record.VegetationScale) continue;
                // Native growth can legitimately alter colliders. Snapshot those
                // current states before replacing only the visual model.
                CaptureBlockSources(record);
                ReleaseBlockObject(record);
                record.VegetationId = next;
                record.VegetationScale = nextScale;
                try { BuildBlockObject(record); }
                catch
                {
                    RestoreBlock(record);
                    blocks.RemoveAt(i);
                    throw;
                }
            }
        }

        private static bool IsInteractionVisual(string name)
        {
            string value = (name ?? "").ToLowerInvariant();
            return value.Contains("prompt") || value.Contains("indicator") || value.Contains("icon") || value.Contains("cursor");
        }

        private Material BlockMaterial(string id, Color color)
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Texture");
            var material = new Material(shader) { mainTexture = Texture(id), color = color };
            material.name = "CultCraft block: " + id;
            materials.Add(material);
            return material;
        }

        private void AddCube(Transform parent, Vector3 position, Vector3 scale, Material side, Material top)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = "Minecraft visual block";
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            Collider collider = obj.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
            MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Mesh source = obj.GetComponent<MeshFilter>().sharedMesh;
            Mesh mesh = UnityEngine.Object.Instantiate(source);
            Color[] vertexColors = new Color[mesh.vertexCount];
            for (int n = 0; n < vertexColors.Length; n++) vertexColors[n] = Color.white;
            mesh.colors = vertexColors;
            // Unity's cube uses separate vertices on each face. Split its upward face into a top material.
            int[] triangles = mesh.triangles;
            Vector3[] normals = mesh.normals;
            var sides = new List<int>();
            var tops = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                List<int> target = normals[triangles[i]].y > 0.5f ? tops : sides;
                target.Add(triangles[i]); target.Add(triangles[i + 1]); target.Add(triangles[i + 2]);
            }
            mesh.subMeshCount = 2;
            mesh.SetTriangles(sides, 0); mesh.SetTriangles(tops, 1);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            renderer.sharedMaterials = new[] { side, top };
            meshes.Add(mesh);
        }

        private void TickBlocks()
        {
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                BlockVisual block = blocks[i];
                if (block.Native == null || block.Native.Brain == null || block.Native.Structure_Info != block.NativeData || (int)block.Native.Type != block.NativeType)
                {
                    RestoreBlock(block);
                    blocks.RemoveAt(i);
                    continue;
                }
                foreach (var entry in block.Renderers) if (entry.Key != null) entry.Key.enabled = false;
                if (block.Object != null)
                {
                    block.Object.transform.position = block.Crop != null ? block.Crop.transform.position : block.Native.transform.position + block.AnchorOffset;
                    ApplyBedPose(block);
                    bool visible = block.Native.gameObject.activeInHierarchy;
                    if ((block.Native.Type == StructureBrain.TYPES.BERRY_BUSH || block.Native.Type == StructureBrain.TYPES.RED_FLOWER_BUSH) && block.Berries != null && block.Berries.DisableOnDie && !block.Berries.gameObject.activeInHierarchy) visible = false;
                    block.Object.SetActive(visible);
                }
            }
        }

        private void RestoreBlock(BlockVisual block)
        {
            blockIndex.Remove(block.Native);
            foreach (var entry in block.Renderers) if (entry.Key != null) entry.Key.enabled = entry.Value;
            ReleaseBlockObject(block);
        }

        private static void ApplyBedPose(BlockVisual block)
        {
            Vector3 position; Quaternion rotation;
            if(block.Object!=null && StationVisuals.TryBedPose(block.Native,block.Design,out position,out rotation))
            {block.Object.transform.position=position;block.Object.transform.rotation=rotation;}
        }

        private void ReleaseBlockObject(BlockVisual block)
        {
            if (block.Object != null) { block.Object.SetActive(false); UnityEngine.Object.Destroy(block.Object); }
            foreach (Material material in block.Materials) { materials.Remove(material); if (material != null) UnityEngine.Object.Destroy(material); }
            foreach (Mesh mesh in block.Meshes) { meshes.Remove(mesh); if (mesh != null) UnityEngine.Object.Destroy(mesh); }
            block.Materials.Clear(); block.Meshes.Clear();
        }

        private void RestoreBlocks()
        {
            foreach (BlockVisual block in blocks) RestoreBlock(block);
            blocks.Clear();
        }

        private void ClearArrows()
        {
            foreach (Arrow arrow in arrows) if (arrow.Object != null) UnityEngine.Object.Destroy(arrow.Object);
            arrows.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            harmony.UnpatchSelf();
            pickupVisuals.Dispose();
            if (current == this) current = null;
            ClearArrows();
            RestoreBlocks();
            foreach (Sprite sprite in sprites.Values) if (sprite != null) UnityEngine.Object.Destroy(sprite);
            foreach (Material material in materials) if (material != null) UnityEngine.Object.Destroy(material);
            foreach (Mesh mesh in meshes) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            sprites.Clear(); materials.Clear(); meshes.Clear();
        }
    }
}
