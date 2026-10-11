using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Memory;

namespace Laura.App.Views;

/// <summary>Native editor for explicit preferences and persistent chat history.</summary>
public sealed class MemoryPage : ContentPage
{
    private readonly IConversationMemory _memory;
    private readonly IUiLocalizer _ui;
    private readonly Entry _query = Theme.Entry();
    private readonly Entry _key = Theme.Entry();
    private readonly Entry _fact = Theme.Entry();
    private readonly VerticalStackLayout _facts = new() { Spacing = 12 };
    /// <summary>Builds search, edit and deletion controls for local memory.</summary>
    public MemoryPage(IConversationMemory memory, IUiLocalizer ui)
    {
        _memory = memory; _ui = ui; Title = ui.Get("ui.tab.memory"); BackgroundColor = Theme.Background;
        NavigationPage.SetHasNavigationBar(this, false);
        _query.Placeholder = ui.Get("ui.memory.search"); _key.Placeholder = ui.Get("ui.memory.key"); _fact.Placeholder = ui.Get("ui.memory.text");
        _query.TextChanged += (_, _) => Refresh();
        var stack = Theme.Stack(Theme.Label(ui.Get("ui.maui.memoryPrivacy"), 13, Theme.Muted), _query, _facts,
            Theme.Card(Theme.Stack(_key, _fact, Theme.Button(ui.Get("ui.memory.save"), SaveAsync, true))),
            Theme.Button(ui.Get("ui.memory.clear"), ClearAsync)); stack.Padding = 26; stack.MaximumWidthRequest = 900;
        Content = Theme.Workspace(Title, "LAURA", new ScrollView { Content = stack }, async () => { await Navigation.PopAsync().ConfigureAwait(true); }); Refresh();
    }
    /// <summary>Refreshes literal search results without interpreting saved preferences as commands.</summary>
    private void Refresh()
    {
        _facts.Clear();
        foreach (MemoryFact fact in _memory.Search(_query.Text ?? ""))
        {
            var actions = new HorizontalStackLayout { Spacing = 10 };
            actions.Add(Theme.Button(_ui.Get("ui.maui.edit"), () => { _key.Text = fact.Key; _fact.Text = fact.Text; return Task.CompletedTask; }));
            actions.Add(Theme.Button(_ui.Get("ui.maui.delete"), () => { _memory.DeleteFact(fact.Key); Refresh(); return Task.CompletedTask; }));
            _facts.Add(Theme.Card(Theme.Stack(Theme.Caption(fact.Key), Theme.Label(fact.Text), actions)));
        }
    }
    /// <summary>Saves one explicit keyed preference.</summary>
    private Task SaveAsync() { _memory.SetFact(_key.Text ?? "", _fact.Text ?? ""); _key.Text = ""; _fact.Text = ""; Refresh(); return Task.CompletedTask; }
    /// <summary>Confirms deletion of persisted history.</summary>
    private async Task ClearAsync()
    { if (await DisplayAlertAsync("Laura", _ui.Get("ui.memory.clearConfirm"), _ui.Get("ui.maui.delete"), _ui.Get("ui.maui.cancel")).ConfigureAwait(true)) { _memory.ClearHistory(); } }
}
