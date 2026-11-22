namespace Koch.Recipes.Domain.Tests;

/// <summary>
/// Phones and the service, without a network: the service applies changes in the order they arrive, as the Recipes
/// service does (a recipe sent with its base is merged part by part, docs/adr/0012), and answers with its whole book.
/// </summary>
public class SyncTests
{
    private sealed class Service
    {
        public RecipeBook Book { get; private set; } = RecipeBook.Empty;

        /// <summary>The request arrives and is saved; the answer reaches the phone unless <paramref name="loseAnswer"/>.</summary>
        public void Sync(SyncedBook phone, bool loseAnswer = false)
        {
            var pending = phone.StartSync();
            Book = Book.Apply(pending.Changes, phone.BasesFor(pending.Changes));
            if (loseAnswer) phone.SyncFailed(pending);
            else phone.CompleteSync(pending, Book, Known.Now);
        }
    }

    private static Recipe Soup(string title = "Kartoffelsuppe") => new(Known.SoupId, title, 4, [], []);

    /// <summary>Order doesn't matter in a book: the app sorts by title.</summary>
    private static void SameContent(RecipeBook expected, RecipeBook actual)
    {
        Assert.Equal(expected.Recipes.OrderBy(r => r.Id), actual.Recipes.OrderBy(r => r.Id));
        Assert.Equal(expected.Ingredients.OrderBy(i => i.Id), actual.Ingredients.OrderBy(i => i.Id));
    }

    [Fact]
    public void Before_the_first_sync_everything_is_unsynced_and_after_it_nothing()
    {
        var phone = new SyncedBook();
        phone.Change(book => book.Save(Known.Soup()));
        Assert.True(phone.HasUnsyncedChanges);

        var service = new Service();
        service.Sync(phone);

        Assert.False(phone.HasUnsyncedChanges);
        SameContent(phone.Book, service.Book);
        Assert.Equal(Known.Now, phone.State?.LastSync);
    }

    [Fact]
    public void The_other_phones_recipe_arrives_with_the_next_sync()
    {
        var (service, anna, ben) = (new Service(), new SyncedBook(), new SyncedBook());
        anna.Change(book => book.Save(Known.Soup()));
        service.Sync(anna);

        service.Sync(ben);

        Assert.Equal(Known.Soup(), Assert.Single(ben.Book.Recipes));
    }

    [Fact]
    public void A_change_made_while_the_sync_was_under_way_is_kept_and_sent_next_time()
    {
        var (service, phone) = (new Service(), new SyncedBook());
        phone.Change(book => book.Save(Soup()));
        var pending = phone.StartSync();
        phone.Change(book => book.Save(Soup("Kartoffelsuppe mit Speck"))); // while the request is under way

        phone.CompleteSync(pending, service.Book.Apply(pending.Changes), Known.Now);

        Assert.Equal("Kartoffelsuppe mit Speck", Assert.Single(phone.Book.Recipes).Title);
        Assert.True(phone.HasUnsyncedChanges);
    }

    [Fact]
    public void A_recipe_saved_by_a_sync_whose_answer_got_lost_and_then_removed_here_is_removed_on_the_service_too()
    {
        var (service, phone) = (new Service(), new SyncedBook());
        phone.Change(book => book.Save(Soup()));
        service.Sync(phone, loseAnswer: true); // saved there, but this phone doesn't know

        phone.Change(book => book.Remove(Known.SoupId));
        service.Sync(phone);

        Assert.Empty(service.Book.Recipes);
        Assert.Empty(phone.Book.Recipes);
        Assert.False(phone.HasUnsyncedChanges);
    }

    [Fact]
    public void Of_two_edits_to_one_recipe_the_one_that_arrives_last_wins_on_both_phones()
    {
        var (service, anna, ben) = (new Service(), new SyncedBook(), new SyncedBook());
        anna.Change(book => book.Save(Soup()));
        service.Sync(anna);
        service.Sync(ben);

        anna.Change(book => book.Save(Soup("Annas Suppe")));
        ben.Change(book => book.Save(Soup("Bens Suppe")));
        service.Sync(anna);
        service.Sync(ben);
        service.Sync(anna);

        Assert.Equal("Bens Suppe", Assert.Single(anna.Book.Recipes).Title);
        Assert.Equal("Bens Suppe", Assert.Single(ben.Book.Recipes).Title);
    }

    [Fact]
    public void Edits_to_different_parts_of_one_recipe_on_two_phones_both_survive_on_both_phones()
    {
        var (service, anna, ben) = (new Service(), new SyncedBook(), new SyncedBook());
        anna.Change(book => book.Save(Known.Soup()));
        service.Sync(anna);
        service.Sync(ben);

        anna.Change(book => book.Save(new Recipe(Known.SoupId, "Annas Kartoffelsuppe", 4, Known.Soup().Lines, Known.Soup().Steps,
            Known.Soup().Notes, Known.Soup().Photo)));
        ben.Change(book => book.Save(new Recipe(Known.SoupId, "Kartoffelsuppe", 4, [.. Known.Soup().Lines, new RecipeLine("Pfeffer", null)],
            Known.Soup().Steps, Known.Soup().Notes, Known.Soup().Photo)));
        service.Sync(anna);
        service.Sync(ben);
        service.Sync(anna);

        foreach (var phone in new[] { anna, ben })
        {
            var soup = Assert.Single(phone.Book.Recipes);
            Assert.Equal("Annas Kartoffelsuppe", soup.Title);
            Assert.Equal("Pfeffer", soup.Lines[^1].Ingredient);
        }
    }

