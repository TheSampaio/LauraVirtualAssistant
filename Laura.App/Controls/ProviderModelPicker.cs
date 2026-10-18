using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Models;

namespace Laura.App.Controls;

/// <summary>Searchable native provider selector that retains the configured ID through failures and catalogue changes.</summary>
public sealed class ProviderModelPicker : ContentView
{
    private readonly IProviderModelCatalog _catalogue;
    private readonly IUiLocalizer _ui;
    private readonly string _provider;
    private readonly Func<string> _credential;
    private readonly Entry _search = Theme.Entry();
    private readonly Picker _picker = Theme.Picker([], "");
    private readonly Label _state = Theme.Label("", 12, Theme.Muted), _selected = Theme.Label("", 12, Theme.Text);
    private readonly Button _refresh;
    private IReadOnlyList<ProviderModel> _models = [];
    private ProviderModel[] _visible = [];
    private CancellationTokenSource? _request;
    private string _text = "";
    private bool _updating;
    private bool _stale;
    private int _revision;

    /// <summary>Creates native search/picker/refresh controls. Args: catalogue: Discovery service; ui: Localized text; provider: Fixed provider; credential: Current in-memory candidate or saved key.</summary>
    public ProviderModelPicker(IProviderModelCatalog catalogue, IUiLocalizer ui, string provider, Func<string> credential)
    {
        _catalogue = catalogue; _ui = ui; _provider = provider; _credential = credential;
        _search.Placeholder = T("search"); _search.TextChanged += (_, _) => Filter();
        _refresh = Theme.ActionButton(T("refresh"), "refresh_cw", () => RefreshAsync(true));
        var selection = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        selection.Add(_picker, 0); selection.Add(_refresh, 1);
        _picker.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating && _picker.SelectedIndex >= 0 && _picker.SelectedIndex < _visible.Length)
            { _text = _visible[_picker.SelectedIndex].Id; UpdateSelected(); }
        };
        Content = Theme.Stack(_search, selection, _selected, _state);
        Loaded += async (_, _) => { if (IsEffectivelyVisible()) { await RefreshAsync().ConfigureAwait(true); } };
        Unloaded += (_, _) => { _revision++; _request?.Cancel(); };
    }

    /// <summary>Gets or restores the actual configured ID without silently choosing an alternative model.</summary>
    public string Text { get => _text; set { _text = value ?? ""; Filter(); } }

    /// <summary>Checks real native catalogue selection/search without provider traffic in the isolated harness.</summary>
    internal async Task VerifyForSmokeAsync()
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Isolated UI diagnostics only."); }
        await RefreshAsync(true).ConfigureAwait(true);
        if (_models.Count == 0 || _picker.Items.Count == 0) { throw new InvalidOperationException("Compatible catalogue entries were not populated."); }
        _picker.SelectedIndex = 0; string selected = Text;
        _search.Text = "synthetic-filter-with-no-matches";
        if (_picker.Items.Count != 0 || Text != selected) { throw new InvalidOperationException("Searching discarded the configured model."); }
        _search.Text = selected;
        if (_picker.Items.Count != 1 || _picker.SelectedIndex != 0 || Text != selected) { throw new InvalidOperationException("Native model search did not restore the selected entry."); }
        _search.Text = "";
    }

    /// <summary>Refreshes after saved-key/candidate changes while cancelling an older account request.</summary>
    public async Task RefreshAsync(bool force = false)
    {
        int revision = ++_revision; _request?.Cancel();
        using var request = new CancellationTokenSource(); _request = request; _refresh.IsEnabled = false; _state.Text = T("loading");
        try
        {
            string credential = _credential();
            var result = await Task.Run(() => _catalogue.ListAsync(_provider, credential, force, request.Token), request.Token).ConfigureAwait(true);
            if (revision != _revision) { return; }
            ModelCapabilities required = _provider == "gemini" ? ModelCapabilities.LiveAudio : ModelCapabilities.Text | ModelCapabilities.Tools;
            _stale = result.Stale; _models = result.Models.Where(model => (model.Capabilities & required) == required).ToArray(); Filter();
            _state.Text = (result.Stale ? T("stale") + " " : T("cloud") + " · ")
                + (result.Error is null ? string.Format(System.Globalization.CultureInfo.CurrentCulture, T("count"), _models.Count) : T(result.Error));
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_request, request)) { _request = null; _refresh.IsEnabled = true; } }
    }

    /// <summary>Searches known compatible metadata and leaves the selection unchanged when it is filtered or absent.</summary>
    private void Filter()
    {
        _updating = true;
        try
        {
            string query = _search.Text?.Trim() ?? "";
            _visible = _models.Where(model => model.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || model.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            _picker.ItemsSource = _visible.Select(model => model.Name == model.Id ? model.Id : model.Name + " · " + model.Id).ToArray();
            _picker.SelectedIndex = Array.FindIndex(_visible, model => model.Id == _text); UpdateSelected();
        }
        finally { _updating = false; }
    }

    /// <summary>Labels the retained current ID and known capabilities without inventing availability.</summary>
    private void UpdateSelected()
    {
        var model = _models.FirstOrDefault(item => item.Id == _text);
        _selected.Text = _text + " · " + (model is null ? T("unverified") : model.Deprecated ? T("deprecated") : _stale ? T("stale") : T("listed"))
            + (model is null ? "" : " · " + string.Join(", ", Enum.GetValues<ModelCapabilities>().Where(capability => capability != ModelCapabilities.None && model.Capabilities.HasFlag(capability)).Select(capability => T("capability." + capability))));
    }

    /// <summary>Resolves centralized interface resources for this control.</summary>
    private string T(string key) => _ui.Get("ui.models." + key);
    /// <summary>Avoids provider requests when a parent configuration section is collapsed.</summary>
    private bool IsEffectivelyVisible()
    {
        for (Element? element = this; element is not null; element = element.Parent)
        { if (element is VisualElement { IsVisible: false }) { return false; } }
        return true;
    }
}
