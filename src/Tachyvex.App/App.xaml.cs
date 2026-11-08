using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;
using Tachyvex.App.Services;
using Tachyvex.App.ViewModels;
using Tachyvex.Core.Abstractions;
using Tachyvex.Core.AutoTune;
using Tachyvex.Core.Catalog;
using Tachyvex.Core.Health;
using Tachyvex.Core.Engine;
using Tachyvex.Core.Profiles;
using Tachyvex.Core.State;
using Tachyvex.Windows;
using Tachyvex.Windows.Handlers;
using Tachyvex.Windows.Infrastructure;

namespace Tachyvex.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow? MainWindow { get; private set; }
    public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Nie zamykamy po cichu: zapisujemy do logu i pokazujemy komunikat zamiast znikać.
            Log.Error(e.Exception, "Unhandled exception");
            e.Handled = true;
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Services = BuildServices();
            await Services.GetRequiredService<IStateStore>().LoadAsync();
            var boost = Services.GetRequiredService<GameBoostService>();
            if (Services.GetRequiredService<IStateStore>().State.Settings.GameBoost) boost.Start();
            else _ = boost.RestoreAsync(); // po awarii przywróć plan zasilania zapisany przed grą
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Startup error");
        }
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private static IServiceProvider BuildServices()
    {
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Neox", "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(logDir, "tachyvex-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        var s = new ServiceCollection();
        s.AddLogging(b => b.AddSerilog(Log.Logger, dispose: true));

        // Core
        s.AddSingleton(_ => TweakCatalog.LoadEmbedded());
        s.AddSingleton<IClock, SystemClock>();
        s.AddSingleton<IStateStore>(sp => new JsonStateStore(JsonStateStore.DefaultPath(), sp.GetRequiredService<ILogger<JsonStateStore>>()));
        s.AddSingleton<TweakEngine>();
        s.AddSingleton<ProfileService>();
        s.AddSingleton<ProfileTransfer>();
        s.AddSingleton<AutoTunePlanner>();
        s.AddSingleton<AutoTuneService>();

        // Windows
        s.AddSingleton<IProcessRunner, ProcessRunner>();
        s.AddSingleton<IElevationService, ElevationService>();
        s.AddSingleton<RegistryService>();
        s.AddSingleton<FileCleaner>();
        s.AddSingleton<IRestorePointService, RestorePointService>();
        s.AddSingleton<ITweakHandler, RegistryHandler>();
        s.AddSingleton<ITweakHandler, PowerHandler>();
        s.AddSingleton<ITweakHandler, ServiceHandler>();
        s.AddSingleton<ITweakHandler, TaskHandler>();
        s.AddSingleton<ITweakHandler, BcdHandler>();
        s.AddSingleton<ITweakHandler, NicHandler>();
        s.AddSingleton<ITweakHandler, CmdHandler>();
        s.AddSingleton<IActionHandler, ActionHandler>();
        s.AddSingleton<NetworkDiagnostics>();
        s.AddSingleton<StartupManager>();
        s.AddSingleton<ProcessOverview>();
        s.AddSingleton<WindowsServicesManager>();
        s.AddSingleton<StorageAnalyzer>();
        s.AddSingleton<FortniteConfigService>();
        s.AddSingleton<UpdateService>();
        s.AddSingleton<IHardwareScanner, HardwareScanner>();
        s.AddSingleton<IBenchmarkSuite, BenchmarkSuite>();
        s.AddSingleton<HealthProbe>();
        s.AddSingleton<GameBoostService>();
        s.AddTransient<SystemMonitor>();

        // App
        s.AddSingleton<DialogService>();
        s.AddSingleton<AppContextService>();
        s.AddSingleton<OptimizationsViewModel>();
        s.AddTransient<DashboardViewModel>();
        s.AddTransient<AutoTuneViewModel>();
        s.AddTransient<GamingViewModel>();
        s.AddTransient<NetworkViewModel>();
        s.AddTransient<SystemViewModel>();
        s.AddTransient<ManagersViewModel>();
        s.AddTransient<DiskViewModel>();
        s.AddTransient<HistoryViewModel>();
        s.AddTransient<SettingsViewModel>();
        return s.BuildServiceProvider();
    }
}
