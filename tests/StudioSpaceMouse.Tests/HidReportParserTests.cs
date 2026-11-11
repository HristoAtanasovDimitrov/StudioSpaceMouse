using Xunit;

namespace StudioSpaceMouse.Tests
{
    public class HidReportParserTests
    {
        private static byte[] Report(byte id, params short[] axes)
        {
            var b = new byte[1 + axes.Length * 2];
            b[0] = id;
            for (int i = 0; i < axes.Length; i++)
            {
                b[1 + i * 2] = (byte)(axes[i] & 0xFF);
                b[2 + i * 2] = (byte)((axes[i] >> 8) & 0xFF);
            }
            return b;
        }

        [Fact]
        public void TranslationReport_SetsTranslationOnly()
        {
            var s = new SpaceMouseState();
            s.Rx = 5; s.Ry = 6; s.Rz = 7;
            var data = Report(1, 100, -200, 350);

            bool changed = HidReportParser.TryParse(data, data.Length, s);

            Assert.True(changed);
            Assert.Equal(100, s.Tx);
            Assert.Equal(-200, s.Ty);
            Assert.Equal(350, s.Tz);
            Assert.Equal(5, s.Rx);
            Assert.Equal(6, s.Ry);
            Assert.Equal(7, s.Rz);
        }

        [Fact]
        public void RotationReport_SetsRotationOnly()
        {
            var s = new SpaceMouseState();
            s.Tx = 1; s.Ty = 2; s.Tz = 3;
            var data = Report(2, -10, 20, -30);

            bool changed = HidReportParser.TryParse(data, data.Length, s);

            Assert.True(changed);
            Assert.Equal(-10, s.Rx);
            Assert.Equal(20, s.Ry);
            Assert.Equal(-30, s.Rz);
            Assert.Equal(1, s.Tx);
            Assert.Equal(2, s.Ty);
            Assert.Equal(3, s.Tz);
        }

        [Fact]
        public void CombinedReport1_With12Bytes_SetsAllSixAxes()
        {
            var s = new SpaceMouseState();
            var data = Report(1, 11, 12, 13, 21, 22, 23);

            bool changed = HidReportParser.TryParse(data, data.Length, s);

            Assert.True(changed);
            Assert.Equal(11, s.Tx); Assert.Equal(12, s.Ty); Assert.Equal(13, s.Tz);
            Assert.Equal(21, s.Rx); Assert.Equal(22, s.Ry); Assert.Equal(23, s.Rz);
        }

        [Fact]
        public void PaddedTranslationReport_AfterRotationReport_KeepsRotation()
        {
            // Raw Input pads reports to the largest report size: a 7-byte translation report
            // shows up as, say, 13 bytes with zeros behind it. Once the device has sent a
            // separate rotation report, those zeros must not clear the rotation.
            var s = new SpaceMouseState();
            var rot = Report(2, 40, 50, 60);
            HidReportParser.TryParse(rot, rot.Length, s);

            var padded = new byte[13];
            padded[0] = 1; padded[1] = 10; padded[3] = 20; padded[5] = 30;
            HidReportParser.TryParse(padded, padded.Length, s);

            Assert.Equal(10, s.Tx); Assert.Equal(20, s.Ty); Assert.Equal(30, s.Tz);
            Assert.Equal(40, s.Rx); Assert.Equal(50, s.Ry); Assert.Equal(60, s.Rz);
        }

        [Fact]
        public void PaddedTranslationReport_BeforeAnyRotationReport_ReadsZeros()
        {
            var s = new SpaceMouseState();
            s.Rx = 99;
            var padded = new byte[13];
            padded[0] = 1; padded[1] = 10;

            HidReportParser.TryParse(padded, padded.Length, s);

            Assert.Equal(0, s.Rx);
            Assert.False(s.RotationReportSeen);
        }

