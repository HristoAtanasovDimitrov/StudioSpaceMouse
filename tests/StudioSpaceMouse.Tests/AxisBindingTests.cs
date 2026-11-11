using Xunit;

namespace StudioSpaceMouse.Tests
{
    public class AxisBindingTests
    {
        private static readonly AxisBinding Fallback = new AxisBinding { Source = AxisBinding.Axis.Ry, Sign = 1f };

        [Theory]
        [InlineData("Tx", AxisBinding.Axis.Tx, 1f)]
        [InlineData("-Tz", AxisBinding.Axis.Tz, -1f)]
        [InlineData("+Rz", AxisBinding.Axis.Rz, 1f)]
        [InlineData(" - rx ", AxisBinding.Axis.Rx, -1f)]
        [InlineData("TY", AxisBinding.Axis.Ty, 1f)]
        public void Parse_AcceptsAxisNamesWithOptionalSign(string text, AxisBinding.Axis axis, float sign)
        {
            var b = AxisBinding.Parse(text, Fallback);
            Assert.Equal(axis, b.Source);
            Assert.Equal(sign, b.Sign);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("banana")]
        [InlineData("-")]
        public void Parse_FallsBackOnGarbage(string text)
        {
            var b = AxisBinding.Parse(text, Fallback);
            Assert.Equal(Fallback.Source, b.Source);
            Assert.Equal(Fallback.Sign, b.Sign);
        }

        [Fact]
        public void Read_ReturnsTheBoundAxis()
        {
            var s = new SpaceMouseState { Tx = 1, Ty = 2, Tz = 3, Rx = 4, Ry = 5, Rz = 6 };
            Assert.Equal(1, AxisBinding.Parse("Tx", Fallback).Read(s));
            Assert.Equal(2, AxisBinding.Parse("Ty", Fallback).Read(s));
            Assert.Equal(3, AxisBinding.Parse("Tz", Fallback).Read(s));
            Assert.Equal(4, AxisBinding.Parse("Rx", Fallback).Read(s));
            Assert.Equal(5, AxisBinding.Parse("Ry", Fallback).Read(s));
            Assert.Equal(6, AxisBinding.Parse("Rz", Fallback).Read(s));
            Assert.Equal(0, new AxisBinding().Read(s));
        }
    }
}
