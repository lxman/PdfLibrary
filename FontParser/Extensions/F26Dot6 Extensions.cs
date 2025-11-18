namespace FontParser.Extensions
{
    public static class F26Dot6Extensions
    {
        public static float ToF26Dot6(this uint value)
        {
            return value / 64f;
        }

        public static uint FromF26Dot6(this float value)
        {
            return (uint)(value * 64);
        }
    }
}