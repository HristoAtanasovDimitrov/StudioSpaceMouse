using System;
using System.Threading;
using TDx.SpaceMouse.Navigation3D;

namespace StudioSpaceMouse.NavlibHarness
{
    /// <summary>
    /// Opens a navlib connection outside Studio with a fake camera, to check that 3DxWare is
    /// reachable, accepts the profile and calls back. Usage: NavlibHarness [seconds] [profile]
    /// </summary>
    internal sealed class FakeScene : INavigation3D1
    {
        public Matrix Camera = new Matrix(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 1000, 1);
        public int CameraWrites;

        public Matrix GetCoordinateSystem() => new Matrix(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
        public Matrix GetFrontView() => new Matrix(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
        public Matrix GetCameraMatrix() => Camera;
        public void SetCameraMatrix(Matrix m)
        {
            Camera = m;
            CameraWrites++;
            if (CameraWrites <= 5 || CameraWrites % 50 == 0)
            {
                Console.WriteLine("  SetCameraMatrix #{0}: pos=({1:F1},{2:F1},{3:F1})", CameraWrites, m.M41, m.M42, m.M43);
            }
        }
        public Box GetViewExtents() => new Box(-100, -100, -10000, 100, 100, -1);
        public void SetViewExtents(Box extents) { }
        public double GetViewFOV() => 0.5;
        public void SetViewFOV(double fov) { }
        public Frustum GetViewFrustum() => new Frustum(-0.1, 0.1, -0.1, 0.1, 1, 10000);
        public void SetViewFrustum(Frustum frustum) { }
        public bool IsViewPerspective() => true;
        public bool IsViewRotatable() => true;
        public Point GetCameraTarget() => new Point(0, 0, 0);
        public void SetCameraTarget(Point target) { }
        public Plane GetViewConstructionPlane() => new Plane(new[] { 0.0, 1.0, 0.0, 0.0 });
        public Point GetPointerPosition() => throw new NoDataException();
        public void SetPointerPosition(Point position) { }
        public double GetViewFocusDistance() => 1000;
        public Box GetModelExtents() => new Box(-200, -100, -200, 200, 100, 200);
        public bool IsSelectionEmpty() => true;
        public Box GetSelectionExtents() => throw new NoDataException();
        public Matrix GetSelectionTransform() => throw new NotImplementedException();
        public void SetSelectionTransform(Matrix matrix) => throw new NotImplementedException();
        public double GetUnitsToMeters() => 0.0004;
        public Plane GetFloorPlane() => new Plane(new[] { 0.0, 1.0, 0.0, 0.0 });
        public Point GetPivotPosition() => new Point(0, 0, 0);
        public void SetPivotPosition(Point position) { }
        public bool IsUserPivot() => false;
        public void SetPivotVisible(bool visible) { }
        public void SetLookFrom(Point eye) { }
        public void SetLookDirection(Vector direction) { }
        public void SetLookAperture(double aperture) { }
        public void SetSelectionOnly(bool onlySelection) { }
        public Point GetLookAt() => throw new NoDataException();
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            int seconds = args.Length > 0 ? int.Parse(args[0]) : 6;
            string profile = args.Length > 1 ? args[1] : "BrickLink Studio";
            var scene = new FakeScene();
            Console.WriteLine("Process is {0}-bit", IntPtr.Size * 8);
            try
            {
                // Multi-threaded here because a console app has no message pump.
                var nav = new Navigation3D(scene, multiThreaded: true);
                nav.ExecuteCommand += (s, e) => { Console.WriteLine("  command: " + e.Command); e.Handled = true; };
                nav.KeyDown += (s, e) => Console.WriteLine("  key down: " + e.Key);
                nav.MotionChanged += (s, e) => Console.WriteLine("  navigating: " + e.IsNavigating);
                nav.SettingsChanged += (s, e) => Console.WriteLine("  settings changed");
                nav.Open3DMouse(profile);
                Console.WriteLine("Open3DMouse('{0}') succeeded.", profile);
                var set = new CommandSet("HarnessSet", "Harness");
                var cat = new Category("cat", "Category");
                cat.Add(new Command("ping", "Ping", "Test command"));
                set.Add(cat);
                nav.AddCommands(new CommandTree { set }).Wait();
                nav.ActiveCommandSet = set.Id;
                Console.WriteLine("Command set registered. Waiting {0}s; move the cap or press a key on the device to see callbacks.", seconds);
                Thread.Sleep(seconds * 1000);
                nav.Close();
                Console.WriteLine("Closed. Camera writes received: {0}", scene.CameraWrites);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED: " + ex);
                return 1;
            }
        }
    }
}
