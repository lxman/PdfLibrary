using System;
using Jp2Codec.Color;

namespace Jp2Codec.Tests.Color
{
    public sealed class InverseDcLevelShiftTests
    {
        // ==== Integer overload (reversible path) =================================

        [Fact]
        public void Int_Unsigned8Bit_AddsHalfRange()
        {
            // For B=8 unsigned, the encoder subtracted 128 before FDWT; the
            // inverse adds it back.
            int[,] block =
            {
                { -128, -1, 0, 1, 127 },
            };

            InverseDcLevelShift.Apply(block, precision: 8, isSigned: false);

            Assert.Equal(0, block[0, 0]);
            Assert.Equal(127, block[0, 1]);
            Assert.Equal(128, block[0, 2]);
            Assert.Equal(129, block[0, 3]);
            Assert.Equal(255, block[0, 4]);
        }

        [Fact]
        public void Int_Unsigned12Bit_AddsCorrectShift()
        {
            // 2^11 = 2048.
            int[,] block = { { -2048, 0, 2047 } };

            InverseDcLevelShift.Apply(block, precision: 12, isSigned: false);

            Assert.Equal(0, block[0, 0]);
            Assert.Equal(2048, block[0, 1]);
            Assert.Equal(4095, block[0, 2]);
        }

        [Fact]
        public void Int_Signed_PassesThrough()
        {
            int[,] block = { { -100, 0, 100 } };

            InverseDcLevelShift.Apply(block, precision: 8, isSigned: true);

            Assert.Equal(-100, block[0, 0]);
            Assert.Equal(0, block[0, 1]);
            Assert.Equal(100, block[0, 2]);
        }

        [Fact]
        public void Int_RejectsZeroPrecision()
        {
            int[,] block = { { 0 } };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                InverseDcLevelShift.Apply(block, precision: 0, isSigned: false));
        }

        [Fact]
        public void Int_RejectsOversizedPrecision()
        {
            int[,] block = { { 0 } };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                InverseDcLevelShift.Apply(block, precision: 39, isSigned: false));
        }

        // ==== Float overload (irreversible path) =================================

        [Fact]
        public void Float_Unsigned8Bit_AddsHalfRange()
        {
            float[,] block =
            {
                { -128.0f, -0.5f, 0.0f, 0.5f, 127.0f },
            };

            InverseDcLevelShift.Apply(block, precision: 8, isSigned: false);

            Assert.Equal(0.0f, block[0, 0]);
            Assert.Equal(127.5f, block[0, 1]);
            Assert.Equal(128.0f, block[0, 2]);
            Assert.Equal(128.5f, block[0, 3]);
            Assert.Equal(255.0f, block[0, 4]);
        }

        [Fact]
        public void Float_Signed_PassesThrough()
        {
            float[,] block = { { -10.5f, 0.0f, 10.5f } };

            InverseDcLevelShift.Apply(block, precision: 8, isSigned: true);

            Assert.Equal(-10.5f, block[0, 0]);
            Assert.Equal(0.0f, block[0, 1]);
            Assert.Equal(10.5f, block[0, 2]);
        }

        [Fact]
        public void Float_RejectsInvalidPrecision()
        {
            float[,] block = { { 0f } };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                InverseDcLevelShift.Apply(block, precision: 0, isSigned: false));
        }

        // ==== Null arg tests =====================================================

        [Fact]
        public void Int_NullArg_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                InverseDcLevelShift.Apply((int[,])null!, precision: 8, isSigned: false));
        }

        [Fact]
        public void Float_NullArg_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                InverseDcLevelShift.Apply((float[,])null!, precision: 8, isSigned: false));
        }
    }
}
