using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Studio.Presentation.Camera;
using UnityEngine;


namespace StudioSpaceMouse
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "studiospacemouse";
        public const string Name = "Studio SpaceMouse";
        public const string Version = "0.1.0";

        public static Plugin Instance { get; private set; }

        // --- config
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _verbose;
        private ConfigEntry<string> _mode;
        private ConfigEntry<float> _rotateSpeed;
        private ConfigEntry<float> _panSpeed;
        private ConfigEntry<float> _zoomSpeed;
        private ConfigEntry<int> _deadZone;
        private ConfigEntry<int> _fullScale;
        private ConfigEntry<float> _curve;
        private ConfigEntry<bool> _dominant;
        private ConfigEntry<float> _smoothing;
        private ConfigEntry<float> _staleSeconds;
        private ConfigEntry<bool> _longPressOpposite;
        private ConfigEntry<float> _longPressSeconds;
        private ConfigEntry<bool> _suppressWheel;
        private ConfigEntry<string> _backend;
        private ConfigEntry<string> _profileName;
        private float _lastLiveAxisTime = -10f;
        private static NavlibBridge s_bridge;
        private static bool s_navlibActive;
        private ConfigEntry<string> _bindPanX;
        private ConfigEntry<string> _bindPanY;
        private ConfigEntry<string> _bindZoom;
        private ConfigEntry<string> _bindYaw;
        private ConfigEntry<string> _bindPitch;
        private ConfigEntry<int> _btnFit;
        private ConfigEntry<int> _btnFitSelected;
        private ConfigEntry<int> _btnTop;
        private ConfigEntry<int> _btnBottom;
        private ConfigEntry<int> _btnFront;
        private ConfigEntry<int> _btnBack;
        private ConfigEntry<int> _btnLeft;
        private ConfigEntry<int> _btnRight;
        private ConfigEntry<int> _btnFree;
        private ConfigEntry<int> _btnLockRotation;

        private AxisBinding _panX, _panY, _zoom, _yaw, _pitch;

        // --- runtime. The reader is static so it survives whatever happens to this component.
        private static RawInputReader s_reader;
        private static readonly Queue<string> s_pendingLog = new Queue<string>();
        private static readonly object s_logLock = new object();

        private readonly SpaceMouseState _state = new SpaceMouseState();
        private CameraDriver _driver;
        private Harmony _harmony;
        private ulong _previousButtons;
        private byte _previousLongPressKey;
        private readonly Dictionary<int, float> _pendingViewPresses = new Dictionary<int, float>();
        private readonly float[] _filtered = new float[5]; // panX, panY, zoom, yaw, pitch after smoothing
        private Quaternion _lastRotation;
        private Vector3 _lastPosition;
        private bool _haveLastPose;
        private int _jumpLogs;
        private bool _reportedDevice;
        private bool _tickSeen;
        private bool _tickFailed;
        private int _lastTickFrame = -1;

        private void Awake()
        {
            // Studio replaces Unity's log handler, so an exception here would vanish; log it ourselves.
            try
            {
                Instance = this;
                BindConfig();
                _driver = new CameraDriver(m => Logger.LogWarning(m));
                Logger.LogInfo(Name + " " + Version + " loading. Studio bindings: " + CameraDriver.DescribeBindings());
                if (!CameraDriver.IsUsable)
                {
                    Logger.LogError("Studio's OrbitControl no longer has the members this plugin needs; camera control disabled.");
                }

                string backend = (_backend.Value ?? "Auto").Trim();
                bool wantNavlib = !string.Equals(backend, "RawHid", StringComparison.OrdinalIgnoreCase);
                bool allowRaw = !string.Equals(backend, "Navlib", StringComparison.OrdinalIgnoreCase);

                if (wantNavlib && s_bridge == null && CameraDriver.IsUsable)
                {
                    try
                    {
                        s_bridge = new NavlibBridge(_driver, QueueLog, () => _verbose.Value);
                        s_bridge.Open(_profileName.Value);
                        s_navlibActive = true;
                        Application.quitting += () => s_bridge?.Dispose();
                        Logger.LogInfo("Connected to 3DxWare through navlib as profile '" + _profileName.Value + "'. Speed, axes and buttons are configured in the 3Dconnexion settings app.");
                    }
                    catch (Exception ex)
                    {
                        s_bridge = null;
                        s_navlibActive = false;
                        Logger.LogWarning("3DxWare navlib not available (" + ex.GetType().Name + ": " + ex.Message + ")" +
                                          (allowRaw ? "; falling back to raw HID input." : "; Backend=Navlib so no fallback."));
                    }
                }

                if (!s_navlibActive && allowRaw && s_reader == null)
                {
                    s_reader = new RawInputReader(QueueLog, _verbose.Value);
                    s_reader.Start();
                    Application.quitting += () => s_reader?.Dispose();
                    Logger.LogInfo("Raw input thread started.");
                }

                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Logger.LogInfo("Harmony patches applied: " + string.Join(", ", ListPatched()));
            }
            catch (Exception ex)
            {
                Logger.LogError("Awake failed: " + ex);
            }
        }

        private IEnumerable<string> ListPatched()
        {
            if (_harmony == null)
            {
                yield break;
            }
            foreach (var m in _harmony.GetPatchedMethods())
            {
                yield return m.DeclaringType.Name + "." + m.Name;
            }
        }

        private void OnEnable() { Logger.LogInfo("Plugin component enabled."); }
        private void OnDisable() { Logger.LogInfo("Plugin component disabled."); }
        private void OnDestroy() { Logger.LogInfo("Plugin component destroyed; raw input thread keeps running."); }

        private void BindConfig()
        {
            _enabled = Config.Bind("General", "Enabled", true, "Master switch.");
            _verbose = Config.Bind("General", "VerboseLog", false,
                "Log raw device reports and button presses to BepInEx/LogOutput.log. Use it to find axis signs and button numbers, then turn it off.");
            _mode = Config.Bind("General", "NavigationMode", "Object",
                "Object: moving the cap moves the model (3Dconnexion default). Camera: moving the cap moves the camera (all axes reversed).");
            _suppressWheel = Config.Bind("General", "SuppressWheelWhileActive", true,
                "Ignore mouse-wheel zoom in the viewport while the cap is deflected. 3DxWare's default emulation profile turns the tilt axis into wheel ticks, which fight the plugin's own zoom.");
            _backend = Config.Bind("General", "Backend", "Auto",
                "Auto: use 3DxWare's navigation library when the driver is running, else read the device directly. Navlib: driver only. RawHid: direct only (the [Sensitivity], [Axes] and [Buttons] sections apply to RawHid only).");
            _profileName = Config.Bind("General", "DriverProfileName", "BrickLink Studio",
                "Name under which Studio appears in the 3Dconnexion settings app when using the driver.");
            _staleSeconds = Config.Bind("General", "StaleSeconds", 1.0f,
                "Ignore the last report if it is older than this many seconds (guards against a missed release report). The device sends an explicit zero report on release, so this only matters if one is lost.");

            _rotateSpeed = Config.Bind("Sensitivity", "RotateDegreesPerSecond", 90f, "Orbit speed at full deflection.");
            _panSpeed = Config.Bind("Sensitivity", "PanSpeed", 0.7f, "Pan speed at full deflection; 1.0 moves half the zoom distance per second.");
            _zoomSpeed = Config.Bind("Sensitivity", "ZoomSpeed", 0.7f, "Zoom speed at full deflection; 1.0 changes the distance by a factor of e per second.");
            _deadZone = Config.Bind("Sensitivity", "DeadZone", 8, "Raw units below which an axis counts as centred.");
            _fullScale = Config.Bind("Sensitivity", "FullScale", 350, "Raw units at full deflection (about 350 on most SpaceMouse models).");
            _curve = Config.Bind("Sensitivity", "Curve", 1.5f, "Response curve exponent. 1 = linear; higher gives finer control near centre.");
            _dominant = Config.Bind("Sensitivity", "DominantAxisOnly", false, "Only the most deflected axis moves the camera.");
            _smoothing = Config.Bind("Sensitivity", "SmoothingSeconds", 0.08f, "Time constant of the low-pass filter on the axes. 0 disables it; larger is smoother but laggier.");

            _bindPanX = Config.Bind("Axes", "PanX", "-Tx", "Device axis for horizontal pan. One of Tx Ty Tz Rx Ry Rz, with an optional leading '-' to flip it.");
            _bindPanY = Config.Bind("Axes", "PanY", "Tz", "Device axis for vertical pan.");
            _bindZoom = Config.Bind("Axes", "Zoom", "-Ty", "Device axis for zoom (dolly).");
            _bindYaw = Config.Bind("Axes", "Yaw", "Rz", "Device axis for orbiting around the vertical axis.");
            _bindPitch = Config.Bind("Axes", "Pitch", "Rx", "Device axis for tilting the view up and down.");

            _btnFit = Config.Bind("Buttons", "Fit", 1, "Button number for zoom to fit (-1 disables). Numbers follow the HID report bit; VerboseLog shows them.");
            _btnFitSelected = Config.Bind("Buttons", "FitSelected", 11, "Button number for zoom to fit the selection.");
            _btnTop = Config.Bind("Buttons", "Top", 2, "Button number for the top view.");
            _btnLeft = Config.Bind("Buttons", "Left", 3, "Button number for the left view.");
            _btnRight = Config.Bind("Buttons", "Right", 4, "Button number for the right view.");
            _btnFront = Config.Bind("Buttons", "Front", 5, "Button number for the front view.");
            _btnBottom = Config.Bind("Buttons", "Bottom", 6, "Button number for the bottom view.");
            _btnBack = Config.Bind("Buttons", "Back", 7, "Button number for the back view.");
            _btnFree = Config.Bind("Buttons", "Free", 10, "Button number for the free (perspective) camera.");
            _btnLockRotation = Config.Bind("Buttons", "LockRotation", 26, "Button number that toggles the viewport rotation lock.");
            _longPressOpposite = Config.Bind("Buttons", "LongPressOppositeView", true,
                "Holding a view button gives the opposite view (Top/Bottom, Front/Back, Left/Right), like 3DxWare's secondary function.");
            _longPressSeconds = Config.Bind("Buttons", "LongPressSeconds", 0.5f, "Hold time that counts as a long press.");

            RefreshBindings();
            Config.SettingChanged += (s, e) => RefreshBindings();
        }

        private void RefreshBindings()
        {
            _panX = AxisBinding.Parse(_bindPanX.Value, new AxisBinding { Source = AxisBinding.Axis.Tx, Sign = -1f });
            _panY = AxisBinding.Parse(_bindPanY.Value, new AxisBinding { Source = AxisBinding.Axis.Tz, Sign = 1f });
            _zoom = AxisBinding.Parse(_bindZoom.Value, new AxisBinding { Source = AxisBinding.Axis.Ty, Sign = 1f });
            _yaw = AxisBinding.Parse(_bindYaw.Value, new AxisBinding { Source = AxisBinding.Axis.Rz, Sign = 1f });
            _pitch = AxisBinding.Parse(_bindPitch.Value, new AxisBinding { Source = AxisBinding.Axis.Rx, Sign = 1f });
        }

        private static void QueueLog(string message)
        {
            lock (s_logLock)
            {
                s_pendingLog.Enqueue(message);
            }
        }

        private void FlushLog()
        {
            lock (s_logLock)
            {
                while (s_pendingLog.Count > 0)
                {
                    Logger.LogInfo(s_pendingLog.Dequeue());
                }
            }
        }

        // Unity's own Update on this component, if it runs at all.
        private void Update()
        {
            Tick("Update");
        }

        /// <summary>
        /// Harmony postfix on OrbitControl.LateUpdate: runs every frame for every live orbit camera,
        /// which is exactly when there is something to drive. Only the first call per frame does work.
        /// </summary>
        [HarmonyPatch(typeof(OrbitControl), "LateUpdate")]
        private static class OrbitControlLateUpdatePatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                Instance?.Tick("OrbitControl.LateUpdate");
            }
        }

        /// <summary>
        /// Harmony prefix on OrbitControl.OnScroll: drops wheel events that arrive while the
        /// SpaceMouse is deflected. 3DxWare's keyboard/mouse emulation maps tilt to the wheel by
        /// default, and those ticks would otherwise step the zoom by 10% on top of our motion.
        /// </summary>
        [HarmonyPatch(typeof(OrbitControl), "OnScroll")]
        private static class OrbitControlOnScrollPatch
        {
            [HarmonyPrefix]
            private static bool Prefix()
            {
                Plugin p = Instance;
                if (p == null || !p._suppressWheel.Value)
                {
                    return true;
                }
                bool active = Time.unscaledTime - p._lastLiveAxisTime < 0.3f || (s_bridge != null && s_bridge.IsNavigating);
                if (active && p._verbose.Value)
                {
                    p.Logger.LogInfo("wheel event suppressed while cap is deflected");
                }
                return !active;
            }
        }

        private void Tick(string source)
        {
            if (_lastTickFrame == Time.frameCount)
            {
                return;
            }
            _lastTickFrame = Time.frameCount;
            try
            {
                FlushLog();
                if (!_tickSeen)
                {
                    _tickSeen = true;
                    Logger.LogInfo("First frame tick via " + source + ".");
                }
                TickCore();
            }
            catch (Exception ex)
            {
                if (!_tickFailed)
                {
                    _tickFailed = true;
                    Logger.LogError("Frame tick failed (further failures suppressed): " + ex);
                }
            }
        }

        private void TickCore()
        {
            if (!_enabled.Value || !CameraDriver.IsUsable)
            {
                return;
            }
            if (s_navlibActive)
            {
                // The driver moves the camera through NavlibBridge callbacks; only button
                // commands it assigned to us are left to run here, on the main thread.
                s_bridge.Pump(ExecuteDriverCommand);
                return;
            }
            if (s_reader == null)
            {
                return;
            }

            double age;
            if (!s_reader.Snapshot(_state, out age))
            {
                return;
            }
            if (!_reportedDevice && s_reader.DeviceSeen)
            {
                _reportedDevice = true;
                Logger.LogInfo("Receiving reports from " + s_reader.DeviceName);
            }

            if (!Application.isFocused)
            {
                _previousButtons = _state.Buttons;
                return;
            }
            if (UIInput.selection != null)
            {
                // A text field has keyboard focus; leave the camera alone.
                _previousButtons = _state.Buttons;
                return;
            }

            CameraTarget target = CameraDriver.Resolve();
            if (!target.IsValid)
            {
                _previousButtons = _state.Buttons;
                return;
            }
            OrbitControl oc = target.Orbit;
            if (_verbose.Value && !ReferenceEquals(oc, _lastOrbit))
            {
                _lastOrbit = oc;
                Logger.LogInfo("Driving " + target.Kind + " camera.");
            }

            HandleButtons(target);

            bool live = age <= _staleSeconds.Value && _state.AnyAxis;
            if (live)
            {
                _lastLiveAxisTime = Time.unscaledTime;
            }
            HandleAxes(oc, live);
        }

        private OrbitControl _lastOrbit;

        /// <summary>Runs a command the 3Dconnexion driver assigned to a button in our command set.</summary>
        private void ExecuteDriverCommand(string command)
        {
            CameraTarget target = CameraDriver.Resolve();
            if (!target.IsValid)
            {
                return;
            }
            if (_verbose.Value)
            {
                Logger.LogInfo("driver command: " + command);
            }
            switch (command)
            {
                case "fit": _driver.ZoomToFit(target, false); break;
                case "fitSelection": _driver.ZoomToFit(target, true); break;
                case "free": _driver.SetOrientation(target, CameraOrientation.Free); break;
                case "top": _driver.SetOrientation(target, CameraOrientation.Top); break;
                case "bottom": _driver.SetOrientation(target, CameraOrientation.Bottom); break;
                case "front": _driver.SetOrientation(target, CameraOrientation.Front); break;
                case "back": _driver.SetOrientation(target, CameraOrientation.Back); break;
                case "left": _driver.SetOrientation(target, CameraOrientation.Left); break;
                case "right": _driver.SetOrientation(target, CameraOrientation.Right); break;
                case "lockRotation": _driver.ToggleRotationLock(target); break;
                default:
                    Logger.LogWarning("Unknown driver command: " + command);
                    break;
            }
        }

        private void HandleButtons(CameraTarget viewport)
        {
            ulong now = _state.Buttons;
            ulong pressed = now & ~_previousButtons;
            ulong released = _previousButtons & ~now;
            _previousButtons = now;
            float t = Time.unscaledTime;

            if (pressed != 0)
            {
                if (_verbose.Value)
                {
                    Logger.LogInfo("Button(s) pressed: " + DescribeBits(pressed));
                }
                if (Pressed(pressed, _btnFit)) _driver.ZoomToFit(viewport, false);
                if (Pressed(pressed, _btnFitSelected)) _driver.ZoomToFit(viewport, true);
                if (Pressed(pressed, _btnFree)) _driver.SetOrientation(viewport, CameraOrientation.Free);
                if (Pressed(pressed, _btnLockRotation)) _driver.ToggleRotationLock(viewport);

                // View buttons: fire on release (short press) or when the hold passes the threshold (opposite view).
                foreach (ConfigEntry<int> b in ViewButtons())
                {
                    if (Pressed(pressed, b))
                    {
                        if (_longPressOpposite.Value)
                        {
                            _pendingViewPresses[b.Value] = t;
                        }
                        else
                        {
                            _driver.SetOrientation(viewport, ViewFor(b));
                        }
                    }
                }
            }

            if (released != 0 && _verbose.Value)
            {
                Logger.LogInfo("Button(s) released: " + DescribeBits(released));
            }

            // Firmware-detected long press (newer devices): the key number arrives in its own
            // report and the ordinary button bit is never set for it.
            byte longKey = _state.LongPressKey;
            if (longKey != _previousLongPressKey)
            {
                _previousLongPressKey = longKey;
                if (longKey != 0)
                {
                    int n = longKey - 1;
                    CameraOrientation view = ViewFor(n);
                    if (_verbose.Value)
                    {
                        Logger.LogInfo("Firmware long press on button " + n + (view != CameraOrientation.Free ? " -> " + Opposite(view) : " (no view mapped)"));
                    }
                    if (view != CameraOrientation.Free && _longPressOpposite.Value)
                    {
                        _driver.SetOrientation(viewport, Opposite(view));
                    }
                }
            }

            if (_pendingViewPresses.Count == 0)
            {
                return;
            }
            var done = new List<int>();
            foreach (var kv in _pendingViewPresses)
            {
                int n = kv.Key;
                bool stillHeld = (now & (1UL << n)) != 0;
                if (stillHeld && t - kv.Value >= _longPressSeconds.Value)
                {
                    if (_verbose.Value)
                    {
                        Logger.LogInfo("Long press on button " + n + " -> " + Opposite(ViewFor(n)));
                    }
                    _driver.SetOrientation(viewport, Opposite(ViewFor(n)));
                    done.Add(n);
                }
                else if (!stillHeld && (released & (1UL << n)) != 0)
                {
                    if (_verbose.Value)
                    {
                        Logger.LogInfo("Short press on button " + n + " (held " + (t - kv.Value).ToString("F2") + "s) -> " + ViewFor(n));
                    }
                    _driver.SetOrientation(viewport, ViewFor(n));
                    done.Add(n);
                }
                else if (!stillHeld)
                {
                    done.Add(n); // released while we were not looking; drop it
                }
            }
            foreach (int n in done)
            {
                _pendingViewPresses.Remove(n);
            }
        }

        private IEnumerable<ConfigEntry<int>> ViewButtons()
        {
            yield return _btnTop;
            yield return _btnBottom;
            yield return _btnFront;
            yield return _btnBack;
            yield return _btnLeft;
            yield return _btnRight;
        }

        private CameraOrientation ViewFor(ConfigEntry<int> button)
        {
            if (button == _btnTop) return CameraOrientation.Top;
            if (button == _btnBottom) return CameraOrientation.Bottom;
            if (button == _btnFront) return CameraOrientation.Front;
            if (button == _btnBack) return CameraOrientation.Back;
            if (button == _btnLeft) return CameraOrientation.Left;
            return CameraOrientation.Right;
        }

        private CameraOrientation ViewFor(int buttonNumber)
        {
            foreach (ConfigEntry<int> b in ViewButtons())
            {
                if (b.Value == buttonNumber)
                {
                    return ViewFor(b);
                }
            }
            return CameraOrientation.Free;
        }

        private static CameraOrientation Opposite(CameraOrientation view)
        {
            switch (view)
            {
                case CameraOrientation.Top: return CameraOrientation.Bottom;
                case CameraOrientation.Bottom: return CameraOrientation.Top;
                case CameraOrientation.Front: return CameraOrientation.Back;
                case CameraOrientation.Back: return CameraOrientation.Front;
                case CameraOrientation.Left: return CameraOrientation.Right;
                case CameraOrientation.Right: return CameraOrientation.Left;
                default: return view;
            }
        }

        private static bool Pressed(ulong pressed, ConfigEntry<int> button)
        {
            int n = button.Value;
            return n >= 0 && n < 64 && (pressed & (1UL << n)) != 0;
        }

        private static string DescribeBits(ulong mask)
        {
            var parts = new List<string>();
            for (int i = 0; i < 64; i++)
            {
                if ((mask & (1UL << i)) != 0)
                {
                    parts.Add(i.ToString());
                }
            }
            return string.Join(", ", parts.ToArray());
        }

        private void HandleAxes(OrbitControl oc, bool live)
        {
            float panX = live ? Shape(_panX) : 0f;
            float panY = live ? Shape(_panY) : 0f;
            float zoom = live ? Shape(_zoom) : 0f;
            float yaw = live ? Shape(_yaw) : 0f;
            float pitch = live ? Shape(_pitch) : 0f;

            if (_dominant.Value)
            {
                KeepDominant(ref panX, ref panY, ref zoom, ref yaw, ref pitch);
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_verbose.Value && Time.unscaledDeltaTime > 0.05f)
            {
                Logger.LogInfo("frame hitch: " + (Time.unscaledDeltaTime * 1000f).ToString("F0") + " ms");
            }

            // Low-pass filter: smooths report-rate steps and lets motion ease out after release.
            float tau = _smoothing.Value;
            float alpha = tau > 0f ? 1f - Mathf.Exp(-dt / tau) : 1f;
            _filtered[0] += (panX - _filtered[0]) * alpha;
            _filtered[1] += (panY - _filtered[1]) * alpha;
            _filtered[2] += (zoom - _filtered[2]) * alpha;
            _filtered[3] += (yaw - _filtered[3]) * alpha;
            _filtered[4] += (pitch - _filtered[4]) * alpha;
            const float eps = 0.002f;
            for (int i = 0; i < _filtered.Length; i++)
            {
                if (Mathf.Abs(_filtered[i]) < eps) _filtered[i] = 0f;
            }
            panX = _filtered[0]; panY = _filtered[1]; zoom = _filtered[2]; yaw = _filtered[3]; pitch = _filtered[4];
            if (panX == 0f && panY == 0f && zoom == 0f && yaw == 0f && pitch == 0f)
            {
                return;
            }

            // Object mode: pushing the cap right should move the model right, i.e. the camera left.
            float modeSign = string.Equals(_mode.Value, "Camera", StringComparison.OrdinalIgnoreCase) ? 1f : -1f;

            bool diag = _verbose.Value;
            Transform camTr = oc.transform;
            if (diag && _haveLastPose && _jumpLogs < 60)
            {
                // Did anything else move the camera since our previous tick?
                float drift = Quaternion.Angle(_lastRotation, camTr.rotation);
                float shift = (camTr.position - _lastPosition).magnitude;
                if (drift > 0.5f || shift > 0.01f * Mathf.Max(oc.ZoomDistance, 1f))
                {
                    _jumpLogs++;
                    Logger.LogInfo(string.Format("camera moved between ticks: {0:F1} deg, {1:F1} units | {2}", drift, shift, DescribeOrbit(oc)));
                }
            }
            Quaternion before = camTr.rotation;

            float yawDeg = 0f, pitchDeg = 0f;
            if (yaw != 0f || pitch != 0f)
            {
                float deg = _rotateSpeed.Value * dt * modeSign;
                yawDeg = yaw * deg;
                pitchDeg = pitch * deg;
                _driver.Rotate(oc, yawDeg, pitchDeg);
            }
            if (panX != 0f || panY != 0f)
            {
                // OrbitControl.pan() treats 800 units as half the zoom distance.
                float px = 800f * _panSpeed.Value * dt * modeSign;
                _driver.Pan(oc, panX * px, panY * px);
            }
            if (zoom != 0f)
            {
                // Pushing the cap away (object mode) should bring the model closer.
                float factor = Mathf.Exp(zoom * _zoomSpeed.Value * dt * modeSign);
                _driver.Zoom(oc, factor);
            }

            if (diag)
            {
                float commanded = Mathf.Sqrt(yawDeg * yawDeg + pitchDeg * pitchDeg);
                float actual = Quaternion.Angle(before, camTr.rotation);
                if (_jumpLogs < 60 && actual > Mathf.Max(3f, 4f * commanded))
                {
                    _jumpLogs++;
                    Logger.LogInfo(string.Format("rotation jump: commanded yaw {0:F2} pitch {1:F2} deg, camera turned {2:F1} deg | {3}",
                        yawDeg, pitchDeg, actual, DescribeOrbit(oc)));
                }
                _lastRotation = camTr.rotation;
                _lastPosition = camTr.position;
                _haveLastPose = true;
            }
        }

        private static string DescribeOrbit(OrbitControl oc)
        {
            return string.Format("theta {0:F3} fai {1:F3} inverted {2} orientation {3} zoom {4:F0} needSet {5}",
                oc.theta, oc.fai, oc.m_isInverted, oc.ViewportCameraOrientation, oc.ZoomDistance, oc.IsNeedToSetTransform);
        }

        private float Shape(AxisBinding binding)
        {
            short raw = binding.Read(_state);
            return binding.Sign * AxisShaper.Shape(raw, _deadZone.Value, _fullScale.Value, _curve.Value);
        }

        private static void KeepDominant(ref float a, ref float b, ref float c, ref float d, ref float e)
        {
            float max = Mathf.Max(Mathf.Abs(a), Mathf.Abs(b), Mathf.Abs(c), Mathf.Abs(d), Mathf.Abs(e));
            if (Mathf.Abs(a) < max) a = 0f;
            if (Mathf.Abs(b) < max) b = 0f;
            if (Mathf.Abs(c) < max) c = 0f;
            if (Mathf.Abs(d) < max) d = 0f;
            if (Mathf.Abs(e) < max) e = 0f;
        }
    }
}
