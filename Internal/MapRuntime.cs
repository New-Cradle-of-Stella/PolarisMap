using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using m2d;
using Polaris.Map.Authoring;
using Polaris.Map.Debugging;

namespace Polaris.Map.Internal
{
    internal static class MapRuntime
    {
        sealed class PendingTransition
        {
            internal PendingTransition(MapTransition transition, M2DBase owner, Map2d target)
            {
                Transition = transition;
                Owner = owner;
                Target = target;
            }

            internal MapTransition Transition { get; }
            internal M2DBase Owner { get; }
            internal Map2d Target { get; }
        }

        sealed class OwnedMap
        {
            internal Assembly Owner;
            internal string SourceXml;
            internal PmapDocument Document;
        }

        sealed class HotReloadRequest
        {
            internal string Key;
            internal string Xml;
            internal readonly ManualResetEventSlim Done = new(false);
            internal bool Ok;
            internal string Error;
        }

        static readonly List<PendingTransition> Pending = new();
        static readonly Dictionary<string, OwnedMap> Owned = new(StringComparer.Ordinal);
        static readonly ConcurrentQueue<HotReloadRequest> HotReloadQueue = new();
        static int mainThreadId;
        static string lastActivity = "No .pmap activity in this session.";

        internal static void Initialize() => mainThreadId = Thread.CurrentThread.ManagedThreadId;

        internal static LiveMap GetCurrent()
        {
            try
            {
                Map2d map = M2DBase.Instance?.curMap;
                return map == null ? null : new LiveMap(map);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static MapTransition CreateAndEnter(MapDraft draft)
        {
            EnsureMainThread();
            M2DBase m2d = M2DBase.Instance;
            if (m2d == null)
            {
                throw new InvalidOperationException("Enter the game world before creating and entering a map.");
            }

            byte[] body = TmapWriter.Build(draft, m2d);
            var maps = m2d.getMapObject();
            if (maps.Get(draft.Key) != null)
            {
                throw new InvalidOperationException($"Map key is already registered in this game session: {draft.Key}.");
            }

            string path = MapPersistence.PersistNew(draft.Key, body);
            var target = new Map2d(m2d, draft.Key, false);
            try
            {
                maps.Add(draft.Key, target);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"The map was persisted, but could not be registered in this game session: {draft.Key}.", ex);
            }
            var transition = new MapTransition(draft.Key, path);

            try
            {
                m2d.initMapMaterialASync(target, 2, false);
                Pending.Add(new PendingTransition(transition, m2d, target));
            }
            catch (Exception ex)
            {
                maps.Remove(draft.Key);
                transition.Fail(ex);
                throw new InvalidOperationException(
                    $"The map was persisted, but the game refused to start loading it: {draft.Key}.", ex);
            }

            return transition;
        }

        internal static MapTransition LoadAndEnterPmap(PmapDocument document, Type ownerType, string sourceXml)
        {
            EnsureMainThread();
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (ownerType == null) throw new ArgumentNullException(nameof(ownerType));

            Assembly owner = ownerType.Assembly;
            if (Owned.TryGetValue(document.Key, out OwnedMap existingOwner)
                && existingOwner.Owner != owner)
            {
                throw new InvalidOperationException(
                    $"The .pmap key '{document.Key}' is already owned by {existingOwner.Owner.GetName().Name}.");
            }

            MapTransition transition = InstallOwnedAndEnter(document);
            Owned[document.Key] = new OwnedMap
            {
                Owner = owner,
                SourceXml = sourceXml ?? document.ToXml(),
                Document = PmapDocument.Parse(sourceXml ?? document.ToXml(), document.Key + ".pmap"),
            };
            lastActivity = $"Loading {document.Key} from .pmap.";

            if (IsHotReloadEnabled(owner))
                PmapHotReloadServer.Start();
            return transition;
        }

        static MapTransition InstallOwnedAndEnter(PmapDocument document)
        {
            M2DBase m2d = M2DBase.Instance;
            if (m2d == null)
                throw new InvalidOperationException("Enter the game world before loading a .pmap.");
            if (Pending.Any(item => item.Transition.TargetKey == document.Key))
                throw new InvalidOperationException($"Map '{document.Key}' is already being loaded.");

            MapDraft draft = PmapCompiler.Compile(document);
            byte[] body = TmapWriter.Build(draft, m2d);
            string path = MapPersistence.PersistOwned(document.Key, body);
            var maps = m2d.getMapObject();
            Map2d old = maps.Get(document.Key);

            // Full reload avoids retaining runtime pools and LP state from the old Map2d.
            if (old != null)
            {
                if (ReferenceEquals(m2d.curMap, old))
                    m2d.changeMap(null);
                old.close(false, true);
                maps.Remove(document.Key);
            }

            var target = new Map2d(m2d, document.Key, false);
            maps.Add(document.Key, target);
            var transition = new MapTransition(document.Key, path);
            try
            {
                m2d.initMapMaterialASync(target, 2, false);
                Pending.Add(new PendingTransition(transition, m2d, target));
                return transition;
            }
            catch (Exception ex)
            {
                maps.Remove(document.Key);
                transition.Fail(ex);
                throw new InvalidOperationException($"The game refused to reload .pmap '{document.Key}'.", ex);
            }
        }

        internal static (bool ok, string error) EnqueueHotReload(string key, string xml, TimeSpan timeout)
        {
            var request = new HotReloadRequest { Key = key, Xml = xml };
            HotReloadQueue.Enqueue(request);
            return request.Done.Wait(timeout)
                ? (request.Ok, request.Error)
                : (false, "Timed out waiting for the game main thread to start the full map reload.");
        }

        internal static void Update()
        {
            DrainHotReloadQueue();
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                PendingTransition item = Pending[i];
                try
                {
                    if (!ReferenceEquals(M2DBase.Instance, item.Owner))
                    {
                        Fail(i, new InvalidOperationException("The game world was unloaded during the map transition."));
                        continue;
                    }

                    if (!ReferenceEquals(item.Owner.curMap, item.Target))
                    {
                        if (item.Owner.getLoaderTargetMap() == null)
                        {
                            Fail(i, new InvalidOperationException(
                                $"The game stopped loading the target map before entering it: {item.Transition.TargetKey}."));
                        }
                        continue;
                    }

                    // Reposition the evacuated player using the game's transfer fallback.
                    item.Target.getKeyPr()?.setToDefaultPosition(false, item.Target);
                    item.Transition.Complete();
                    lastActivity = $"Entered {item.Transition.TargetKey}; full map instance is ready.";
                    Pending.RemoveAt(i);
                }
                catch (Exception ex)
                {
                    Fail(i, ex);
                    PolarisAPI.Errors.Report(ex, "PolarisMap transition completion");
                }
            }
        }

