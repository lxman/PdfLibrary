using ICCSharp.Profile;

namespace ICCSharp.Tests;

public class IccTransformTests
{
    private static readonly string SrgbPath =
        @"C:\Windows\System32\spool\drivers\color\sRGB Color Space Profile.icm";

    [Fact]
    public void Create_from_profiles_returns_working_transform()
    {
        if (!File.Exists(SrgbPath)) return;
        IccProfile p = IccProfile.Parse(File.ReadAllBytes(SrgbPath));
        IccTransform t = IccTransform.Create(p, p);

        Assert.Equal(3, t.InputChannels);
        Assert.Equal(3, t.OutputChannels);
        double[] result = t.Apply(0.5, 0.5, 0.5);
        Assert.Equal(0.5, result[0], 4);
        Assert.Equal(0.5, result[1], 4);
        Assert.Equal(0.5, result[2], 4);
    }

    [Fact]
    public void Create_from_byte_arrays_round_trips()
    {
        if (!File.Exists(SrgbPath)) return;
        byte[] bytes = File.ReadAllBytes(SrgbPath);
        IccTransform t = IccTransform.Create(bytes, bytes);
        double[] result = t.Apply(0.2, 0.4, 0.6);
        Assert.Equal(0.2, result[0], 4);
        Assert.Equal(0.4, result[1], 4);
        Assert.Equal(0.6, result[2], 4);
    }

    [Fact]
    public void Create_from_file_paths_round_trips()
    {
        if (!File.Exists(SrgbPath)) return;
        IccTransform t = IccTransform.Create(SrgbPath, SrgbPath);
        double[] result = t.Apply(0.7, 0.3, 0.9);
        Assert.Equal(0.7, result[0], 4);
        Assert.Equal(0.3, result[1], 4);
        Assert.Equal(0.9, result[2], 4);
    }

    [Fact]
    public void Options_default_intent_is_relative_colorimetric()
    {
        TransformOptions opts = new();
        Assert.Equal(RenderingIntent.RelativeColorimetric, opts.Intent);
        Assert.False(opts.BlackPointCompensation);
    }

    [Fact]
    public void ApplyMany_batches_through_a_buffer()
    {
        if (!File.Exists(SrgbPath)) return;
        IccTransform t = IccTransform.Create(SrgbPath, SrgbPath);

        const int pixels = 4;
        double[] input = { 0.0, 0.0, 0.0,
                           1.0, 1.0, 1.0,
                           0.5, 0.5, 0.5,
                           0.1, 0.4, 0.8 };
        double[] output = new double[pixels * 3];

        t.ApplyMany(input, output);

        for (int p = 0; p < pixels; p++)
        {
            Assert.Equal(input[p * 3 + 0], output[p * 3 + 0], 4);
            Assert.Equal(input[p * 3 + 1], output[p * 3 + 1], 4);
            Assert.Equal(input[p * 3 + 2], output[p * 3 + 2], 4);
        }
    }

    [Fact]
    public void ApplyMany_rejects_mismatched_input_length()
    {
        if (!File.Exists(SrgbPath)) return;
        IccTransform t = IccTransform.Create(SrgbPath, SrgbPath);
        // 7 samples — not a multiple of 3
        Assert.Throws<ArgumentException>(() => t.ApplyMany(new double[7], new double[9]));
    }

    [Fact]
    public void ApplyMany_rejects_short_output_buffer()
    {
        if (!File.Exists(SrgbPath)) return;
        IccTransform t = IccTransform.Create(SrgbPath, SrgbPath);
        Assert.Throws<ArgumentException>(() => t.ApplyMany(new double[9], new double[6]));
    }

    [Fact]
    public void Custom_options_are_threaded_through()
    {
        if (!File.Exists(SrgbPath)) return;
        IccProfile p = IccProfile.Parse(File.ReadAllBytes(SrgbPath));
        TransformOptions opts = new() { Intent = RenderingIntent.Perceptual, BlackPointCompensation = true };
        IccTransform t = IccTransform.Create(p, p, opts);
        Assert.Equal(RenderingIntent.Perceptual, t.Options.Intent);
        Assert.True(t.Options.BlackPointCompensation);
    }

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => IccTransform.Create((IccProfile)null!, (IccProfile)null!));
        Assert.Throws<ArgumentNullException>(() => IccTransform.Create((byte[])null!, new byte[0]));
        Assert.Throws<ArgumentNullException>(() => IccTransform.Create((string)null!, "x"));
    }
}
