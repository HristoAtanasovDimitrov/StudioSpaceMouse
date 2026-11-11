namespace StudioSpaceMouse
{
    /// <summary>
    /// Latest raw values reported by the device. Axis values are the device's native
    /// signed 16-bit units (roughly -350..350 at full deflection on most models).
    /// </summary>
    public sealed class SpaceMouseState
    {
        public short Tx;
        public short Ty;
        public short Tz;
        public short Rx;
        public short Ry;
        public short Rz;

        /// <summary>Button bitmask, bit N = button N as numbered by the HID report.</summary>
        public ulong Buttons;

        /// <summary>
        /// Set once a separate rotation report (id 2) has been seen. Raw Input pads every report
        /// to the device's largest report size, so a translation report can carry zero bytes where
        /// a combined-format device would put rotation; this flag tells the parser which it is.
        /// </summary>
        public bool RotationReportSeen;

        /// <summary>
        /// Key currently held, as reported by report id 0x1C on newer devices: 1-based button
        /// number, 0 when none. Older devices never send it.
        /// </summary>
        public byte HeldKey;

        /// <summary>
        /// Key the device's firmware recognised as a long press (report id 0x1D): 1-based button
        /// number while the long press is active, 0 when released. Such a press never sets the
        /// bit in <see cref="Buttons"/>; the firmware decides short versus long itself.
        /// </summary>
        public byte LongPressKey;

        public void CopyFrom(SpaceMouseState other)
        {
            Tx = other.Tx; Ty = other.Ty; Tz = other.Tz;
            Rx = other.Rx; Ry = other.Ry; Rz = other.Rz;
            Buttons = other.Buttons;
            RotationReportSeen = other.RotationReportSeen;
            HeldKey = other.HeldKey;
            LongPressKey = other.LongPressKey;
        }

        public void ClearAxes()
        {
            Tx = Ty = Tz = Rx = Ry = Rz = 0;
        }

        public bool AnyAxis => Tx != 0 || Ty != 0 || Tz != 0 || Rx != 0 || Ry != 0 || Rz != 0;
    }
}
