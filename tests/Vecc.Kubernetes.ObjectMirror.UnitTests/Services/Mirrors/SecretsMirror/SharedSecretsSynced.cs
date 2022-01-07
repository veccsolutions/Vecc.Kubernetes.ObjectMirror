using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Vecc.Kubernetes.ObjectMirror.Models;
using Vecc.Kubernetes.ObjectMirror.Services;
using Vecc.Kubernetes.ObjectMirror.Services.Mirrors;

namespace Vecc.Kubernetes.ObjectMirror.UnitTests.Services.Mirrors.SecretMirror
{
    [ExcludeFromCodeCoverage]
    public class SharedSecretsSynced
    {
        [Test]
        public async Task MissingSecretCreated()
        {
            var kubernetes = new Mock<IKubernetes>(MockBehavior.Strict);
            var sharedData = new SharedData();
            var logger = new Mock<ILogger<SecretsMirror>>(MockBehavior.Strict);

            var cancellationTokenSource = new CancellationTokenSource();
            var testAllowedNamespace = new V1Namespace { Metadata = new V1ObjectMeta { Name = "test-allowed" } };
            var testBlockedNamespace = new V1Namespace { Metadata = new V1ObjectMeta { Name = "test-blocked" } };
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
            var secretSource = new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-secret",
                    NamespaceProperty = "test-source"
                }
            };
            var secretSourceJson = JsonConvert.SerializeObject(secretSource);