        [Fact]
        public void ButtonReport_BuildsLittleEndianBitmask()
        {
            var s = new SpaceMouseState();
            // bit 1 in byte 0, bit 9 in byte 1, bit 31 in byte 3, bit 32 in byte 4
            var data = new byte[] { 3, 0x02, 0x02, 0x00, 0x80, 0x01 };

            bool changed = HidReportParser.TryParse(data, data.Length, s);

            Assert.True(changed);
            ulong expected = (1UL << 1) | (1UL << 9) | (1UL << 31) | (1UL << 32);
            Assert.Equal(expected, s.Buttons);
        }

        [Fact]
        public void ButtonReport_ReleasesButtons()
        {
            var s = new SpaceMouseState();
            s.Buttons = 0xFF;
            var data = new byte[] { 3, 0x00, 0x00, 0x00, 0x00 };

            HidReportParser.TryParse(data, data.Length, s);

            Assert.Equal(0UL, s.Buttons);
        }

        [Fact]
        public void HeldKeyReport_SetsOneBasedKeyNumber()
        {
            var s = new SpaceMouseState();
            Assert.True(HidReportParser.TryParse(new byte[] { 0x1C, 3, 0, 0 }, 4, s));
            Assert.Equal(3, s.HeldKey);
            Assert.True(HidReportParser.TryParse(new byte[] { 0x1C, 0 }, 2, s));
            Assert.Equal(0, s.HeldKey);
            Assert.Equal(0UL, s.Buttons);
        }

        [Fact]
        public void LongPressReport_SetsAndClearsKey_WithoutTouchingButtons()
        {
            var s = new SpaceMouseState { Buttons = 0x10 };
            Assert.True(HidReportParser.TryParse(new byte[] { 0x1D, 3 }, 2, s));
            Assert.Equal(3, s.LongPressKey);
            Assert.Equal(0x10UL, s.Buttons);
            Assert.True(HidReportParser.TryParse(new byte[] { 0x1D, 0 }, 2, s));
            Assert.Equal(0, s.LongPressKey);
        }

        [Fact]
        public void TruncatedKeyReports_AreIgnored()
        {
            var s = new SpaceMouseState { HeldKey = 5, LongPressKey = 6 };
            Assert.False(HidReportParser.TryParse(new byte[] { 0x1C }, 1, s));
            Assert.False(HidReportParser.TryParse(new byte[] { 0x1D }, 1, s));
            Assert.Equal(5, s.HeldKey);
            Assert.Equal(6, s.LongPressKey);
        }

        [Fact]
        public void UnknownReportId_IsIgnored()
        {
            var s = new SpaceMouseState();
            s.Tx = 42;
            var data = new byte[] { 0x17, 1, 2, 3, 4, 5, 6 };

            bool changed = HidReportParser.TryParse(data, data.Length, s);

            Assert.False(changed);
            Assert.Equal(42, s.Tx);
        }

        [Fact]
        public void TruncatedAxisReport_IsIgnored()
        {
            var s = new SpaceMouseState();
            s.Tx = 42;
            var data = new byte[] { 1, 1, 2, 3 }; // one and a half axes

            bool changed = HidReportParser.TryParse(data, data.Length, s);

            Assert.False(changed);
            Assert.Equal(42, s.Tx);
        }

        [Fact]
        public void LengthArgument_LimitsParsing_NotArraySize()
        {
            var s = new SpaceMouseState();
            // Buffer is larger than the report actually delivered.
            var data = new byte[64];
            data[0] = 1; data[1] = 0x10; data[2] = 0x00; data[3] = 0x20; data[4] = 0x00; data[5] = 0x30; data[6] = 0x00;
            data[7] = 0x7F; data[8] = 0x7F; // bytes beyond length 7 must not be read as rotation

            bool changed = HidReportParser.TryParse(data, 7, s);

            Assert.True(changed);
            Assert.Equal(0x10, s.Tx);
            Assert.Equal(0, s.Rx);
        }
    }
}
