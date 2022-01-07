#pragma warning disable CS8602 // Dereference of a possibly null reference.
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Vecc.Kubernetes.ObjectMirror.Models;
using Vecc.Kubernetes.ObjectMirror.Services;
using L = Vecc.Kubernetes.ObjectMirror.Services.Listeners;

namespace Vecc.Kubernetes.ObjectMirror.UnitTests.Services.Listeners.SharedSecretListener
{
    [ExcludeFromCodeCoverage]
    internal class QueuedSharedSecretsToSync
    {
        [Test]
        public async Task NewSharedSecretsGetSynced()
        {
            var logger = new Mock<ILogger<L.SharedSecretListener>>();
            var dispatcher = new Dispatcher<V1beta1SharedSecret>();
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

            var listener = new L.SharedSecretListener(logger.Object, dispatcher, sharedData);
            await listener.StartAsync(CancellationToken.None);
            Exception? exception = null;
            var runner = new Task(async () =>
            {
                try
                {
                    await Task.Delay(100);
                    dispatcher.Dispatch(sharedSecret, WatchEventType.Added);
                    await Task.Delay(100);
                    Assert.AreEqual(1, sharedData.SharedSecrets.Count, "Added - SharedSecretsCount != 1");
                    Assert.AreEqual("shared-found", sharedData.SharedSecrets[0].Metadata.Name, "Added - SharedSecrets Wrong name");
                    Assert.AreEqual(1, sharedData.SecretsToSync.Count, "Added - SecretsToSyncCount != 1");
                    Assert.True(sharedData.SecretsToSync.TryDequeue(out var secret), "Added - Unable to dequeue");
                    Assert.AreEqual("shared-found", secret.Item.Metadata.Name, "Added - Queued Item wrong name");

                    dispatcher.Dispatch(sharedSecret, WatchEventType.Deleted);
                    await Task.Delay(100);
                    Assert.AreEqual(0, sharedData.SharedSecrets.Count, "Deleted - SharedSecretsCount != 0");
                    Assert.AreEqual(0, sharedData.SecretsToSync.Count, "Deleted - SecretsToSync != 0");

                    dispatcher.Dispatch(sharedSecret, WatchEventType.Modified);
                    await Task.Delay(100);
                    Assert.AreEqual(1, sharedData.SharedSecrets.Count, "Modified - SharedSecretsCount != 1");
                    Assert.AreEqual("shared-found", sharedData.SharedSecrets[0].Metadata.Name, "Modified - SharedSecrets Wrong name");
                    Assert.AreEqual(1, sharedData.SecretsToSync.Count, "Modified - SecretsToSyncCount != 1");
                    secret = null;
                    Assert.True(sharedData.SecretsToSync.TryDequeue(out secret), "Modified - Unable to dequeue");
                    Assert.AreEqual("shared-found", secret.Item.Metadata.Name, "Modified - Queued Item wrong name");
                }
                catch(Exception ex)
                {
                    exception = ex;
                }
                await listener.StopAsync(CancellationToken.None);
            });
            runner.Start();
            await listener.ExecuteTask;
            if (exception != null)
            {
                throw exception;
            }
        }
    }
}
