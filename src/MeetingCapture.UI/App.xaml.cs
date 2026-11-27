using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MeetingCapture.AI;
using MeetingCapture.Audio;
using MeetingCapture.Chat;
using MeetingCapture.Core;
using MeetingCapture.Core.Interfaces;
using MeetingCapture.Integrations;
using MeetingCapture.Storage;
using MeetingCapture.Synchronization;
using MeetingCapture.Transcription;
using MeetingCapture.UI.ViewModels;
using MeetingCapture.UI.Views;
using MeetingCapture.Video;

namespace MeetingCapture.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(b =>
        {
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Information);
        });

        // Core & Engines
        services.AddSingleton<ISynchronizationService, SynchronizationService>();
        services.AddSingleton<IMeetingStorageService, MeetingStorageService>();
        services.AddSingleton<IVideoCaptureService, VideoCaptureService>();
        services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
        services.AddSingleton<IMicrophoneService, MicrophoneService>();
        services.AddSingleton<ITranscriptionService, TranscriptionService>();
        services.AddSingleton<IChatCaptureService, ChatCaptureService>();
        services.AddSingleton<IMeetingAnalysisService, MeetingAnalysisService>();
        services.AddSingleton<TeamsWindowTracker>();
        services.AddSingleton<IMeetingOrchestrator, MeetingOrchestrator>();

        // ViewModels
        services.AddSingleton<MainViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
