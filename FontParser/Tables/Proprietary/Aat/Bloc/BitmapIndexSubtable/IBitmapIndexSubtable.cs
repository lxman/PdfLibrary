namespace FontParser.Tables.Proprietary.Aat.Bloc.BitmapIndexSubtable
{
    public interface IBitmapIndexSubtable
    {
        IndexFormat IndexFormat { get; }

        BlocImageFormat ImageFormat { get; }
    }
}