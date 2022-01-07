#pragma warning disable CS8602 // Dereference of a possibly null reference.
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vecc.Kubernetes.ObjectMirror.Models;
using Vecc.Kubernetes.ObjectMirror.Services;
using L = Vecc.Kubernetes.ObjectMirror.Services.Listeners;

namespace Vecc.Kubernetes.ObjectMirror.UnitTests.Services.Listeners.NamespaceListener
{
    [ExcludeFromCodeCoverage]
    public class QueuesNamespacesToSync
    {
        [Test]
        public async Task NewAllowedOnlyNamespacesAreQueued()
        {
            var logger = new Mock<ILogger<L.NamespaceListener>>();
            var dispatcher = new Dispatcher<V1Namespace>();
            var sharedData = new SharedData();
            var namespaces = new[] { "test-allowed", "test-allowed", "test-blocked" }.Select(x => new V1Namespace { Metadata = new V1ObjectMeta { Name = x } }).ToList();
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

            foreach (var space in namespaces)
            {
                dispatcher.Dispatch(space, WatchEventType.Added);
            }

            var listener = new L.NamespaceListener(logger.Object, dispatcher, sharedData);
            await listener.StartAsync(CancellationToken.None);
            var task = new Task(async () =>
            {
                await Task.Delay(1000);
                await listener.StopAsync(CancellationToken.None);
            });
            task.Start();
            await listener.ExecuteTask;
            Assert.AreEqual(0, dispatcher.QueueCount);
            Assert.AreEqual(1, sharedData.NamespacesToSync.Count);
            sharedData.NamespacesToSync.TryDequeue(out var item);
            Assert.IsNotNull(item);
            Assert.AreEqual("test-allowed", item.Namespace);
        }

        [Test]
        public async Task DeletedNamespacesAreRemovedFromCache()
        {
            var logger = new Mock<ILogger<L.NamespaceListener>>();
            var dispatcher = new Dispatcher<V1Namespace>();
            var sharedData = new SharedData();
            var namespaces = new[] { "test-allowed" }.Select(x => new V1Namespace { Metadata = new V1ObjectMeta { Name = x } }).ToList();
            sharedData.KnownNamespaces = new List<string> { "test-allowed" };

            foreach (var space in namespaces)
            {
                dispatcher.Dispatch(space, WatchEventType.Deleted);
            }

            var listener = new L.NamespaceListener(logger.Object, dispatcher, sharedData);
            await listener.StartAsync(CancellationToken.None);
            var task = new Task(async () =>
            {
                await Task.Delay(1000);
                await listener.StopAsync(CancellationToken.None);
            });
            task.Start();
            await listener.ExecuteTask;
            Assert.AreEqual(0, sharedData.KnownNamespaces.Count);
        }

    }
}
