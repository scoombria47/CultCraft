using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using MMTools;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;

namespace CultCraft
{
    /// <summary>
    /// Keeps native movement, hitboxes and combat on the game's XY plane. Photo mode is
    /// deliberately left independent: the native photo camera pauses gameplay.
    /// </summary>
    public sealed class CameraRig : IDisposable
    {
        private static CameraRig _instance;
        private readonly ManualLogSource _logger;
        private readonly Harmony _harmony;
        private readonly CameraRow _settings;
        private readonly List<VisualCopy> _billboards = new List<VisualCopy>();
        private readonly Dictionary<Renderer, VisualCopy> _visualIndex = new Dictionary<Renderer, VisualCopy>();
        private readonly HashSet<Health> _knownUnits = new HashSet<Health>();
        private readonly List<SkeletonAnimation> _characters = new List<SkeletonAnimation>();
        private readonly HashSet<SkeletonAnimation> _characterIndex = new HashSet<SkeletonAnimation>();
        private readonly Dictionary<BillboardFacingCamera, StaticPropState> _fixedProps = new Dictionary<BillboardFacingCamera, StaticPropState>();
        private readonly HashSet<BillboardFacingCamera> _characterBillboards = new HashSet<BillboardFacingCamera>();
        private readonly HashSet<BillboardFacingCamera> _uiBillboards = new HashSet<BillboardFacingCamera>();
        private readonly Plane[] _frustum = new Plane[6];
        private static readonly Quaternion NativeRotation = Quaternion.Euler(-45f, 0f, 0f);
        private readonly ConfigEntry<float> _sensitivityConfig;
        private readonly ConfigEntry<float> _fovConfig;
        private readonly ConfigEntry<int> _uiScaleConfig;
        private readonly ConfigEntry<bool> _showHintsConfig;
        private readonly ConfigFile _config;
        private float _sensitivity;
        private float _userFov;
        private bool _sceneDirty = true;
        private bool _repairTopDown;
        private int _visualFrame = -1;
        private int _discoveryPasses;
        private int _visibleBillboards;
        private readonly MinecraftSettingsUi _settingsUi;
        private int _settingsSaveCount;
        private readonly Dictionary<Renderer, bool> _hiddenPlayer = new Dictionary<Renderer, bool>();
        private readonly Dictionary<DepthOfField, DepthOfFieldState> _depthOfField = new Dictionary<DepthOfField, DepthOfFieldState>();
        private PlayerFarming _player;
        private Camera _camera;
        private CameraFollowTarget _follower;
        private bool _disposed;
        private bool _readingAxes;
        private bool _testInput;
        private Vector2 _testMovement;
        private bool _testAllowed;
        private bool _perspective;
        private float _fov;
        private float _near;
        private float _orthoSize;
        private Vector3 _vanillaPosition;
        private Quaternion _vanillaRotation;
        private CursorLockMode _cursorLock;
        private bool _cursorVisible;
        private float _yaw = 90f;
        private float _pitch;
        private float _nextRefresh;
        private Vector2 _virtualAimPoint;
        private Vector3 _virtualAimOrigin;
        private int _virtualAimFrame = -1;

        private struct DepthOfFieldState
        {
            public bool Enabled;
            public bool Override;
        }

        private struct StaticPropState
        {
            public Quaternion Original;
            public Quaternion Fixed;
        }

        public bool FirstPerson { get; private set; }
        public event Action<Camera> RenderPoseApplied;
        public bool Active { get; private set; }
        public Camera ActiveCamera { get { return Active ? _camera : null; } }
        public Vector2 LastRawMovement { get; private set; }
        public Vector2 LastWorldMovement { get; private set; }
        public int BillboardCount { get { return _billboards.Count; } }
        public float Yaw { get { return _yaw; } }
        public bool SettingsOpen { get; private set; }
        public float MouseSensitivity { get { return _sensitivity; } }
        public float FieldOfView { get { return _userFov; } }
        public Rect SettingsWindowRect { get { return _settingsUi==null?new Rect():_settingsUi.WindowRect; } }
        public Rect[] SettingsSliderRects { get { return _settingsUi==null?new Rect[0]:_settingsUi.SliderRects; } }
        public Rect SettingsResetRect { get { return _settingsUi==null?new Rect():_settingsUi.ResetRect; } }
        public Rect SettingsCloseRect { get { return _settingsUi==null?new Rect():_settingsUi.CloseRect; } }
        public Rect SettingsScaleRect { get { return _settingsUi==null?new Rect():_settingsUi.ScaleRect; } }
        public Rect SettingsHintsRect { get { return _settingsUi==null?new Rect():_settingsUi.HintsRect; } }
        public int SettingsRenderFrameCount { get { return _settingsUi==null?0:_settingsUi.RenderFrameCount; } }
        public int SettingsSliderChangeCount { get { return _settingsUi==null?0:_settingsUi.SliderChangeCount; } }
        public bool SettingsPixelStyleReady { get { return _settingsUi!=null&&_settingsUi.PixelStyleReady; } }
        public string SettingsStyleError { get { return _settingsUi==null?"Minecraft settings assets were not supplied":_settingsUi.StyleError; } }
        public int SettingsSaveCount { get { return _settingsSaveCount; } }
        internal void SetSkins(PlayerSkins skins){if(_settingsUi!=null)_settingsUi.Skins=skins;}
        internal MinecraftSettingsUi SkinUi { get { return _settingsUi; } }
        public int StaticPropCount { get { return _fixedProps.Count; } }
        public int VisibleBillboardCount { get { return _visibleBillboards; } }
        public int DiscoveryPasses { get { return _discoveryPasses; } }

