#pragma warning disable CS8602 // Dereference of a possibly null reference.
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Vecc.Kubernetes.ObjectMirror.Models;
using Vecc.Kubernetes.ObjectMirror.Services;
using L = Vecc.Kubernetes.ObjectMirror.Services.Listeners;

namespace Vecc.Kubernetes.ObjectMirror.UnitTests.Services.Listeners.SecretsListener
{
    [ExcludeFromCodeCoverage]
    public class QueuedSecretsToSync
    {
        [Test]
        public async Task NewSecretsGetSynced()
        {
            var logger = new Mock<ILogger<L.SecretListener>>();
            var dispatcher = new Dispatcher<V1Secret>();
            var sharedData = new SharedData();
            var sharedSecret = new V1beta1SharedSecret
            {
                ApiVersion = "v1",
                Kind = "SharedSecret",
                Metadata = new V1ObjectMeta
                {
                    Name = "shared-found",
                },
                Spec = new V1beta1SharedSecretSpec
                {
                    Source = new V1beta1SharedSecretSource
                    {
                        Name = "test-secret",
                        Namespace = "test-source"
                    },
                    Target = new V1beta1SharedSecretTarget
                    {
                        AllowedNamespaces = (new List<string> { "^test-.*" }).ToArray(),
                        BlockedNamespaces = (new List<string> { "^test-b.*" }).ToArray()
                    }
                }
            };
            sharedData.SharedSecrets = new List<V1beta1SharedSecret> { sharedSecret };
            var addedSecret = new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-secret",
                    NamespaceProperty = "test-added"
                }
            };
            var modifiedSecret = new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-secret",
                    NamespaceProperty = "test-modified"
                }
            };
            var deletedSecret = new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-secret",
                    NamespaceProperty = "test-deleted"
                }
            };
            var deletedSourceSecret = new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-secret",
                    NamespaceProperty = "test-source"
                }
            };
            var blockedSecret = new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-secret",
                    NamespaceProperty = "test-blocked"
                }
            };

            dispatcher.Dispatch(addedSecret, WatchEventType.Added);
            dispatcher.Dispatch(modifiedSecret, WatchEventType.Modified);
            dispatcher.Dispatch(deletedSecret, WatchEventType.Deleted);
            dispatcher.Dispatch(deletedSourceSecret, WatchEventType.Deleted);
            dispatcher.Dispatch(blockedSecret, WatchEventType.Modified);

            var listener = new L.SecretListener(logger.Object, dispatcher, sharedData);
            await listener.StartAsync(CancellationToken.None);
            var task = new Task(async () =>
            {
                await Task.Delay(1000);
                await listener.StopAsync(CancellationToken.None);
            });
            task.Start();
            await listener.ExecuteTask;

            Assert.AreEqual(3, sharedData.KnownSecrets.Count);
            Assert.Contains("test-added/test-secret", sharedData.KnownSecrets);
            Assert.Contains("test-modified/test-secret", sharedData.KnownSecrets);
            Assert.Contains("test-blocked/test-secret", sharedData.KnownSecrets);
            Assert.AreEqual(2, sharedData.NamespacesToSync.Count);
            sharedData.NamespacesToSync.TryDequeue(out var first);
            Assert.AreEqual("test-modified", first.Namespace);
            sharedData.NamespacesToSync.TryDequeue(out var second);
            Assert.AreEqual("test-deleted", second.Namespace);
        }
    }
}
