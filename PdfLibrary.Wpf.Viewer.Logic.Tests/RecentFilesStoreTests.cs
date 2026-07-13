using PdfLibrary.Wpf.Viewer.Logic;
using Xunit;

public class RecentFilesStoreTests
{
    [Fact]
    public void Add_MovesExistingToFront_NoDuplicate()
    {
        var s = new RecentFilesStore(max: 5);
        s.Add("a.pdf"); s.Add("b.pdf"); s.Add("a.pdf");
        Assert.Equal(new[] { "a.pdf", "b.pdf" }, s.Items);
    }

    [Fact]
    public void Add_TrimsToMax()
    {
        var s = new RecentFilesStore(max: 2);
        s.Add("a.pdf"); s.Add("b.pdf"); s.Add("c.pdf");
        Assert.Equal(new[] { "c.pdf", "b.pdf" }, s.Items);
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var s = new RecentFilesStore(max: 5);
        s.Add("a.pdf"); s.Add("b.pdf");
        var back = RecentFilesStore.Deserialize(s.Serialize(), max: 5);
        Assert.Equal(s.Items, back.Items);
    }

    [Fact]
    public void Deserialize_BlankOrNull_IsEmpty()
    {
        Assert.Empty(RecentFilesStore.Deserialize("").Items);
        Assert.Empty(RecentFilesStore.Deserialize("   \n  ").Items);
    }
}
