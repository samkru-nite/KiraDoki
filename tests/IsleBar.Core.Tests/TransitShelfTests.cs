using IsleBar.Core.SystemWatch;
using Xunit;

namespace IsleBar.Core.Tests;

public class TransitShelfTests
{
    [Fact]
    public void AddSkipsDuplicatesAndBlanks()
    {
        var shelf = new TransitShelf();
        Assert.Equal(2, shelf.Add([@"C:\a\x.txt", @"C:\a\y.txt", " ", @"c:\A\X.TXT"]));
        Assert.Equal(0, shelf.Add([@"C:\a\x.txt\"]));
        Assert.Equal([@"C:\a\x.txt", @"C:\a\y.txt"], shelf.Paths);
    }

    [Fact]
    public void RemoveAndClear()
    {
        var shelf = new TransitShelf();
        shelf.Add([@"C:\a\x.txt", @"D:\b"]);
        Assert.Equal(1, shelf.Remove([@"d:\B\", @"C:\nothing"]));
        Assert.Equal([@"C:\a\x.txt"], shelf.Paths);
        shelf.Clear();
        Assert.Equal(0, shelf.Count);
    }

    [Fact]
    public void SavesAndLoadsInOrder()
    {
        var shelf = new TransitShelf();
        shelf.Add([@"C:\a\x.txt", @"E:\", @"D:\folder"]);
        var back = TransitShelf.Load(shelf.Save());
        Assert.Equal(shelf.Paths, back.Paths);
        Assert.Equal(0, TransitShelf.Load(null).Count);
        Assert.Equal(1, TransitShelf.Load("C:\\a.txt\r\n\r\n").Count);
    }

    [Fact]
    public void Words()
    {
        Assert.Equal("1 个文件", TransitShelf.CountText(1, "1 个文件", "{n} 个文件"));
        Assert.Equal("3 个文件", TransitShelf.CountText(3, "1 个文件", "{n} 个文件"));
        Assert.Equal("x.txt", TransitShelf.NameOf(@"C:\a\x.txt"));
        Assert.Equal("folder", TransitShelf.NameOf(@"D:\folder\"));
        Assert.Equal(@"E:\", TransitShelf.NameOf(@"E:\"));
    }
}
