using k8s;
using k8s.Models;
using System.Text.RegularExpressions;
using Vecc.Kubernetes.ObjectMirror.Models;

namespace Vecc.Kubernetes.ObjectMirror.Services.Listeners
{
    public class NamespaceListener : BackgroundService
    {
        private readonly ILogger<NamespaceListener> _logger;
        private readonly Dispatcher<V1Namespace> _namespaceDispatcher;
        private readonly SharedData _sharedData;

        public static bool Running { get; private set; }

        public NamespaceListener(ILogger<NamespaceListener> logger,
            Dispatcher<V1Namespace> namespaceDispatcher,
            SharedData sharedData)
        {
            _logger = logger;
            _namespaceDispatcher = namespaceDispatcher;
            _sharedData = sharedData;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Running = true;
            _logger.LogInformation("Starting namespace listener");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var item = await _namespaceDispatcher.GetItemAsync(stoppingToken);
                    if (item?.Item != null)
                    {
                        var sharedSecrets = _sharedData.SharedSecrets;
                        var knownNamespaces = _sharedData.KnownNamespaces.ToList();
                        var namespaceName = item.Item.Metadata.Name;

                        if (item.EventType == WatchEventType.Added)
                        {
                            //check to see if we already know about this namespace, every time this application starts watching the namespaces (restarted every 300 seconds)
                            //  it gets a list of all namespaces and processes the secrets assigned to them. This could take a while (1200 secrets is about 15 seconds)
                            //this check helps with that by not re-syncing namespaces it doesn't need to.
                            if (knownNamespaces.Contains(namespaceName))
                            {
                                continue;
                            }
                            knownNamespaces.Add(namespaceName);
                            _sharedData.KnownNamespaces = knownNamespaces;
                            foreach (var sharedSecret in sharedSecrets)
                            {
                                var blocked = false;
                                foreach (var blockedNamespace in (sharedSecret.Spec?.Target?.BlockedNamespaces ?? Enumerable.Empty<string>()))
                                {
                                    if (Regex.IsMatch(item.Item?.Metadata?.NamespaceProperty ?? string.Empty, blockedNamespace))
                                    {
                                        _logger.LogDebug("{@namespace} is blocked for the shared secret {@sharedSecret}", item.Item?.Metadata?.NamespaceProperty, $"{sharedSecret.Spec?.Source?.Namespace}/{sharedSecret.Spec?.Source?.Name}");
                                        blocked = true;
                                    }
                                }
                                if (!blocked)
                                {
                                    foreach (var allowedNamespace in (sharedSecret.Spec?.Target?.AllowedNamespaces ?? Enumerable.Empty<string>()))
                                    {
                                        if (Regex.IsMatch(item.Item?.Metadata?.Name ?? string.Empty, allowedNamespace))
                                        {
                                            _logger.LogInformation("Syncing secret {@secret} to {@namespace}", $"{sharedSecret.Spec?.Source?.Namespace}/{sharedSecret.Spec?.Source?.Name}", item.Item?.Metadata?.Name);
                                            _sharedData.NamespacesToSync.Enqueue(new NamespaceToSync(sharedSecret, item.Item?.Metadata?.Name ?? string.Empty));
                                            _sharedData.ResetEvent.Set();
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        else if (item.EventType == WatchEventType.Deleted)
                        {
                            if (knownNamespaces.Contains(namespaceName))
                            {
                                knownNamespaces.Remove(namespaceName);
                                _sharedData.KnownNamespaces = knownNamespaces;
                            }
                        }
                        else
                        {
                            _logger.LogDebug("Unhandled namespace event {@namespace} {@event}", item.Item.Metadata?.Name, item.EventType);
                        }
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Unexpected exception in the NamespaceListener method");
                }
            }

            _logger.LogInformation("Secrets namespace listener cleanly exited.");
            Running = false;
        }
    }
}
