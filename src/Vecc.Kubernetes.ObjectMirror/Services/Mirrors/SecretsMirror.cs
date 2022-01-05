using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Microsoft.Rest;
using Newtonsoft.Json;
using System.Net;
using System.Text.RegularExpressions;
using Vecc.Kubernetes.ObjectMirror.Models;

namespace Vecc.Kubernetes.ObjectMirror.Services.Mirrors
{
    public class SecretsMirror : BackgroundService
    {
        private readonly ILogger<SecretsMirror> _logger;
        private readonly IKubernetes _kubernetes;
        private readonly SharedData _sharedData;

        public static bool Running { get; private set; }

        public SecretsMirror(ILogger<SecretsMirror> logger,
            IKubernetes kubernetes,
            SharedData sharedData)
        {
            _logger = logger;
            _kubernetes = kubernetes;
            _sharedData = sharedData;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(1000); //let the other threads do their initial stuff as quickly as possible. Helps with startup time.
            _logger.LogInformation("Starting the secrets mirror.");
            Running = true;
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var syncedSharedSecrets = new Dictionary<string, List<string>>();

                    if (_sharedData.ResetEvent.WaitOne(new TimeSpan(0, 0, 1)))
                    {
                        var namespaceObjects = await _kubernetes.ListNamespaceAsync(cancellationToken: stoppingToken);
                        var namespaces = namespaceObjects.Items.Select(x => x.Metadata.Name).ToArray();

                        _logger.LogTrace("Checking secrets to sync");
                        while (_sharedData.SecretsToSync.TryDequeue(out var dispatchedSharedSecret))
                        {
                            await SyncSharedSecretAsync(dispatchedSharedSecret, namespaces);
                        }

                        _logger.LogTrace("Checking namespaces to sync");
                        while (_sharedData.NamespacesToSync.TryDequeue(out var space))
                        {
                            await SyncNamespaceAsync(space);
                        }
                    }

                }