        public CameraRig(ManualLogSource logger, ConfigFile config = null, MinecraftAssets uiAssets = null)
        {
            _logger = logger;
            _settings = Spec.Camera("first_person");
            _config = config;
            if(uiAssets!=null)_settingsUi=new MinecraftSettingsUi(uiAssets);
            if (config != null)
            {
                _sensitivityConfig = config.Bind("First person", "Mouse sensitivity", _settings.MouseSensitivity,
                    new ConfigDescription("Degrees per raw mouse delta. Adjustable with F6 in game.", new AcceptableValueRange<float>(0.02f, 1f)));
                _fovConfig = config.Bind("First person", "Field of view", _settings.Fov,
                    new ConfigDescription("Vertical field of view in degrees. Adjustable with F6 in game.", new AcceptableValueRange<float>(55f, 110f)));
                _uiScaleConfig = config.Bind("Interface", "Scale", PixelUi.DefaultScaleMode,
                    new ConfigDescription("0 = automatic, 1 = larger, 2 = original, 3 = smaller. Adjustable with F6.", new AcceptableValueRange<int>(0, 3)));
                _showHintsConfig = config.Bind("Interface", "Show hints", true, "Show the control hint below the hotbar. Adjustable with F6.");
            }
            PixelUi.ScaleMode = _uiScaleConfig != null ? _uiScaleConfig.Value : PixelUi.DefaultScaleMode;
            PixelUi.ShowHints = _showHintsConfig == null || _showHintsConfig.Value;
            _sensitivity = _sensitivityConfig != null ? _sensitivityConfig.Value : _settings.MouseSensitivity;
            _userFov = _fovConfig != null ? _fovConfig.Value : _settings.Fov;
            _harmony = new Harmony("cultcraft.camera");
            _instance = this;
            try
            {
                Patch(typeof(RewiredGameplayInputSource), "GetHorizontalAxis", "HorizontalPostfix", false);
                Patch(typeof(RewiredGameplayInputSource), "GetVerticalAxis", "VerticalPostfix", false);
                Patch(typeof(GeneralInputSource), "GetMousePosition", "MousePositionPostfix", false);
                Patch(typeof(GeneralInputSource), "get_MouseInputActive", "MouseActivePostfix", false);
                Patch(typeof(Utils), "GetAngle", "MouseAnglePrefix", true);
                Patch(typeof(PlayerSpells), "GetTargetWithMouse", "SpellTargetPrefix", true);
                Patch(typeof(CameraFollowTarget), "LateUpdate", "FollowerPostfix", false);
                Patch(typeof(CameraFollowTarget), "SetTargetCamera", "SuspendPrefix", true);
                Patch(typeof(BillboardFacingCamera), "Update", "NativeBillboardPrefix", true);
                Patch(typeof(PhotoModeManager), "EnablePhotoMode", "SuspendPrefix", true);
                Patch(typeof(PlayerController), "Update", "ControllerPostfix", false);
                Camera.onPreCull += BeforeCameraRender;
                SceneManager.sceneLoaded += SceneLoaded;
            }
            catch
            {
                // A throwing constructor is never assigned to Plugin.camera, so Plugin
                // cannot dispose it. Roll back any hooks already installed in this call.
                Camera.onPreCull -= BeforeCameraRender;
                SceneManager.sceneLoaded -= SceneLoaded;
                try { _harmony.UnpatchSelf(); }
                catch (Exception cleanupError) { if (_logger != null) _logger.LogError(cleanupError); }
                _disposed = true;
                if (_instance == this) _instance = null;
                throw;
            }
        }

        private void Patch(Type type, string method, string callback, bool prefix)
        {
            var target = AccessTools.Method(type, method);
            if (target == null) throw new MissingMethodException(type.FullName, method);
            var hook = new HarmonyMethod(typeof(CameraRig), callback);
            if (prefix) _harmony.Patch(target, prefix: hook);
            else _harmony.Patch(target, postfix: hook);
        }

        public void Toggle()
        {
            FirstPerson = !FirstPerson;
            if (!FirstPerson) { _repairTopDown = true; Suspend(); RepairNativeRotation(); }
            if (_logger != null) _logger.LogInfo("Camera requested: " + (FirstPerson ? "first person" : "native top-down"));
        }

