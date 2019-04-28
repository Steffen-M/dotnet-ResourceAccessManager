using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Foundation.Threading.Tasks
{
    /// <summary>
    /// Manages exclusive access to named resources in a TPL friendly manner.
    /// </summary>
    /// <remarks>
    /// Nested calls to <see cref="AcquireExclusiveAccessAsync(string, CancellationToken)"/>
    /// for the same resource are not supported by design.
    /// </remarks>
    public sealed class ResourceAccessManager
    {
        /// <summary>
        /// Gets the default manager.
        /// </summary>
        public static ResourceAccessManager Default { get; } = new ResourceAccessManager();

        /// <summary>
        /// Creates an instance of a <see cref="ResourceAccessManager"/>.
        /// </summary>
        public ResourceAccessManager()
        {
            _resources = new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Acquires access to the requested named resource.
        /// </summary>
        /// <param name="name">Name of the resource to get exclusive access to.</param>
        /// <param name="cancellationToken">Cancel waiting from the outside.</param>
        /// <returns>Disposable handle to release the exclusive access.</returns>
        /// <exception cref="System.OperationCanceledException">When the <paramref name="cancellationToken"/> indicates cancellation.</exception>
        /// <remarks>
        /// Consider setting a timeout on the <see cref="System.Threading.CancellationTokenSource"/> of <paramref name="cancellationToken"/> to prevent deadlocks.
        /// </remarks>
        /// <example><code>
        ///     using (await ResourceAccessManager.Default.AcquiresExclusiveAccessAsync(fileName, CancellationToken.None))
        ///     {
        ///         ...
        ///     }
        /// </code></example>
        public async Task<IDisposable> AcquireExclusiveAccessAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
#if DEBUG
            // prevent unwanted cancellation while debugging
            if (Debugger.IsAttached)
                cancellationToken = default(CancellationToken);
#endif
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            AccessHandle accessHandle;
            lock (_resources)
            {
                Resource resource;
                if (!_resources.TryGetValue(name, out resource))
                {
                    resource = new Resource(name, resourceToDelete =>
                    {
                        lock (_resources)
                        {
                            // only remove from _resources when evident, that it is still no longer in use
                            if (resourceToDelete.RefCount == 0)
                            {
                                _resources.Remove(resourceToDelete.Name);
                                resourceToDelete.Dispose();
                            }
                        }
                    });
                    _resources.Add(name, resource);
                }
                accessHandle = new AccessHandle(resource);
            }

            try
            {
                await accessHandle.WaitAsync(cancellationToken);
                return accessHandle;
            }
            catch
            {
                accessHandle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Acquires access to the requested named resource.
        /// </summary>
        /// <param name="name">Name of the resource to get exclusive access to.</param>
        /// <param name="timeout">Cancel waiting after this timeout.</param>
        /// <returns>Disposable handle to release the exclusive access.</returns>
        /// <exception cref="System.OperationCanceledException">When the timeout expired</exception>
        /// <example><code>
        ///     using (await ResourceAccessManager.Default.AcquiresExclusiveAccessAsync(fileName, TimeSpan.FromSeconds(5)))
        ///     {
        ///         ...
        ///     }
        /// </code></example>
        public Task<IDisposable> AcquireExclusiveAccessAsync(string name, TimeSpan timeout)
        {
            return AcquireExclusiveAccessAsync(name, new CancellationTokenSource(timeout).Token);
        }

        /// <summary>
        /// Handles waiting for a <see cref="Resource"/>.
        /// </summary>
        private class AccessHandle : IDisposable
        {
            public AccessHandle(Resource resource)
            {
                _resource = resource;
                _resource.AddRef();
            }

            public async Task WaitAsync(CancellationToken cancellationToken)
            {
                await _resource.WaitAsync(cancellationToken);
                _acquired += 1;
            }

            public void Dispose()
            {
                // ensure the resource is only released once
                var resource = _resource;
                _resource = null;
                resource?.Release(_acquired);
            }

            private Resource _resource;
            private int _acquired;
        }

        /// <summary>
        /// Encapsulates a <see cref="System.Threading.SemaphoreSlim"/>.
        /// </summary>
        private class Resource : IDisposable
        {
            public string Name { get; }
            public int RefCount => _refCount;

            public Resource(string name, Action<Resource> release)
            {
                Name = name;
                _release = release;
                _semaphore = new SemaphoreSlim(1);
            }

            public void AddRef()
            {
                Interlocked.Increment(ref _refCount);
            }

            public Task WaitAsync(CancellationToken cancellationToken)
            {
                return _semaphore.WaitAsync(cancellationToken);
            }

            public void Release(int acquired)
            {
                if (acquired > 0)
                    _semaphore.Release(acquired);

                if (Interlocked.Decrement(ref _refCount) == 0)
                    _release(this);
            }

            public void Dispose()
            {
                _semaphore.Dispose();
            }

            private readonly SemaphoreSlim _semaphore;
            private int _refCount;
            private Action<Resource> _release;
        }

        private readonly Dictionary<string, Resource> _resources;
    }
}
