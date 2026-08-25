using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Polaris.Map.Internal
{
    internal static class MapPreviewQueue
    {
        sealed class Request
        {
            internal uint[] ImageIds;
            internal readonly ManualResetEventSlim Done = new(false);
            internal bool Ok;
            internal string Message;
        }

        static readonly ConcurrentQueue<Request> Requests = new();

        internal static (bool ok, string message) Enqueue(uint[] imageIds, TimeSpan timeout)
        {
            var request = new Request { ImageIds = imageIds };
            Requests.Enqueue(request);
            return request.Done.Wait(timeout)
                ? (request.Ok, request.Message)
                : (false, "Timed out waiting for the game main thread to prepare preview assets.");
        }

        internal static void Drain()
        {
            while (Requests.TryDequeue(out Request request))
            {
                try
                {
                    request.Message = request.ImageIds != null
                        ? MapPreviewExtractor.Extract(request.ImageIds)
                        : MapPreviewExtractor.Clear();
                    request.Ok = true;
                }
                catch (Exception ex)
                {
                    request.Ok = false;
                    request.Message = ex.Message;
                    PolarisAPI.Errors.Report(ex, "PolarisMap preview extraction");
                }
                finally { request.Done.Set(); }
            }
        }

        internal static void CancelAll(string reason)
        {
            while (Requests.TryDequeue(out Request request))
            {
                request.Ok = false;
                request.Message = reason;
                request.Done.Set();
            }
        }
    }
}
