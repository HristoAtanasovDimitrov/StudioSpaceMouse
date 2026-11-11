using System;

namespace StudioSpaceMouse
{
    /// <summary>
    /// Turns a raw device axis value into a normalised -1..1 control value:
    /// dead zone (rescaled so the output is continuous at the edge), clamp to
    /// full scale, then an optional power curve that keeps the sign.
    /// </summary>
    public static class AxisShaper
    {
        public static float Shape(short raw, int deadZone, int fullScale, float curve)
        {
            if (fullScale <= 0)
            {
                return 0f;
            }
            if (deadZone < 0)
            {
                deadZone = 0;
            }
            if (deadZone >= fullScale)
            {
                deadZone = fullScale - 1;
            }

            int magnitude = Math.Abs((int)raw);
            if (magnitude <= deadZone)
            {
                return 0f;
            }

            float normalised = (magnitude - deadZone) / (float)(fullScale - deadZone);
            if (normalised > 1f)
            {
                normalised = 1f;
            }
            if (curve > 0f && Math.Abs(curve - 1f) > 1e-6f)
            {
                normalised = (float)Math.Pow(normalised, curve);
            }
            return raw < 0 ? -normalised : normalised;
        }
    }
}
