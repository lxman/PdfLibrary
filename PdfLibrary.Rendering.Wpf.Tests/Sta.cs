namespace PdfLibrary.Rendering.Wpf.Tests;

/// <summary>
/// Utility for running WPF-requiring code on a dedicated STA thread.
/// xUnit v2 runs tests on MTA by default; WPF DrawingContext / BitmapSource require STA.
/// </summary>
internal static class Sta
{
    public static T Run<T>(Func<T> func)
    {
        T result = default!;
        Exception? caught = null;

        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { caught = ex; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();

        return result;
    }

    public static void Run(Action action) => Run<bool>(() => { action(); return true; });
}
