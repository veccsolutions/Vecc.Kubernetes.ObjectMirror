using k8s;
using Vecc.Kubernetes.ObjectMirror.Models;

namespace Vecc.Kubernetes.ObjectMirror.Services.Listeners
{
    public class SharedSecretListener : BackgroundService
    {
        private readonly ILogger<SharedSecretListener> _logger;
        private readonly Dispatcher<V1beta1SharedSecret> _sharedSecretDispatcher;
        private readonly SharedData _sharedData;

        public static bool Running { get; private set; }

        public SharedSecretListener(ILogger<SharedSecretListener> logger,
            Dispatcher<V1beta1SharedSecret> sharedSecretDispatcher,
            SharedData sharedData)
        {
            _logger = logger;
            _sharedSecretDispatcher = sharedSecretDispatcher;
            _sharedData = sharedData;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Running = true;
            _logger.LogInformation("Starting shared secret listener");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var item = await _sharedSecretDispatcher.GetItemAsync(stoppingToken);
                    if (item?.Item != null)
                    {
                        var currentSharedSecrets = _sharedData.SharedSecrets.ToList();
                        if (item.EventType == WatchEventType.Added)
                        {
                            if (!currentSharedSecrets.Any(x => x.Metadata?.Name == item.Item.Metadata?.Name))
                            {
                                _logger.LogInformation("Syncing added secret {@secret}", $"{item.Item.Spec?.Source?.Namespace}/{item.Item.Spec?.Source?.Name}");
                                currentSharedSecrets.Add(item.Item);
                                _sharedData.SharedSecrets = currentSharedSecrets;
                                _sharedData.SecretsToSync.Enqueue(item);
                                _sharedData.ResetEvent.Set();
                            }
                        }
                        else if (item.EventType == WatchEventType.Deleted)
                        {
                            // all we need to do is remove it from our cache. K8s will handle garbage collection because we set the owner on the created secrets.
                            var currentItem = currentSharedSecrets.FirstOrDefault(x => x.Metadata?.Name == item.Item.Metadata?.Name);
                            if (currentItem != null)
                            {
                                _logger.LogInformation("Syncing deleted secret {@secret}", $"{item.Item.Spec?.Source?.Namespace}/{item.Item.Spec?.Source?.Name}");
                                currentSharedSecrets.Remove(currentItem);
                                _sharedData.SharedSecrets = currentSharedSecrets;
                            }
                        }
                        else if (item.EventType == WatchEventType.Modified)
                        {
                            var currentItem = currentSharedSecrets.FirstOrDefault(x => x.Metadata?.Name == item.Item.Metadata?.Name);
                            _logger.LogInformation("Syncing modified secret {@secret}", $"{item.Item.Spec?.Source?.Namespace}/{item.Item.Spec?.Source?.Name}");

                            if (currentItem != null)
                            {
                                currentSharedSecrets.Remove(currentItem);
                            }

                            currentSharedSecrets.Add(item.Item);
                            _sharedData.SharedSecrets = currentSharedSecrets;
                            _sharedData.SecretsToSync.Enqueue(item);
                            _sharedData.ResetEvent.Set();
                        }
                        else
                        {
                            _logger.LogDebug("Unhandled shared secret event {@sharedSecret} {@event}", item.Item.Metadata?.Name, item.EventType);
                        }
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Unexpected exception in the SharedSecretListener method");
                }
            }
            _logger.LogInformation("Secrets shared secret listener cleanly exited.");
            Running = false;
        }
    }
}
