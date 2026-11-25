namespace Koch.Domain.Tests;

public class RecipeMergeTests
{
    private static readonly Recipe Base = Known.Soup();

    private static Recipe With(Recipe recipe, string? title = null, int? servings = null, IEnumerable<RecipeLine>? lines = null,
        IEnumerable<string>? steps = null, string? notes = null, Guid? photo = null, bool noPhoto = false) =>
        new(recipe.Id, title ?? recipe.Title, servings ?? recipe.Servings, lines ?? recipe.Lines, steps ?? recipe.Steps,
            notes ?? recipe.Notes, noPhoto ? null : photo ?? recipe.Photo);

    private static RecipeLine Line(string ingredient, decimal grams, string note = "") => new(ingredient, new Quantity(grams, Unit.Gramm), note);

    private static string[] Names(Recipe recipe) => [.. recipe.Lines.Select(l => l.Ingredient)];

    [Fact]
    public void Edits_to_different_parts_are_both_kept()
    {
        var ben = With(Base, title: "Bens Kartoffelsuppe", notes: "Mit Majoran.");
        var anna = With(Base, steps: [.. Base.Steps, "Mit Petersilie servieren."], noPhoto: true);

        var merged = RecipeMerge.Merge(Base, ben, anna);

        Assert.Equal("Bens Kartoffelsuppe", merged.Title);
        Assert.Equal("Mit Majoran.", merged.Notes);
        Assert.Equal(anna.Steps, merged.Steps);
        Assert.Null(merged.Photo);
    }

    [Fact]
    public void The_same_part_changed_on_both_phones_goes_to_the_one_that_arrives_last()
    {
        var merged = RecipeMerge.Merge(Base, With(Base, title: "Bens Suppe"), With(Base, title: "Annas Suppe"));

        Assert.Equal("Annas Suppe", merged.Title);
    }

    [Fact]
    public void Lines_added_on_both_phones_are_all_kept_each_after_the_line_it_followed()
    {
        var ben = With(Base, lines: [Base.Lines[0], Line("Lauch", 200), .. Base.Lines.Skip(1)]);
        var anna = With(Base, lines: [.. Base.Lines, new RecipeLine("Pfeffer", null)]);

        var merged = RecipeMerge.Merge(Base, ben, anna);

        Assert.Equal(["Kartoffeln", "Lauch", "Knoblauch", "Öl", "Salz", "Pfeffer"], Names(merged));
    }

    [Fact]
    public void A_line_removed_on_one_phone_stays_removed_while_the_other_changes_another_line()
    {
        var ben = With(Base, lines: [Line("Kartoffeln", 750, "festkochend"), .. Base.Lines.Skip(1)]);
        var anna = With(Base, lines: Base.Lines.Where(l => l.Ingredient != "Knoblauch"));

        var merged = RecipeMerge.Merge(Base, ben, anna);

        Assert.Equal(["Kartoffeln", "Öl", "Salz"], Names(merged));
        Assert.Equal(750, merged.Lines[0].Amount!.Amount);
    }

    [Fact]
    public void A_line_removed_on_the_service_stays_removed_when_the_phone_did_not_touch_it()
    {
        var ben = With(Base, lines: Base.Lines.Where(l => l.Ingredient != "Salz"));
        var anna = With(Base, title: "Annas Suppe");

        Assert.Equal(["Kartoffeln", "Knoblauch", "Öl"], Names(RecipeMerge.Merge(Base, ben, anna)));
    }

    [Fact]
    public void A_line_removed_on_one_phone_and_edited_on_the_other_keeps_the_edit()
    {
        var ben = With(Base, lines: Base.Lines.Where(l => l.Ingredient != "Öl"));
        var anna = With(Base, lines: Base.Lines.Select(l => l.Ingredient == "Öl" ? new RecipeLine("Öl", new Quantity(3, Unit.Essloeffel)) : l));

        var merged = RecipeMerge.Merge(Base, ben, anna);

        Assert.Equal(3, merged.Lines.Single(l => l.Ingredient == "Öl").Amount!.Amount);
    }

    [Fact]
    public void The_same_line_changed_on_both_phones_goes_to_the_one_that_arrives_last()
    {
        var ben = With(Base, lines: [Line("Kartoffeln", 600), .. Base.Lines.Skip(1)]);
        var anna = With(Base, lines: [Line("Kartoffeln", 700), .. Base.Lines.Skip(1)]);

        Assert.Equal(700, RecipeMerge.Merge(Base, ben, anna).Lines[0].Amount!.Amount);
    }

