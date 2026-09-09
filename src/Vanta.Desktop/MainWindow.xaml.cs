using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Vanta.Core;
using Vanta.Infrastructure;
using Vanta.Network;

namespace Vanta.Desktop;

public partial class MainWindow : Window
{
    private readonly MiningSession _session = new();
    private readonly DesktopSettingsStore _settingsStore = new();
    private readonly DispatcherTimer _metricsTimer;
    private DesktopSettings _settings = new();
    private bool _allowClose;
    private CancellationTokenSource? _notificationCancellation;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsStore.Load();
        PopulateControls();
        ApplySettings();

        _session.ShareResultReceived += OnShareResultReceived;
        _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _metricsTimer.Tick += (_, _) => RefreshMetrics();
        _metricsTimer.Start();
        SetStatus("PRONTO", "Insira sua carteira e inicie quando quiser.", "#9CA9BC");
    }

    private void PopulateControls()
    {
        foreach (var port in new[] { 3333, 5555, 7777, 9000 })
        {
            PortComboBox.Items.Add(port.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var count in Enumerable.Range(1, HardwareDetector.DetectLogicalCpuCount()))
        {
            ThreadsComboBox.Items.Add(count);
        }
    }

    private void ApplySettings()
    {
        WalletTextBox.Text = _settings.WalletAddress;
        PoolHostTextBox.Text = _settings.PoolHost;
        PortComboBox.Text = _settings.Port.ToString(CultureInfo.InvariantCulture);
        ThreadsComboBox.SelectedItem = Math.Clamp(
            _settings.Threads == 0 ? HardwareDetector.DetectPreferredThreadCount() : _settings.Threads,
            1,
            HardwareDetector.DetectLogicalCpuCount());
        TlsCheckBox.IsChecked = _settings.Tls;
    }

    private async void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session.IsRunning)
        {
            await StopMiningAsync();
            return;
        }

        if (!TryCreateConfiguration(out var configuration, out var validationError))
        {
            SetStatus("REVISE OS DADOS", validationError, "#F4C96B");
            return;
        }

        SetInputsEnabled(false);
        SetStatus("CONECTANDO", "Conectando ao pool e preparando os workers…", "#F4C96B");

        try
        {
            _settings = DesktopSettings.From(configuration);
            _settingsStore.Save(_settings);
            await _session.StartAsync(configuration);
            StartStopButton.Content = "Parar mineração";
            SetStatus("MINERANDO", "Workers ativos. A taxa exibida é o total agregado.", "#46E6A2");
        }
        catch (Exception ex)
        {
            SetStatus("NÃO FOI POSSÍVEL INICIAR", ex.Message, "#F17C7C");
            SetInputsEnabled(true);
        }
    }

    private async Task StopMiningAsync()
    {
        StartStopButton.IsEnabled = false;
        SetStatus("PARANDO", "Finalizando os workers com segurança…", "#F4C96B");
        await _session.StopAsync();
        StartStopButton.Content = "Iniciar mineração";
        StartStopButton.IsEnabled = true;
        SetInputsEnabled(true);
        SetStatus("PRONTO", "Mineração parada.", "#9CA9BC");
    }

    private bool TryCreateConfiguration(out MiningConfiguration configuration, out string error)
    {
        configuration = new MiningConfiguration
        {
            WalletAddress = WalletTextBox.Text.Trim(),
            Threads = ThreadsComboBox.SelectedItem is int threads ? threads : 0,
            Pool = new PoolConfiguration
            {
                Host = PoolHostTextBox.Text.Trim(),
                Port = int.TryParse(PortComboBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : 0,
                Tls = TlsCheckBox.IsChecked == true
            }
        };

        try
        {
            MiningConfigurationValidator.Validate(configuration);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void RefreshMetrics()
    {
        var statistics = _session.Statistics;
        if (statistics is null)
        {
            return;
        }

        HashrateText.Text = $"{statistics.CurrentHashrate:N2} H/s";
        HashesText.Text = statistics.TotalHashes.ToString("N0");
        UptimeText.Text = statistics.Uptime.ToString(@"hh\:mm\:ss");
        AcceptedText.Text = statistics.AcceptedShares.ToString("N0");
        RejectedText.Text = statistics.RejectedShares.ToString("N0");
    }

    private void OnShareResultReceived(object? sender, ShareResult result)
    {
        _ = Dispatcher.InvokeAsync(() => ShowShareNotification(result));
    }

    private async void ShowShareNotification(ShareResult result)
    {
        _notificationCancellation?.Cancel();
        _notificationCancellation?.Dispose();
        _notificationCancellation = new CancellationTokenSource();
        var cancellationToken = _notificationCancellation.Token;

        ShareNotificationBorder.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(result.Accepted ? "#173D2E" : "#49321B"));
        ShareNotificationTitle.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(result.Accepted ? "#46E6A2" : "#F4C96B"));
        ShareNotificationTitle.Text = result.Accepted ? "Share aceita pelo pool" : "Share rejeitada pelo pool";
        ShareNotificationText.Text = result.Accepted
            ? "Seu trabalho foi aceito e contabilizado pelo pool."
            : string.IsNullOrWhiteSpace(result.Error) ? "O pool rejeitou esta share." : result.Error;
        ShareNotificationBorder.Visibility = Visibility.Visible;
        ShareNotificationBorder.Opacity = 1;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            ShareNotificationBorder.Opacity = 0;
            ShareNotificationBorder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetInputsEnabled(bool enabled)
    {
        WalletTextBox.IsEnabled = enabled;
        PoolHostTextBox.IsEnabled = enabled;
        PortComboBox.IsEnabled = enabled;
        ThreadsComboBox.IsEnabled = enabled;
        TlsCheckBox.IsEnabled = enabled;
        StartStopButton.IsEnabled = true;
    }

    private void SetStatus(string title, string message, string color)
    {
        StatusText.Text = title;
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        ConnectionMessageText.Text = message;
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        _metricsTimer.Stop();
        await _session.StopAsync();
        _allowClose = true;
        Close();
    }
}
