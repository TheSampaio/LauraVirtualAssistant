using Laura.App.Controls;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Memory;

namespace Laura.App.Views;

public sealed partial class SettingsForm
{
    private IConversationMemory? _memory;
    private ComboBox _uiCultureCombo = null!;
    private ComboBox _conversationCultureCombo = null!;
    private ToggleSwitch _rememberToggle = null!;
    private TextBox _memorySearch = null!;
    private TextBox _memoryKey = null!;
    private TextBox _memoryText = null!;
    private ListBox _memoryList = null!;

    /// <summary>Binds the single persistent memory store in the composition root.</summary>
    public void BindMemory(IConversationMemory memory) => _memory = memory;

    /// <summary>Builds explicit memory management and local history privacy controls.</summary>
    private Panel BuildMemorySection()
    {
        (Panel page, TableLayoutPanel stack) = CreateSection(Text_("ui.tab.memory"));
        AddRow(stack, SettingRowFactory.Toggle(Text_("ui.memory.remember"), Text_("ui.memory.privacy"), out _rememberToggle));
        AddRow(stack, SettingRowFactory.TextRow(Text_("ui.memory.search"), string.Empty, out _memorySearch));
        var search = new FlatButton { Text = Text_("ui.memory.search") };
        search.Click += async (_, _) => await RunMemoryOperationAsync(RefreshMemoryAsync).ConfigureAwait(true);
        AddRow(stack, search);
        _memoryList = new ListBox { Dock = DockStyle.Top, Height = 150, BackColor = Palette.Field,
            ForeColor = Palette.TextPrimary, DisplayMember = nameof(MemoryFact.Text) };
        AddRow(stack, _memoryList);
        AddRow(stack, SettingRowFactory.TextRow(Text_("ui.memory.key"), string.Empty, out _memoryKey));
        AddRow(stack, SettingRowFactory.MultilineRow(Text_("ui.memory.text"), Text_("ui.memory.explicit"), 72, out _memoryText));
        _memoryList.SelectedIndexChanged += (_, _) =>
        {
            if (_memoryList.SelectedItem is MemoryFact fact)
            {
                _memoryKey.Text = fact.Key;
                _memoryText.Text = fact.Text;
            }
        };
        var save = new FlatButton { Text = Text_("ui.memory.save") };
        save.Click += async (_, _) => await RunMemoryOperationAsync(async () =>
        {
            string key = _memoryKey.Text;
            string text = _memoryText.Text;
            await Task.Run(() => _memory?.SetFact(key, text)).ConfigureAwait(true);
            await RefreshMemoryAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
        AddRow(stack, save);
        var delete = new FlatButton { Text = Text_("ui.memory.delete") };
        delete.Click += async (_, _) => await RunMemoryOperationAsync(async () =>
        {
            if (_memoryList.SelectedItem is MemoryFact fact)
            {
                await Task.Run(() => _memory?.DeleteFact(fact.Key)).ConfigureAwait(true);
                await RefreshMemoryAsync().ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
        AddRow(stack, delete);
        var clear = new FlatButton { Text = Text_("ui.memory.clear") };
        clear.Click += async (_, _) =>
        {
            if (MessageBox.Show(this, Text_("ui.memory.clearConfirm"), Text_("ui.tab.memory"), MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { return; }
            await RunMemoryOperationAsync(() => Task.Run(() => _memory?.ClearHistory())).ConfigureAwait(true);
        };
        AddRow(stack, clear);
        return page;
    }

    /// <summary>Loads literal search results off the UI thread.</summary>
    private async Task RefreshMemoryAsync()
    {
        string query = _memorySearch.Text;
        IReadOnlyList<MemoryFact> facts = await Task.Run(() => _memory?.Search(query) ?? []).ConfigureAwait(true);
        _memoryList.Items.Clear();
        foreach (MemoryFact fact in facts) { _memoryList.Items.Add(fact); }
    }

    /// <summary>Keeps storage errors visible without terminating the UI.</summary>
    private async Task RunMemoryOperationAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(true); ShowTransientStatus(Text_("ui.memory.done")); }
        catch (Exception exception) { ShowTransientStatus(_localizer.Get("ui.models.failed", DescribeFailure(exception))); }
    }
}
