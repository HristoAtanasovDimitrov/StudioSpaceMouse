using System;
using System.Reflection;
using Studio.Presentation.Camera;
using UnityEngine;
using StudioViewport = Script.UI_Script.Viewport.Viewport;

namespace StudioSpaceMouse
{
    /// <summary>
    /// The camera the SpaceMouse should drive right now: the orbit control plus whichever
    /// Studio viewport object owns it, since build mode and the instruction maker's step
    /// editor expose view switching and zoom-to-fit through different classes.
    /// </summary>
    internal struct CameraTarget
    {
        public OrbitControl Orbit;
        public StudioViewport BuildViewport;
        public Ins_StepEditor_Viewport StepEditor;

        public bool IsValid => Orbit != null;
        public string Kind => StepEditor != null ? "step editor" : "build viewport";
    }

    /// <summary>
    /// Drives Studio's own orbit camera (OrbitControl) the same way its mouse
    /// handlers do, so the orientation cube, viewport prefs and zoom-to-fit state
    /// stay consistent. Rotation goes through the private f0Dir/setTransform pair
    /// because that is where Studio wraps angles and tracks the inverted state.
    /// </summary>
    internal sealed class CameraDriver
    {
        private static readonly FieldInfo F0Dir =
            typeof(OrbitControl).GetField("f0Dir", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo SetTransform =
            typeof(OrbitControl).GetMethod("setTransform", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo UpdateLockButton =
            typeof(StudioViewport).GetMethod("UpdateLockButton", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ZoomDistanceField =
            typeof(OrbitControl).GetField("zoomDistance", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Action<string> _warn;
        private float _lastLockMessageTime = -10f;

        public CameraDriver(Action<string> warn)
        {
            _warn = warn ?? (_ => { });
        }

        public static string DescribeBindings()
        {
            return string.Format("f0Dir={0} setTransform={1} zoomDistance={2} UpdateLockButton={3}",
                F0Dir != null ? "ok" : "MISSING",
                SetTransform != null ? "ok" : "MISSING",
                ZoomDistanceField != null ? "ok" : "MISSING",
                UpdateLockButton != null ? "ok" : "missing (lock button icon will not refresh)");
        }

        public static bool IsUsable => F0Dir != null && SetTransform != null && ZoomDistanceField != null;

        /// <summary>
        /// Puts the orbit camera at an arbitrary pose given by the driver: position, view
        /// direction and up vector in Studio world space. Studio's camera has no roll, so the
        /// pose is reduced to orbit angles, an orbit target on the view ray and a distance.
        /// Returns a short description of what changed, for verbose logging.
        /// </summary>
        public string ApplyPose(OrbitControl oc, Vector3 position, Vector3 forward, Vector3 up)
        {
            if (forward.sqrMagnitude < 1e-12f)
            {
                return "ignored (degenerate forward)";
            }
            forward.Normalize();

            float oldTheta = oc.theta, oldFai = oc.fai, oldZoom = oc.ZoomDistance;
            Vector3 oldTarget = oc.targetPos;
            bool fixedView = oc.ViewportCameraOrientation != CameraOrientation.Free;

            // Keep the orbit target at the same depth along the new view ray, so the camera
            // position the driver asked for is reproduced exactly by Studio's orbit maths.
            float d = Vector3.Dot(oldTarget - position, forward);
            if (fixedView)
            {
                // Studio renders fixed views from a constant distance; keep its own zoom value
                // so switching back to Free does not land somewhere absurd.
                d = Mathf.Max(oldZoom, OrbitControl.ZOOM_DISTANCE_MIN);
            }
            else if (d < OrbitControl.ZOOM_DISTANCE_MIN)
            {
                d = Mathf.Max(oldZoom, OrbitControl.ZOOM_DISTANCE_MIN);
            }
            if (d > OrbitControl.ZOOM_DISTANCE_MAX)
            {
                d = OrbitControl.ZOOM_DISTANCE_MAX;
            }
            Vector3 newTarget = position + forward * d;

            // Studio: offset = target -> camera = (zoom cos fai sin theta, zoom sin fai, zoom cos fai cos theta).
            // Camera looks along -offset, so offset direction = -forward.
            float sinFai = Mathf.Clamp(-forward.y, -1f, 1f);
            float fai = Mathf.Asin(sinFai);
            float theta = Mathf.Atan2(-forward.x, -forward.z);

            // Over the pole the same direction is represented with cos(fai) < 0 and theta + pi;
            // Studio's camera is upright (rolled 180 about forward) when not inverted, so the
            // world-up component of the camera's up vector tells the two apart.
            bool inverted = Vector3.Dot(up, Vector3.up) > 0f;
            if (inverted)
            {
                fai = fai >= 0f ? Mathf.PI - fai : -Mathf.PI - fai;
                theta += Mathf.PI;
            }
            if (theta > Mathf.PI) theta -= 2f * Mathf.PI;
            if (theta <= -Mathf.PI) theta += 2f * Mathf.PI;

            oc.theta = theta;
            oc.fai = fai;
            oc.targetPos = newTarget;
            ZoomDistanceField.SetValue(oc, d);
            F0Dir.SetValue(oc, Vector3.zero);
            SetTransform.Invoke(oc, null);
            oc.IsNeedToSetTransform = false;

            bool rotated = Mathf.Abs(theta - oldTheta) > 1e-6f || Mathf.Abs(fai - oldFai) > 1e-6f;
            bool zoomed = Mathf.Abs(d - oldZoom) > 1e-4f;
            bool moved = (newTarget - oldTarget).sqrMagnitude > 1e-8f;
            if (rotated)
            {
                oc.onChangeRotation?.Invoke();
                oc.onChangeRotationByMouse?.Invoke();
            }
            if (zoomed)
            {
                oc.onChangeZoom?.Invoke();
            }
            if (moved)
            {
                oc.onChangePosition?.Invoke();
            }

            float error = fixedView ? 0f : (oc.transform.position - position).magnitude;
            return string.Format("theta {0:F3} fai {1:F3} zoom {2:F0} inverted {3} posError {4:F2}", theta, fai, d, inverted, error);
        }

        /// <summary>
        /// The step editor's camera when the instruction maker is open, otherwise the build-mode
        /// viewport under the mouse, otherwise the selected build-mode viewport.
        /// </summary>
        public static CameraTarget Resolve()
        {
            var target = new CameraTarget();

            Ins_StepEditor_Viewport stepEditor = Ins_StepEditor_Viewport.Current;
            if (stepEditor != null && stepEditor.isActiveAndEnabled)
            {
                OrbitControl oc = OrbitOf(stepEditor.ViewportCameraRig);
                if (oc != null)
                {
                    target.Orbit = oc;
                    target.StepEditor = stepEditor;
                    return target;
                }
            }

            ViewportsManager vm = ViewportsManager.Instance;
            if (vm == null)
            {
                return target;
            }
            StudioViewport v = vm.IsHoverViewport ? vm.HoverViewport : null;
            if (v == null)
            {
                v = vm.CurrentViewport;
            }
            if (v != null)
            {
                OrbitControl oc = OrbitOf(v.ViewportCameraRig);
                if (oc != null)
                {
                    target.Orbit = oc;
                    target.BuildViewport = v;
                }
            }
            return target;
        }

        private static OrbitControl OrbitOf(ViewportCameraRig rig)
        {
            if (rig == null)
            {
                return null;
            }
            OrbitControl oc = rig.OrbitControl;
            if (oc == null || !oc.isActiveAndEnabled)
            {
                return null;
            }
            return oc;
        }

        /// <summary>Orbit by the given angles in degrees. Honours the viewport rotation lock.</summary>
        public void Rotate(OrbitControl oc, float yawDegrees, float pitchDegrees)
        {
            if (yawDegrees == 0f && pitchDegrees == 0f)
            {
                return;
            }
            if (!oc.isAllowRotation)
            {
                if (Time.unscaledTime - _lastLockMessageTime > 1.5f)
                {
                    _lastLockMessageTime = Time.unscaledTime;
                    ProgressMsg.Show("The viewport rotation lock is on.");
                }
                return;
            }
            // setTransform: theta += PI/180 * f0Dir.x ; fai += -PI/180 * f0Dir.y
            F0Dir.SetValue(oc, new Vector3(yawDegrees, pitchDegrees, 0f));
            SetTransform.Invoke(oc, null);
            oc.onChangeRotation?.Invoke();
            oc.onChangeRotationByMouse?.Invoke();
        }

        /// <summary>Pan in Studio's own "screen pixel" units; 800 units moves half the zoom distance.</summary>
        public void Pan(OrbitControl oc, float dx, float dy)
        {
            if (dx == 0f && dy == 0f)
            {
                return;
            }
            if (!oc.isAllowPanning)
            {
                return;
            }
            oc.pan(new Vector2(dx, dy));
        }

        /// <summary>Multiply the zoom distance by <paramref name="factor"/> (greater than 1 moves away).</summary>
        public void Zoom(OrbitControl oc, float factor)
        {
            if (factor == 1f || factor <= 0f)
            {
                return;
            }
            float target = oc.ZoomDistance * factor;
            if (target < OrbitControl.ZOOM_DISTANCE_MIN)
            {
                target = OrbitControl.ZOOM_DISTANCE_MIN;
            }
            if (target > OrbitControl.ZOOM_DISTANCE_MAX)
            {
                target = OrbitControl.ZOOM_DISTANCE_MAX;
            }
            oc.ZoomDistance = target;
        }

        public void ToggleRotationLock(CameraTarget t)
        {
            t.Orbit.isAllowRotation = !t.Orbit.isAllowRotation;
            if (t.BuildViewport != null && UpdateLockButton != null)
            {
                try
                {
                    UpdateLockButton.Invoke(t.BuildViewport, null);
                }
                catch (Exception ex)
                {
                    _warn("UpdateLockButton failed: " + ex.Message);
                }
            }
        }

        public void SetOrientation(CameraTarget t, CameraOrientation orientation)
        {
            if (t.StepEditor != null)
            {
                t.StepEditor.SetCameraOrientation(orientation);
            }
            else if (t.BuildViewport != null)
            {
                t.BuildViewport.SetCameraOrientation(orientation);
            }
        }

        public void ZoomToFit(CameraTarget t, bool selectedOnly)
        {
            if (t.StepEditor != null)
            {
                if (selectedOnly)
                {
                    t.StepEditor.ZoomToFitSelection();
                }
                else
                {
                    t.StepEditor.zoomToFit();
                }
            }
            else if (t.BuildViewport != null)
            {
                t.BuildViewport.zoomToFit(selectedOnly);
            }
        }
    }
}
