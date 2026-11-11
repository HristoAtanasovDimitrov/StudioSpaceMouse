using System;
using System.Collections.Generic;
using System.Reflection;
using TDx.SpaceMouse.Navigation3D;
using UnityEngine;
using NlMatrix = TDx.SpaceMouse.Navigation3D.Matrix;
using NlPlane = TDx.SpaceMouse.Navigation3D.Plane;
using NlPoint = TDx.SpaceMouse.Navigation3D.Point;
using NlVector = TDx.SpaceMouse.Navigation3D.Vector;

namespace StudioSpaceMouse
{
    /// <summary>
    /// Connects Studio's camera to 3DxWare through the navigation library (navlib). The driver
    /// does the navigation maths and applies the user's settings; this class only answers its
    /// questions about the scene and writes the resulting camera pose back into Studio.
    ///
    /// Coordinate frames: Studio keeps raw LDraw coordinates (Y down) inside Unity's left-handed
    /// engine and rolls its camera 180 degrees so models appear upright. Negating Y turns that
    /// into a right-handed, Y-up world with a conventional camera (X right, Y up, looking down
    /// -Z), which is exactly what navlib expects, so every point and vector crossing this
    /// boundary gets its Y flipped and nothing else.
    ///
    /// Callbacks arrive on Unity's main thread: in single-threaded mode navlib delivers them via
    /// the window message queue, which Unity pumps once per frame.
    /// </summary>
    internal sealed class NavlibBridge : INavigation3D1, IDisposable
    {
        private const string CommandSetId = "StudioSpaceMouse";
        private const double LdrawUnitsToMeters = 0.0004; // 1 LDU = 0.4 mm