        public void ToggleSettings()
        {
            SettingsOpen = !SettingsOpen;
            if (SettingsOpen)
            {
                if(_settingsUi!=null)_settingsUi.Open(_sensitivity,_userFov);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else {if(_settingsUi!=null)_settingsUi.Release();SaveSettings();}
        }

        public void DrawSettings()
        {
            if (!SettingsOpen) return;
            if(_settingsUi==null)return;
            bool close=_settingsUi.Draw(_settings.MouseSensitivity,_settings.Fov,_testAllowed);
            _sensitivity=_settingsUi.Sensitivity;_userFov=_settingsUi.Fov;
            if(close)ToggleSettings();
        }

        private void SaveSettings()
        {
            if (_config == null) return;
            _sensitivityConfig.Value = Mathf.Clamp(_sensitivity, 0.02f, 1f);
            _fovConfig.Value = Mathf.Clamp(_userFov, 55f, 110f);
            _uiScaleConfig.Value = PixelUi.ScaleMode;
            _showHintsConfig.Value = PixelUi.ShowHints;
            _config.Save();
            _settingsSaveCount++;
        }

        public void RestoreTestPreferences(float sensitivity,float fov,int uiScale,bool showHints)
        {
            if(!_testAllowed)throw new InvalidOperationException("Preference test cleanup requires the explicit test harness marker.");
            _sensitivity=Mathf.Clamp(sensitivity,0.02f,1f);_userFov=Mathf.Clamp(fov,55f,110f);
            PixelUi.ScaleMode = uiScale;
            PixelUi.ShowHints = showHints;
            if(SettingsOpen&&_settingsUi!=null)_settingsUi.Open(_sensitivity,_userFov);
            SaveSettings();
        }

        public void Tick(PlayerFarming player)
        {
            if (_disposed) return;
            if (_player != player)
            {
                Suspend();
                _player = player;
                _testInput = false;
                _yaw = player != null && player.state != null ? player.state.LookAngle : 90f;
                _pitch = 0f;
            }
            if (!CanControl()) { Suspend(); return; }
            if (!Active) Activate();
            if (!Active) return;
            // Hitstop and the mod settings window keep the perspective intact. They
            // freeze look input, not the camera mode, so a kill never flashes top-down.
            if (SettingsOpen || Time.timeScale <= 0f) return;

            // Native Rewired mouse deltas do not depend on legacy Unity Input axis names.
            if (!_testInput && player.canUseKeyboard && player.rewiredPlayer != null &&
                !InputManager.General.InputIsController(player))
            {
                var mouse = player.rewiredPlayer.controllers.Mouse;
                if (mouse != null)
                {
                    _yaw -= mouse.GetAxisRaw(_settings.MouseHorizontalAxis) * _sensitivity;
                    _pitch += mouse.GetAxisRaw(_settings.MouseVerticalAxis) * _sensitivity;
                }
            }
            else if (!_testInput)
            {
                _yaw -= InputManager.Gameplay.GetHorizontalSecondaryAxis(player) * _settings.ControllerLookSpeed * Time.unscaledDeltaTime;
                _pitch += InputManager.Gameplay.GetVerticalSecondaryAxis(player) * _settings.ControllerLookSpeed * Time.unscaledDeltaTime;
            }
            _yaw = Mathf.Repeat(_yaw, 360f);
            _pitch = Mathf.Clamp(_pitch, -_settings.PitchLimit, _settings.PitchLimit);
            SetLook(player);
            if (Application.isFocused && !SettingsOpen)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void LateTick(PlayerFarming player)
        {
            if (_disposed) return;
            if (player != _player || !CanControl()) { Suspend(); return; }
            if (!Active) return;
            ApplyPose();
            if (Time.unscaledTime >= _nextRefresh)
            {
                RefreshVisuals();
                _nextRefresh = Time.unscaledTime + _settings.RefreshSeconds;
            }
        }

        private bool CanControl()
        {
            if (!FirstPerson || _disposed || (!Application.isFocused && !_testAllowed) ||
                _player == null || !_player.gameObject.activeInHierarchy || !_player.enabled ||
                _player.state == null || _player.GoToAndStopping || _player.IsKnockedOut ||
                PhotoModeManager.PhotoModeActive || PhotoModeManager.TakeScreenShotActive ||
                LetterBox.IsPlaying || (MMConversation.isPlaying && !MMConversation.isBark) || MMTransition.IsPlaying || PlayerFarming.playersCount > 1)
                return false;
            // MenusBlocked also becomes true whenever native combat slow-motion
            // lowers timeScale below .25. It does not mean a menu is actually open.
            // Using it here caused every hit/kill freeze to flash the native view.
            var ui = Lamb.UI.UIManager.Instance;
            if ((ui != null && (ui.IsPaused || ui.CurrentMenu != null)) ||
                Lamb.UI.UIMenuBase.ActiveMenus.Count > 0 || GameManager.InMenu) return false;
            var follower = CameraFollowTarget.Instance;
            if (follower == null || !follower.enabled || follower.TargetCamera != null || follower.IN_CONVERSATION) return false;
            switch (_player.state.CURRENT_STATE)
            {
                case StateMachine.State.Idle:
                case StateMachine.State.Moving:
                case StateMachine.State.Attacking:
                case StateMachine.State.Defending:
                case StateMachine.State.RecoverFromAttack:
                case StateMachine.State.Dodging:
                case StateMachine.State.AimDodge:
                case StateMachine.State.Casting:
                case StateMachine.State.Aiming:
                case StateMachine.State.ChargingHeavyAttack:
                case StateMachine.State.HitLeft:
                case StateMachine.State.HitRight:
                case StateMachine.State.HitRecover:
                case StateMachine.State.HitThrown:
                case StateMachine.State.KnockBack:
                case StateMachine.State.Idle_CarryingBody:
                case StateMachine.State.Moving_CarryingBody:
                case StateMachine.State.Idle_Winter:
                case StateMachine.State.Moving_Winter:
                case StateMachine.State.ChargingSnowball:
                case StateMachine.State.TimedAction:
                case StateMachine.State.CustomAnimation:
                case StateMachine.State.Building:
                case StateMachine.State.Meditate:
                case StateMachine.State.Heal:
                    return true;
                default:
                    return false;
            }
        }

        private void Activate()
        {
            _follower = CameraFollowTarget.Instance;
            _camera = _follower != null ? _follower.GetComponent<Camera>() : null;
            if (_camera == null || !_camera.enabled) return;
            _vanillaPosition = _camera.transform.position;
            // Native LateUpdate moves only the position. It cannot fix a camera
            // whose FPS rotation was captured during a previous transient suspend.
            _vanillaRotation = NativeRotation;
            _perspective = _camera.orthographic;
            _fov = _camera.fieldOfView;
            _near = _camera.nearClipPlane;
            _orthoSize = _camera.orthographicSize;
            _cursorLock = Cursor.lockState;
            _cursorVisible = Cursor.visible;
            Active = true;
            _visualFrame = -1;
            _nextRefresh = 0f;
            // Suspend restores and releases each volume snapshot. Re-capture on every
            // activation, including repeated F5 toggles in the same loaded scene.
            CaptureDepthOfField();
            ApplyPose();
            RefreshVisuals();
        }

        private void ApplyPose()
        {
            if (!Active || _camera == null || _player == null) return;
            float heading = _yaw * Mathf.Deg2Rad;
            float pitch = _pitch * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Cos(heading) * Mathf.Cos(pitch), Mathf.Sin(heading) * Mathf.Cos(pitch), -Mathf.Sin(pitch));
            _camera.transform.SetPositionAndRotation(_player.transform.position + Vector3.back * _settings.EyeHeight,
                Quaternion.LookRotation(forward, Vector3.back));
            _camera.orthographic = !_settings.Perspective;
            _camera.fieldOfView = _userFov;
            _camera.nearClipPlane = _settings.NearClip;
            HoldDepthOfFieldDisabled();
        }

        private void Suspend()
        {
            if (!Active) return;
            Active = false;
            _testInput = false;
            if (_camera != null)
            {
                _camera.transform.SetPositionAndRotation(_vanillaPosition, _vanillaRotation);
                _camera.orthographic = _perspective;
                _camera.fieldOfView = _fov;
                _camera.nearClipPlane = _near;
                _camera.orthographicSize = _orthoSize;
            }
            foreach (var copy in _billboards) copy.Dispose();
            _billboards.Clear();
            _visualIndex.Clear();
            foreach (var entry in _fixedProps)
                if (entry.Key != null) entry.Key.transform.rotation = entry.Value.Original;
            _fixedProps.Clear();
            _characterBillboards.Clear();
            _uiBillboards.Clear();
            foreach (var item in _hiddenPlayer)
                if (item.Key != null) item.Key.forceRenderingOff = item.Value;
            _hiddenPlayer.Clear();
            foreach (var entry in _depthOfField)
            {
                if (entry.Key == null) continue;
                entry.Key.enabled.value = entry.Value.Enabled;
                entry.Key.enabled.overrideState = entry.Value.Override;
            }
            _depthOfField.Clear();
            Cursor.lockState = _cursorLock;
            Cursor.visible = _cursorVisible;
            _camera = null;
            _follower = null;
            LastRawMovement = Vector2.zero;
            LastWorldMovement = Vector2.zero;
        }

        private void BeforeCameraRender(Camera camera)
        {
            if (!Active || camera != _camera) return;
            if (!CanControl()) { Suspend(); return; }
            ApplyPose();
            UpdateVisuals();
            if (RenderPoseApplied != null) RenderPoseApplied(camera);
            if (Application.isFocused && !SettingsOpen)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void RefreshVisuals()
        {
            if (_player == null) return;
            foreach (var renderer in _player.GetComponentsInChildren<Renderer>(true))
            {
                if (!_hiddenPlayer.ContainsKey(renderer)) _hiddenPlayer.Add(renderer, renderer.forceRenderingOff);
                renderer.forceRenderingOff = true;
            }
            if (_sceneDirty)
            {
                _sceneDirty = false;
                _discoveryPasses++;
                _knownUnits.Clear();
                _characters.Clear();
                _characterIndex.Clear();
                // One discovery pass per loaded scene, never once per frame/half
                // second. New enemies and followers are discovered from native lists.
                foreach (var skeleton in UnityEngine.Object.FindObjectsOfType<SkeletonAnimation>())
                    if (IsCharacter(skeleton.transform)) RegisterCharacter(skeleton);
                CaptureDepthOfField();
            }
            foreach (var follower in Follower.Followers)
                if (follower != null) RegisterCharacter(follower.Spine);
            foreach (var unit in Health.allUnits)
            {
                if (unit == null || _knownUnits.Contains(unit) || unit.GetComponent<UnitObject>() == null) continue;
                _knownUnits.Add(unit);
                foreach (var skeleton in unit.GetComponentsInChildren<SkeletonAnimation>(true)) RegisterCharacter(skeleton);
            }
            for (int i = _characters.Count - 1; i >= 0; i--)
            {
                var skeleton = _characters[i];
                if (skeleton == null) { _characters.RemoveAt(i); continue; }
                if (!skeleton.gameObject.activeInHierarchy || skeleton.transform.IsChildOf(_player.transform)) continue;
                var renderer = skeleton.GetComponent<MeshRenderer>();
                var mesh = skeleton.GetComponent<MeshFilter>();
                if (renderer != null && mesh != null) AddVisual(renderer, mesh, null);
            }
        }

        private void RegisterCharacter(SkeletonAnimation skeleton)
        {
            if (skeleton == null || !_characterIndex.Add(skeleton)) return;
            _characters.Add(skeleton);
        }

        private static bool IsCharacter(Transform transform)
        {
            return transform.GetComponentInParent<UnitObject>() != null || transform.GetComponentInParent<Follower>() != null ||
                transform.GetComponentInParent<StateMachine>() != null;
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _sceneDirty = true;
            _nextRefresh = 0f;
        }

        private void CaptureDepthOfField()
        {
            if (!_settings.DisableDepthOfField) return;
            foreach (var volume in UnityEngine.Object.FindObjectsOfType<PostProcessVolume>())
            {
                if (volume == null || !volume.enabled || (volume.sharedProfile == null && !volume.HasInstantiatedProfile())) continue;
                var profile = volume.profile;
                DepthOfField depth;
                if (profile == null || !profile.TryGetSettings<DepthOfField>(out depth) || depth == null || _depthOfField.ContainsKey(depth)) continue;
                _depthOfField.Add(depth, new DepthOfFieldState { Enabled = depth.enabled.value, Override = depth.enabled.overrideState });
            }
            HoldDepthOfFieldDisabled();
        }

        private void HoldDepthOfFieldDisabled()
        {
            if (!_settings.DisableDepthOfField) return;
            foreach (var entry in _depthOfField)
            {
                if (entry.Key == null) continue;
                entry.Key.enabled.value = false;
                entry.Key.enabled.overrideState = true;
            }
        }

        private void AddVisual(Renderer original, MeshFilter mesh, SpriteRenderer sprite)
        {
            if (_visualIndex.ContainsKey(original)) return;
            if ((original.transform.position - _player.transform.position).sqrMagnitude > _settings.BillboardDistance * _settings.BillboardDistance) return;
            if (original.GetComponentInParent<Canvas>() != null || original.forceRenderingOff) return;
            var visual = new VisualCopy(original, mesh, sprite);
            _billboards.Add(visual);
            _visualIndex.Add(original, visual);
        }

        private void UpdateVisuals()
        {
            if (_camera == null || _visualFrame == Time.frameCount) return;
            _visualFrame = Time.frameCount;
            _visibleBillboards = 0;
            GeometryUtility.CalculateFrustumPlanes(_camera, _frustum);
            float heading = _yaw * Mathf.Deg2Rad;
            Quaternion upright = Quaternion.LookRotation(new Vector3(Mathf.Cos(heading), Mathf.Sin(heading), 0f), Vector3.back);
            for (int i = _billboards.Count - 1; i >= 0; i--)
            {
                var copy = _billboards[i];
                if (copy.Source == null || _player == null ||
                    (copy.Source.transform.position - _player.transform.position).sqrMagnitude > _settings.BillboardDistance * _settings.BillboardDistance)
                {
                    copy.Dispose();
                    _billboards.RemoveAt(i);
                    if (!ReferenceEquals(copy.Source, null)) _visualIndex.Remove(copy.Source);
                }
                else
                {
                    bool visible = Visible(copy.Source);
                    if (visible) _visibleBillboards++;
                    copy.Sync(upright, visible);
                }
            }
            foreach (var item in _hiddenPlayer)
                if (item.Key != null) item.Key.forceRenderingOff = true;
        }

        private bool Visible(Renderer source)
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy) return false;
            // A sphere avoids using the flat source mesh's top-down bounds as an
            // FPS culling rectangle. Off-screen actors retain their native LOD.
            float radius = Mathf.Max(1.5f, source.bounds.extents.magnitude);
            Vector3 center = source.transform.position + Vector3.back * 0.6f;
            for (int i = 0; i < _frustum.Length; i++)
                if (_frustum[i].GetDistanceToPoint(center) < -radius) return false;
            return true;
        }

