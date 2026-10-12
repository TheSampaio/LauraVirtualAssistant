using Laura.App.Controls;
using Laura.App.Infrastructure;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Markdig;
using Microsoft.Maui.Layouts;

namespace Laura.App.Views;

/// <summary>Native Brain workspace for real files, force graph, Markdown editing and attributed node details.</summary>
public sealed class MemoryPage : ContentPage
{
    private readonly BrainService _brain;
    private readonly BrainSkills _skills;
    private readonly ISettingsService _settings;
    private readonly IUiLocalizer _ui;
    private readonly IConversationMemory _memory;
    private readonly Entry _query = Theme.Entry();
    private readonly Editor _editor = new() { FontFamily = "Consolas", FontSize = 14, TextColor = Theme.Text, BackgroundColor = Theme.Background, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
    private readonly CollectionView _files = new() { SelectionMode = SelectionMode.Single };
    private readonly VerticalStackLayout _details = new() { Spacing = 12 }, _reader = new() { Spacing = 12, Padding = 20 };
    private readonly ScrollView _readerScroll = new(), _detailsScroll = new();
    private readonly Label _status = Theme.Label("", 12, Theme.Muted), _location = Theme.Label("", 11, Theme.Muted);
    private readonly BrainGraphView _graph = new();
    private readonly ContentView _center = new();
    private readonly Grid _columns = new() { ColumnSpacing = 12 };
    private readonly Picker _folder = Theme.Picker([], ""), _type = Theme.Picker([], "");
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly MarkdownPipeline _markdown = new MarkdownPipelineBuilder().DisableHtml().Build();
    private readonly Label _emptyGraph;
    private readonly FlexLayout _noteActions = new() { Wrap = FlexWrap.NoWrap, AlignItems = FlexAlignItems.Start, HeightRequest = 48, WidthRequest = 380 };
    private readonly Button _compactNoteActions;
    private readonly Button _newNoteButton;
    private readonly Button _graphMenu;
    private readonly Grid _tools = new() { ColumnSpacing = 4, RowSpacing = 4 };
    private readonly Grid _noteFooter = new() { RowSpacing = 8, ColumnSpacing = 8 };
    private readonly Grid _actionSlot = new();
    private readonly Dictionary<string, Button> _modeButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _documentButtons = new(StringComparer.Ordinal);
    private readonly FlexLayout _graphActions = new() { Wrap = FlexWrap.NoWrap, AlignItems = FlexAlignItems.Start, HeightRequest = 48, WidthRequest = 240 };
    private readonly Grid _filters = new() { ColumnSpacing = 8, RowSpacing = 8 };
    private readonly HorizontalStackLayout _headerActions = new() { Spacing = 4, HorizontalOptions = LayoutOptions.End };
    private readonly Label _documentTitle = Theme.Label("", 12, Theme.Muted);
    private readonly Label _documentMeta = Theme.Label("", 11, Theme.Muted);
    private readonly Grid _workspaceHeader = new() { ColumnSpacing = 12, RowSpacing = 8, Padding = Theme.HeaderPadding };
    private readonly VerticalStackLayout _brand;
    private readonly FlexLayout _modes = new() { Wrap = FlexWrap.NoWrap, AlignItems = FlexAlignItems.Start, HeightRequest = 48 };
    private readonly Button _communitiesButton;
    private bool _selectingFile;
    private View? _right;
    private BrainNote? _selected;
    private string _mode = "graph", _selectedPath = "", _loadedText = "", _observedHash = "";
    private bool _busy, _detailsVisible = true, _loading, _visible, _editingSource;
    private int _refreshVersion;
    private int _queryVersion;
    private int _fileTreeVersion;
    private bool _selectingTreeItem;
    private BrainSnapshot? _searchSnapshot;
    private string _searchQuery = "";
    private HashSet<string> _matchedPaths = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _queryMatches = new(StringComparer.Ordinal);
    private int _graphDepth = 1;
    private bool _groupCommunities;
    private readonly string _createdCulture;
    private BrainSnapshot? _renderedSnapshot, _inventorySnapshot;
    private string _renderedQuery = "";
    private IReadOnlyList<string> _inventoryFiles = [], _inventoryFolders = [];
    private bool _needsRefresh = true;
    private int _renderedLimit;
    private int _layoutBucket = -1;
    private string _layoutMode = "";
    private bool _layoutDetails;
    private readonly Microsoft.UI.Xaml.Input.KeyboardAccelerator _findShortcut = new() { Key = global::Windows.System.VirtualKey.K, Modifiers = global::Windows.System.VirtualKeyModifiers.Control, IsEnabled = false };
    private Microsoft.UI.Xaml.UIElement? _shortcutOwner;
    private Microsoft.UI.Xaml.UIElement? _fileTreeOwner;
    /// <summary>Gets whether cached controls still match the current interface language.</summary>
    public bool CanReuse => _createdCulture == _ui.Culture.Name;
    /// <summary>Builds the existing Memory destination using native MAUI controls.</summary>
    public MemoryPage(BrainService brain, BrainSkills skills, ISettingsService settings, IUiLocalizer ui, IConversationMemory memory)
    {
        using var performance = PerformanceTrace.Measure("brain.construct");
        _brain = brain; _skills = skills; _settings = settings; _ui = ui; _memory = memory;
        _createdCulture = ui.Culture.Name;
        _brand = Theme.WorkspaceBrand(ui.Get("ui.hud.subtitle"));
        _findShortcut.Invoked += (_, args) => { _query.Focus(); args.Handled = true; };
        HandlerChanged += (_, _) => AttachSearchShortcut();
        Title = T("title"); BackgroundColor = Theme.Background; NavigationPage.SetHasNavigationBar(this, false);
        _emptyGraph = Theme.Label(T("empty"), 15, Theme.Muted); _emptyGraph.HorizontalOptions = LayoutOptions.Center; _emptyGraph.VerticalOptions = LayoutOptions.Center; _emptyGraph.Margin = 24;
        _query.Placeholder = T("search"); _query.TextChanged += async (_, _) => { int version = ++_queryVersion; await Task.Delay(180).ConfigureAwait(true); if (version == _queryVersion) { await RefreshSafelyAsync().ConfigureAwait(true); } };
        _folder.SelectedIndexChanged += async (_, _) => { if (!_loading) { await RefreshGraphAsync().ConfigureAwait(true); } };
        _type.SelectedIndexChanged += async (_, _) => { if (!_loading) { await RefreshGraphAsync().ConfigureAwait(true); } };
        _graph.NodeSelected += async (_, id) => { BrainNote? note = _brain.Snapshot.Notes.FirstOrDefault(note => note.Id == id); if (note is not null) { await OpenAsync(note.Path, false).ConfigureAwait(true); } };
        _graph.NodeOpened += (_, _) => SetMode("read");
        _files.ItemTemplate = new DataTemplate(() =>
        {
            var label = Theme.Label("", 12); label.SetBinding(Label.TextProperty, nameof(FileItem.Label)); label.VerticalOptions = LayoutOptions.Center; label.LineBreakMode = LineBreakMode.TailTruncation;
            var icon = Theme.Icon("file", size: 18); icon.SetBinding(Image.SourceProperty, nameof(FileItem.Icon));
            var row = new Grid { ColumnDefinitions = [new(new GridLength(Theme.TargetSize)), new(new GridLength(18)), new(GridLength.Star)], ColumnSpacing = 6, Padding = new Thickness(0, 0, 4, 0), MinimumHeightRequest = Theme.TargetSize };
            var chevron = Theme.IconButton("chevron_right", async () =>
            { if (row.BindingContext is FileItem { Folder: true } item) { await SetFolderExpandedAsync(item.Path, !item.Expanded).ConfigureAwait(true); } });
            chevron.AutomationId = "brain-folder-disclosure";
            chevron.BackgroundColor = Colors.Transparent; chevron.Padding = 16;
            chevron.SetBinding(ImageButton.SourceProperty, nameof(FileItem.Chevron)); chevron.SetBinding(IsVisibleProperty, nameof(FileItem.Folder));
            row.Add(chevron); row.Add(icon, 1); row.Add(label, 2);
            row.BindingContextChanged += (_, _) =>
            {
                if (row.BindingContext is FileItem item)
                {
                    row.Padding = new Thickness(item.Margin.Left, 0, 4, 0);
                    Theme.DescribeAction(row, item.Path);
                    Theme.DescribeAction(chevron, T(item.Expanded ? "collapseFolder" : "expandFolder") + " · " + item.Label);
                }
            };
            var states = new VisualStateGroup { Name = "CommonStates" };
            var normal = new VisualState { Name = "Normal" }; normal.Setters.Add(new Setter { Property = BackgroundColorProperty, Value = Colors.Transparent });
            var selected = new VisualState { Name = "Selected" }; selected.Setters.Add(new Setter { Property = BackgroundColorProperty, Value = Theme.AssistantSurface });
            states.States.Add(normal); states.States.Add(selected); VisualStateManager.SetVisualStateGroups(row, [states]); return row;
        });
        _files.SelectionChanged += async (_, args) =>
        {
            if (_selectingFile || _selectingTreeItem || args.CurrentSelection.Count == 0 || args.CurrentSelection[0] is not FileItem item || item.Path == _selectedPath) { return; }
            _selectingTreeItem = true;
            try
            {
                if (!item.Folder && item.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { await OpenAsync(item.Path, true).ConfigureAwait(true); }
                else { await SelectPathAsync(item.Path).ConfigureAwait(true); }
            }
            finally { _selectingTreeItem = false; }
        };
        _files.HandlerChanged += (_, _) => { ApplyFileSelectionTheme(); AttachFileTreeInput(); };
        var fileGrid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], RowSpacing = 12 };
        var fileHeading = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] }; fileHeading.Add(Section("folder", T("files"))); fileHeading.Add(Action("folder", FolderAsync, true), 1);
        _newNoteButton = Action("new", NewAsync); _newNoteButton.HorizontalOptions = LayoutOptions.Fill;
        var rootFolder = Action("root", async () => { _query.Text = ""; _folder.SelectedIndex = 0; await RefreshSafelyAsync().ConfigureAwait(true); }); rootFolder.HorizontalOptions = LayoutOptions.Fill;
        fileGrid.Add(fileHeading); fileGrid.Add(_newNoteButton, 0, 1); fileGrid.Add(rootFolder, 0, 2); fileGrid.Add(_files, 0, 3);
        var indexSummary = Theme.Stack(_status, _location); indexSummary.Spacing = 6; fileGrid.Add(Theme.Card(indexSummary, 12), 0, 4);
        _files.BackgroundColor = Theme.Surface; _files.EmptyView = Theme.Label(T("noResults"), 12, Theme.Muted);
        _readerScroll.Content = _reader; _detailsScroll.Content = _details;
        _detailsScroll.SizeChanged += (_, _) => { if (_detailsScroll.Width > 0) { _details.WidthRequest = _detailsScroll.Width; } };
        var left = Theme.Card(fileGrid, 16); _right = Theme.Card(_detailsScroll, 16); ShowEmptyDetails();
        foreach ((string key, string mode) in new[] { ("global", "graph"), ("local", "local"), ("read", "read"), ("edit", "edit"), ("split", "split") })
        { Button button = Action(key, () => { SetMode(mode); return Task.CompletedTask; }); _modeButtons.Add(mode, button); _modes.Add(button); }
        _graphActions.Add(Action("zoomIn", () => { _graph.Zoom(1.2f); return Task.CompletedTask; }, true)); _graphActions.Add(Action("zoomOut", () => { _graph.Zoom(1 / 1.2f); return Task.CompletedTask; }, true));
        _graphActions.Add(Action("fit", () => { _graph.ResetView(); return Task.CompletedTask; }, true));
        _graphActions.Add(Action("expand", ExpandGraphAsync, true));
        _communitiesButton = Action("communities", ToggleCommunitiesAsync, true);
        _graphActions.Add(_communitiesButton);
        _graphMenu = Action("graphTools", GraphToolsAsync, true);
        _tools.Add(_modes); _tools.Add(_graphActions, 1); _tools.Add(_graphMenu, 2);
        var central = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], RowSpacing = 8 }; central.Add(_tools);
        var canvas = Theme.Card(_center, 0); canvas.BackgroundColor = Theme.Background; central.Add(canvas, 0, 1);
        foreach ((string key, Func<Task> action) in new[] { ("save", (Func<Task>)SaveAsync), ("rename", RenameAsync), ("move", MoveAsync), ("duplicate", DuplicateAsync), ("delete", DeleteAsync), ("relation", RelationAsync) })
        { Button button = Action(key, action, key != "save"); _documentButtons.Add(key, button); _noteActions.Add(button); }
        _compactNoteActions = Action("noteActions", NoteActionsAsync, true);
        _documentTitle.FontSize = 14; _documentTitle.FontAttributes = FontAttributes.Bold; _documentTitle.TextColor = Theme.Text; _documentTitle.LineBreakMode = LineBreakMode.TailTruncation; _documentTitle.VerticalOptions = LayoutOptions.Center;
        _documentMeta.LineBreakMode = LineBreakMode.TailTruncation;
        _actionSlot.Add(_noteActions); _actionSlot.Add(_compactNoteActions);
        var document = new Grid { ColumnDefinitions = [new(new GridLength(36)), new(GridLength.Star)], ColumnSpacing = 12, VerticalOptions = LayoutOptions.Center }; document.Add(Theme.Icon("brain", true, 28)); var documentText = Theme.Stack(_documentTitle, _documentMeta); documentText.Spacing = 4; document.Add(documentText, 1);
        _noteFooter.Add(document); _noteFooter.Add(_actionSlot, 1); central.Add(_noteFooter, 0, 2);
        var search = new Grid { ColumnDefinitions = [new(new GridLength(36)), new(GridLength.Star), new(new GridLength(62))], BackgroundColor = Theme.Field }; search.Add(Theme.Icon("search")); search.Add(_query, 1); var shortcut = Theme.Label("Ctrl + K", 10, Theme.Muted); shortcut.VerticalOptions = LayoutOptions.Center; search.Add(shortcut, 2);
        _headerActions.Add(Action("manage", ManageAsync, true)); _headerActions.Add(Action("importSkill", ImportSkillAsync, true));
        _headerActions.Add(Action("details", () => { if (_mode == "details") { SetMode("graph"); } else if (Width < 1200) { SetMode("details"); } else { _detailsVisible = !_detailsVisible; LayoutPanels(); } return Task.CompletedTask; }, true));
        _filters.Add(search); _filters.Add(_folder, 1); _filters.Add(_type, 2); _filters.Add(_headerActions, 3);
        _location.LineBreakMode = LineBreakMode.TailTruncation; _status.LineBreakMode = LineBreakMode.TailTruncation;
        var root = new Grid { Padding = new Thickness(Theme.PageInset, 0, Theme.PageInset, Theme.PageInset) };
        root.Add(_columns); var centerPanel = Theme.Card(central, 8);
        _columns.Add(left); _columns.Add(centerPanel, 1); _columns.Add(_right, 2);
        Content = Workspace(root);
        _editor.TextChanged += (_, _) => UpdateDocumentActions();
        _brain.Changed += OnBrainChanged; SizeChanged += (_, _) => LayoutPanels(); LayoutPanels(); SetMode("graph");
    }
    /// <summary>Resolves localized Brain UI text.</summary>
    private string T(string key) => _ui.Get("ui.brain." + key);
    /// <summary>Creates a compact native action and reports privacy-safe failures.</summary>
    private Button Action(string key, Func<Task> action, bool iconOnly = false)
    {
        Button button = Theme.ActionButton(T(key), ActionIcon(key), async () =>
        {
            if (_busy) { return; } _busy = true;
            try { await action().ConfigureAwait(true); }
            catch (BrainConflictException) { await RefreshSafelyAsync().ConfigureAwait(true); _status.Text = T("conflict"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException) { _status.Text = T("failure") + " · " + error.GetType().Name; }
            finally { _busy = false; Dispatcher.Dispatch(UpdateDocumentActions); }
        }, primary: key is "new" or "save", iconOnly: iconOnly);
        button.Margin = new Thickness(0, 0, Theme.Space, Theme.Space); return button;
    }
    /// <summary>Maps workspace actions to the same bundled Lucide family used by the HUD.</summary>
    private static string ActionIcon(string key) => key switch
    {
        "new" => "file_plus", "folder" => "folder_plus", "root" => "folder", "global" => "network", "local" => "focus", "read" => "book_open", "edit" or "rename" => "pencil", "split" => "columns_2",
        "zoomIn" => "zoom_in", "zoomOut" => "zoom_out", "fit" => "scan", "expand" => "git_fork", "communities" => "group", "save" => "save", "move" => "folder_input",
        "duplicate" => "copy", "delete" => "archive", "relation" => "link", "manage" => "settings_2", "importSkill" => "upload", "details" => "panel_right", "noteActions" => "ellipsis",
        "sourceEditor" or "metadata" => "code", "graphTools" => "scan", "enableSkill" or "disableSkill" or "acceptCorrection" or "resolveConflict" => "shield_check", _ => "chevron_down"
    };
    /// <summary>Creates a restrained icon and caption row for real metadata sections.</summary>
    private static View Section(string icon, string text)
    { var row = new HorizontalStackLayout { Spacing = 8 }; row.Add(Theme.Icon(icon, icon is "brain" or "folder" or "network", 18)); row.Add(Theme.Caption(text)); return row; }
    /// <summary>Gives the initially empty inspector a clear selection affordance.</summary>
    private void ShowEmptyDetails()
    { _details.Clear(); _details.Add(Section("brain", T("details"))); _details.Add(Theme.Label(T("selectNote"), 13, Theme.Muted)); }
    /// <summary>Aligns real note metadata consistently without a stack of nested cards.</summary>
    private static View DetailValue(string name, string value)
    { var row = new Grid { ColumnDefinitions = [new(new GridLength(76)), new(GridLength.Star)], ColumnSpacing = 8 }; row.Add(Theme.Label(name, 11, Theme.Muted)); row.Add(Theme.Label(value, 11), 1); return row; }
    /// <summary>Counts authored words without treating Markdown punctuation as text.</summary>
    private static int WordCount(string body) => System.Text.RegularExpressions.Regex.Count(body, @"[\p{L}\p{N}]+", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    /// <summary>Builds a compact workspace heading using the shared native palette and accessible back action.</summary>
    private View Workspace(View body)
    {
        var heading = Theme.PageHeading(Title, T("subtitle"), "brain", _ui.Get("ui.navigation.backToLaura"),
            async () => { if (await CanLeaveAsync().ConfigureAwait(true)) { _editor.Text = _loadedText; await NavigationMotion.PopAsync(Navigation).ConfigureAwait(true); } });
        _workspaceHeader.Add(_brand); _workspaceHeader.Add(heading, 1); _workspaceHeader.Add(_filters, 2);
        var root = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)], BackgroundColor = Theme.Background }; root.Add(_workspaceHeader); root.Add(body, 0, 1); return root;
    }
    /// <summary>Overrides only file-tree selection resources so Windows' accent cannot introduce a conflicting color.</summary>
    private void ApplyFileSelectionTheme()
    {
        if (_files.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement native) { return; }
        Theme.Accent.ToRgba(out byte red, out byte green, out byte blue, out byte alpha);
        var accent = new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(alpha, red, green, blue));
        native.Resources["ListViewItemSelectionIndicatorBrush"] = accent;
        native.Resources["ListViewItemSelectionIndicatorPointerOverBrush"] = accent;
        native.Resources["ListViewItemSelectionIndicatorPressedBrush"] = accent;
    }
    /// <summary>Moves the accelerator between native handlers when MAUI reconnects a cached page.</summary>
    private void AttachSearchShortcut()
    {
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement native || ReferenceEquals(_shortcutOwner, native)) { return; }
        _shortcutOwner?.KeyboardAccelerators.Remove(_findShortcut); _shortcutOwner = native;
        _findShortcut.ScopeOwner = native; native.KeyboardAccelerators.Add(_findShortcut);
        native.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
    }
    /// <summary>Moves explicit folder keyboard input with the cached tree's native handler.</summary>
    private void AttachFileTreeInput()
    {
        if (_files.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement native || ReferenceEquals(_fileTreeOwner, native)) { return; }
        if (_fileTreeOwner is not null) { _fileTreeOwner.KeyDown -= OnFileTreeKeyDown; }
        _fileTreeOwner = native; native.KeyDown += OnFileTreeKeyDown;
    }
    /// <summary>Uses arrow keys or Enter for disclosure without treating selection restoration as expansion.</summary>
    private async void OnFileTreeKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (_files.SelectedItem is not FileItem { Folder: true } item || args.Key is not (global::Windows.System.VirtualKey.Left or global::Windows.System.VirtualKey.Right or global::Windows.System.VirtualKey.Enter)) { return; }
        args.Handled = true;
        await SetFolderExpandedAsync(item.Path, args.Key == global::Windows.System.VirtualKey.Right || args.Key == global::Windows.System.VirtualKey.Enter && !item.Expanded).ConfigureAwait(true);
    }
    /// <summary>Changes only disclosure state; existing editor drafts and file selection remain intact.</summary>
    private async Task SetFolderExpandedAsync(string path, bool expanded)
    {
        using var performance = PerformanceTrace.Measure("brain.folder-toggle");
        if (expanded ? !_expanded.Add(path) : !_expanded.Remove(path)) { return; }
        await RefreshFilesAsync().ConfigureAwait(true);
    }
    /// <summary>Keeps graph operations reachable without an extra toolbar row in compact windows.</summary>
    private async Task GraphToolsAsync()
    {
        string? choice = await DisplayActionSheetAsync(T("graphTools"), _ui.Get("ui.maui.cancel"), null, T("zoomIn"), T("zoomOut"), T("fit"), T("expand"), T("communities")).ConfigureAwait(true);
        if (choice == T("zoomIn")) { _graph.Zoom(1.2f); } else if (choice == T("zoomOut")) { _graph.Zoom(1 / 1.2f); } else if (choice == T("fit")) { _graph.ResetView(); }
        else if (choice == T("expand")) { await ExpandGraphAsync().ConfigureAwait(true); }
        else if (choice == T("communities")) { await ToggleCommunitiesAsync().ConfigureAwait(true); }
    }
    /// <summary>Cycles the actual bounded local neighborhood used by both toolbar variants.</summary>
    private async Task ExpandGraphAsync()
    { _graphDepth = _graphDepth % 4 + 1; SetMode("local"); await RefreshGraphAsync().ConfigureAwait(true); }
    /// <summary>Toggles connected-community grouping consistently in either toolbar.</summary>
    private async Task ToggleCommunitiesAsync()
    { _groupCommunities = !_groupCommunities; Theme.SetSelected(_communitiesButton, _groupCommunities, T("communities") + (_groupCommunities ? " · " + _ui.Get("ui.navigation.selected") : "")); _graph.SetCommunities(_groupCommunities); await RefreshGraphAsync().ConfigureAwait(true); }
    /// <summary>Updates actual document selection, edit state and applicable operations.</summary>
    private void UpdateDocumentActions()
    {
        bool selected = _selectedPath.Length > 0, markdown = _selectedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        bool dirty = (_editor.Text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal) != _loadedText;
        _documentTitle.Text = selected ? (_selected?.Title ?? Path.GetFileName(_selectedPath)) + (dirty ? " · " + T("unsaved") : "") : T("selectNote");
        _documentMeta.Text = _selected is { } note ? _brain.Snapshot.Edges.Count(edge => edge.Source == note.Id || edge.Target == note.Id) + " " + T("links") + " · " + WordCount(note.Body) + " " + T("words") : "";
        Theme.DescribeAction(_documentTitle, selected ? _selectedPath : T("selectNote"));
        foreach ((string key, Button button) in _documentButtons) { button.IsEnabled = !_busy && (key == "save" ? markdown && dirty : key == "relation" ? _selected is not null : selected); }
        _documentButtons["save"].BackgroundColor = Theme.Primary; _documentButtons["save"].TextColor = Theme.Text; _compactNoteActions.IsEnabled = selected && !_busy;
    }
    /// <inheritdoc />
    protected override async void OnAppearing() { base.OnAppearing(); _visible = true; _findShortcut.IsEnabled = true; _brain.Changed -= OnBrainChanged; _brain.Changed += OnBrainChanged; await RefreshSafelyAsync().ConfigureAwait(true); }
    /// <inheritdoc />
    protected override void OnDisappearing() { _visible = false; _findShortcut.IsEnabled = false; _brain.Changed -= OnBrainChanged; base.OnDisappearing(); }
    /// <summary>Marshals storage notifications while preserving unsaved editor buffers.</summary>
    private void OnBrainChanged(object? sender, EventArgs args) { _needsRefresh = true; _inventorySnapshot = null; if (_visible) { Dispatcher.Dispatch(async () => await RefreshSafelyAsync().ConfigureAwait(true)); } }
    /// <summary>Handles access failures without embedding note contents in exception reports.</summary>
    private async Task RefreshSafelyAsync()
    { try { await RefreshAsync().ConfigureAwait(true); } catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { _status.Text = T("failure") + " · " + error.GetType().Name; } }
    /// <summary>Refreshes files, current filters and index status asynchronously.</summary>
    private async Task RefreshAsync()
    {
        using var performance = PerformanceTrace.Measure("brain.refresh");
        int version = ++_refreshVersion; await _brain.InitializeAsync().ConfigureAwait(true); if (version != _refreshVersion) { return; }
        BrainSnapshot snapshot = _brain.Snapshot; string query = _query.Text?.Trim() ?? "";
        if (!_needsRefresh && ReferenceEquals(snapshot, _renderedSnapshot) && query == _renderedQuery && _renderedLimit == _settings.Current.Brain.GraphLimit) { return; }
        _location.Text = Path.GetFileName(_brain.Location.TrimEnd(Path.DirectorySeparatorChar)); Theme.DescribeAction(_location, _brain.Location); _loading = true;
        string folder = _folder.SelectedItem as string ?? T("allFolders"), type = _type.SelectedItem as string ?? T("allTypes");
        SetItems(_folder, new[] { T("allFolders") }.Concat(_brain.Snapshot.Notes.Select(note => note.Path.Split('/')[0]).Where(name => !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).Distinct().Order(StringComparer.Ordinal)), folder);
        SetItems(_type, new[] { T("allTypes") }.Concat(_brain.Snapshot.Notes.Select(note => note.Type).Distinct().Order(StringComparer.Ordinal)), type); _loading = false;
        await RefreshFilesAsync().ConfigureAwait(true); await RefreshGraphAsync().ConfigureAwait(true);
        _status.Text = _brain.Snapshot.Notes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + T("notes") + " · " + _brain.Snapshot.Edges.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + T("links") + "\n" + (_brain.SemanticAvailable ? T("semanticReady") : T("semanticMissing"))
            + (_brain.Snapshot.Issues.Count > 0 ? " · " + T("issues") + ": " + _brain.Snapshot.Issues.Count : "");
        Theme.DescribeAction(_status, _status.Text); UpdateDocumentActions();
        if (version == _refreshVersion) { _renderedSnapshot = snapshot; _renderedQuery = query; _renderedLimit = _settings.Current.Brain.GraphLimit; _needsRefresh = false; }
    }
    /// <summary>Preserves filter selection while updating actual choices.</summary>
    private static void SetItems(Picker picker, IEnumerable<string> values, string selection)
    { string[] items = values.ToArray(); if (!picker.Items.SequenceEqual(items)) { picker.Items.Clear(); foreach (string item in items) { picker.Items.Add(item); } } picker.SelectedIndex = Math.Max(0, Array.IndexOf(items, selection)); }
    /// <summary>Loads actual paths once per snapshot instead of rescanning the vault on each folder selection.</summary>
    private async Task EnsureInventoryAsync(BrainSnapshot snapshot)
    {
        if (ReferenceEquals(_inventorySnapshot, snapshot)) { return; }
        Task<IReadOnlyList<string>> files = _brain.FilesAsync(), folders = _brain.FoldersAsync();
        await Task.WhenAll(files, folders).ConfigureAwait(true);
        _inventoryFiles = await files.ConfigureAwait(true); _inventoryFolders = await folders.ConfigureAwait(true); _inventorySnapshot = snapshot;
    }
    /// <summary>Lists the real tree only through expanded folders; name search also reveals matching descendants.</summary>
    private async Task RefreshFilesAsync()
    {
        using var performance = PerformanceTrace.Measure("brain.file-tree");
        int version = ++_fileTreeVersion;
        BrainSnapshot inventory = _brain.Snapshot;
        await EnsureInventoryAsync(inventory).ConfigureAwait(true);
        IReadOnlyList<string> actualFiles = _inventoryFiles, folders = _inventoryFolders;
        string[] files = actualFiles.Concat(folders.Select(folder => folder + "/")).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var items = new List<FileItem>(); string query = _query.Text?.Trim() ?? "";
        var folderEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!ReferenceEquals(_searchSnapshot, inventory) || _searchQuery != query)
        {
            var matches = query.Length == 0 ? new HashSet<string>(StringComparer.Ordinal) : (await _brain.SearchAsync(query, 100).ConfigureAwait(true)).Select(hit => hit.Note.Id).ToHashSet(StringComparer.Ordinal);
            if (version != _fileTreeVersion || query != (_query.Text?.Trim() ?? "")) { return; }
            _queryMatches = matches;
            _matchedPaths = query.Length == 0 ? new(StringComparer.OrdinalIgnoreCase) : inventory.Notes.Where(note => matches.Contains(note.Id) || note.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || note.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase))).Select(note => note.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _searchSnapshot = inventory; _searchQuery = query;
        }
        if (version != _fileTreeVersion || query != (_query.Text?.Trim() ?? "")) { return; }
        foreach (string path in files.Where(path => query.Length == 0 || path.Contains(query, StringComparison.OrdinalIgnoreCase) || _matchedPaths.Contains(path)))
        {
            string[] parts = path.Split('/'); bool visible = true;
            for (int depth = 0; depth < parts.Length - 1; depth++)
            {
                string folder = string.Join('/', parts.Take(depth + 1));
                bool expanded = _expanded.Contains(folder) || query.Length > 0;
                if (folderEntries.Add(folder)) { items.Add(new(folder, true, new Thickness(depth * 12, 0, 0, 0), parts[depth], Theme.IconSource(expanded ? "folder_open" : "folder", true), Theme.IconSource(expanded ? "chevron_down" : "chevron_right"), expanded)); }
                if (!_expanded.Contains(folder) && query.Length == 0) { visible = false; break; }
            }
            if (visible && parts[^1].Length > 0) { items.Add(new(path, false, new Thickness((parts.Length - 1) * 12, 0, 0, 0), parts[^1], Theme.IconSource(path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? "file_text" : "file"), Theme.IconSource("chevron_right"))); }
        }
        _selectingFile = true;
        try { _files.ItemsSource = items; _files.SelectedItem = items.FirstOrDefault(item => item.Path == _selectedPath); }
        finally { _selectingFile = false; }
        if (items.Count == 0) { _status.Text = T("noResults"); }
    }
    /// <summary>Filters the graph before layout and rendering.</summary>
    private async Task RefreshGraphAsync()
    {
        string query = _query.Text?.Trim() ?? ""; BrainSnapshot snapshot = _brain.Snapshot;
        if (query.Length > 0) { snapshot = snapshot with { Notes = snapshot.Notes.Where(note => _queryMatches.Contains(note.Id) || note.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || note.Path.Contains(query, StringComparison.OrdinalIgnoreCase) || note.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray() }; }
        await _graph.SetGraphAsync(snapshot, "", _folder.SelectedIndex > 0 ? _folder.SelectedItem as string ?? "" : "", _type.SelectedIndex > 0 ? _type.SelectedItem as string ?? "" : "", _mode == "local" ? _selected?.Id : null, _settings.Current.Brain.GraphLimit, _graphDepth).ConfigureAwait(true);
        _emptyGraph.IsVisible = _graph.VisibleNodeCount == 0;
    }
    /// <summary>Switches native graph, reader, editor and split views without discarding text.</summary>
    private void SetMode(string mode)
    {
        if (_mode == mode && _center.Content is not null) { return; }
        _mode = mode; _center.Content = null;
        if (_editor.Parent is Layout editorParent) { editorParent.Children.Remove(_editor); }
        if (_readerScroll.Parent is Layout readerParent) { readerParent.Children.Remove(_readerScroll); }
        // Move the inspector's container with it so WinUI recalculates the rounded clip and keeps the scroll parent stable.
        if (_right is Border right)
        {
            if (mode == "details" && right.Parent is Layout parent) { parent.Children.Remove(right); }
            else if (mode != "details" && right.Parent is null) { _columns.Add(right, 2); }
            right.Padding = mode == "details" ? 0 : 16; right.StrokeThickness = mode == "details" ? 0 : 1;
        }
        if (_graph.Parent is Layout graphParent) { graphParent.Children.Remove(_graph); }
        if (_emptyGraph.Parent is Layout emptyParent) { emptyParent.Children.Remove(_emptyGraph); }
        if (mode is "graph" or "local") { var graphLayout = new Grid(); graphLayout.Add(_graph); _emptyGraph.IsVisible = !_brain.Snapshot.Notes.Any(note => note.IsKnowledge); graphLayout.Add(_emptyGraph); _center.Content = graphLayout; _ = RefreshGraphAsync(); }
        else if (mode == "edit") { _center.Content = _editor; }
        else if (mode == "split") { var split = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 12 }; split.Add(_editor); split.Add(_readerScroll, 1); _center.Content = split; }
        else if (mode == "details") { _center.Content = _right; }
        else { _center.Content = _readerScroll; }
        foreach ((string value, Button button) in _modeButtons)
        {
            string label = T(value == "graph" ? "global" : value);
            Theme.SetSelected(button, value == mode, label + (value == mode ? " · " + _ui.Get("ui.navigation.selected") : ""));
        }
        LayoutPanels(); UpdateDocumentActions();
    }
    /// <summary>Opens the actual Markdown file after guarding unsaved edits.</summary>
    private async Task OpenAsync(string path, bool read, bool saved = false)
    {
        if (!saved && !await CanLeaveAsync().ConfigureAwait(true)) { return; }
        string content = await _brain.ReadAsync(path).ConfigureAwait(true); _observedHash = FileBrainStorage.Hash(content); _selectedPath = path; _selected = _brain.Snapshot.Notes.FirstOrDefault(note => note.Path == path);
        _editingSource = _selected is null; _editor.Text = _selected?.Body ?? content; _loadedText = (_editor.Text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal); _reader.Clear(); _details.Clear();
        BrainNote? note = _selected;
        if (note is not null) { _graph.Select(note.Id); }
        if (note is not null)
        {
            AddReaderPage(note, note.Body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries), 0);
            _details.Add(Section("brain", T("details"))); _details.Add(new BoxView { Color = Theme.Line, HeightRequest = 1 });
            var noteHeading = new Grid { ColumnDefinitions = [new(new GridLength(36)), new(GridLength.Star)], ColumnSpacing = 12 }; noteHeading.Add(Theme.Icon("file_text", true, 28)); var noteTitle = Theme.Label(note.Title, 17); noteTitle.FontAttributes = FontAttributes.Bold; noteHeading.Add(noteTitle, 1); _details.Add(noteHeading);
            _details.Add(DetailValue(T("type"), note.Type + " · " + note.MemoryType));
            _details.Add(DetailValue(T("folder"), Path.GetDirectoryName(note.Path)?.Replace('\\', '/') is { Length: > 0 } parent ? parent : T("root")));
            _details.Add(DetailValue(T("created"), note.CreatedAt.ToString("g", _ui.Culture))); _details.Add(DetailValue(T("modified"), note.UpdatedAt.ToString("g", _ui.Culture)));
            _details.Add(DetailValue(T("words"), WordCount(note.Body).ToString(_ui.Culture)));
            _details.Add(DetailValue(T("links"), _brain.Snapshot.Edges.Count(edge => edge.Source == note.Id || edge.Target == note.Id).ToString(_ui.Culture)));
            Theme.DescribeAction(noteHeading, note.Path);
            _details.Add(Action("sourceEditor", async () =>
            {
                if (!await CanLeaveAsync().ConfigureAwait(true)) { return; }
                _editingSource = true; _editor.Text = await _brain.ReadAsync(note.Path).ConfigureAwait(true); _loadedText = _editor.Text.Replace("\r\n", "\n", StringComparison.Ordinal); SetMode("edit");
            }));
            _details.Add(new BoxView { Color = Theme.Line, HeightRequest = 1 }); _details.Add(Section("tag", T("tags")));
            var tags = new FlexLayout { Wrap = FlexWrap.Wrap }; foreach (string tag in note.Tags) { var chip = Theme.Card(Theme.Label(tag, 11), 6); chip.StrokeThickness = 0; chip.BackgroundColor = Theme.Field; chip.Margin = new Thickness(0, 0, 6, 6); tags.Add(chip); } _details.Add(tags);
            _details.Add(Section("book_open", T("preview")));
            string preview = Markdown.ToPlainText(note.Body, _markdown).Trim(); _details.Add(Theme.Card(Theme.Label(preview.Length > 360 ? preview[..360] + "…" : preview, 12, Theme.Muted), 12));
            _details.Add(Theme.Label(T("source") + " · " + note.Source + " · " + note.Status, 11, Theme.Muted));
            _details.Add(Theme.Label(T("confidence") + " " + note.Confidence.ToString("P0", _ui.Culture) + " · " + T("importance") + " " + note.Importance.ToString("P0", _ui.Culture), 12));
            _details.Add(new BoxView { Color = Theme.Line, HeightRequest = 1 }); _details.Add(Section("network", T("relations")));
            if (!_brain.Snapshot.Edges.Any(edge => edge.Source == note.Id || edge.Target == note.Id)) { _details.Add(Theme.Label(T("noRelations"), 12, Theme.Muted)); }
            foreach (BrainEdge edge in _brain.Snapshot.Edges.Where(edge => edge.Source == note.Id || edge.Target == note.Id).Take(30))
            {
                string otherId = edge.Source == note.Id ? edge.Target : edge.Source; BrainNote? other = _brain.Snapshot.Notes.FirstOrDefault(item => item.Id == otherId);
                if (other is not null)
                { Button link = Theme.ActionButton(other.Title, "file_text", () => OpenAsync(other.Path, false)); Theme.DescribeAction(link, other.Path + " · " + edge.Type); _details.Add(link); }
            }
            foreach (BrainIssue issue in _brain.Snapshot.Issues.Where(issue => issue.Path == note.Path)) { _details.Add(Theme.Label(T("issues") + " · " + issue.Code, 12, Theme.Accent)); }
            if (_brain.Snapshot.Issues.Any(issue => issue.Path == note.Path && issue.Code == "conflicting_id"))
            {
                _details.Add(Action("resolveConflict", async () =>
                {
                    string? choice = await DisplayActionSheetAsync(T("resolveConflict"), _ui.Get("ui.maui.cancel"), null, T("keepThis"), T("keepBoth")).ConfigureAwait(true);
                    if (choice != T("keepThis") && choice != T("keepBoth")) { return; }
                    await _brain.ResolveConflictAsync(note, choice == T("keepBoth")).ConfigureAwait(true); await OpenAsync(note.Path, true).ConfigureAwait(true);
                }));
            }
            if (note.Type == "skill")
            {
                bool enabled = _settings.Current.Brain.TrustedSkills.Contains(note.Id + ":" + note.Hash, StringComparer.Ordinal);
                _details.Add(Theme.Label(T("skillPrivacy"), 12, Theme.Muted));
                _details.Add(Theme.Label(T("skillCompatibility"), 12, Theme.Muted));
                var metadata = Theme.Label(note.Frontmatter, 11, Theme.Muted); metadata.IsVisible = false;
                _details.Add(Action("metadata", () => { metadata.IsVisible = !metadata.IsVisible; return Task.CompletedTask; })); _details.Add(metadata);
                _details.Add(Action(enabled ? "disableSkill" : "enableSkill", async () => { if (!enabled && !await DisplayAlertAsync(T("enableSkill"), T("skillPrivacy"), T("enableSkill"), _ui.Get("ui.maui.cancel")).ConfigureAwait(true)) { return; } _selected = await _skills.SetEnabledAsync(note, !enabled).ConfigureAwait(true); await OpenAsync(note.Path, false).ConfigureAwait(true); }));
            }
            if (note.Status == "draft" && note.Relations.Any(relation => relation.Type == "contradicts"))
            {
                _details.Add(Action("acceptCorrection", async () =>
                {
                    BrainNote? original = _brain.Snapshot.Notes.FirstOrDefault(candidate => candidate.Id == note.Relations.First(relation => relation.Type == "contradicts").Target);
                    if (original is null || !await DisplayAlertAsync(T("acceptCorrection"), T("confirmCorrection"), T("acceptCorrection"), _ui.Get("ui.maui.cancel")).ConfigureAwait(true)) { return; }
                    await _brain.SaveAsync(original with { Body = note.Body, Source = "user_reviewed_correction", Relations = note.Relations.Where(relation => relation.Type != "contradicts").ToArray() }).ConfigureAwait(true); await _brain.DeleteAsync(note.Path).ConfigureAwait(true); await OpenAsync(original.Path, true).ConfigureAwait(true);
                }));
            }
        }
        else { _reader.Add(Theme.Label(content)); _details.Add(Theme.Label(T("invalid"))); }
        _selectingFile = true; try { _files.SelectedItem = (_files.ItemsSource as IEnumerable<FileItem>)?.FirstOrDefault(item => item.Path == path); } finally { _selectingFile = false; }
        UpdateDocumentActions(); if (read) { SetMode("read"); } await RefreshGraphAsync().ConfigureAwait(true);
    }
    /// <summary>Selects folders and attachments for file operations without parsing them as Markdown.</summary>
    private async Task SelectPathAsync(string path)
    {
        await EnsureInventoryAsync(_brain.Snapshot).ConfigureAwait(true);
        path = _inventoryFiles.Concat(_inventoryFolders).FirstOrDefault(candidate => candidate.Equals(path, StringComparison.OrdinalIgnoreCase)) ?? path;
        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { await OpenAsync(path, false).ConfigureAwait(true); return; }
        if (!await CanLeaveAsync().ConfigureAwait(true)) { return; }
        _selectedPath = path; _selected = null; _editor.Text = _loadedText = ""; _reader.Clear(); _details.Clear();
        _details.Add(Theme.Label(path)); _details.Add(Theme.Label(T("attachment"), 12, Theme.Muted));
        UpdateDocumentActions();
    }
    /// <summary>Renders long documents in bounded native batches while retaining access to every paragraph.</summary>
    private void AddReaderPage(BrainNote note, string[] paragraphs, int start)
    {
        int end = Math.Min(start + 100, paragraphs.Length);
        for (int index = start; index < end; index++) { _reader.Add(Paragraph(paragraphs[index], note)); }
        if (end < paragraphs.Length)
        {
            Button? more = null;
            more = Action("loadMore", () => { _reader.Remove(more!); AddReaderPage(note, paragraphs, end); return Task.CompletedTask; });
            _reader.Add(more);
        }
    }
    /// <summary>Asks before discarding actual modified editor content.</summary>
    private async Task<bool> CanLeaveAsync() => (_editor.Text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal) == _loadedText || await DisplayAlertAsync(T("unsaved"), T("discard"), T("discardAction"), _ui.Get("ui.maui.cancel")).ConfigureAwait(true);
    /// <summary>Renders conventional Markdown as native text with navigable, resolved wikilinks and no active HTML.</summary>
    private Label Paragraph(string markdown, BrainNote owner)
    {
        string plain = Markdown.ToPlainText(markdown, _markdown).Trim(); var label = Theme.Label("", markdown.StartsWith('#') ? 22 : 14);
        var formatted = new FormattedString(); int offset = 0;
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(plain, @"\[\[([^\]]+)\]\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            formatted.Spans.Add(new Span { Text = plain[offset..match.Index] }); string[] parts = match.Groups[1].Value.Split('|', 2);
            string? id = BrainService.ResolveLink(parts[0].Split('#')[0], owner.Path, _brain.Snapshot.Notes); BrainNote? target = id is null ? null : _brain.Snapshot.Notes.FirstOrDefault(note => note.Id == id);
            var span = new Span { Text = parts.Length > 1 ? parts[1] : parts[0], TextColor = Theme.Accent, TextDecorations = TextDecorations.Underline };
            if (target is not null) { var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) => await OpenAsync(target.Path, true).ConfigureAwait(true); span.GestureRecognizers.Add(tap); }
            formatted.Spans.Add(span); offset = match.Index + match.Length;
        }
        formatted.Spans.Add(new Span { Text = plain[offset..] }); label.FormattedText = formatted; return label;
    }
    /// <summary>Creates a user-named note under a stable identity.</summary>
    private async Task NewAsync()
    { if (!await CanLeaveAsync().ConfigureAwait(true)) { return; } string? path = await DisplayPromptAsync(T("new"), T("relativePath"), initialValue: "Inbox/note.md").ConfigureAwait(true); if (string.IsNullOrWhiteSpace(path)) { return; } BrainNote saved = await _brain.SaveAsync(new BrainNote { Path = path, Title = Path.GetFileNameWithoutExtension(path), Body = "# " + Path.GetFileNameWithoutExtension(path) + "\n" }).ConfigureAwait(true); await OpenAsync(saved.Path, false).ConfigureAwait(true); SetMode("edit"); }
    /// <summary>Creates only a user-requested folder.</summary>
    private async Task FolderAsync() { string? path = await DisplayPromptAsync(T("folder"), T("relativePath")).ConfigureAwait(true); if (!string.IsNullOrWhiteSpace(path)) { await _brain.CreateFolderAsync(path).ConfigureAwait(true); await RefreshFilesAsync().ConfigureAwait(true); } }
    /// <summary>Saves raw Markdown with a stable identity and the last observed file hash.</summary>
    private async Task SaveAsync()
    {
        if (!_selectedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { return; }
        BrainNote parsed = _editingSource || _selected is null ? BrainMarkdown.Parse(_editor.Text ?? "", _selectedPath) : _selected with { Body = _editor.Text ?? "" };
        if (_selected is not null && parsed.Id != _selected.Id) { throw new FormatException("Editing cannot change a stable note id; duplicate instead."); }
        _selected = await _brain.SaveAsync(parsed with { Hash = _observedHash }).ConfigureAwait(true); await OpenAsync(_selected.Path, false, true).ConfigureAwait(true); _status.Text = T("saved");
    }
    /// <summary>Renames a selected note while preserving former link aliases.</summary>
    private async Task RenameAsync()
    { if (_selectedPath.Length == 0 || !await CanLeaveAsync().ConfigureAwait(true)) { return; } string? name = await DisplayPromptAsync(T("rename"), T("name"), initialValue: Path.GetFileName(_selectedPath)).ConfigureAwait(true); if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\')) { return; } string target = (Path.GetDirectoryName(_selectedPath)?.Replace('\\', '/') is { Length: > 0 } folder ? folder + "/" : "") + name; await _brain.MoveAsync(_selectedPath, target).ConfigureAwait(true); await SelectPathAsync(target).ConfigureAwait(true); }
    /// <summary>Moves the actual selected file without overwriting destinations.</summary>
    private async Task MoveAsync()
    { if (_selectedPath.Length == 0 || !await CanLeaveAsync().ConfigureAwait(true)) { return; } string? target = await DisplayPromptAsync(T("move"), T("relativePath"), initialValue: _selectedPath).ConfigureAwait(true); if (!string.IsNullOrWhiteSpace(target)) { await _brain.MoveAsync(_selectedPath, target).ConfigureAwait(true); await SelectPathAsync(target).ConfigureAwait(true); } }
    /// <summary>Duplicates a note under a fresh identity.</summary>
    private async Task DuplicateAsync()
    { if (_selectedPath.Length == 0 || !await CanLeaveAsync().ConfigureAwait(true)) { return; } string? target = await DisplayPromptAsync(T("duplicate"), T("relativePath"), initialValue: _selected is null ? _selectedPath + "-copy" : "Inbox/copy.md").ConfigureAwait(true); if (!string.IsNullOrWhiteSpace(target)) { await _brain.CopyPathAsync(_selectedPath, target).ConfigureAwait(true); await SelectPathAsync(target).ConfigureAwait(true); } }
    /// <summary>Confirms reversible archival rather than silently erasing files.</summary>
    private async Task DeleteAsync()
    { if (_selectedPath.Length == 0 || !await CanLeaveAsync().ConfigureAwait(true) || !await DisplayAlertAsync(T("delete"), T("archiveConfirm"), T("delete"), _ui.Get("ui.maui.cancel")).ConfigureAwait(true)) { return; } await _brain.DeleteAsync(_selectedPath).ConfigureAwait(true); _selected = null; _selectedPath = ""; _reader.Clear(); ShowEmptyDetails(); _editor.Text = _loadedText = ""; await RefreshAsync().ConfigureAwait(true); }
    /// <summary>Adds an explicitly selected factual relation with its authored evidence.</summary>
    private async Task RelationAsync()
    {
        if (_selected is null) { return; } string? title = await DisplayPromptAsync(T("relation"), T("targetId")).ConfigureAwait(true);
        string? id = title is null ? null : BrainService.ResolveLink(title, _selected.Path, _brain.Snapshot.Notes); if (id is null) { _status.Text = T("ambiguous"); return; }
        string? relation = await DisplayActionSheetAsync(T("relation"), _ui.Get("ui.maui.cancel"), null, BrainMarkdown.RelationTypes.Order(StringComparer.Ordinal).ToArray()).ConfigureAwait(true); if (relation is null || !BrainMarkdown.RelationTypes.Contains(relation)) { return; }
        string? evidence = await DisplayPromptAsync(T("relation"), T("evidence")).ConfigureAwait(true); if (string.IsNullOrWhiteSpace(evidence)) { return; }
        _selected = await _brain.SaveAsync(_selected with { Relations = _selected.Relations.Concat([new BrainRelation(id, relation, .7, .8, evidence)]).ToArray() }).ConfigureAwait(true); await OpenAsync(_selected.Path, false).ConfigureAwait(true);
    }
    /// <summary>Imports Markdown as disabled instructions without executing scripts or installing dependencies.</summary>
    private async Task ImportSkillAsync()
    { FileResult? file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = T("importSkill") }).ConfigureAwait(true); if (file is null) { return; } if (!file.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { throw new FormatException("Choose Markdown."); } await using Stream stream = await file.OpenReadAsync().ConfigureAwait(true); using var reader = new StreamReader(stream); string content = await reader.ReadToEndAsync().ConfigureAwait(true); BrainNote saved = await _skills.ImportAsync(content, Path.GetFileNameWithoutExtension(file.FileName)).ConfigureAwait(true); await OpenAsync(saved.Path, true).ConfigureAwait(true); }
    /// <summary>Offers location, integrity, retrieval and private backup controls as concrete native actions.</summary>
    private async Task ManageAsync()
    {
        string? action = await DisplayActionSheetAsync(T("manage"), _ui.Get("ui.maui.cancel"), null, T("newBrain"), T("existingBrain"), T("migrate"), T("integrity"), T("rebuild"), T("embedding"), T("learning"), T("budget"), T("backup"), T("restore"), T("clearHistory")).ConfigureAwait(true);
        if (action == T("newBrain") || action == T("existingBrain") || action == T("migrate"))
        { if (!await CanLeaveAsync().ConfigureAwait(true)) { return; } string? path = await PickFolderAsync().ConfigureAwait(true); if (path is null) { return; } await _brain.ChangeLocationAsync(path, action == T("migrate"), action == T("existingBrain")).ConfigureAwait(true); _selected = null; _selectedPath = ""; _editor.Text = _loadedText = ""; _reader.Clear(); ShowEmptyDetails(); await RefreshAsync().ConfigureAwait(true); }
        else if (action == T("rebuild") || action == T("integrity"))
        { await _brain.RefreshAsync(action == T("rebuild")).ConfigureAwait(true); if (_brain.Snapshot.Issues.Count > 0) { await DisplayAlertAsync(T("integrity"), string.Join("\n", _brain.Snapshot.Issues.Take(20).Select(issue => issue.Path + " · " + issue.Code)), "OK").ConfigureAwait(true); } else { _status.Text = T("healthy"); } }
        else if (action == T("learning")) { await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { AutoLearn = !_settings.Current.Brain.AutoLearn } }).ConfigureAwait(true); _status.Text = _settings.Current.Brain.AutoLearn ? T("learningOn") : T("learningOff"); }
        else if (action == T("budget")) { string? value = await DisplayPromptAsync(T("budget"), T("budgetHelp"), initialValue: _settings.Current.Brain.ContextCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture), keyboard: Keyboard.Numeric).ConfigureAwait(true); if (int.TryParse(value, out int budget)) { await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { ContextCharacters = budget } }).ConfigureAwait(true); } }
        else if (action == T("embedding"))
        { FileResult? model = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = T("modelHelp") }).ConfigureAwait(true); if (model is null || !model.FileName.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)) { return; } FileResult? vocabulary = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = T("vocabularyHelp") }).ConfigureAwait(true); if (vocabulary is null) { return; } await _settings.UpdateAsync(_settings.Current with { Brain = _settings.Current.Brain with { EmbeddingModel = model.FullPath, EmbeddingVocabulary = vocabulary.FullPath } }).ConfigureAwait(true); await _brain.RefreshAsync().ConfigureAwait(true); }
        else if (action == T("backup")) { string? path = await DisplayPromptAsync(T("backup"), T("backupHelp"), initialValue: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "brain-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture) + ".zip")).ConfigureAwait(true); if (!string.IsNullOrWhiteSpace(path)) { await _brain.BackupAsync(path).ConfigureAwait(true); } }
        else if (action == T("restore")) { FileResult? file = await FilePicker.Default.PickAsync().ConfigureAwait(true); if (file is null) { return; } string? destination = await PickFolderAsync().ConfigureAwait(true); if (destination is not null) { await _brain.RestoreAsync(file.FullPath, destination).ConfigureAwait(true); await RefreshAsync().ConfigureAwait(true); } }
        else if (action == T("clearHistory") && await DisplayAlertAsync(T("clearHistory"), _ui.Get("ui.memory.clearConfirm"), _ui.Get("ui.maui.delete"), _ui.Get("ui.maui.cancel")).ConfigureAwait(true)) { await Task.Run(_memory.ClearHistory).ConfigureAwait(true); }
    }
    /// <summary>Keeps document actions accessible without consuming several rows in compact windows.</summary>
    private async Task NoteActionsAsync()
    {
        string? choice = await DisplayActionSheetAsync(T("noteActions"), _ui.Get("ui.maui.cancel"), null, T("save"), T("rename"), T("move"), T("duplicate"), T("delete"), T("relation")).ConfigureAwait(true);
        if (choice == T("save")) { await SaveAsync().ConfigureAwait(true); } else if (choice == T("rename")) { await RenameAsync().ConfigureAwait(true); } else if (choice == T("move")) { await MoveAsync().ConfigureAwait(true); }
        else if (choice == T("duplicate")) { await DuplicateAsync().ConfigureAwait(true); } else if (choice == T("delete")) { await DeleteAsync().ConfigureAwait(true); } else if (choice == T("relation")) { await RelationAsync().ConfigureAwait(true); }
    }
    /// <summary>Selects a real Windows folder; a mobile provider will require its own document-storage adapter.</summary>
    private async Task<string?> PickFolderAsync()
    { var picker = new global::Windows.Storage.Pickers.FolderPicker(); picker.FileTypeFilter.Add("*"); var window = (Microsoft.UI.Xaml.Window)Window.Handler!.PlatformView!; WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window)); global::Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync(); return folder?.Path; }
    /// <summary>Preserves the editor and file manager at small window sizes, with recollectable details.</summary>
    private void LayoutPanels()
    {
        if (_right is null || (Width <= 0 && _layoutBucket >= 0)) { return; } bool compact = Width < 1000, show = _detailsVisible && Width >= 1200 && _mode != "details", inlineHeader = Width >= 1200;
        int bucket = compact ? 0 : !inlineHeader ? 1 : Width < 1360 ? 2 : 3;
        if (bucket == _layoutBucket && _mode == _layoutMode && _detailsVisible == _layoutDetails) { return; }
        _layoutBucket = bucket; _layoutMode = _mode; _layoutDetails = _detailsVisible;
        _columns.ColumnDefinitions.Clear(); _columns.ColumnDefinitions.Add(new(new GridLength(compact ? 180 : 250))); _columns.ColumnDefinitions.Add(new(GridLength.Star)); _columns.ColumnDefinitions.Add(new(new GridLength(show ? 280 : 0)));
        _right.IsVisible = show || _mode == "details"; _noteActions.IsVisible = Width >= 1000; _compactNoteActions.IsVisible = Width < 1000;
        _newNoteButton.Text = T("new"); _newNoteButton.WidthRequest = -1;
        foreach ((string mode, Button button) in _modeButtons) { button.Text = Width < 1360 ? "" : T(mode == "graph" ? "global" : mode); button.WidthRequest = Width < 1360 ? Theme.TargetSize : -1; }
        _graphActions.IsVisible = !compact && (_mode is "graph" or "local"); _graphMenu.IsVisible = compact && (_mode is "graph" or "local");
        _tools.ColumnDefinitions.Clear(); _tools.RowDefinitions.Clear(); _tools.ColumnDefinitions.Add(new(GridLength.Star)); _tools.ColumnDefinitions.Add(new(GridLength.Auto)); _tools.ColumnDefinitions.Add(new(GridLength.Auto)); _tools.RowDefinitions.Add(new(GridLength.Auto));
        _noteFooter.ColumnDefinitions.Clear(); _noteFooter.RowDefinitions.Clear(); _noteFooter.ColumnDefinitions.Add(new(GridLength.Star)); _noteFooter.ColumnDefinitions.Add(new(GridLength.Auto)); _noteFooter.RowDefinitions.Add(new(GridLength.Auto));
        Grid.SetRow(_actionSlot, 0); Grid.SetColumn(_actionSlot, 1); _noteFooter.SetColumnSpan(_actionSlot, 1);
        _brand.IsVisible = Width >= 1360; _workspaceHeader.ColumnDefinitions.Clear(); _workspaceHeader.RowDefinitions.Clear();
        _workspaceHeader.ColumnDefinitions.Add(new(new GridLength(_brand.IsVisible ? 155 : 0))); _workspaceHeader.ColumnDefinitions.Add(new(new GridLength(inlineHeader ? 300 : 0))); _workspaceHeader.ColumnDefinitions.Add(new(GridLength.Star)); _workspaceHeader.RowDefinitions.Add(new(GridLength.Auto));
        View heading = (View)_workspaceHeader.Children[1]; Grid.SetColumn(heading, inlineHeader && _brand.IsVisible ? 1 : 0);
        _workspaceHeader.SetColumnSpan(heading, inlineHeader ? _brand.IsVisible ? 1 : 2 : 3);
        Grid.SetColumn(_filters, inlineHeader ? 2 : 0); Grid.SetRow(_filters, inlineHeader ? 0 : 1); _workspaceHeader.SetColumnSpan(_filters, inlineHeader ? 1 : 3); if (!inlineHeader) { _workspaceHeader.RowDefinitions.Add(new(GridLength.Auto)); }
        _filters.ColumnDefinitions.Clear(); _filters.RowDefinitions.Clear();
        if (compact)
        {
            _filters.ColumnDefinitions.Add(new(GridLength.Star)); _filters.ColumnDefinitions.Add(new(GridLength.Star)); _filters.ColumnDefinitions.Add(new(new GridLength(152))); _filters.ColumnDefinitions.Add(new(new GridLength(0)));
            _filters.RowDefinitions.Add(new(GridLength.Auto)); _filters.RowDefinitions.Add(new(GridLength.Auto));
            _filters.SetColumnSpan(_filters.Children[0], 3); Grid.SetRow(_folder, 1); Grid.SetColumn(_folder, 0); Grid.SetRow(_type, 1); Grid.SetColumn(_type, 1); Grid.SetRow(_headerActions, 1); Grid.SetColumn(_headerActions, 2);
        }
        else
        {
            _filters.ColumnDefinitions.Add(new(GridLength.Star)); _filters.ColumnDefinitions.Add(new(new GridLength(145))); _filters.ColumnDefinitions.Add(new(new GridLength(145))); _filters.ColumnDefinitions.Add(new(new GridLength(152)));
            _filters.RowDefinitions.Add(new(GridLength.Auto)); _filters.SetColumnSpan(_filters.Children[0], 1); Grid.SetRow(_folder, 0); Grid.SetColumn(_folder, 1); Grid.SetRow(_type, 0); Grid.SetColumn(_type, 2); Grid.SetRow(_headerActions, 0); Grid.SetColumn(_headerActions, 3);
        }
    }
    /// <summary>Represents a real file-tree entry with native indentation and bundled SVG assets.</summary>
    private sealed record FileItem(string Path, bool Folder, Thickness Margin, string Label, ImageSource Icon, ImageSource Chevron, bool Expanded = false);
    /// <summary>Exercises actual native views and Markdown writes only in isolated smoke mode.</summary>
    internal async Task PreviewBrainForSmokeAsync(string mode)
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Brain previews require isolated UI smoke mode."); }
        await RefreshAsync().ConfigureAwait(true); BrainNote? project = _brain.Snapshot.Notes.FirstOrDefault(note => note.IsKnowledge && note.Type == "project");
        if (project is null) { throw new InvalidOperationException("Synthetic project memory was not learned."); }
        _expanded.Add("Projects"); _expanded.Add("Knowledge"); _expanded.Add("Knowledge/Entities"); await RefreshFilesAsync().ConfigureAwait(true);
        await OpenAsync(project.Path, false).ConfigureAwait(true);
        if (mode == "edit")
        { _editor.Text += "\n\nSynthetic native editor verification."; if (!_documentButtons["save"].IsEnabled) { throw new InvalidOperationException("Modified Markdown cannot be saved from the document toolbar."); } await SaveAsync().ConfigureAwait(true); if (!(await _brain.ReadAsync(project.Path).ConfigureAwait(true)).Contains("Synthetic native editor verification", StringComparison.Ordinal)) { throw new InvalidOperationException("Editor did not write actual Markdown."); } }
        SetMode(mode); await RefreshGraphAsync().ConfigureAwait(true); _graph.ResetView();
        if (_brain.Snapshot.Edges.Count == 0) { throw new InvalidOperationException("Learned project and entity have no evidence-backed connection."); }
    }
    /// <summary>Verifies usable native viewport geometry after responsive layout and inspector reparenting.</summary>
    internal void VerifyBrainForSmoke()
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Brain geometry checks require isolated smoke mode."); }
        if (_center.Width < 250 || _center.Height < 120 || _query.Width < 150) { throw new InvalidOperationException("Brain viewport or search is too small to use."); }
        if (_mode == "details" && (Math.Abs(_detailsScroll.Width - _center.Width) > 2 || Math.Abs(_details.Width - _detailsScroll.Width) > 2)) { throw new InvalidOperationException("Brain inspector retained an obsolete sidebar width."); }
        if (_selected is not null && _documentButtons["save"].IsEnabled) { throw new InvalidOperationException("Unchanged Markdown is incorrectly presented as modified."); }
        if (_noteActions.IsVisible && _documentButtons.Values.Any(button => button.X + button.Width > _noteActions.Width + 1)) { throw new InvalidOperationException("A document action is clipped by its toolbar."); }
    }
    /// <summary>Exercises actual disclosure-button invocation and selection restoration without touching a real vault.</summary>
    internal async Task VerifyFolderExpansionForSmokeAsync()
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Folder checks require isolated smoke mode."); }
        _query.Text = ""; await RefreshAsync().ConfigureAwait(true); await RefreshFilesAsync().ConfigureAwait(true);
        FileItem folder = (_files.ItemsSource as IEnumerable<FileItem>)!.First(item => item.Folder);
        bool initial = _expanded.Contains(folder.Path); _files.SelectedItem = folder;
        await Task.Delay(150).ConfigureAwait(true);
        if (_expanded.Contains(folder.Path) != initial) { throw new InvalidOperationException("Selecting a folder unexpectedly toggled disclosure."); }
        int version = _fileTreeVersion;
        for (int iteration = 0; iteration < 6; iteration++)
        {
            var items = (_files.ItemsSource as IEnumerable<FileItem>)!.ToArray(); int index = Array.FindIndex(items, item => item.Path == folder.Path);
            _files.ScrollTo(index, position: ScrollToPosition.Start, animate: false); await Task.Delay(80).ConfigureAwait(true);
            var list = _files.Handler!.PlatformView as Microsoft.UI.Xaml.Controls.ListViewBase ?? throw new InvalidOperationException("File tree did not use a native virtualized list.");
            var container = list.ContainerFromIndex(index) as Microsoft.UI.Xaml.DependencyObject ?? throw new InvalidOperationException("Folder row was not realized.");
            var nativeButton = FindDisclosure(container) as Microsoft.UI.Xaml.Controls.Button ?? throw new InvalidOperationException("Folder disclosure has no native action.");
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(nativeButton);
            var invoke = peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke) as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider
                ?? throw new InvalidOperationException("Folder disclosure cannot be invoked accessibly.");
            invoke.Invoke(); await Task.Delay(140).ConfigureAwait(true);
            bool expected = iteration % 2 == 0 ? !initial : initial;
            if (_expanded.Contains(folder.Path) != expected) { throw new InvalidOperationException("Native folder disclosure did not settle to the requested state."); }
        }
        if (_fileTreeVersion - version > 12) { throw new InvalidOperationException("Folder selection caused repeated tree reconstruction."); }
        if (_graph.VisibleNodeCount > 0 && _emptyGraph.IsVisible) { throw new InvalidOperationException("The empty graph message covered existing nodes."); }
    }
    /// <summary>Finds a disclosure action within one realized native tree row.</summary>
    private static Microsoft.UI.Xaml.DependencyObject? FindDisclosure(Microsoft.UI.Xaml.DependencyObject parent)
    {
        if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(parent) == "brain-folder-disclosure") { return parent; }
        for (int index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        { var found = FindDisclosure(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index)); if (found is not null) { return found; } }
        return null;
    }
}
