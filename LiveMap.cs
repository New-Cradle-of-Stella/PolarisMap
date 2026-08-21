using System;
using System.Collections.Generic;
using m2d;
using Polaris.Map.Internal;

namespace Polaris.Map
{
    /// <summary>当前地图的实时视图；切图后失效。</summary>
    public sealed class LiveMap
    {
        readonly Map2d map;

        internal LiveMap(Map2d map) => this.map = map;

        public bool IsValid
        {
            get
            {
                try
                {
                    return map != null && !map.closed && ReferenceEquals(M2DBase.Instance?.curMap, map);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public string Key => Read(static value => value.key, null, requireValid: false);
        public int Width => Read(static value => value.clms, 0);
        public int Height => Read(static value => value.rows, 0);

        /// <summary>枚举当前所有 CP/PIC。返回的是调用时的快照。</summary>
        public IReadOnlyList<LiveMapElement> GetElements(string layerName = null)
        {
            EnsureUsable();
            var result = new List<LiveMapElement>();

            if (!string.IsNullOrEmpty(layerName))
            {
                M2MapLayer layer = map.getLayer(layerName);
                if (layer != null)
                {
                    AddLayerElements(layer, result);
                }
                return result;
            }

            int count = map.count_layers;
            for (int i = 0; i < count; i++)
            {
                M2MapLayer layer = map.getLayer(i);
                if (layer != null)
                {
                    AddLayerElements(layer, result);
                }
            }
            return result;
        }

        /// <summary>即时向当前地图添加一个碰撞/配置芯片。</summary>
        public LiveMapElement AddChip(
            string layerName,
            string imageSource,
            int x,
            int y,
            int quarterTurns = 0,
            bool flip = false,
            int opacityPercent = 100)
        {
            M2MapLayer layer = RequireLayer(layerName);
            M2ChipImage image = RequireImage(imageSource);
            int rotation = MapFormat.NormalizeQuarterTurns(quarterTurns);
            int opacity = MapFormat.RequireOpacity(opacityPercent, nameof(opacityPercent));

            List<M2Chip> chips = image.MakeChip(layer, x, y, opacity, rotation, flip);
            return AttachCreated(layer, chips, imageSource, "chip");
        }

        /// <summary>即时向当前地图添加一张装饰图片。X/Y 是图片中心的地图坐标。</summary>
        public LiveMapElement AddPicture(
            string layerName,
            string imageSource,
            float x,
            float y,
            int rotationDegrees = 0,
            bool flip = false,
            int opacityPercent = 100)
        {
            MapFormat.RequireFinite(x, y, nameof(x));
            M2MapLayer layer = RequireLayer(layerName);
            M2ChipImage image = RequireImage(imageSource);
            int opacity = MapFormat.RequireOpacity(opacityPercent, nameof(opacityPercent));

            List<M2Picture> pictures = image.MakePicture(layer, x, y, opacity, rotationDegrees, flip);
            return AttachCreated(layer, pictures, imageSource, "picture");
        }

        internal void EnsureUsable()
        {
            MapRuntime.EnsureMainThread();
            if (!IsValid)
            {
                throw new InvalidLiveMapException(Key);
            }
        }

        internal void RefreshLayer(M2MapLayer layer)
        {
            EnsureUsable();
            Map2d.reentryAllChipsForOneLayer(map, layer);
        }

        M2MapLayer RequireLayer(string layerName)
        {
            EnsureUsable();
            if (string.IsNullOrEmpty(layerName))
            {
                throw new ArgumentException("Layer name cannot be empty.", nameof(layerName));
            }

            M2MapLayer layer = map.getLayer(layerName);
            if (layer == null)
            {
                throw new ArgumentException($"No such layer on map {Key}: {layerName}.", nameof(layerName));
            }
            return layer;
        }

        M2ChipImage RequireImage(string imageSource)
            => MapImageResolver.Resolve(
                map.IMGS, imageSource, requireStableId: false, nameof(imageSource)).Image;

        LiveMapElement AttachCreated<TElement>(
            M2MapLayer layer,
            List<TElement> created,
            string imageSource,
            string kind)
            where TElement : M2Puts
        {
            if (created == null || created.Count == 0)
            {
                throw new InvalidOperationException($"The game refused to create {kind} image: {imageSource}.");
            }

            var puts = new List<M2Puts>(created.Count);
            puts.AddRange(created);
            map.assignNewMapChip(puts, false, true, true);
            RefreshLayer(layer);
            return new LiveMapElement(this, created[0]);
        }

        void AddLayerElements(M2MapLayer layer, ICollection<LiveMapElement> destination)
        {
            var native = new List<M2Puts>();
            layer.copyPutsTo(native);
            foreach (M2Puts element in native)
            {
                if (element is M2Chip || element is M2Picture)
                {
                    destination.Add(new LiveMapElement(this, element));
                }
            }
        }

        TValue Read<TValue>(Func<Map2d, TValue> read, TValue fallback, bool requireValid = true)
        {
            if (map == null || (requireValid && !IsValid))
            {
                return fallback;
            }
            try { return read(map); }
            catch (Exception) { return fallback; }
        }
    }
}
