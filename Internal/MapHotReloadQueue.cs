using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Polaris.Map.Internal
{
    /// <summary>
    /// 把后台线程（调试管道）送来的 .pmap 热重载请求排给 Unity 主线程，并把结果同步回等待中的调用方。
    /// 这里只管排队和等待，怎么套用一帧热重载由 <see cref="Drain"/> 的回调决定。
    /// </summary>
    internal static class MapHotReloadQueue
    {
        sealed class Request
        {
            internal Request(string key, string xml)
            {
                Key = key;
                Xml = xml;
            }

            internal string Key { get; }
            internal string Xml { get; }
            internal ManualResetEventSlim Done { get; } = new(false);
            internal bool Ok;
            internal string Error;
        }

        static readonly ConcurrentQueue<Request> Requests = new();

        /// <summary>由后台线程调用：入队后阻塞等待主线程给出结果。</summary>
        internal static (bool ok, string error) Enqueue(string key, string xml, TimeSpan timeout)
        {
            var request = new Request(key, xml);
            Requests.Enqueue(request);
            return request.Done.Wait(timeout)
                ? (request.Ok, request.Error)
                : (false, "Timed out waiting for the game main thread to start the full map reload.");
        }

        /// <summary>由主线程每帧调用。</summary>
        /// <param name="apply">套用一帧热重载（key, xml）；抛出异常即视为该请求失败，异常消息回传给调用方。</param>
        internal static void Drain(Action<string, string> apply)
        {
            while (Requests.TryDequeue(out Request request))
            {
                try
                {
                    apply(request.Key, request.Xml);
                    request.Ok = true;
                    request.Error = "";
                }
                catch (Exception ex)
                {
                    request.Ok = false;
                    request.Error = ex.Message;
                    PolarisAPI.Errors.Report(ex, "PolarisMap full hot reload");
                }
                finally
                {
                    request.Done.Set();
                }
            }
        }

        /// <summary>关停时唤醒所有仍在等待的调用方。</summary>
        internal static void CancelAll(string reason)
        {
            while (Requests.TryDequeue(out Request request))
            {
                request.Ok = false;
                request.Error = reason;
                request.Done.Set();
            }
        }
    }
}
