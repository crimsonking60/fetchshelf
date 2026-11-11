using Fetchshelf.Web.Pages;

namespace Fetchshelf.Web.Tests;

public class PagingTests
{
    private static readonly int[] Seven = [1, 2, 3, 4, 5, 6, 7];

    [Fact]
    public void Slice_MiddlePage_ReturnsThatPagesItems()
    {
        var (items, pageNumber, totalPages) = Paging.Slice(Seven, 7, pageNumber: 2, pageSize: 3);

        Assert.Equal([4, 5, 6], items);
        Assert.Equal(2, pageNumber);
        Assert.Equal(3, totalPages);
    }

    [Fact]
    public void Slice_PageBeyondTheEnd_ShowsTheLastPage()
    {
        var (items, pageNumber, _) = Paging.Slice(Seven, 7, pageNumber: 99, pageSize: 3);

        Assert.Equal([7], items);
        Assert.Equal(3, pageNumber);
    }

    [Fact]
    public void Slice_PageBelowOne_ShowsTheFirstPage() =>
        Assert.Equal(1, Paging.Slice(Seven, 7, pageNumber: 0, pageSize: 3).PageNumber);

    [Fact]
    public void Slice_PageSizeAll_PutsEverythingOnOnePage()
    {
        var (items, pageNumber, totalPages) = Paging.Slice(Seven, 7, pageNumber: 3, pageSize: Paging.All);

        Assert.Equal(Seven, items);
        Assert.Equal(1, pageNumber);
        Assert.Equal(1, totalPages);
    }

    [Fact]
    public void Slice_NegativePageSize_UsesTheDefaultPageSize()
    {
        var thirty = Enumerable.Range(1, 30).ToList();

        Assert.Equal(2, Paging.Slice(thirty, 30, pageNumber: 1, pageSize: -1).TotalPages);
    }

    [Fact]
    public void Slice_NoItems_HasOneEmptyPage()
    {
        var (items, pageNumber, totalPages) = Paging.Slice(Array.Empty<int>(), 0, pageNumber: 1, pageSize: 3);

        Assert.Empty(items);
        Assert.Equal(1, pageNumber);
        Assert.Equal(1, totalPages);
    }

    [Theory]
    [InlineData(7, 2, 3, 3, 3, 2, 3)]
    [InlineData(7, 9, 3, 6, 3, 3, 3)]
    [InlineData(7, 0, 3, 0, 3, 1, 3)]
    [InlineData(7, 2, Paging.All, 0, 7, 1, 1)]
    [InlineData(0, 1, 3, 0, 3, 1, 1)]
    public void Window_PicksTheSamePageAsSlice(int total, int page, int size, int skip, int take, int pageNumber, int totalPages) =>
        Assert.Equal(new PageWindow(skip, take, pageNumber, totalPages), Paging.Window(total, page, size));
}