    [Fact]
    public void Two_lines_of_the_same_ingredient_are_told_apart_by_their_order()
    {
        var baseRecipe = With(Base, lines: [Line("Butter", 100, "für den Teig"), Line("Butter", 20, "für die Form")]);
        var ben = With(baseRecipe, lines: [Line("Butter", 100, "für den Teig"), Line("Butter", 30, "für die Form")]);
        var anna = With(baseRecipe, lines: [Line("Butter", 120, "für den Teig"), Line("Butter", 20, "für die Form")]);

        var merged = RecipeMerge.Merge(baseRecipe, ben, anna);

        Assert.Equal([120m, 30m], merged.Lines.Select(l => l.Amount!.Amount));
    }

    [Fact]
    public void Once_servings_changed_amounts_are_never_mixed_and_the_later_phone_gives_both()
    {
        var ben = With(Base, servings: 2, lines: Base.LinesFor(2));
        var anna = With(Base, lines: [Line("Kartoffeln", 800, "festkochend"), .. Base.Lines.Skip(1)]);

        var merged = RecipeMerge.Merge(Base, ben, anna);

        Assert.Equal(4, merged.Servings);
        Assert.Equal(anna.Lines, merged.Lines);
    }

    [Fact]
    public void Servings_changed_only_on_the_service_stay_with_their_lines_when_the_phone_changed_something_else()
    {
        var ben = With(Base, servings: 2, lines: Base.LinesFor(2));
        var anna = With(Base, title: "Annas Suppe");

        var merged = RecipeMerge.Merge(Base, ben, anna);

        Assert.Equal((2, ben.Lines), (merged.Servings, merged.Lines));
        Assert.Equal("Annas Suppe", merged.Title);
    }

    [Fact]
    public void A_phone_that_changed_nothing_keeps_the_service_version_and_a_repeat_changes_nothing()
    {
        var ben = With(Base, title: "Bens Suppe");
        var anna = With(Base, notes: "Annas Notiz");

        Assert.Equal(ben, RecipeMerge.Merge(Base, ben, Base));
        var once = RecipeMerge.Merge(Base, ben, anna);
        Assert.Equal(once, RecipeMerge.Merge(Base, once, anna));
    }

    [Fact]
    public void When_the_merge_would_break_a_rule_the_later_phone_wins_whole()
    {
        var baseRecipe = With(Base, lines: []);
        var half = Recipe.MaxLines / 2 + 1;
        var ben = With(baseRecipe, lines: Enumerable.Range(0, half).Select(i => Line($"Ben {i}", 1)));
        var anna = With(baseRecipe, lines: Enumerable.Range(0, half).Select(i => Line($"Anna {i}", 1)));

        Assert.Equal(anna, RecipeMerge.Merge(baseRecipe, ben, anna));
    }
}

public class BasesTests
{
    [Fact]
    public void An_edited_recipe_goes_with_the_version_the_service_confirmed_a_new_one_with_none()
    {
        var phone = new SyncedBook();
        phone.Change(book => book.Save(Known.Soup()));
        phone.CompleteSync(phone.StartSync(), Known.Book(), Known.Now);

        var fresh = Known.Recipe(Guid.NewGuid(), "Neu");
        phone.Change(book => book.Save(new Recipe(Known.SoupId, "Annas Suppe", 4, [], [])).Save(fresh));
        var pending = phone.StartSync();

        Assert.Equal([Known.Soup()], pending.Changes.BasesIn(phone.State?.Base));
    }

    [Fact]
    public void An_edit_whose_sync_failed_offline_still_goes_with_its_base()
    {
        var phone = new SyncedBook();
        phone.Change(book => book.Save(Known.Soup()));
        phone.CompleteSync(phone.StartSync(), Known.Book(), Known.Now);
        phone.Change(book => book.Save(new Recipe(Known.SoupId, "Annas Suppe", 4, [], [])));
        phone.SyncFailed(phone.StartSync());

        Assert.Equal([Known.Soup()], phone.StartSync().Changes.BasesIn(phone.State?.Base));
    }

    [Fact]
    public void Before_the_first_sync_there_are_no_bases()
    {
        var phone = new SyncedBook();
        phone.Change(book => book.Save(Known.Soup()));

        Assert.Empty(phone.StartSync().Changes.BasesIn(phone.State?.Base));
    }
}