        public Vector2 RemapMovement(Vector2 raw)
        {
            float angle = _yaw * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(angle) * raw.x + Mathf.Cos(angle) * raw.y,
                -Mathf.Cos(angle) * raw.x + Mathf.Sin(angle) * raw.y);
        }

        // The harness must explicitly enable testing after verifying the Melty test marker.
        // These values are consumed by the real native gameplay-axis methods, never teleported.
        public void AllowTestInput(bool allowed) { _testAllowed = allowed; if (!allowed) EndTestInput(); }
        public void BeginTestInput(Vector2 movement)
        {
            if (!_testAllowed) throw new InvalidOperationException("Camera test input requires the explicit test harness marker.");
            _testMovement = Vector2.ClampMagnitude(movement, 1f);
            _testInput = true;
        }
        public void EndTestInput() { _testInput = false; _testMovement = Vector2.zero; }

        public bool TestLookAt(Vector2 target)
        {
            if (!_testAllowed || !Active || _player == null) return false;
            Vector2 offset = target - (Vector2)_player.transform.position;
            if (offset.sqrMagnitude < 0.001f) return false;
            _yaw = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
            _pitch = 0f;
            ApplyPose();
            return true;
        }

        private bool Eligible(PlayerFarming player)
        {
            return Active && !SettingsOpen && Time.timeScale > 0f && CanControl() && (player == null ? PlayerFarming.Instance : player) == _player;
        }

