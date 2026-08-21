using System;
using m2d;
using Polaris.Map.Internal;

namespace Polaris.Map
{
    /// <summary>当前地图中的一个 CP/PIC 句柄；删除或切图后失效。</summary>
    public sealed class LiveMapElement
    {
        readonly LiveMap owner;
        readonly M2Puts element;
        bool removed;

        internal LiveMapElement(LiveMap owner, M2Puts element)
        {
            this.owner = owner;
            this.element = element;
        }

        public bool IsValid
        {
            get
            {
                try { return !removed && owner.IsValid && element != null && element.index >= 0; }
                catch (Exception) { return false; }
            }
        }

        public MapElementKind Kind => element is M2Picture ? MapElementKind.Picture : MapElementKind.Chip;
        public string LayerName => Read(static e => e.Lay?.name, null);
        public string ImageSource => Read(static e => e.src, null);
        public float X => Read(e => e is M2Picture ? e.mapcx : e.mapx, 0f);
        public float Y => Read(e => e is M2Picture ? e.mapcy : e.mapy, 0f);
        public int Rotation => Read(static e => e.rotation, 0);
        public bool Flip => Read(static e => e.flip, false);
        public int OpacityPercent => Read(static e => (int)e.opacity, 0);

        /// <summary>移动图元；Chip 使用格坐标，Picture 使用中心坐标。</summary>
        public void MoveTo(float x, float y)
        {
            EnsureUsable();
            MapFormat.RequireFinite(x, y, nameof(x));
            M2MapLayer layer = element.Lay;

            if (element is M2Chip chip)
            {
                if (x != (int)x || y != (int)y)
                {
                    throw new ArgumentException("Chip coordinates must be whole map cells.");
                }
                layer.translateChip(chip, checked((int)x), checked((int)y), false);
            }
            else if (element is M2Picture picture)
            {
                Relink(layer, () => picture.finePos(
                    x * layer.CLEN, y * layer.CLEN, picture.rotR, true));
                return;
            }

            owner.RefreshLayer(layer);
        }

        /// <summary>即时修改不透明度、旋转和翻转，并同步空间索引、cfg 与网格。</summary>
        public void SetAppearance(int opacityPercent, int rotation, bool flip)
        {
            EnsureUsable();
            int opacity = MapFormat.RequireOpacity(opacityPercent, nameof(opacityPercent));
            M2MapLayer layer = element.Lay;
            float pictureCenterX = element is M2Picture picture ? picture.mapcx * layer.CLEN : 0f;
            float pictureCenterY = element is M2Picture picture2 ? picture2.mapcy * layer.CLEN : 0f;

            Relink(layer, () =>
            {
                element.opacity = (byte)opacity;
                element.flip = flip;
                if (element is M2Chip chip)
                {
                    chip.rotation = MapFormat.NormalizeQuarterTurns(rotation);
                    chip.inputRots(true);
                }
                else if (element is M2Picture currentPicture)
                {
                    currentPicture.finePos(
                        pictureCenterX,
                        pictureCenterY,
                        rotation / 180f * (float)Math.PI,
                        true);
                }
            });
        }

        /// <summary>从当前地图即时删除该图元。可重复调用，第二次起返回 <c>false</c>。</summary>
        public bool Remove()
        {
            if (removed)
            {
                return false;
            }

            EnsureUsable();
            M2MapLayer layer = element.Lay;
            bool changed = layer.removeChip(element, false, false, true);
            removed = changed;
            if (changed)
            {
                owner.RefreshLayer(layer);
            }
            return changed;
        }

        void EnsureUsable()
        {
            owner.EnsureUsable();
            if (removed || element == null || element.index < 0)
            {
                throw new InvalidMapElementException();
            }
        }

        void Relink(M2MapLayer layer, Action mutation)
        {
            layer.connectImgLink(element, Map2d.CONNECTIMG.DELETE_TEMP, true);
            try
            {
                mutation();
            }
            finally
            {
                layer.connectImgLink(element, Map2d.CONNECTIMG.ASSIGN, false);
                owner.RefreshLayer(layer);
            }
        }

        TValue Read<TValue>(Func<M2Puts, TValue> read, TValue fallback)
        {
            if (!IsValid)
            {
                return fallback;
            }
            try { return read(element); }
            catch (Exception) { return fallback; }
        }
    }
}
