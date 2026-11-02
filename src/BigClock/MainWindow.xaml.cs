using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BigClock;

public partial class MainWindow : Window
{
    private const string CheckLabel = "Check for updates";

    private static readonly Version CurrentVersion =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    private readonly Settings _settings = Settings.Load();
    private readonly DispatcherTimer _timer = new();
    private readonly Updater _updater = new(Updater.CreateHttpClient());
    private readonly string _exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown exe path.");
    private readonly StartupRegistration _startup;
    private readonly WheelAccumulator _wheel = new(Mouse.MouseWheelDeltaForOneLine);

    private DateOnly _today;
    private DateOnly _shownMonth;
    private UpdateInfo? _pendingUpdate;

    public MainWindow()
    {
        InitializeComponent();
        _startup = new StartupRegistration(_exePath);

        HeaderItems.ItemsSource = CalendarMonth.DayHeaders(CultureInfo.CurrentCulture.DateTimeFormat);
        _today = DateOnly.FromDateTime(DateTime.Now);
        ShowMonth(_today);

        RestoreWindow();
        PinToggle.IsChecked = _settings.Pinned;
        CompactToggle.IsChecked = _settings.Compact;
        StartupToggle.IsChecked = _startup.IsEnabled;
        Topmost = _settings.Pinned;

        // Subscribed after restoring state, and not Click, which UI Automation toggling doesn't raise.
        PinToggle.Checked += OnPinChanged;
        PinToggle.Unchecked += OnPinChanged;
        CompactToggle.Checked += OnCompactChanged;
        CompactToggle.Unchecked += OnCompactChanged;
        StartupToggle.Checked += OnStartupChanged;
        StartupToggle.Unchecked += OnStartupChanged;

        UpdateButton.Content = CheckLabel;
        UpdateButton.ToolTip = $"BigClock {FormatVersion(CurrentVersion)}";

        _timer.Tick += (_, _) => Tick();
        Tick();
        _timer.Start();
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Show();
        Activate();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString("T", CultureInfo.CurrentCulture);
        DateText.Text = now.ToString("D", CultureInfo.CurrentCulture);

        var today = DateOnly.FromDateTime(now);
        if (today != _today)
        {
            // Follow the date over midnight unless the user is browsing another month.
            var wasShowingToday = _shownMonth.Year == _today.Year && _shownMonth.Month == _today.Month;
            _today = today;
            ShowMonth(wasShowingToday ? today : _shownMonth);
        }

        // Re-align every tick so seconds change on the second, including after sleep.
        _timer.Interval = TimeSpan.FromMilliseconds(1000 - now.Millisecond);
    }

    private void ShowMonth(DateOnly month)
    {
        _shownMonth = new DateOnly(month.Year, month.Month, 1);
        var culture = CultureInfo.CurrentCulture;
        MonthText.Text = _shownMonth.ToString("Y", culture);
        DayItems.ItemsSource = CalendarMonth.Build(month.Year, month.Month, culture.DateTimeFormat.FirstDayOfWeek, _today);
    }

    private void MoveMonth(int delta)
    {
        // The six-week grid spills past the month, so stay clear of DateOnly's limits.
        var target = _shownMonth.AddMonths(delta);
        if (target.Year is > 1 and < 9999)
            ShowMonth(target);
    }

    private void OnPreviousMonth(object sender, RoutedEventArgs e) => MoveMonth(-1);

    private void OnNextMonth(object sender, RoutedEventArgs e) => MoveMonth(1);

    private void OnToday(object sender, RoutedEventArgs e) => ShowMonth(_today);

    private void OnCalendarMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var steps = _wheel.Add(e.Delta);
        if (steps != 0)
            MoveMonth(-steps);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: MoveMonth(-1); break;
            case Key.Right: MoveMonth(1); break;
            case Key.Home: ShowMonth(_today); break;
            default: return;
        }

        e.Handled = true;
    }

    private void OnPinChanged(object sender, RoutedEventArgs e)
    {
        _settings.Pinned = PinToggle.IsChecked == true;
        Topmost = _settings.Pinned;
    }

    private void OnCompactChanged(object sender, RoutedEventArgs e)
    {
        RememberSize();
        _settings.Compact = CompactToggle.IsChecked == true;
        ApplyLayoutMode();
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        _startup.SetEnabled(StartupToggle.IsChecked == true);
        StartupToggle.IsChecked = _startup.IsEnabled;
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        try
        {
            if (_pendingUpdate is null)
                await CheckForUpdateAsync();
            else
                await InstallUpdateAsync(_pendingUpdate);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                       or OperationCanceledException or JsonException or UpdateException or Win32Exception)
        {
            UpdateButton.Content = "Update failed, retry";
            UpdateButton.ToolTip = ex is OperationCanceledException ? "Timed out talking to GitHub." : ex.Message;
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private async Task CheckForUpdateAsync()
    {
        UpdateButton.Content = "Checking…";
        _pendingUpdate = await _updater.CheckAsync(CurrentVersion);

        if (_pendingUpdate is null)
        {
            UpdateButton.Content = $"Up to date ({FormatVersion(CurrentVersion)})";
            UpdateButton.ToolTip = "Click to check again";
        }
        else
        {
            UpdateButton.Content = $"Install {_pendingUpdate.Tag}";
            UpdateButton.ToolTip = $"Download {_pendingUpdate.Tag} and restart BigClock";
        }
    }

    private async Task InstallUpdateAsync(UpdateInfo update)
    {
        UpdateButton.Content = "Downloading…";
        var progress = new Progress<int>(percent => UpdateButton.Content = $"Downloading {percent}%");
        var newExe = await _updater.DownloadAsync(update, _exePath, progress);

        UpdateButton.Content = "Restarting…";
        Updater.SwapIn(_exePath, newExe);
        try
        {
            Process.Start(new ProcessStartInfo(_exePath, App.UpdatedArgument) { UseShellExecute = false });
        }
        catch (Win32Exception)
        {
            Updater.RollBack(_exePath);
            throw;
        }

        Close();
    }

    private static string FormatVersion(Version version) => $"v{version.ToString(3)}";

    private void RestoreWindow()
    {
        ApplyLayoutMode();

        if (_settings.Left is { } left && _settings.Top is { } top && IsOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void ApplyLayoutMode()
    {
        var compact = _settings.Compact;
        CalendarPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CalendarRow.Height = compact ? new GridLength(0) : new GridLength(2.6, GridUnitType.Star);
        Width = compact ? _settings.CompactWidth : _settings.FullWidth;
        Height = compact ? _settings.CompactHeight : _settings.FullHeight;
    }

    // A monitor may have been unplugged since the position was saved.
    private static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 50
        && top >= SystemParameters.VirtualScreenTop
        && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
        && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;

    private void RememberSize()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        _settings.Left = bounds.Left;
        _settings.Top = bounds.Top;

        if (_settings.Compact)
        {
            _settings.CompactWidth = bounds.Width;
            _settings.CompactHeight = bounds.Height;
        }
        else
        {
            _settings.FullWidth = bounds.Width;
            _settings.FullHeight = bounds.Height;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _timer.Stop();
        RememberSize();
        _settings.Save();
    }
}