        private void SetLook(PlayerFarming player)
        {
            if (player == null || player.state == null) return;
            player.state.LookAngle = _yaw;
            var state = player.state.CURRENT_STATE;
            if (state == StateMachine.State.Idle || state == StateMachine.State.Idle_Winter ||
                state == StateMachine.State.Idle_CarryingBody || state == StateMachine.State.Moving ||
                state == StateMachine.State.Moving_Winter || state == StateMachine.State.Moving_CarryingBody ||
                state == StateMachine.State.Aiming || state == StateMachine.State.ChargingHeavyAttack)
                player.state.facingAngle = _yaw;
        }

        private static void HorizontalPostfix(RewiredGameplayInputSource __instance, PlayerFarming __0, ref float __result)
        {
            var rig = _instance;
            if(rig!=null&&rig.SettingsOpen&&rig._player!=null&&(__0??PlayerFarming.Instance)==rig._player){__result=0f;return;}
            if (rig == null || rig._readingAxes || !rig.Eligible(__0)) return;
            rig._readingAxes = true;
            try
            {
                var raw = rig._testInput ? rig._testMovement : new Vector2(__result, __instance.GetVerticalAxis(__0));
                rig.LastRawMovement = raw;
                rig.LastWorldMovement = rig.RemapMovement(raw);
                __result = rig.LastWorldMovement.x;
            }
            finally { rig._readingAxes = false; }
        }

