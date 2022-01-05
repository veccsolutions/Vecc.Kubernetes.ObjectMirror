using k8s;
using k8s.Models;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Vecc.Kubernetes.ObjectMirror.Models;
using Vecc.Kubernetes.ObjectMirror.Services;
using Vecc.Kubernetes.ObjectMirror.Services.Listeners;
using Vecc.Kubernetes.ObjectMirror.Services.Mirrors;
using Vecc.Kubernetes.ObjectMirror.Services.Watchers;

var builder = WebApplication.CreateBuilder(args);
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();
var serilog = new Serilog.LoggerConfiguration().ReadFrom.Configuration(configuration).CreateLogger();

builder.Services.AddLogging((builder) =>
{
    builder.ClearProviders();
    builder.AddSerilog(serilog);
});

builder.Host.UseConsoleLifetime();
builder.Services.AddHttpClient();
builder.Services.AddOptions();
builder.Services.AddHealthChecks()
    .AddCheck("NamespaceWatcher", () => { return NamespaceWatcher.Running ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(); })
    .AddCheck("SecretWatcher", () => { return NamespaceWatcher.Running ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(); })
    .AddCheck("SharedSecretWatcher", () => { return NamespaceWatcher.Running ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(); })
    .AddCheck("SecretsMirror", () => { return SecretsMirror.Running ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(); });

builder.Services.AddSingleton(KubernetesClientConfiguration.BuildDefaultConfig());
builder.Services.AddTransient<IKubernetes>(services => new Kubernetes(services.GetRequiredService<KubernetesClientConfiguration>()));
builder.Services.AddSingleton<Dispatcher<V1Namespace>>();
builder.Services.AddSingleton<Dispatcher<V1beta1SharedSecret>>();
builder.Services.AddSingleton<Dispatcher<V1Secret>>();
builder.Services.AddSingleton<SharedData>();

builder.Host.ConfigureServices((serviceCollection) =>
{
    serviceCollection.AddHostedService<NamespaceWatcher>();
    serviceCollection.AddHostedService<SharedSecretWatcher>();
    serviceCollection.AddHostedService<SecretWatcher>();
    serviceCollection.AddHostedService<SecretsMirror>();
    serviceCollection.AddHostedService<NamespaceListener>();
    serviceCollection.AddHostedService<SecretListener>();
    serviceCollection.AddHostedService<SharedSecretListener>();
});

var app = builder.Build();

app.UseRouting();
app.UseEndpoints(endpoints => endpoints.MapHealthChecks("/healthz", new HealthCheckOptions { AllowCachingResponses = false }));

app.Run();