        internal static void Shutdown()
        {
            PmapHotReloadServer.Stop();
            var error = new InvalidOperationException("PolarisMap shut down before the map transition completed.");
            foreach (PendingTransition item in Pending)
            {
                item.Transition.Fail(error);
            }
            Pending.Clear();
            Owned.Clear();
            lastActivity = "PolarisMap is shut down.";
            while (HotReloadQueue.TryDequeue(out HotReloadRequest request))
            {
                request.Ok = false;
                request.Error = "PolarisMap shut down before the hot reload was processed.";
                request.Done.Set();
            }
            mainThreadId = 0;
        }

        internal static void EnsureMainThread()
        {
            int current = Thread.CurrentThread.ManagedThreadId;
            if (mainThreadId == 0)
            {
                mainThreadId = current;
            }
            if (mainThreadId != current)
            {
                throw new InvalidOperationException("PolarisMap mutations must run on the Unity main thread.");
            }
        }

        static void Fail(int index, Exception error)
        {
            PendingTransition item = Pending[index];
            item.Transition.Fail(error);
            lastActivity = $"Loading {item.Transition.TargetKey} failed: {error.Message}";
            Pending.RemoveAt(index);
        }

        static void DrainHotReloadQueue()
        {
            while (HotReloadQueue.TryDequeue(out HotReloadRequest request))
            {
                try
                {
                    if (!Owned.TryGetValue(request.Key, out OwnedMap owned))
                        throw new InvalidOperationException($".pmap is not loaded through PolarisMap: {request.Key}.");
                    if (!IsHotReloadEnabled(owned.Owner))
                        throw new InvalidOperationException(
                            $"The plugin owning '{request.Key}' has not enabled PMapHotFixEnabled.");

                    PmapDocument document = PmapDocument.Parse(request.Xml, request.Key + ".pmap hot reload");
                    if (!string.Equals(document.Key, request.Key, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Hot reload frame key '{request.Key}' does not match XML key '{document.Key}'.");

                    InstallOwnedAndEnter(document);
                    owned.SourceXml = request.Xml;
                    owned.Document = document;
                    lastActivity = $"Full hot reload started for {request.Key}.";
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

        static bool IsHotReloadEnabled(Assembly assembly)
            => PolarisAPI.Types.Of(assembly)
                .Any(type => type.GetCustomAttribute<PMapHotFixEnabledAttribute>() != null);

        internal static bool HasHotReloadMarker(Assembly assembly)
            => assembly != null && IsHotReloadEnabled(assembly);

        internal static MapDebugSnapshot GetDebugSnapshot()
        {
            EnsureMainThread();
            string current = M2DBase.Instance?.curMap?.key;
            MapDebugEntry[] maps = Owned
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new MapDebugEntry
                {
                    Key = pair.Key,
                    Owner = pair.Value.Owner.GetName().Name,
                    Xml = pair.Value.SourceXml,
                    Document = pair.Value.Document,
                    IsCurrent = string.Equals(pair.Key, current, StringComparison.Ordinal),
                    IsLoading = Pending.Any(item => item.Transition.TargetKey == pair.Key),
                })
                .ToArray();
            return new MapDebugSnapshot
            {
                CurrentKey = current,
                Activity = lastActivity,
                CapturedAt = DateTime.Now,
                Maps = maps,
            };
        }

        internal static MapTransition DebugReload(string key)
        {
            EnsureMainThread();
            if (!Owned.TryGetValue(key, out OwnedMap owned))
                throw new InvalidOperationException($".pmap is not loaded through PolarisMap: {key}.");
            if (!IsHotReloadEnabled(owned.Owner))
                throw new InvalidOperationException($"The plugin owning '{key}' has not enabled PMapHotFixEnabled.");

            MapTransition transition = InstallOwnedAndEnter(owned.Document);
            lastActivity = $"F11 requested a full reload for {key}.";
            return transition;
        }
    }
}
