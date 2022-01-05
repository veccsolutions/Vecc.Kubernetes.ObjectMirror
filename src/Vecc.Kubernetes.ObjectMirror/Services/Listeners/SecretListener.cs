using k8s;
using k8s.Models;
using System.Text.RegularExpressions;
using Vecc.Kubernetes.ObjectMirror.Models;

namespace Vecc.Kubernetes.ObjectMirror.Services.Listeners
{
    public class SecretListener : BackgroundService
    {
        private readonly ILogger<SecretListener> _logger;
        private readonly Dispatcher<V1Secret> _secretDispatcher;
        private readonly SharedData _sharedData;

        public static bool Running { get; private set; }

        public SecretListener(ILogger<SecretListener> logger,
            Dispatcher<V1Secret> secretDispatcher,
            SharedData sharedData)
        {
            _logger = logger;
            _secretDispatcher = secretDispatcher;
            _sharedData = sharedData;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Running = true;
            _logger.LogInformation("Starting secret listener");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var item = await _secretDispatcher.GetItemAsync(stoppingToken);
                    if (item?.Item != null)
                    {
                        var sharedSecrets = _sharedData.SharedSecrets;
                        var knownSecrets = _sharedData.KnownSecrets.ToList();

                        if (item.EventType == WatchEventType.Deleted)
                        {
                            // when the secret is deleted because the namespace is deleted (which we don't know) it gets in to a nasty race condition and logs a bunch of errors
                            // this allows the api to return "notfound" in the namespace check when re-creating the secret. If the namespace is not found we cleanly accept that
                            // and don't do anything with the secret.
                            await Task.Delay(200);
                        }

                        if (item.EventType == WatchEventType.Added ||
                            item.EventType == WatchEventType.Modified ||
                            item.EventType == WatchEventType.Deleted)
                        {
                            var secretKey = $"{item.Item.Metadata.NamespaceProperty}/{item.Item.Metadata.Name}";

                            //performance checks, if we are adding a secret, only do stuff if we haven't already added it.
                            if (item.EventType == WatchEventType.Added && knownSecrets.Contains(secretKey))
                            {
                                continue;
                            }
                            else if (item.EventType == WatchEventType.Added)
                            {
                                knownSecrets.Add(secretKey);
                                _sharedData.KnownSecrets = knownSecrets;
                            }
                            if (item.EventType == WatchEventType.Deleted && knownSecrets.Contains(secretKey))
                            {
                                knownSecrets.Remove(secretKey);
                                _sharedData.KnownSecrets = knownSecrets;
                            }

                            //do we have a SharedSecret object tied to this? This is the only time we care about an added, modified or deleted secret
                            foreach (var sharedSecret in sharedSecrets)
                            {
                                //check to see if this secret is used by a shared secret to populate others
                                if (sharedSecret.Spec?.Source?.Namespace == item.Item.Metadata?.NamespaceProperty &&
                                    sharedSecret.Spec?.Source?.Name == item.Item.Metadata?.Name)
                                {
                                    if (item.EventType == WatchEventType.Deleted)
                                    {
                                        _logger.LogError("A secret used as the source to a shared secret has been deleted! {@secret} referenced by {@sharedsecret}",
                                            $"{item.Item.Metadata?.NamespaceProperty}/{item.Item.Metadata?.Name}",
                                            $"{sharedSecret.Spec?.Source?.Namespace}/{sharedSecret.Spec?.Source?.Name}");
                                        continue; //we will just end up throwing errors all over the place if the source secret doesn't exist and we try and sync it.
                                    }

                                    _sharedData.SecretsToSync.Enqueue(new DispatchedEvent<V1beta1SharedSecret> { EventType = item.EventType, Item = sharedSecret, TimeStamp = DateTime.UtcNow });
                                    _sharedData.ResetEvent.Set();

                                    break;
                                }

                                //check to see if this secret is managed by a shared secret
                                //we don't want to spend cycles on added secrets since they don't apply here.
                                if (item.EventType != WatchEventType.Added && sharedSecret.Spec?.Source?.Name == item.Item.Metadata?.Name)
                                {
                                    foreach (var allowedNamespace in (sharedSecret.Spec?.Target?.AllowedNamespaces ?? Enumerable.Empty<string>()))
                                    {
                                        if (Regex.IsMatch(item.Item.Metadata?.NamespaceProperty ?? string.Empty, allowedNamespace))
                                        {
                                            _logger.LogInformation("Syncing source {@secret} to {@namespace}", $"{sharedSecret.Spec?.Source?.Namespace}/{sharedSecret.Spec?.Source?.Name}", item.Item.Metadata?.NamespaceProperty);
                                            _sharedData.NamespacesToSync.Enqueue(new NamespaceToSync(sharedSecret, item.Item.Metadata?.NamespaceProperty ?? string.Empty));
                                            _sharedData.ResetEvent.Set();
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            _logger.LogDebug("Unhandled secret event {@namespace} {@event}", item.Item.Metadata?.Name, item.EventType);
                        }
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Unexpected exception in the NamespaceListener method");
                }
            }
            _logger.LogInformation("Secrets secrets listener cleanly exited.");
            Running = false;
        }
    }
}
