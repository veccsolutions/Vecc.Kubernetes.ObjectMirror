using k8s;
using k8s.Models;

namespace Vecc.Kubernetes.ObjectMirror.Services.Watchers
{
    public abstract class NamespacedObjectWatcher<T, TMetadataType> : BackgroundService
        where T : class, IKubernetesObject, IMetadata<TMetadataType>, new()
    {
        private readonly IKubernetes _kubernetes;
        private readonly ILogger<NamespacedObjectWatcher<T, TMetadataType>> _logger;
        private readonly Dispatcher<T> _dispatcher;

        protected abstract string ApiVersion { get; }
        protected abstract string Group { get; }
        protected abstract string Plural { get; }
        protected abstract int InitialDelay { get; }
        public static bool Running { get; protected set; }

        public NamespacedObjectWatcher(IKubernetes kubernetes, ILogger<NamespacedObjectWatcher<T, TMetadataType>> logger, Dispatcher<T> dispatcher)
        {
            _kubernetes = kubernetes ?? throw new ArgumentNullException(nameof(kubernetes));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        protected async override Task ExecuteAsync(CancellationToken cancellationToken)
        {
            Running = true;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var watcher = _kubernetes.ListClusterCustomObjectWithHttpMessagesAsync(Group, ApiVersion, Plural, watch: true, timeoutSeconds: 300, cancellationToken: cancellationToken);

                    var watchList = watcher.WatchAsync<T, object>(onError: (Exception exception) => _logger.LogError(exception, "Error while starting watch"));

                    _logger.LogInformation("Starting {className}", GetType().Name);
                    try
                    {
                        await foreach (var (eventType, item) in watchList.WithCancellation(cancellationToken))
                        {
                            if (item == null)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    break;
                                }
                                continue;
                            }
                            _logger.LogDebug("Event: {version}/{resource}/{name} {eventType}", item.ApiVersion, item.Kind, ((IMetadata<V1ObjectMeta>)item).Metadata.Name, eventType);
                            _dispatcher.Dispatch(item, eventType);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.LogInformation("Time to quit cleanly");
                        await Task.Delay(500, cancellationToken);
                    }
                    catch (Exception excepion)
                    {
                        _logger.LogError(excepion, "Unexpected error while watching the namespaces");
                    }
                    finally
                    {
                        _logger.LogInformation("We stopped watching.");
                    }
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception exception)
            {
                Running = false;
                _logger.LogCritical(exception, "Unexpected exception in namespaced object watcher.");
            }
        }
    }
}
