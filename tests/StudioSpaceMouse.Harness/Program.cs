using System;
using System.Threading;

namespace StudioSpaceMouse.Harness
{
    /// <summary>
    /// Runs the raw input reader for a few seconds and prints what it sees.
    /// Usage: StudioSpaceMouse.Harness [seconds] [verbose]
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            int seconds = args.Length > 0 ? int.Parse(args[0]) : 8;
            bool verbose = args.Length > 1 && args[1] == "verbose";

            using (var reader = new RawInputReader(m => Console.WriteLine("[reader] " + m), verbose))
            {
                reader.Start();
                var state = new SpaceMouseState();
                var deadline = DateTime.UtcNow.AddSeconds(seconds);
                int reports = 0;
                SpaceMouseState last = new SpaceMouseState();
                while (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(50);
                    double age;
                    if (!reader.Snapshot(state, out age))
                    {
                        continue;
                    }
                    if (state.Tx != last.Tx || state.Ty != last.Ty || state.Tz != last.Tz ||
                        state.Rx != last.Rx || state.Ry != last.Ry || state.Rz != last.Rz || state.Buttons != last.Buttons)
                    {
                        reports++;
                        last.CopyFrom(state);
                        Console.WriteLine("T=({0},{1},{2}) R=({3},{4},{5}) buttons=0x{6:X} age={7:F3}s",
                            state.Tx, state.Ty, state.Tz, state.Rx, state.Ry, state.Rz, state.Buttons, age);
                    }
                }
                Console.WriteLine("device seen: {0} ({1}); state changes observed: {2}", reader.DeviceSeen, reader.DeviceName ?? "-", reports);
                return reader.DeviceSeen ? 0 : 1;
            }
        }
    }
}
