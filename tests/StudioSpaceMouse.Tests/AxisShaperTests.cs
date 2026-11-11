using Xunit;

namespace StudioSpaceMouse.Tests
{
    public class AxisShaperTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(-5)]
        [InlineData(10)]
        public void InsideDeadZone_IsZero(short raw)
        {
            Assert.Equal(0f, AxisShaper.Shape(raw, deadZone: 10, fullScale: 350, curve: 1f));
        }

        [Fact]
        public void FullDeflection_IsOne()
        {
            Assert.Equal(1f, AxisShaper.Shape(350, 10, 350, 1f), 4);
            Assert.Equal(-1f, AxisShaper.Shape(-350, 10, 350, 1f), 4);
        }

        [Fact]
        public void BeyondFullScale_IsClampedToOne()
        {
            Assert.Equal(1f, AxisShaper.Shape(900, 10, 350, 1f), 4);
        }

        [Fact]
        public void DeadZoneEdge_IsContinuous()
        {
            // Just past the dead zone the output should be near zero, not jump.
            float v = AxisShaper.Shape(11, 10, 350, 1f);
            Assert.True(v > 0f && v < 0.01f, "got " + v);
        }

        [Fact]
        public void Curve_Two_SquaresTheNormalisedValue_KeepingSign()
        {
            float linear = AxisShaper.Shape(180, 10, 350, 1f);
            float squared = AxisShaper.Shape(180, 10, 350, 2f);
            Assert.Equal(linear * linear, squared, 4);
            Assert.True(AxisShaper.Shape(-180, 10, 350, 2f) < 0f);
        }
    }
}
