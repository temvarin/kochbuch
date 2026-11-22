using Koch.App.Recipes;
using Koch.Kernel.Measures;
using Koch.Recipes.Domain.Cooking;

namespace Koch.App.Tests;

/// <summary>A recipe's photo: chosen on the phone, uploaded straight to Blob Storage, shown from the gateway.</summary>
public sealed class PhotoTests : AppTestContext
{
    private static readonly Guid SoupId = new("6f1d1c2a-7b4e-4c55-9d2b-1e0b5a6c7d80");

    [Fact]
    public void A_chosen_photo_goes_straight_to_blob_storage_and_its_id_into_the_recipe()
    {
        var page = Render<RecipeEdit>();
        page.Find("[data-testid=title]").Change("Kartoffelsuppe");
        var file = InputFileContent.CreateFromBinary([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3], "suppe.jpg", contentType: "image/jpeg");

        page.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(file);
        page.WaitForAssertion(() => Assert.Contains("Anderes Foto", page.Markup));

        page.Find("form").Submit();

        var recipe = Assert.Single(Recipes.All);
        var (path, upload) = Assert.Single(Server.Uploaded);
        Assert.Equal($"/uploads/{recipe.Photo}", path);
        Assert.Equal("BlockBlob", upload.BlobType);
        Assert.Equal(7, upload.Bytes.Length);
    }

    [Fact]
    public void Offline_the_upload_is_refused_with_a_reason_and_the_recipe_still_saves()
    {
        Server.Offline = true;
        var page = Render<RecipeEdit>();
        page.Find("[data-testid=title]").Change("Kartoffelsuppe");

        page.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "suppe.jpg", contentType: "image/jpeg"));
        page.WaitForAssertion(() => Assert.Contains("Ohne Verbindung", page.Find("[data-testid=photo-error]").TextContent));
        page.Find("form").Submit();

        Assert.Null(Assert.Single(Recipes.All).Photo);
    }

    [Fact]
    public void The_cook_view_shows_the_photo_from_the_gateway_and_a_placeholder_until_it_is_ready()
    {
        var photo = new Guid("3c9e8f10-5a1b-4c2d-8e3f-4a5b6c7d8e9f");
        Recipes.Save(new Recipe(SoupId, "Kartoffelsuppe", 4, [new RecipeLine("Kartoffeln", new Quantity(500, Unit.Gramm))], [], photo: photo));
        var page = Render<RecipeView>(p => p.Add(x => x.Id, SoupId));

        var image = page.Find("[data-testid=photo]");
        Assert.Equal($"photos/{photo}/1200.webp", image.GetAttribute("src"));

        image.TriggerEvent("onerror", new Microsoft.AspNetCore.Components.Web.ErrorEventArgs());

        Assert.Contains("wird noch vorbereitet", page.Markup);
    }

    [Fact]
    public void Removing_the_photo_takes_it_off_the_recipe()
    {
        Recipes.Save(new Recipe(SoupId, "Kartoffelsuppe", 4, [], [], photo: Guid.NewGuid()));
        var page = Render<RecipeEdit>(p => p.Add(x => x.Id, SoupId));

        page.FindAll("button").Single(b => b.TextContent.Contains("Foto entfernen")).Click();
        page.Find("form").Submit();

        Assert.Null(Recipes.Find(SoupId)!.Photo);
    }
}
