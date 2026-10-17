using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.ChatGpt;
using Laura.Core.Configuration;

namespace Laura.App.Views;

/// <summary>Native optional ChatGPT connection controls; no credentials enter view models, chat or diagnostic exports.</summary>
public sealed class ChatGptSettingsView : ContentView
{
    private readonly IChatGptConnection _connection;
    private readonly IUiLocalizer _ui;
    private readonly Picker _accounts = Theme.Picker([], ""), _models = Theme.Picker([], "");
    private readonly Label _state = Theme.Label("", 13, Theme.Muted), _result = Theme.Label("", 13, Theme.Listening);
    private readonly List<Button> _actions = [];
    private readonly Button _cancel;
    private readonly CheckBox _includedPlanOnly = new() { Color = Theme.Accent };
    private CancellationTokenSource? _operation;
    private IReadOnlyList<ChatGptAccount> _registrations = [];
    private IReadOnlyList<ChatGptModel> _catalog = [];
    private bool _loading;
    private readonly Switch _enabled = new() { OnColor = Theme.Accent }, _share = new() { OnColor = Theme.Accent }, _automatic = new() { OnColor = Theme.Accent };
    private readonly Picker _selection;
    private string _savedModel = "";
    private Guid? _activeRegistration;
    /// <summary>Creates translated connection controls without opening a microphone or starting cloud requests.</summary>
    public ChatGptSettingsView(IChatGptConnection connection, IUiLocalizer ui, LauraSettings settings)
    {
        _connection = connection; _ui = ui;
        _selection = Theme.Picker([T("gemini"), "ChatGPT", T("automatic")], ""); Reload(settings);
        _cancel = Theme.ActionButton(T("cancel"), "x", () => { _operation?.Cancel(); return Task.CompletedTask; }); _cancel.IsEnabled = false;
        Content = Theme.Stack(Theme.Label("ChatGPT", 22), Theme.Label(T("intro"), 13, Theme.Muted), _state, _accounts,
            Action("connect", "link", token => SignInAsync(null, token)),
            Action("reconnect", "link", token => SignInAsync(SelectedAccount(), token)),
            Action("status", "refresh_cw", RefreshAsync),
            Action("models", "refresh_cw", LoadModelsAsync), _models,
            Theme.Label(T("catalogHelp"), 12, Theme.Muted),
            Theme.Stack(Theme.Label(T("includedPlanOnly"), 13), _includedPlanOnly),
            Action("probe", "play", ProbeAsync),
            Action("disconnect", "trash", async token => { Render(await _connection.DisconnectAsync(token).ConfigureAwait(true)); _models.Items.Clear(); _catalog = []; }),
            Theme.ActionButton(T("usage"), "external_link", () => Launcher.Default.OpenAsync(new Uri("https://chatgpt.com/#settings/Usage"))),
            Theme.Label(T("limits"), 12, Theme.Muted),
            Theme.Label(T("selection"), 13), _selection, Theme.Label(T("enabled"), 13), _enabled,
            Theme.Label(T("share"), 13), _share, Theme.Label(T("allowAutomatic"), 13), _automatic, _cancel, _result);
        _accounts.SelectedIndexChanged += async (_, _) =>
        {
            if (_loading || _operation is not null || _accounts.SelectedIndex < 0) { return; }
            Guid selected = SelectedAccount();
            await ExecuteAsync(async token => { Render(await _connection.SelectAsync(selected, token).ConfigureAwait(true)); _models.Items.Clear(); _catalog = []; }).ConfigureAwait(true);
        };
        Loaded += async (_, _) => await ExecuteAsync(RefreshAsync).ConfigureAwait(true);
        Unloaded += (_, _) => _operation?.Cancel();
    }
    /// <summary>Gets explicitly edited nonsecret provider options for the settings Save action.</summary>
    public ChatGptOptions Options => new ChatGptOptions
    {
        Enabled = _enabled.IsToggled, ShareContext = _share.IsToggled, AllowAutomatic = _automatic.IsToggled, IncludedPlanOnly = _includedPlanOnly.IsChecked,
        Model = _models.SelectedIndex >= 0 && _models.SelectedIndex < _catalog.Count ? _catalog[_models.SelectedIndex].Slug : _savedModel,
        Registration = _activeRegistration
    }.Sanitized();
    /// <summary>Gets manual choice, which always precedes structured automatic selection.</summary>
    public ConversationProviderSelection Selection => (ConversationProviderSelection)Math.Max(0, _selection.SelectedIndex);
    /// <summary>Reloads saved owner preferences without creating authorization or querying a provider.</summary>
    public void Reload(LauraSettings settings)
    {
        _savedModel = settings.ChatGpt.Model; _enabled.IsToggled = settings.ChatGpt.Enabled; _share.IsToggled = settings.ChatGpt.ShareContext;
        _automatic.IsToggled = settings.ChatGpt.AllowAutomatic; _includedPlanOnly.IsChecked = settings.ChatGpt.IncludedPlanOnly; _selection.SelectedIndex = (int)settings.ProviderSelection;
    }
    /// <summary>Resolves only centralized presentation resources.</summary>
    private string T(string key) => _ui.Get("ui.chatgpt." + key);
    /// <summary>Wraps a native asynchronous action in cancellation and safe error presentation.</summary>
    private Button Action(string key, string icon, Func<CancellationToken, Task> action)
    { var button = Theme.ActionButton(T(key), icon, () => ExecuteAsync(action)); _actions.Add(button); return button; }
    /// <summary>Returns the selected protected registration identity without relying on email equality.</summary>
    private Guid SelectedAccount() => _accounts.SelectedIndex >= 0 && _accounts.SelectedIndex < _registrations.Count
        ? _registrations[_accounts.SelectedIndex].Id : throw new ChatGptConnectionException("account_unavailable");
    /// <summary>Refreshes local metadata; this does not claim the provider is reachable.</summary>
    private async Task RefreshAsync(CancellationToken token) => Render(await _connection.ReadAsync(token).ConfigureAwait(true));
    /// <summary>Completes account authorization before making any new account active.</summary>
    private async Task SignInAsync(Guid? registration, CancellationToken token)
    { Render(await _connection.SignInAsync(registration, token).ConfigureAwait(true)); _models.Items.Clear(); _catalog = []; }
    /// <summary>Reads the current account's official model catalog, preserving server order.</summary>
    private async Task LoadModelsAsync(CancellationToken token)
    {
        _catalog = await _connection.ModelsAsync(token).ConfigureAwait(true); _models.Items.Clear();
        foreach (var model in _catalog) { _models.Items.Add(model.DisplayName); }
        _models.SelectedIndex = _catalog.Count > 0 ? 0 : -1; _result.Text = T("catalogHelp");
    }
    /// <summary>Uses only a synthetic probe and reports success after a genuine terminal completion.</summary>
    private async Task ProbeAsync(CancellationToken token)
    {
        if (_models.SelectedIndex < 0 || _models.SelectedIndex >= _catalog.Count) { throw new ChatGptConnectionException("model_required"); }
        var result = await _connection.ProbeAsync(_catalog[_models.SelectedIndex].Slug, _includedPlanOnly.IsChecked, token).ConfigureAwait(true);
        _result.Text = string.Format(_ui.Culture, T("verified"), result.Model,
            result.InputTokens?.ToString(_ui.Culture) ?? T("unavailable"), result.OutputTokens?.ToString(_ui.Culture) ?? T("unavailable"));
        Render(await _connection.ReadAsync(token).ConfigureAwait(true));
    }
    /// <summary>Renders safe registration labels; provider messages and tokens never become UI error text.</summary>
    private void Render(ChatGptConnectionState state)
    {
        _loading = true;
        try
        {
            Guid? previous = _accounts.SelectedIndex >= 0 && _accounts.SelectedIndex < _registrations.Count ? _registrations[_accounts.SelectedIndex].Id : null;
            if (previous != state.Active && _activeRegistration is not null) { _includedPlanOnly.IsChecked = false; _share.IsToggled = false; _automatic.IsToggled = false; _enabled.IsToggled = false; }
            _activeRegistration = state.Active;
            _registrations = state.Accounts; _accounts.Items.Clear();
            foreach (var account in _registrations) { _accounts.Items.Add((account.Email.Length > 0 ? account.Email : T("account")) + " · " + account.Id.ToString("N")[..6]); }
            _accounts.SelectedIndex = state.Active is { } active ? _registrations.ToList().FindIndex(account => account.Id == active) : -1;
            _state.Text = T(state.Status) + (state.VerifiedModel.Length > 0 ? " · " + state.VerifiedModel : "");
            if (state.Error.Length > 0) { _result.Text = Error(state.Error, state.HttpStatus); }
        }
        finally { _loading = false; }
    }
    /// <summary>Maps known failures to useful localized text while preserving the actual safe HTTP category.</summary>
    private string Error(string code, int? status)
    {
        string key = code switch
        {
            "oauth_access_denied" => "denied", "plan_permission_missing" => "identity_only", "account_identity_mismatch" => "identityMismatch",
            "subscription_sharing_usage_limit_exceeded" => "usageLimit", "subscription_sharing_user_not_eligible" => "ineligible",
            "revocation_unconfirmed" => "revocationUnconfirmed", "timeout" => "timeout", "canceled" => "canceled", _ => "error"
        };
        return T(key) + " (" + code + (status is { } value ? ", HTTP " + value.ToString(_ui.Culture) : "") + ")";
    }
    /// <summary>Runs at most one native connection action and restores controls after cancellation or failure.</summary>
    private async Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        if (_operation is not null) { return; }
        using var operation = new CancellationTokenSource(); _operation = operation;
        foreach (var button in _actions) { button.IsEnabled = false; } _accounts.IsEnabled = false; _models.IsEnabled = false; _cancel.IsEnabled = true; _result.Text = T("working");
        try { await action(operation.Token).ConfigureAwait(true); if (_result.Text == T("working")) { _result.Text = ""; } }
        catch (ChatGptConnectionException error) { _result.Text = Error(error.Code, error.HttpStatus); }
        catch (OperationCanceledException) { _result.Text = T("canceled"); }
        finally
        {
            _operation = null; foreach (var button in _actions) { button.IsEnabled = true; } _accounts.IsEnabled = true; _models.IsEnabled = true; _cancel.IsEnabled = false;
        }
    }
}
