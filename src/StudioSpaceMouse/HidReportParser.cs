namespace StudioSpaceMouse
{
    /// <summary>
    /// Decodes 3Dconnexion HID input reports. Three report layouts are handled:
    ///   id 1: translation x,y,z (3 x int16 LE), optionally followed by rotation (newer firmware sends all six in one report)
    ///   id 2: rotation rx,ry,rz (3 x int16 LE)
    ///   id 3: button bitmask, little-endian, variable length
    /// Anything else is ignored.
    /// </summary>
    public static class HidReportParser
    {
        public const byte ReportTranslation = 1;
        public const byte ReportRotation = 2;
        public const byte ReportButtons = 3;
        /// <summary>Newer devices: 1-based number of the key currently held, 0 when none.</summary>
        public const byte ReportHeldKey = 0x1C;
        /// <summary>Newer devices: 1-based number of a key the firmware recognised as a long press, 0 on release.</summary>
        public const byte ReportLongPress = 0x1D;

        /// <summary>
        /// Parses one report. <paramref name="length"/> is the number of valid bytes in
        /// <paramref name="data"/> including the leading report id. Returns true when the
        /// state was updated.
        /// </summary>
        public static bool TryParse(byte[] data, int length, SpaceMouseState state)
        {
            if (data == null || state == null || length < 1 || length > data.Length)
            {
                return false;
            }

            switch (data[0])
            {
                case ReportTranslation:
                    if (length < 7)
                    {
                        return false;
                    }
                    state.Tx = ReadInt16(data, 1);
                    state.Ty = ReadInt16(data, 3);
                    state.Tz = ReadInt16(data, 5);
                    // Only a device that never sends report 2 packs rotation into report 1;
                    // otherwise the bytes past the translation are Raw Input padding.
                    if (length >= 13 && !state.RotationReportSeen)
                    {
                        state.Rx = ReadInt16(data, 7);
                        state.Ry = ReadInt16(data, 9);
                        state.Rz = ReadInt16(data, 11);
                    }
                    return true;

                case ReportRotation:
                    if (length < 7)
                    {
                        return false;
                    }
                    state.RotationReportSeen = true;
                    state.Rx = ReadInt16(data, 1);
                    state.Ry = ReadInt16(data, 3);
                    state.Rz = ReadInt16(data, 5);
                    return true;

                case ReportButtons:
                    {
                        ulong mask = 0;
                        int bytes = length - 1;
                        if (bytes > 8)
                        {
                            bytes = 8;
                        }
                        for (int i = 0; i < bytes; i++)
                        {
                            mask |= (ulong)data[1 + i] << (8 * i);
                        }
                        state.Buttons = mask;
                        return true;
                    }

                case ReportHeldKey:
                    if (length < 2)
                    {
                        return false;
                    }
                    state.HeldKey = data[1];
                    return true;

                case ReportLongPress:
                    if (length < 2)
                    {
                        return false;
                    }
                    state.LongPressKey = data[1];
                    return true;

                default:
                    return false;
            }
        }

        private static short ReadInt16(byte[] data, int offset)
        {
            return (short)(data[offset] | (data[offset + 1] << 8));
        }
    }
}
