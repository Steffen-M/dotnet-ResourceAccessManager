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
    public sealed class ResourceAccessManager
    {
        /// <summary>
        /// Gets the default manager.
        /// </summary>
        public static ResourceAccessManager Default { get; } = new ResourceAccessManager();

        /// <summary>
        /// Creates an instance.
        /// </summary>
        public ResourceAccessManager()
        {
            _resources = new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Locks access to the requested named resource.
        /// </summary>
        /// <param name="name">Name of the resource to get exclusive access</param>
        /// <param name="cancellationToken">Cancel waiting from the outside</param>
        /// <returns>Disposable handle to release the exclusive access.</returns>
        /// <example><code>
        ///     using (await ResourceAccessManager.Default.LockAsync(fileName, CancellationToken.None))
        ///     {
        ///         ...
        ///     }
        /// </code></example>
        public async Task<IDisposable> LockAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
#if DEBUG
            // prevent unwanted cancellation while debugging
            if (Debugger.IsAttached)
                cancellationToken = default(CancellationToken);
#endif
            AwaitableLock awaitableLock;
            lock (_resources)
            {
                Resource resource;
                if (!_resources.TryGetValue(name, out resource))
                {
                    resource = new Resource(name, resourceToDelete =>
                    {
                        lock (_resources)
                        {
                            // only remove from _resources when evident, that it is no longer in use
                            if (resourceToDelete.RefCount == 0)
                                _resources.Remove(resourceToDelete.Name);
                        }
                    });
                    _resources.Add(name, resource);
                }
                awaitableLock = new AwaitableLock(resource);
            }

            try
            {
                await awaitableLock.WaitAsync(cancellationToken);
                return awaitableLock;
            }
            catch
            {
                awaitableLock.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Locks access to the requested named resource.
        /// </summary>
        /// <param name="name">Name of the resource to get exclusive access</param>
        /// <param name="timeout">Cancel waiting after this timeout</param>
        /// <returns>Disposable handle to release the exclusive access.</returns>
        /// <example><code>
        ///     using (await ResourceAccessManager.Default.LockAsync(fileName, TimeSpan.FromSeconds(5)))
        ///     {
        ///         ...
        ///     }
        /// </code></example>
        public Task<IDisposable> LockAsync(string name, TimeSpan timeout)
        {            
            return LockAsync(name, new CancellationTokenSource(timeout).Token);
        }

        private class AwaitableLock : IDisposable
        {
            public AwaitableLock(Resource resource)
            {
                _resource = resource;
                _resource.AddRef();
            }

            public async Task WaitAsync(CancellationToken cancellationToken)
            {
                await _resource.WaitAsync(cancellationToken);
                _aquired += 1;
            }

            public void Dispose()
            {
                _resource.Release(_aquired);
            }

            private readonly Resource _resource;
            private int _aquired;
        }

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

            public void Release(int aquired)
            {
                if (aquired > 0)
                    _semaphore.Release(aquired);

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