    [Fact]
    public void An_edit_made_offline_is_merged_with_what_the_other_phone_synced_meanwhile()
    {
        var (service, anna, ben) = (new Service(), new SyncedBook(), new SyncedBook());
        anna.Change(book => book.Save(Known.Soup()));
        service.Sync(anna);
        service.Sync(ben);

        // Ben is offline: his sync never arrives. Anna renames the soup and syncs.
        ben.Change(book => book.Save(new Recipe(Known.SoupId, Known.Soup().Title, 4, [.. Known.Soup().Lines, new RecipeLine("Pfeffer", null)],
            Known.Soup().Steps, Known.Soup().Notes, Known.Soup().Photo)));
        ben.SyncFailed(ben.StartSync());
        anna.Change(book => book.Save(new Recipe(Known.SoupId, "Annas Kartoffelsuppe", 4, Known.Soup().Lines, Known.Soup().Steps,
            Known.Soup().Notes, Known.Soup().Photo)));
        service.Sync(anna);
        service.Sync(ben); // back online
        service.Sync(anna);

        foreach (var phone in new[] { anna, ben })
        {
            var soup = Assert.Single(phone.Book.Recipes);
            Assert.Equal("Annas Kartoffelsuppe", soup.Title);
            Assert.Equal("Pfeffer", soup.Lines[^1].Ingredient);
        }
    }

    [Fact]
    public void Known_limit_a_part_changed_back_after_a_lost_answer_keeps_the_lost_syncs_value()
    {
        // docs/adr/0012: the service saved "Bens Suppe" but the answer got lost; Ben changes the title back to what
        // his base has. The merge sees no change from Ben and keeps the service's title. Ben sees it after the sync.
        var (service, ben) = (new Service(), new SyncedBook());
        ben.Change(book => book.Save(Soup()));
        service.Sync(ben);
        ben.Change(book => book.Save(Soup("Bens Suppe")));
        service.Sync(ben, loseAnswer: true);
        ben.Change(book => book.Save(Soup()));

        service.Sync(ben);

        Assert.Equal("Bens Suppe", Assert.Single(ben.Book.Recipes).Title);
    }

    [Fact]
    public void An_ingredient_both_phones_add_offline_ends_up_once()
    {
        var (service, anna, ben) = (new Service(), new SyncedBook(), new SyncedBook());
        anna.Change(book => book.Save(Known.Recipe(Guid.NewGuid(), "Suppe", 2, new RecipeLine("Kartoffeln", null))));
        ben.Change(book => book.Save(Known.Recipe(Guid.NewGuid(), "Auflauf", 2, new RecipeLine(" kartoffeln", null))));

        service.Sync(anna);
        service.Sync(ben);
        service.Sync(anna);

        Assert.Single(service.Book.Ingredients);
        SameContent(service.Book, anna.Book);
        SameContent(service.Book, ben.Book);
    }

    /// <summary>
    /// Three phones of two people edit, remove, sync and lose answers in random order. However it goes, once every
    /// phone has synced again, all of them show exactly what the service has (DDIA ch06: convergence).
    /// </summary>
    [Fact]
    public void Whatever_happens_in_between_every_phone_ends_up_with_the_services_book()
    {
        Guid[] ids = [.. Enumerable.Range(1, 5).Select(i => new Guid($"00000000-0000-0000-0000-00000000000{i}"))];
        string[] ingredients = ["Kartoffeln", "Zwiebeln", "Salz", "Mehl", "Butter"];
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var service = new Service();
            SyncedBook[] phones = [new(), new(), new()];
            for (var step = 0; step < 40; step++)
            {
                var phone = phones[random.Next(phones.Length)];
                switch (random.Next(6))
                {
                    case 5 when phone.Book.Recipes.Count > 0: // edit one part of a recipe, so merges happen too
                        var old = phone.Book.Recipes[random.Next(phone.Book.Recipes.Count)];
                        var edited = random.Next(3) switch
                        {
                            0 => new Recipe(old.Id, $"Rezept {random.Next(100)}", old.Servings, old.Lines, old.Steps, old.Notes),
                            1 => new Recipe(old.Id, old.Title, old.Servings, [.. old.Lines.Skip(1), new RecipeLine(
                                ingredients[random.Next(ingredients.Length)], new Quantity(random.Next(1, 500), Unit.Gramm))], old.Steps, old.Notes),
                            _ => new Recipe(old.Id, old.Title, old.Servings, old.Lines, old.Steps, $"Notiz {random.Next(100)}"),
                        };
                        phone.Change(book => book.Save(edited));
                        break;
                    case 0 or 1:
                        var lines = Enumerable.Range(0, random.Next(3)).Select(_ => new RecipeLine(
                            ingredients[random.Next(ingredients.Length)], new Quantity(random.Next(1, 500), Unit.Gramm)));
                        var recipe = Known.Recipe(ids[random.Next(ids.Length)], $"Rezept {random.Next(100)}", random.Next(1, 6), [.. lines]);
                        phone.Change(book => book.Save(recipe));
                        break;
                    case 2 when phone.Book.Recipes.Count > 0:
                        var id = phone.Book.Recipes[random.Next(phone.Book.Recipes.Count)].Id;
                        phone.Change(book => book.Remove(id));
                        break;
                    case 3:
                        service.Sync(phone, loseAnswer: random.Next(3) == 0);
                        break;
                    case 4: // the request never arrives
                        phone.SyncFailed(phone.StartSync());
                        break;
                }
            }

            foreach (var phone in phones) service.Sync(phone);
            foreach (var phone in phones) service.Sync(phone);

            foreach (var phone in phones)
            {
                SameContent(service.Book, phone.Book);
                Assert.False(phone.HasUnsyncedChanges, $"seed {seed}");
            }
        }
    }
}