        private static void VerticalPostfix(RewiredGameplayInputSource __instance, PlayerFarming __0, ref float __result)
        {
            var rig = _instance;
            if(rig!=null&&rig.SettingsOpen&&rig._player!=null&&(__0??PlayerFarming.Instance)==rig._player){__result=0f;return;}
            if (rig == null || rig._readingAxes || !rig.Eligible(__0)) return;
            rig._readingAxes = true;
            try
            {
                var raw = rig._testInput ? rig._testMovement : new Vector2(__instance.GetHorizontalAxis(__0), __result);
                rig.LastRawMovement = raw;
                rig.LastWorldMovement = rig.RemapMovement(raw);
                __result = rig.LastWorldMovement.y;
            }
            finally { rig._readingAxes = false; }
        }

        private static void MousePositionPostfix(PlayerFarming __0, ref Vector2 __result)
        {
            var rig = _instance;
            // Native gameplay always supplies its player. Null arguments are UI cursor queries.
            if (rig == null || __0 == null || !rig.Eligible(__0) || rig._camera == null) return;
            rig._virtualAimOrigin = rig._camera.WorldToScreenPoint(__0.transform.position);
            rig._virtualAimPoint = rig._camera.pixelRect.center;
            rig._virtualAimFrame = Time.frameCount;
            __result = rig._virtualAimPoint;
        }

        private static bool Same(float a, float b) { return a == b || (float.IsNaN(a) && float.IsNaN(b)); }

