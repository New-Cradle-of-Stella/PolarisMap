using System;

namespace Polaris.Map
{
    public enum MapTransitionStatus
    {
        Pending,
        Completed,
        Failed,
    }

    /// <summary>创建、写盘和切换新地图这一操作的可观察句柄。</summary>
    public sealed class MapTransition
    {
        internal MapTransition(string targetKey, string filePath)
        {
            TargetKey = targetKey;
            FilePath = filePath;
        }

        public string TargetKey { get; }
        public string FilePath { get; }
        public MapTransitionStatus Status { get; private set; }
        public Exception Error { get; private set; }
        public bool IsFinished => Status != MapTransitionStatus.Pending;

        /// <summary>成功或失败时触发一次；回调在 Unity 主线程的 PolarisMap Update 中执行。</summary>
        public event Action<MapTransition> Finished;

        internal void Complete()
        {
            if (IsFinished)
            {
                return;
            }

            Status = MapTransitionStatus.Completed;
            NotifyFinished();
        }

        internal void Fail(Exception error)
        {
            if (IsFinished)
            {
                return;
            }

            Error = error;
            Status = MapTransitionStatus.Failed;
            NotifyFinished();
        }

        void NotifyFinished()
        {
            Action<MapTransition> handlers = Finished;
            if (handlers == null)
            {
                return;
            }

            foreach (Action<MapTransition> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this);
                }
                catch (Exception ex)
                {
                    PolarisAPI.Errors.Report(ex, "PolarisMap transition callback", handler.Method?.DeclaringType?.Assembly);
                }
            }
        }
    }
}
