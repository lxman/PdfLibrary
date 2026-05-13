using Jp2Codec.Codestream;

namespace Jp2Codec.Tests.Codestream
{
    public sealed class MarkerCodeTests
    {
        [Theory]
        [InlineData(MarkerCode.Soc, true)]
        [InlineData(MarkerCode.Sot, true)]
        [InlineData(MarkerCode.Sod, true)]
        [InlineData(MarkerCode.Eoc, true)]
        [InlineData(MarkerCode.Siz, true)]
        [InlineData(MarkerCode.Cod, true)]
        [InlineData(MarkerCode.Qcd, true)]
        public void IsValidMarker_PartOneMarkers_ReturnsTrue(ushort code, bool expected)
        {
            Assert.Equal(expected, MarkerCode.IsValidMarker(code));
        }

        [Theory]
        [InlineData((ushort)0x0000)]
        [InlineData((ushort)0x1234)]
        [InlineData((ushort)0xFE00)]
        [InlineData((ushort)0xFF00)]
        [InlineData((ushort)0xFF4E)]
        public void IsValidMarker_NonMarkerBytes_ReturnsFalse(ushort code)
        {
            Assert.False(MarkerCode.IsValidMarker(code));
        }

        [Theory]
        [InlineData(MarkerCode.Soc, false)]
        [InlineData(MarkerCode.Sod, false)]
        [InlineData(MarkerCode.Eoc, false)]
        [InlineData(MarkerCode.Eph, false)]
        [InlineData(MarkerCode.Sot, true)]
        [InlineData(MarkerCode.Siz, true)]
        [InlineData(MarkerCode.Cod, true)]
        [InlineData(MarkerCode.Qcd, true)]
        [InlineData(MarkerCode.Com, true)]
        public void HasSegmentLength_DelimitersHaveNoLength_OthersDo(ushort marker, bool expected)
        {
            Assert.Equal(expected, MarkerCode.HasSegmentLength(marker));
        }

        [Theory]
        [InlineData(MarkerCode.Soc, "SOC")]
        [InlineData(MarkerCode.Sot, "SOT")]
        [InlineData(MarkerCode.Eoc, "EOC")]
        [InlineData((ushort)0xFFAB, "0xFFAB")]
        public void Format_KnownMarkersGetMnemonic_OthersGetHex(ushort code, string expected)
        {
            Assert.Equal(expected, MarkerCode.Format(code));
        }
    }
}