        private static bool MouseAnglePrefix(Vector3 __0, Vector3 __1, ref float __result)
        {
            var rig = _instance;
            if (rig == null || !rig.Active || rig._virtualAimFrame != Time.frameCount ||
                __1.x != rig._virtualAimPoint.x || __1.y != rig._virtualAimPoint.y || __1.z != 0f ||
                !Same(__0.x, rig._virtualAimOrigin.x) || !Same(__0.y, rig._virtualAimOrigin.y) || !Same(__0.z, rig._virtualAimOrigin.z) ||
                !rig.Eligible(rig._player)) return true;
            // The player's feet can lie exactly on the perspective camera plane. Do not
            // subtract two infinite screen projections: native melee still receives XY yaw.
            __result = rig._yaw;
            return false;
        }

        private static bool SpellTargetPrefix(PlayerSpells __instance, float __0, ref Vector3 __result)
        {
            var rig = _instance;
            if (rig == null || !rig.Eligible(rig._player) || rig._camera == null ||
                __instance.GetComponent<PlayerFarming>() != rig._player) return true;
            float heading = rig._yaw * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Cos(heading), Mathf.Sin(heading), 0f);
            float distance = __0;
            var ray = rig._camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var floor = new Plane(Vector3.forward, rig._player.transform.position);
            float enter;
            if (floor.Raycast(ray, out enter))
                distance = Mathf.Min(__0, ((Vector2)(ray.GetPoint(enter) - rig._player.transform.position)).magnitude);
            if (SettingsManager.Settings != null && SettingsManager.Settings.Game.InvertAiming) direction = -direction;
            __result = rig._player.transform.position + direction * distance;
            return false;
        }

        private static void MouseActivePostfix(ref bool __result)
        {
            var rig = _instance;
            if (rig != null && rig.Eligible(rig._player) && rig._player.canUseKeyboard && !InputManager.General.InputIsController(rig._player))
                __result = true;
        }

        private static void ControllerPostfix(PlayerController __instance)
        {
            var rig = _instance;
            if (rig != null && rig.Eligible(rig._player) && rig._player.playerController == __instance)
                rig.SetLook(rig._player);
        }

        private static void FollowerPostfix(CameraFollowTarget __instance)
        {
            var rig = _instance;
            if (rig == null) return;
            if (!rig.Active) { rig.RepairNativeRotation(); return; }
            if (rig._follower != __instance || rig._camera == null) return;
            // The native follower keeps its own CurrentPosition; let it progress even in first person.
            rig._vanillaPosition = rig._camera.transform.position;
            if (!rig.CanControl()) { rig.Suspend(); return; }
            rig.ApplyPose();
        }

        private void RepairNativeRotation()
        {
            if (!_repairTopDown || Active || FirstPerson || PhotoModeManager.PhotoModeActive || PhotoModeManager.TakeScreenShotActive ||
                LetterBox.IsPlaying || MMConversation.isPlaying || MMTransition.IsPlaying) return;
            CameraFollowTarget follower = CameraFollowTarget.Instance;
            if (follower == null || !follower.enabled || follower.TargetCamera != null || follower.IN_CONVERSATION) return;
            follower.transform.rotation = NativeRotation;
        }

        private static bool NativeBillboardPrefix(BillboardFacingCamera __instance)
        {
            var rig = _instance;
            if (rig == null || !rig.Active || __instance == null) return true;
            if (rig._uiBillboards.Contains(__instance)) return true;
            if (rig._characterBillboards.Contains(__instance)) return false;
            if (rig._fixedProps.ContainsKey(__instance)) return false;
            if (__instance.GetComponentInParent<Canvas>() != null || __instance.GetComponentInParent<DevotionOrbVisual>() != null)
            {
                rig._uiBillboards.Add(__instance);
                return true;
            }
            // Character meshes are represented by dynamic visual clones; their
            // native gameplay transforms must not be rotated by this camera helper.
            if (IsCharacter(__instance.transform))
            {
                rig._characterBillboards.Add(__instance);
                return false;
            }
            StaticPropState state;
            if (!rig._fixedProps.TryGetValue(__instance, out state))
            {
                Quaternion original = __instance.transform.rotation;
                Quaternion fixedRotation = original;
                // Only visual leaf nodes can be raised vertically. Never rotate a
                // transform containing native collision, interaction or canvas data.
                if (__instance.GetComponent<Renderer>() != null && __instance.GetComponentInChildren<Collider2D>() == null &&
                    __instance.GetComponentInChildren<Collider>() == null)
                {
                    Vector3 normal = original * Vector3.forward;
                    normal.z = 0f;
                    if (normal.sqrMagnitude > 0.01f)
                    {
                        normal = Mathf.Abs(normal.x) > Mathf.Abs(normal.y)
                            ? new Vector3(Mathf.Sign(normal.x), 0f, 0f)
                            : new Vector3(0f, Mathf.Sign(normal.y), 0f);
                        fixedRotation = Quaternion.LookRotation(normal, Vector3.back);
                    }
                }
                state = new StaticPropState { Original = original, Fixed = fixedRotation };
                rig._fixedProps.Add(__instance, state);
                __instance.transform.rotation = state.Fixed;
            }
            return false;
        }

        private static void SuspendPrefix() { if (_instance != null) _instance.Suspend(); }

        public void Dispose()
        {
            if (_disposed) return;
            Suspend();
            _disposed = true;
            FirstPerson = false;
            if (SettingsOpen) SaveSettings();
            if(_settingsUi!=null)_settingsUi.Dispose();
            SettingsOpen = false;
            Camera.onPreCull -= BeforeCameraRender;
            SceneManager.sceneLoaded -= SceneLoaded;
            _harmony.UnpatchSelf();
            if (_instance == this) _instance = null;
        }

        /// <summary>A visual-only clone never rotates the game's hitboxes, bones or world geometry.</summary>
        private sealed class VisualCopy : IDisposable
        {
            public readonly Renderer Source;
            private readonly bool _oldHidden;
            private readonly MeshFilter _sourceMesh;
            private readonly SpriteRenderer _sourceSprite;
            private readonly SkeletonAnimationLODManager _lod;
            private readonly bool _oldIgnoreCulling;
            private readonly GameObject _object;
            private readonly Renderer _renderer;
            private readonly MeshFilter _mesh;
            private readonly SpriteRenderer _sprite;
            private Material[] _materials;
            private readonly List<Material> _materialBuffer = new List<Material>();
            private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();

            public VisualCopy(Renderer source, MeshFilter sourceMesh, SpriteRenderer sourceSprite)
            {
                Source = source;
                _oldHidden = source.forceRenderingOff;
                _sourceMesh = sourceMesh;
                _sourceSprite = sourceSprite;
                var skeleton = source.GetComponent<SkeletonAnimation>();
                var lod = source.GetComponent<SkeletonAnimationLODManager>() ?? source.GetComponentInParent<SkeletonAnimationLODManager>();
                if (skeleton != null && lod != null && lod.SkeletonAnimation == skeleton)
                {
                    _lod = lod;
                    _oldIgnoreCulling = lod.IgnoreCulling;
                    // Native culling uses the unrotated source mesh bounds. The clone
                    // can be visible even while those bounds are below the FPS frustum.
                    lod.IgnoreCulling = _oldIgnoreCulling;
                }
                _object = new GameObject("CultCraft.Billboard." + source.name);
                _object.layer = source.gameObject.layer;
                if (sourceSprite != null)
                {
                    _sprite = _object.AddComponent<SpriteRenderer>();
                    _renderer = _sprite;
                }
                else
                {
                    _mesh = _object.AddComponent<MeshFilter>();
                    _renderer = _object.AddComponent<MeshRenderer>();
                }
                _materials = source.sharedMaterials;
                _renderer.sharedMaterials = _materials;
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = source.receiveShadows;
                _renderer.sortingLayerID = source.sortingLayerID;
                _renderer.sortingOrder = source.sortingOrder;
                source.forceRenderingOff = true;
            }

            public void Sync(Quaternion rotation, bool visible)
            {
                if (Source == null || _object == null) return;
                _object.SetActive(visible);
                Source.forceRenderingOff = true;
                if (_lod != null) _lod.IgnoreCulling = _oldIgnoreCulling || visible;
                if (!visible) return;
                _object.transform.SetPositionAndRotation(Source.transform.position, rotation);
                _object.transform.localScale = Source.transform.lossyScale;
                _renderer.sortingLayerID = Source.sortingLayerID;
                _renderer.sortingOrder = Source.sortingOrder;
                Source.GetSharedMaterials(_materialBuffer);
                bool changed = _materialBuffer.Count != _materials.Length;
                for (int i = 0; !changed && i < _materialBuffer.Count; i++) changed = _materialBuffer[i] != _materials[i];
                if (changed) { _materials = _materialBuffer.ToArray(); _renderer.sharedMaterials = _materials; }
                Source.GetPropertyBlock(_properties);
                _renderer.SetPropertyBlock(_properties);
                if (_mesh != null && _sourceMesh != null) _mesh.sharedMesh = _sourceMesh.sharedMesh;
                if (_sprite != null && _sourceSprite != null)
                {
                    _sprite.sprite = _sourceSprite.sprite;
                    _sprite.color = _sourceSprite.color;
                    _sprite.flipX = _sourceSprite.flipX;
                    _sprite.flipY = _sourceSprite.flipY;
                    _sprite.drawMode = _sourceSprite.drawMode;
                    _sprite.size = _sourceSprite.size;
                    _sprite.maskInteraction = _sourceSprite.maskInteraction;
                }
            }

            public void Dispose()
            {
                if (Source != null) Source.forceRenderingOff = _oldHidden;
                if (_lod != null) _lod.IgnoreCulling = _oldIgnoreCulling;
                if (_object != null) UnityEngine.Object.Destroy(_object);
            }
        }
    }
}
