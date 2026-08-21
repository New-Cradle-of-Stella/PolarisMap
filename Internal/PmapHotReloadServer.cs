using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Polaris.Map.HotReload;

namespace Polaris.Map.Internal
{
    /// <summary>游戏侧 .pmap 调试管道；解析和地图操作均交给主线程的 MapRuntime。</summary>
    internal static class PmapHotReloadServer
    {
        static Thread thread;
        static volatile bool running;

        internal static void Start()
        {
            if (thread != null) return;
            running = true;
            thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "Polaris.PMap.HotReloadServer",
            };
            thread.Start();
        }

        internal static void Stop()
        {
            if (thread == null) return;
            running = false;
            try
            {
                using (var dummy = new NamedPipeClientStream(".", PmapWireProtocol.PipeName, PipeDirection.InOut))
                    dummy.Connect(200);
            }
            catch { }
            thread.Join(TimeSpan.FromSeconds(2));
            thread = null;
        }

        static void Loop()
        {
            while (running)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(
                        PmapWireProtocol.PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.None))
                    {
                        pipe.WaitForConnection();
                        if (!running) break;
                        Handle(pipe);
                    }
                }
                catch (Exception ex)
                {
                    if (!running) break;
                    PolarisAPI.Errors.Report(ex, "PolarisMap hot reload pipe");
                }
            }
        }

        static void Handle(NamedPipeServerStream pipe)
        {
            string key;
            string xml;
            using (var reader = new BinaryReader(pipe, Encoding.UTF8, true))
            {
                int version = reader.ReadInt32();
                if (version != PmapWireProtocol.Version)
                    throw new InvalidDataException($"Unsupported .pmap hot reload protocol {version}.");
                key = reader.ReadString();
                int length = reader.ReadInt32();
                if (length < 0 || length > PmapWireProtocol.MaxDocumentBytes)
                    throw new InvalidDataException("The .pmap hot reload document is too large.");
                byte[] bytes = reader.ReadBytes(length);
                if (bytes.Length != length) throw new EndOfStreamException("Incomplete .pmap hot reload frame.");
                xml = new UTF8Encoding(false, true).GetString(bytes);
            }

            (bool ok, string error) = MapRuntime.EnqueueHotReload(key, xml, TimeSpan.FromSeconds(8));
            using (var writer = new BinaryWriter(pipe, Encoding.UTF8, true))
            {
                writer.Write(ok);
                writer.Write(error ?? "");
                writer.Flush();
            }
        }
    }
}