                _logger.LogInformation("Secrets mirror cleanly exited.");
            }
            catch (Exception exception)
            {
                _logger.LogCritical(exception, "Unhandled exception in the mirror thread!");
            }
            finally
            {
                _logger.LogInformation("Done in the mirror thread.");
            }
            Running = false;
        }

        private async Task SyncSharedSecretAsync(DispatchedEvent<V1beta1SharedSecret> dispatchedSharedSecret, string[] namespaces)
        {
            var target = dispatchedSharedSecret.Item?.Spec?.Target;
            var source = dispatchedSharedSecret.Item?.Spec?.Source;
            var sharedSecret = dispatchedSharedSecret.Item;

            if (target == null || target.AllowedNamespaces == null)
            {
                _logger.LogError("Shared secret target is null: {@sharedSecret}", dispatchedSharedSecret);
                return;
            }

            if (source == null)
            {
                _logger.LogError("Shared secret source is null: {@sharedSecret}", dispatchedSharedSecret);
                return;
            }

            if (sharedSecret == null)
            {
                _logger.LogError("Shared secret is null: {@item}", dispatchedSharedSecret);
                return;
            }

            _logger.LogInformation("Syncing shared secret: {@sharedsecret}", sharedSecret.Metadata?.Name);

            V1Secret? secret = null;
            var errored = false;
            try
            {
                secret = await _kubernetes.ReadNamespacedSecretAsync(source.Name, source.Namespace);
                if (secret == null)
                {
                    _logger.LogError("Source secret was null: {@source}", source);
                    _sharedData.SecretsToSync.Enqueue(dispatchedSharedSecret);
                    errored = true;
                }
            }
            catch (HttpOperationException exception)
            {
                _logger.LogError(exception, "Unexpected response from the Kubernetes API getting source secret. {@source}", sharedSecret);
                _sharedData.SecretsToSync.Enqueue(dispatchedSharedSecret);
                errored = true;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error getting source secret: {@source}", source);
                _sharedData.SecretsToSync.Enqueue(dispatchedSharedSecret);
                errored = true;
            }

            if (errored)
            {
                _logger.LogInformation("An error occurred, waiting 5 seconds and trying again.");
                await Task.Delay(5000);
                _sharedData.ResetEvent.Set();
                return;
            }

            foreach (var space in namespaces)
            {
                foreach (var allowedNamespace in target.AllowedNamespaces)
                {
                    if (Regex.IsMatch(space, allowedNamespace))
                    {
                        try
                        {
                            //not calling await on an async method schedules it on the task scheduler and lets it run to its end.
                            CopySharedSecretToNamespaceAsync(secret, sharedSecret, space);
                        }
                        catch (HttpOperationException exception)
                        {
                            _logger.LogError(exception, "Unable to sync secret. {@source}", source);
                            await Task.Delay(5000);
                            _sharedData.SecretsToSync.Enqueue(dispatchedSharedSecret);
                        }
                        catch (Exception exception)
                        {
                            _logger.LogError(exception, "Unable to sync secret. {@source}", source);
                            await Task.Delay(5000);
                            _sharedData.SecretsToSync.Enqueue(dispatchedSharedSecret);
                        }
                    }
                }
            }
        }

        private async Task SyncNamespaceAsync(NamespaceToSync space)
        {
            if (space == null)
            {
                _logger.LogError("Namespace to sync object was null");
                return;
            }

            var namespaceName = space.Namespace;
            var sharedSecrets = _sharedData.SharedSecrets;

            foreach (var sharedSecret in sharedSecrets)
            {
                var errored = false;
                var target = sharedSecret.Spec?.Target;
                var source = sharedSecret.Spec?.Source;

                if (target == null || target.AllowedNamespaces == null)
                {
                    _logger.LogError("Shared secret target is null: {@sharedSecret}", sharedSecret);
                    _sharedData.NamespacesToSync.Enqueue(space);
                    _sharedData.ResetEvent.Set();
                    errored = true;
                    return;
                }

                if (source == null)
                {
                    _logger.LogError("Shared secret source is null: {@sharedSecret}", sharedSecret);
                    _sharedData.NamespacesToSync.Enqueue(space);
                    _sharedData.ResetEvent.Set();
                    errored = true;
                    return;
                }

                var synced = false;
                foreach (var targetSpace in target.AllowedNamespaces)
                {
                    _logger.LogDebug("Checking namespace {@space} against {@targetSpace}", namespaceName, targetSpace);

                    if (synced)
                    {
                        _logger.LogDebug("Already synced the secret, no need to re-check it.");
                        return;
                    }

                    if (Regex.IsMatch(namespaceName, targetSpace))
                    {
                        V1Secret? secret = null;
                        try
                        {
                            secret = await _kubernetes.ReadNamespacedSecretAsync(source.Name, source.Namespace);
                            if (secret == null)
                            {
                                _logger.LogError("Secret was null: {@source}", source);
                                _sharedData.NamespacesToSync.Enqueue(space);
                                _sharedData.ResetEvent.Set();
                                errored = true;
                            }
                        }
                        catch (HttpOperationException exception)
                        {
                            _logger.LogError(exception, "Unexpected response from the Kubernetes API getting source secret. {@source}", sharedSecret);
                            throw;
                        }
                        catch (Exception exception)
                        {
                            _logger.LogError(exception, "Error getting source secret");
                            _sharedData.NamespacesToSync.Enqueue(space);
                            _sharedData.ResetEvent.Set();
                            errored = true;
                        }

                        if (errored)
                        {
                            _logger.LogInformation("An error occurred, waiting 5 seconds and trying again.");
                            await Task.Delay(5000);
                            _sharedData.ResetEvent.Set();
                            return;
                        }

                        try
                        {
                            //not calling await on an async method schedules it on the task scheduler and lets it run to its end.
                            CopySharedSecretToNamespaceAsync(secret, sharedSecret, namespaceName);
                        }
                        catch (HttpOperationException exception)
                        {
                            _logger.LogError(exception, "Unable to sync secret. {@source}", source);
                            await Task.Delay(5000);
                            _sharedData.NamespacesToSync.Enqueue(space);
                            _sharedData.ResetEvent.Set();
                        }
                        catch (Exception exception)
                        {
                            _logger.LogError(exception, "Unable to sync secret. {@source}", source);
                            await Task.Delay(5000);
                            _sharedData.NamespacesToSync.Enqueue(space);
                            _sharedData.ResetEvent.Set();
                        }
                    }
                }
            }
        }

        private async void CopySharedSecretToNamespaceAsync(V1Secret? secret, V1beta1SharedSecret sharedSecret, string space)
        {
            if (secret == null)
            {
                _logger.LogError("Source secret is null, this should never happen. Not doing anything.");
                return;
            }

            if (secret.Metadata == null)
            {
                _logger.LogError("Source secret metadata is null, this should never happen. Not doing anything.");
            }

            try
            {
                // check to see if the namespace exists, this is needed during a deletion of the namespace
                var k8sNamespace = await _kubernetes.ReadNamespaceAsync(space);
            }
            catch (HttpOperationException exception)
            {
                if (exception.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Attempted to create a secret in a missing namespace (this can happen many times when a namespace containing a managed secret gets deleted). {@content}", exception.Response.Content);
                    return;
                }
                else
                {
                    _logger.LogError(exception, "Unexpected HttpOperationException response from the Kubernetes API while creating the destination secret. {@secret}", $"{space}/{secret.Metadata?.Name}");
                    return;
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unexpected error getting Namespace");
            }

            try
            {
                var originalSecret = await _kubernetes.ReadNamespacedSecretAsync(secret.Metadata?.Name ?? string.Empty, space);
                if (originalSecret.Metadata == null)
                {
                    _logger.LogError("Original secret metadata is null, this should never happen. Not doing anything.");
                    return;
                }

                if (originalSecret != null)
                {
                    var jsonPatchDocument = new JsonPatchDocument<V1Secret>(new List<Operation<V1Secret>>(), new JsonContractResolver());
                    var sync = false;

                    if (!Compare(originalSecret.Metadata?.Annotations, secret.Metadata?.Annotations))
                    {
                        jsonPatchDocument.Replace((secret) => secret.Metadata.Annotations, secret.Metadata?.Annotations);
                        sync = true;
                    }

                    if (!Compare(originalSecret.Metadata?.Labels, secret.Metadata?.Labels))
                    {
                        jsonPatchDocument.Replace((secret) => secret.Metadata.Labels, secret.Metadata?.Labels);
                        sync = true;
                    }

                    if (!Compare(originalSecret.Data?.ToDictionary(x => x.Key, x => Convert.ToBase64String(x.Value)), secret.Data?.ToDictionary(x => x.Key, x => Convert.ToBase64String(x.Value))))
                    {
                        jsonPatchDocument.Replace((secret) => secret.Data, secret.Data);
                        sync = true;
                    }

                    if (!Compare(originalSecret.StringData, secret.StringData))
                    {
                        jsonPatchDocument.Replace((secret) => secret.StringData, secret.StringData);
                        sync = true;
                    }

                    if (sync)
                    {
                        var patchJson = JsonConvert.SerializeObject(jsonPatchDocument, Formatting.Indented);
                        _logger.LogDebug("Patching existing secret {@secret} with new data.", $"{space}/{secret.Metadata?.Name}");
                        await _kubernetes.PatchNamespacedSecretWithHttpMessagesAsync(new V1Patch(patchJson, V1Patch.PatchType.JsonPatch), originalSecret.Metadata?.Name, originalSecret.Metadata?.NamespaceProperty);
                        _logger.LogInformation("Patched secret {@secret} with updated information.", $"{space}/{secret.Metadata?.Name}");
                    }
                    else
                    {
                        _logger.LogInformation("Secret {@secret} is already the same. Ignoring.", $"{space}/{secret.Metadata?.Name}");
                    }

                    return;
                }
            }
            catch (HttpOperationException exception)
            {
                if (exception.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("This is a new secret. Creating.");
                }
                else
                {
                    _logger.LogError(exception, "Unexpected HttpOperationException response from the Kubernetes API while checking to see if the destination secret already existed. {@secret}", $"{space}/{secret.Metadata?.Name}");
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unexpected Exception while checking to see if the destination secret already existed. {@secret}", $"{space}/{secret.Metadata?.Name}");
            }

            var newSecret = new V1Secret()
            {
                ApiVersion = secret.ApiVersion,
                Data = secret.Data,
                Kind = secret.Kind,
                Metadata = new V1ObjectMeta
                {
                    Annotations = secret.Metadata?.Annotations,
                    Labels = secret.Metadata?.Labels,
                    Name = secret.Metadata?.Name,
                    NamespaceProperty = space,
                    OwnerReferences = new List<V1OwnerReference>
                        {
                            new V1OwnerReference
                            {
                                ApiVersion = "veccsolutions.com/v1beta1",
                                Controller = false,
                                Kind = "SharedSecret",
                                Name = sharedSecret.Metadata?.Name,
                                Uid = sharedSecret.Metadata?.Uid
                            }
                        }
                },
                StringData = secret.StringData,
                Type = secret.Type
            };

            try
            {
                await _kubernetes.CreateNamespacedSecretAsync(newSecret, space);
                _logger.LogInformation("New secret {@secret} created.", $"{space}/{secret.Metadata?.Name}");
            }
            catch (HttpOperationException exception)
            {
                if (exception.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Attempted to create a secret in a missing namespace (this can happen many times when a namespace containing a managed secret gets deleted). {@content}", exception.Response.Content);
                    return;
                }
                else
                {
                    _logger.LogError(exception, "Unexpected HttpOperationException response from the Kubernetes API while creating the destination secret. {@secret}", $"{space}/{secret.Metadata?.Name}");
                    return;
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unexpected Exception response from the Kubernetes API while creating the destination secret. {@secret}", $"{space}/{secret.Metadata?.Name}");
            }
        }

        private static bool Compare(IDictionary<string, string>? destination, IDictionary<string, string>? source)
        {
            if ((source == null) != (destination == null))
            {
                //either source or destination is null, but not both
                return false;
            }

            //destination == null is to make the compiler get rid of the warning, since right here if source is null, destination has to be null.
            if (source == null || destination == null)
            {
                return true;
            }

            if (!source.All(s => destination.Any(x => x.Key == s.Key && x.Value == s.Value)))
            {
                return false;
            }

            if (!destination.All(s => source.Any(x => x.Key == s.Key && x.Value == s.Value)))
            {
                return false;
            }

            return true;
        }
    }
}