            logger.Setup(l => l.Log(LogLevel.Debug, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.Critical, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Throws<Exception>().Verifiable();
            logger.Setup(l => l.Log(LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.None, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.Trace, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();

            kubernetes.Setup(x => x.ListNamespaceWithHttpMessagesAsync(null, null, null, null, null, null, null, null, null, null, It.IsAny<IDictionary<string, IList<string>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                new HttpOperationResponse<V1NamespaceList>
                {
                    Body = new V1NamespaceList
                    {
                        ApiVersion = "v1",
                        Items = new[] { testAllowedNamespace, testBlockedNamespace }
                    }
                })
                .Verifiable();
            kubernetes.Setup(x => x.ReadNamespacedSecretWithHttpMessagesAsync("test-secret", "test-source", null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new HttpOperationResponse<V1Secret> { Body = secretSource }).Verifiable();
            kubernetes.Setup(x => x.ReadNamespaceWithHttpMessagesAsync("test-allowed", null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new HttpOperationResponse<V1Namespace> { Body = testAllowedNamespace }).Verifiable();
            kubernetes.Setup(x => x.ReadNamespacedSecretWithHttpMessagesAsync("test-secret", "test-allowed", null, null, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpOperationException
                {
                    Response = new HttpResponseMessageWrapper(new HttpResponseMessage { StatusCode = HttpStatusCode.NotFound }, "Not Found")
                }).Verifiable();
            kubernetes.Setup(x => x.CreateNamespacedSecretWithHttpMessagesAsync(It.IsAny<V1Secret>(), "test-allowed", null, null, null, null, null, It.IsAny<CancellationToken>()))
                .Callback((V1Secret secret, string space, string dryRun, string fieldManager, string fieldValidation, bool? pretty, IDictionary<string, IList<string>> customHeaders, CancellationToken cancellationToken) =>
                {
                    Assert.IsNotNull(secret);
                    Assert.AreEqual("test-allowed", space);
                    Assert.AreEqual(secretSource.Metadata.Name, secret.Metadata.Name);
                })
                .ReturnsAsync(new HttpOperationResponse<V1Secret> { Body = secretSource })
                .Verifiable();

            sharedData.SharedSecrets = new List<V1beta1SharedSecret> { sharedSecret };
            sharedData.ResetEvent.Set();
            sharedData.SecretsToSync.Enqueue(new DispatchedEvent<V1beta1SharedSecret> { EventType = WatchEventType.Added, Item = sharedSecret });
            var mirror = new SecretsMirror(logger.Object, kubernetes.Object, sharedData);
            await mirror.StartAsync(cancellationTokenSource.Token);
            var task = new Task(async () =>
            {
                await Task.Delay(5000);
                await mirror.StopAsync(CancellationToken.None);
            });
            task.Start();
            await mirror.ExecuteTask;

            logger.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
            logger.Verify(x => x.Log(LogLevel.Critical, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
            kubernetes.VerifyAll();
        }

        [Test]
        public async Task OutOfSyncSecretUpdated()
        {
            var kubernetes = new Mock<IKubernetes>(MockBehavior.Strict);
            var sharedData = new SharedData();
            var logger = new Mock<ILogger<SecretsMirror>>(MockBehavior.Strict);

            var cancellationTokenSource = new CancellationTokenSource();
            var testAllowedNamespace = new V1Namespace { Metadata = new V1ObjectMeta { Name = "test-allowed" } };
            var testBlockedNamespace = new V1Namespace { Metadata = new V1ObjectMeta { Name = "test-blocked" } };
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
            var secretSource = new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-secret",
                    NamespaceProperty = "test-source"
                },
                Data = new Dictionary<string, byte[]>
                {
                    { "test1", new byte[] { 0, 1, 2, 3 } }
                }
            };
            var syncedSecret = new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-secret",
                    NamespaceProperty = "test-allowed"
                },
                Data = new Dictionary<string, byte[]>
                {
                    { "test1", new byte[] { 0 } }
                }
            };

            logger.Setup(l => l.Log(LogLevel.Debug, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.Critical, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Throws<Exception>().Verifiable();
            logger.Setup(l => l.Log(LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.None, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.Trace, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();

            kubernetes.Setup(x => x.ListNamespaceWithHttpMessagesAsync(null, null, null, null, null, null, null, null, null, null, It.IsAny<IDictionary<string, IList<string>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                new HttpOperationResponse<V1NamespaceList>
                {
                    Body = new V1NamespaceList
                    {
                        ApiVersion = "v1",
                        Items = new[] { testAllowedNamespace, testBlockedNamespace }
                    }
                })
                .Verifiable();
            kubernetes.Setup(x => x.ReadNamespacedSecretWithHttpMessagesAsync("test-secret", "test-source", null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new HttpOperationResponse<V1Secret> { Body = secretSource }).Verifiable();
            kubernetes.Setup(x => x.ReadNamespaceWithHttpMessagesAsync("test-allowed", null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new HttpOperationResponse<V1Namespace> { Body = testAllowedNamespace }).Verifiable();
            kubernetes.Setup(x => x.ReadNamespacedSecretWithHttpMessagesAsync("test-secret", "test-allowed", null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new HttpOperationResponse<V1Secret> { Body = syncedSecret }).Verifiable();
            kubernetes.Setup(x => x.PatchNamespacedSecretWithHttpMessagesAsync(It.IsAny<V1Patch>(), "test-secret", "test-allowed", null, null, null, null, null, null, It.IsAny<CancellationToken>()))
                .Callback((V1Patch patch, string name, string space, string dryRun, string fieldManager, string fieldValidation, bool? force, bool? pretty, IDictionary<string, IList<string>> customHeaders, CancellationToken cancellationToken) =>
                {

                    Assert.IsNotNull(patch);
                    Assert.AreEqual("test-allowed", space);
                    Assert.AreEqual(secretSource.Metadata.Name, name);
                })
                .ReturnsAsync(new HttpOperationResponse<V1Secret> { Body = secretSource })
                .Verifiable();

            sharedData.SharedSecrets = new List<V1beta1SharedSecret> { sharedSecret };
            sharedData.ResetEvent.Set();
            sharedData.SecretsToSync.Enqueue(new DispatchedEvent<V1beta1SharedSecret> { EventType = WatchEventType.Added, Item = sharedSecret });
            var mirror = new SecretsMirror(logger.Object, kubernetes.Object, sharedData);
            await mirror.StartAsync(cancellationTokenSource.Token);
            var task = new Task(async () =>
            {
                await Task.Delay(5000);
                await mirror.StopAsync(CancellationToken.None);
            });
            task.Start();
            await mirror.ExecuteTask;

            logger.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
            logger.Verify(x => x.Log(LogLevel.Critical, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
            kubernetes.VerifyAll();
        }


        [Test]
        public async Task InSyncSecretSkipped()
        {
            var kubernetes = new Mock<IKubernetes>(MockBehavior.Strict);
            var sharedData = new SharedData();
            var logger = new Mock<ILogger<SecretsMirror>>(MockBehavior.Strict);

            var cancellationTokenSource = new CancellationTokenSource();
            var testAllowedNamespace = new V1Namespace { Metadata = new V1ObjectMeta { Name = "test-allowed" } };
            var testBlockedNamespace = new V1Namespace { Metadata = new V1ObjectMeta { Name = "test-blocked" } };
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
            var secretSource = new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-secret",
                    NamespaceProperty = "test-source"
                },
                Data = new Dictionary<string, byte[]>
                {
                    { "test1", new byte[] { 0, 1, 2, 3 } }
                }
            };
            var syncedSecret = new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-secret",
                    NamespaceProperty = "test-allowed"
                },
                Data = new Dictionary<string, byte[]>
                {
                    { "test1", new byte[] { 0, 1, 2, 3 } }
                }
            };

            logger.Setup(l => l.Log(LogLevel.Debug, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.Critical, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Throws<Exception>().Verifiable();
            logger.Setup(l => l.Log(LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.None, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.Trace, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();
            logger.Setup(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>())).Verifiable();

            kubernetes.Setup(x => x.ListNamespaceWithHttpMessagesAsync(null, null, null, null, null, null, null, null, null, null, It.IsAny<IDictionary<string, IList<string>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                new HttpOperationResponse<V1NamespaceList>
                {
                    Body = new V1NamespaceList
                    {
                        ApiVersion = "v1",
                        Items = new[] { testAllowedNamespace, testBlockedNamespace }
                    }
                })
                .Verifiable();
            kubernetes.Setup(x => x.ReadNamespacedSecretWithHttpMessagesAsync("test-secret", "test-source", null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new HttpOperationResponse<V1Secret> { Body = secretSource }).Verifiable();
            kubernetes.Setup(x => x.ReadNamespaceWithHttpMessagesAsync("test-allowed", null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new HttpOperationResponse<V1Namespace> { Body = testAllowedNamespace }).Verifiable();
            kubernetes.Setup(x => x.ReadNamespacedSecretWithHttpMessagesAsync("test-secret", "test-allowed", null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new HttpOperationResponse<V1Secret> { Body = syncedSecret }).Verifiable();

            sharedData.SharedSecrets = new List<V1beta1SharedSecret> { sharedSecret };
            sharedData.ResetEvent.Set();
            sharedData.SecretsToSync.Enqueue(new DispatchedEvent<V1beta1SharedSecret> { EventType = WatchEventType.Added, Item = sharedSecret });
            var mirror = new SecretsMirror(logger.Object, kubernetes.Object, sharedData);
            await mirror.StartAsync(cancellationTokenSource.Token);
            var task = new Task(async () =>
            {
                await Task.Delay(5000);
                await mirror.StopAsync(CancellationToken.None);
            });
            task.Start();
            await mirror.ExecuteTask;

            logger.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
            logger.Verify(x => x.Log(LogLevel.Critical, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
            kubernetes.VerifyAll();
        }

    }
}