        private static readonly FieldInfo ModelBoundsField =
            typeof(GameWorld).GetField("m_modelBounds", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly CameraDriver _driver;
        private readonly Action<string> _log;
        private readonly Func<bool> _verbose;
        private readonly object _queueLock = new object();
        private readonly Queue<string> _pendingCommands = new Queue<string>();

        private Navigation3D _nav;
        private Vector3 _hitFrom;
        private Vector3 _hitDirection = Vector3.forward;
        private bool _hitSelectionOnly;
        private int _poseLogs;

        public bool IsOpen { get; private set; }
        public bool IsNavigating { get; private set; }
        public string ProfileName { get; private set; }

        public NavlibBridge(CameraDriver driver, Action<string> log, Func<bool> verbose)
        {
            _driver = driver;
            _log = log ?? (_ => { });
            _verbose = verbose ?? (() => false);
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Opens the connection to 3DxWare. Throws when the driver is not available.</summary>
        public void Open(string profileName)
        {
            _nav = new Navigation3D((INavigation3D1)this, multiThreaded: false);
            _nav.ExecuteCommand += OnExecuteCommand;
            _nav.KeyDown += OnKeyDown;
            _nav.MotionChanged += OnMotionChanged;
            _nav.SettingsChanged += OnSettingsChanged;
            _nav.Open3DMouse(profileName);
            ProfileName = profileName;
            IsOpen = true;

            try
            {
                RegisterCommands();
            }
            catch (Exception ex)
            {
                _log("Command set registration failed (buttons still work through the driver defaults): " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_nav == null)
            {
                return;
            }
            try
            {
                _nav.ExecuteCommand -= OnExecuteCommand;
                _nav.KeyDown -= OnKeyDown;
                _nav.MotionChanged -= OnMotionChanged;
                _nav.SettingsChanged -= OnSettingsChanged;
                if (IsOpen)
                {
                    _nav.Close();
                }
            }
            catch (Exception ex)
            {
                _log("navlib close failed: " + ex.Message);
            }
            finally
            {
                IsOpen = false;
                _nav = null;
            }
        }

        private void RegisterCommands()
        {
            var set = new CommandSet(CommandSetId, "BrickLink Studio");
            var view = new Category("view", "View");
            view.Add(new Command("fit", "Fit model", "Zoom to fit the whole model (Studio)"));
            view.Add(new Command("fitSelection", "Fit selection", "Zoom to fit the selected bricks"));
            view.Add(new Command("free", "Free camera", "Perspective camera"));
            view.Add(new Command("top", "Top view", "Studio top view"));
            view.Add(new Command("bottom", "Bottom view", "Studio bottom view"));
            view.Add(new Command("front", "Front view", "Studio front view"));
            view.Add(new Command("back", "Back view", "Studio back view"));
            view.Add(new Command("left", "Left view", "Studio left view"));
            view.Add(new Command("right", "Right view", "Studio right view"));
            view.Add(new Command("lockRotation", "Toggle rotation lock", "Lock or unlock viewport rotation"));
            set.Add(view);
            _nav.AddCommands(new CommandTree { set });
            _nav.ActiveCommandSet = set.Id;
        }

        /// <summary>Runs queued command executions. Called once per frame from the plugin's tick.</summary>
        public void Pump(Action<string> executeCommand)
        {
            while (true)
            {
                string cmd;
                lock (_queueLock)
                {
                    if (_pendingCommands.Count == 0)
                    {
                        return;
                    }
                    cmd = _pendingCommands.Dequeue();
                }
                executeCommand(cmd);
            }
        }

        // ------------------------------------------------------------------ events

        private void OnExecuteCommand(object sender, CommandEventArgs e)
        {
            lock (_queueLock)
            {
                _pendingCommands.Enqueue(e.Command);
            }
            e.Handled = true;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // Not handled on purpose: unassigned keys keep the driver's built-in behaviour
            // (fit, standard views, rotation lock and so on) which navlib performs itself.
            if (_verbose())
            {
                _log("driver key " + e.Key);
            }
        }

        private void OnMotionChanged(object sender, MotionEventArgs e)
        {
            IsNavigating = e.IsNavigating;
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            if (_verbose())
            {
                _log("3DxWare settings changed");
            }
        }

        // ------------------------------------------------------------------ frame helpers

        private static Vector3 Flip(Vector3 v)
        {
            return new Vector3(v.x, -v.y, v.z);
        }

        private static NlPoint ToPoint(Vector3 studio)
        {
            Vector3 n = Flip(studio);
            return new NlPoint(n.x, n.y, n.z);
        }

        private static Vector3 FromPoint(NlPoint p)
        {
            return Flip(new Vector3((float)p.X, (float)p.Y, (float)p.Z));
        }

        private static Vector3 FromVector(NlVector v)
        {
            return Flip(new Vector3((float)v.X, (float)v.Y, (float)v.Z));
        }

        private static Box ToBox(Bounds b)
        {
            // Flipping Y swaps which corner is the minimum on that axis.
            return new Box(b.min.x, -b.max.y, b.min.z, b.max.x, -b.min.y, b.max.z);
        }

        private static CameraTarget RequireTarget()
        {
            CameraTarget t = CameraDriver.Resolve();
            if (!t.IsValid)
            {
                throw new NoDataException("No viewport camera");
            }
            return t;
        }

        private static Camera CameraOf(CameraTarget t)
        {
            ViewportCameraRig rig = t.StepEditor != null ? t.StepEditor.ViewportCameraRig : t.BuildViewport.ViewportCameraRig;
            Camera cam = rig != null ? rig.RaycastCamera : null;
            if (cam == null)
            {
                throw new NoDataException("No camera");
            }
            return cam;
        }

        // ------------------------------------------------------------------ ISpace3D

        public NlMatrix GetCoordinateSystem()
        {
            // After the Y flip the world is right-handed and Y-up: navlib's own convention.
            return new NlMatrix(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
        }

        public NlMatrix GetFrontView()
        {
            // Studio's front view (theta 0, fai 0) puts the camera on +Z looking down -Z.
            return new NlMatrix(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
        }

        // ------------------------------------------------------------------ IView

        public NlMatrix GetCameraMatrix()
        {
            CameraTarget t = RequireTarget();
            Transform tr = t.Orbit.transform;
            Vector3 r = Flip(tr.right);
            Vector3 u = Flip(tr.up);
            Vector3 b = Flip(-tr.forward);
            Vector3 p = Flip(tr.position);
            return new NlMatrix(
                r.x, r.y, r.z, 0,
                u.x, u.y, u.z, 0,
                b.x, b.y, b.z, 0,
                p.x, p.y, p.z, 1);
        }

        public void SetCameraMatrix(NlMatrix m)
        {
            CameraTarget t = RequireTarget();
            Vector3 up = Flip(new Vector3((float)m.M21, (float)m.M22, (float)m.M23));
            Vector3 forward = Flip(-new Vector3((float)m.M31, (float)m.M32, (float)m.M33));
            Vector3 position = Flip(new Vector3((float)m.M41, (float)m.M42, (float)m.M43));
            string result = _driver.ApplyPose(t.Orbit, position, forward, up);
            if (_verbose() && _poseLogs < 20)
            {
                _poseLogs++;
                _log("pose from driver: " + result);
            }
        }

        public Box GetViewExtents()
        {
            CameraTarget t = RequireTarget();
            Camera cam = CameraOf(t);
            double h = cam.orthographicSize;
            double w = h * cam.aspect;
            return new Box(-w, -h, -cam.farClipPlane, w, h, -cam.nearClipPlane);
        }

        public void SetViewExtents(Box extents)
        {
            CameraTarget t = RequireTarget();
            Camera cam = CameraOf(t);
            double newHalfHeight = (extents.Max.Y - extents.Min.Y) * 0.5;
            double oldHalfHeight = cam.orthographicSize;
            if (oldHalfHeight <= 1e-6 || newHalfHeight <= 1e-6)
            {
                return;
            }
            // Studio derives its orthographic size from the orbit distance, so scale that.
            _driver.Zoom(t.Orbit, (float)(newHalfHeight / oldHalfHeight));
        }

        public double GetViewFOV()
        {
            CameraTarget t = RequireTarget();
            Camera cam = CameraOf(t);
            double vertical = cam.fieldOfView * Math.PI / 180.0;
            return 2.0 * Math.Atan(Math.Tan(vertical * 0.5) * cam.aspect);
        }

        public void SetViewFOV(double fov)
        {
            // Studio's field of view is fixed.
        }

        public Frustum GetViewFrustum()
        {
            CameraTarget t = RequireTarget();
            Camera cam = CameraOf(t);
            double near = cam.nearClipPlane;
            double far = cam.farClipPlane;
            double halfHeight = near * Math.Tan(cam.fieldOfView * Math.PI / 360.0);
            double halfWidth = halfHeight * cam.aspect;
            return new Frustum(-halfWidth, halfWidth, -halfHeight, halfHeight, near, far);
        }

        public void SetViewFrustum(Frustum frustum)
        {
        }

        public bool IsViewPerspective()
        {
            CameraTarget t = RequireTarget();
            return !CameraOf(t).orthographic;
        }

        public bool IsViewRotatable()
        {
            CameraTarget t = RequireTarget();
            return t.Orbit.isAllowRotation;
        }

        public NlPoint GetCameraTarget()
        {
            CameraTarget t = RequireTarget();
            return ToPoint(t.Orbit.targetPos);
        }

        public void SetCameraTarget(NlPoint target)
        {
            CameraTarget t = RequireTarget();
            t.Orbit.TargetPos = FromPoint(target);
        }

        public NlPlane GetViewConstructionPlane()
        {
            // Studio's baseplate is the plane y = 0; after the flip its normal is +Y.
            return new NlPlane(new[] { 0.0, 1.0, 0.0, 0.0 });
        }

        public NlPoint GetPointerPosition()
        {
            throw new NoDataException("No 3D pointer");
        }

        public void SetPointerPosition(NlPoint position)
        {
        }

        // ------------------------------------------------------------------ IView1

        public double GetViewFocusDistance()
        {
            CameraTarget t = RequireTarget();
            return t.Orbit.ZoomDistance;
        }

        // ------------------------------------------------------------------ IModel

        public Box GetModelExtents()
        {
            GameWorld world = GameWorld.Current;
            if (world == null || ModelBoundsField == null)
            {
                throw new NoDataException("No model");
            }
            var bounds = (Bounds)ModelBoundsField.GetValue(world);
            if (bounds.size.x <= 0f && bounds.size.y <= 0f && bounds.size.z <= 0f)
            {
                throw new NoDataException("Empty model");
            }
            return ToBox(bounds);
        }

        public bool IsSelectionEmpty()
        {
            SelectionManager sm = SelectionManager.Instance;
            return sm == null || sm.SelectingCount <= 0;
        }

        public Box GetSelectionExtents()
        {
            SelectionManager sm = SelectionManager.Instance;
            if (sm == null || sm.SelectingCount <= 0)
            {
                throw new NoDataException("Nothing selected");
            }
            return ToBox(sm.GetBoundsOfSelectingBricks());
        }

        public NlMatrix GetSelectionTransform()
        {
            throw new NotImplementedException();
        }

        public void SetSelectionTransform(NlMatrix matrix)
        {
            throw new NotImplementedException();
        }

        // ------------------------------------------------------------------ IModel1

        public double GetUnitsToMeters()
        {
            return LdrawUnitsToMeters;
        }

        public NlPlane GetFloorPlane()
        {
            return new NlPlane(new[] { 0.0, 1.0, 0.0, 0.0 });
        }

        // ------------------------------------------------------------------ IPivot

        public NlPoint GetPivotPosition()
        {
            CameraTarget t = RequireTarget();
            return ToPoint(t.Orbit.targetPos);
        }

        public void SetPivotPosition(NlPoint position)
        {
            // The driver's pivot is reflected back into Studio's orbit target by ApplyPose,
            // which keeps the target on the view ray; nothing to display here.
        }

        public bool IsUserPivot()
        {
            return false;
        }

        public void SetPivotVisible(bool visible)
        {
        }

        // ------------------------------------------------------------------ IHit

        public void SetLookFrom(NlPoint eye)
        {
            _hitFrom = FromPoint(eye);
        }

        public void SetLookDirection(NlVector direction)
        {
            _hitDirection = FromVector(direction);
        }

        public void SetLookAperture(double aperture)
        {
        }

        public void SetSelectionOnly(bool onlySelection)
        {
            _hitSelectionOnly = onlySelection;
        }

        public NlPoint GetLookAt()
        {
            if (_hitSelectionOnly && IsSelectionEmpty())
            {
                throw new NoDataException("Nothing selected");
            }
            RaycastHit hit;
            if (Physics.Raycast(_hitFrom, _hitDirection, out hit, float.PositiveInfinity))
            {
                return ToPoint(hit.point);
            }
            throw new NoDataException("No hit");
        }
    }
}
