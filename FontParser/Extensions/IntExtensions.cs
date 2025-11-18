namespace FontParser.Extensions
{
    public static class IntExtensions
    {
        public static float ToFixed(this int value)
        {
            return value / 65536f;
        }
    }
}