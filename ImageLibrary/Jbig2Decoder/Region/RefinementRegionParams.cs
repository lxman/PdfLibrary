using Jbig2Decoder.Image;

namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Parameters for generic refinement region decoding (T.88 §6.3 / Table 6).
    /// </summary>
    internal struct RefinementRegionParams
    {
        public int GrTemplate;            // 0 or 1
        public bool TpgrOn;
        public Bitmap Reference;          // GRREFERENCE (the image being refined)
        public int ReferenceDx;           // GRREFERENCEDX
        public int ReferenceDy;           // GRREFERENCEDY
        public sbyte[] Grat;              // 4 bytes — only template 0 uses these (templates 1 has fixed positions)
    }
}
