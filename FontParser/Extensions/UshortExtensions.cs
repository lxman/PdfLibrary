namespace FontParser.Extensions
{
    public static class UshortExtensions
    {
        public static float ToF26Dot6(this ushort value)
        {
            return value / 64f;
        }

        public static float ToF2Dot14(this ushort value)
        {
            return value / 16384f;
        }
    }
}