using System.Collections.Generic;
using Wadd.Core.Models;
using Xunit;

namespace Wadd.Tests;

public class CategoryTests
{
    [Fact]
    public void CategoriesList_SingleCategory_ReturnsSingleItem()
    {
        var item = new TodoItem { Category = "Work" };

        Assert.Equal(new List<string> { "Work" }, item.CategoriesList);
    }

    [Fact]
    public void CategoriesList_MultipleCommaSeparatedCategories_SplitsAndTrimsDistinct()
    {
        var item = new TodoItem { Category = "Work, Personal, Shopping, work" };

        Assert.Equal(new List<string> { "Work", "Personal", "Shopping" }, item.CategoriesList);
    }

    [Fact]
    public void CategoriesList_NullOrEmpty_ReturnsEmptyList()
    {
        var item1 = new TodoItem { Category = null };
        var item2 = new TodoItem { Category = "   " };

        Assert.Empty(item1.CategoriesList);
        Assert.Empty(item2.CategoriesList);
    }

    [Fact]
    public async System.Threading.Tasks.Task RenameCategoryAsync_UpdatesAllMatchingItems()
    {
        var service = new Wadd.Services.InMemoryTodoService();
        var item1 = new TodoItem { Id = System.Guid.NewGuid(), Title = "Task 1", Category = "Work, Personal" };
        var item2 = new TodoItem { Id = System.Guid.NewGuid(), Title = "Task 2", Category = "Work" };
        await service.CreateAsync(item1);
        await service.CreateAsync(item2);

        var renamed = await service.RenameCategoryAsync("Work", "Office");

        Assert.True(renamed);
        var updated1 = await service.GetByIdAsync(item1.Id);
        var updated2 = await service.GetByIdAsync(item2.Id);

        Assert.Equal(new List<string> { "Office", "Personal" }, updated1!.CategoriesList);
        Assert.Equal(new List<string> { "Office" }, updated2!.CategoriesList);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteCategoryAsync_RemovesTagFromAllItems()
    {
        var service = new Wadd.Services.InMemoryTodoService();
        var item1 = new TodoItem { Id = System.Guid.NewGuid(), Title = "Task 1", Category = "Work, Personal" };
        var item2 = new TodoItem { Id = System.Guid.NewGuid(), Title = "Task 2", Category = "Work" };
        await service.CreateAsync(item1);
        await service.CreateAsync(item2);

        var deleted = await service.DeleteCategoryAsync("Work");

        Assert.True(deleted);
        var updated1 = await service.GetByIdAsync(item1.Id);
        var updated2 = await service.GetByIdAsync(item2.Id);

        Assert.Equal(new List<string> { "Personal" }, updated1!.CategoriesList);
        Assert.Null(updated2!.Category);
        Assert.Empty(updated2.CategoriesList);
    }
}
