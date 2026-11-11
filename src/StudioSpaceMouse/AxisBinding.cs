using System;

namespace StudioSpaceMouse
{
    /// <summary>
    /// Which raw device axis feeds a camera action, with an optional sign flip.
    /// Parsed from config strings such as "Tx", "-Tz", "Rz".
    /// </summary>
    public struct AxisBinding
    {
        public enum Axis { None, Tx, Ty, Tz, Rx, Ry, Rz }

        public Axis Source;
        public float Sign;

        public static AxisBinding Parse(string text, AxisBinding fallback)
        {
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }
            string t = text.Trim();
            float sign = 1f;
            if (t.StartsWith("-"))
            {
                sign = -1f;
                t = t.Substring(1).Trim();
            }
            else if (t.StartsWith("+"))
            {
                t = t.Substring(1).Trim();
            }
            Axis axis;
            try
            {
                axis = (Axis)Enum.Parse(typeof(Axis), t, true);
            }
            catch (ArgumentException)
            {
                return fallback;
            }
            return new AxisBinding { Source = axis, Sign = sign };
        }

        public short Read(SpaceMouseState s)
        {
            switch (Source)
            {
                case Axis.Tx: return s.Tx;
                case Axis.Ty: return s.Ty;
                case Axis.Tz: return s.Tz;
                case Axis.Rx: return s.Rx;
                case Axis.Ry: return s.Ry;
                case Axis.Rz: return s.Rz;
                default: return 0;
            }
        }

        public override string ToString()
        {
            return (Sign < 0 ? "-" : "") + Source;
        }
    }
}
