# PolarisMap

Polaris 的自定义地图能力组件。依赖同级 `PolarisCore`，并由 [Polaris](https://github.com/New-Cradle-of-Stella/Polaris) 聚合仓库作为 Git submodule 引用。

组件提供三条明确分开的能力：

- `MapAPI.Current` 即时修改当前运行中的地图。支持枚举、添加、移动、改外观和删除 CP/PIC，并同步原版空间索引、cfg 和绘制网格。
- `MapAPI.CreateAndEnter` 从受控的 `MapDraft` 生成 TMAP v4，原子写入磁盘、更新地图清单，然后走原版异步材质预载和切图链路。
- `MapAPI.LoadAndEnterPmap` 加载 `.pmap` XML 高层封装；PolarisTools 可用完全不读取原版贴图的色块蓝图编辑器制作并整图热重载。
- 完整写出 TMAP v4 的 PAT_CHANGE、LP、GRD、SM、JOINT、CSP、EDITOR_ADDITIONAL 与 MESH_RECT；旧的 CP/PIC API 保持兼容。

## `.pmap` XML 与蓝图编辑器

```xml
<?xml version="1.0" encoding="utf-8"?>
<pmap version="1" key="polaris_demo_room" width="32" height="18" background="#202832">
  <layers>
    <layer name="main" key="true" color="#7F7F7F">
      <chip id="floor" image="my_mod/floor.png" x="4" y="8"
            rotation="0" flip="false" opacity="100"
            visualWidth="4" visualHeight="1" color="#5B6477" label="Floor" />
      <labelPoint id="spawn" key="start" x="8" y="7" width="2" height="2"
            focusX="0" focusY="0" visualWidth="2" visualHeight="2" color="#4B9B77" />
      <gradation id="fog" key="fog" x="0" y="0" width="32" height="8"
            order="5" direction="1" startColor="#8090A0CC" endColor="#8090A000"
            visualWidth="32" visualHeight="8" color="#8874B8AA" />
      <subMap id="scape" target="_city_back_fence" x="0" y="0"
            baseX="0" baseY="0" scaleX="1" scaleY="1" scrollX="0" scrollY="0"
            order="1" repeatX="0" repeatY="0" intervalX="0" intervalY="0" cameraLength="0"
            visualWidth="8" visualHeight="4" color="#4A83A8" />
      <joint id="rope" x="9" y="8" thickness="2" visualWidth="4" visualHeight="1" color="#E0A048">
        <point x="0" y="0" chip="floor" />
        <point x="4" y="0" />
      </joint>
    </layer>
  </layers>
</pmap>
```

`image/x/y/rotation/flip/opacity/pattern` 编译到 CP/PIC；LP、GRD、SM、JOINT 的属性按同名 TMAP 块写出。`id/label/visualWidth/visualHeight` 是 PolarisTools 的蓝图元数据（Joint 的 `chip` 用 `id` 建立稳定引用，可指向 CP 或 PIC）。实际 TMAP 尺寸仍由 `image` 指向的 Chip/Picture 定义决定，`visualWidth/visualHeight` 不会偷偷缩放素材。

## 原版 TMAP 转 PMAP

项目根目录旁提供与 `AicCmdToPevt` 同形态的独立转换器：

```powershell
dotnet run --project E:\Projects\AicTmapToPmap -- `
  "D:\AliceInCradle\AliceInCradle_Data\StreamingAssets\m2d\forest_01.tmap" `
  -o "E:\Temp\forest_01.pmap"
```

准备在游戏里编译进入时可添加 `--key polaris_test_forest_01`，生成新的地图 key，避开原版 key 的所有权保护。

目录输入可添加 `--recursive`；已有输出需明确添加 `--force` 才会覆盖。转换器从
`E:\Projects\Polaris\aic_path.txt` 自动定位 `__m2d_chips.dat`，离线还原 imageId、嵌套芯片、PixelLiner
源路径和图像几何。它只读取原版 TMAP/芯片元数据，输出不含 PNG/PXLS 像素数据，也不会修改原文件。
转换结果的 `image` 会采用 `<PixelLiner路径>#<imageId>` 形式；路径供人和预览识别，ID 用于精确取回
嵌套芯片，避免把同一张源图中的不同子芯片错误合并。

原版 CP 允许像素级偏移和跨出地图边界，因此 PMAP 中 Chip 的 `x/y` 可以是 `1/28` 格粒度；
PolarisTools 拖拽也使用同样步进。这样转换后的坐标重新编译时不会被吸附到整数格。

编辑器默认仍只画网格、色块和文字。需要核对原版 MapChips 时，先进入带 `[PMapHotFixEnabled]` 的 `.pmap`，再点 **Preview originals**：游戏进程只把当前 PMAP 实际引用、且已在本局图集中加载的原版图块临时裁成 PNG，PolarisTools 会把它们直接贴回蓝图；不会解包整套素材，也不再只启动一份无法对应地图的 PXLS。**Clear preview** 会删除该目录；这些文件不会进入工程、Git 或 VSIX，也不应被重新分发。

保存 `.pmap` 后，PolarisTools 会生成同名静态类。模组进入游戏世界后调用：

```csharp
[BepInPlugin("example.maps", "Example Maps", "1.0.0")]
[PMapHotFixEnabled] // 只在需要 PolarisTools 调试推送时添加
public sealed class Plugin : BaseUnityPlugin
{
    // 在已经进入游戏世界后，由你的菜单/事件/调试入口调用。
    internal void EnterDemoMap() => PolarisDemoRoom.Enter(typeof(Plugin));
}
```

蓝图编辑器的 **Full hot reload** 会发送完整 XML。游戏主线程先通过 `M2DBase.changeMap(null)` 正常撤离当前地图，再彻底释放旧层、替换注册表中的 `Map2d`、重新进行材质预载并切回新实例；不是在旧对象上打增量补丁。只有已经由同一程序集加载、且程序集存在 `[PMapHotFixEnabled]` 的 key 才接受推送。

无修饰键 **F11** 会打开 PolarisMap 自己的默认 Unity IMGUI 小窗口并独占该键；`[PMapHotFixEnabled]`
只控制 `.pmap` 热重载管道，不再控制 F11 页面本身。窗口不加载自定义皮肤、字体或纹理，
只保留地图 ID、PNG 路径、实体/暗色遮挡开关、排除特效层输入、导出按钮，以及已加载
`.pmap` 的重载和复制 XML 按钮。

再次按 F11 或点击 **Close** 关闭窗口；窗口打开期间会占用游戏 UI 输入标志，避免操作穿透。

`.pmap` 写盘时会创建 `<key>.tmap.polaris-map` 所有权 sidecar；后续保存/热重载只允许覆盖带匹配 sidecar 的文件，拒绝覆盖原版或其它模组的同名地图。

## 即时修改当前地图

```csharp
using Polaris.Map;

LiveMap map = MapAPI.Current;
if (map != null)
{
    // Chip 使用整数格坐标，旋转单位为顺时针 90°。
    LiveMapElement chip = map.AddChip(
        layerName: "Layer",
        imageSource: "interior_bar/dish_cheese.png",
        x: 10,
        y: 8,
        quarterTurns: 1);

    chip.MoveTo(12, 8);
    chip.SetAppearance(opacityPercent: 70, rotation: 2, flip: true);
    chip.Remove();

    // Picture 使用图片中心的地图坐标，旋转单位为角度。
    LiveMapElement picture = map.AddPicture(
        "Layer", "interior_bar/dish_cheese.png", 16.5f, 9f, rotationDegrees: 15);
}
```

`GetElements()` 返回调用时的快照，可传图层名过滤。切图后 `LiveMap` 和其 `LiveMapElement` 都失效；对失效实例写入会抛出清晰的实例异常。

即时修改只改变本局内存，不会覆盖原版 TMAP。反编译确认 ver029 的 `Map2d`/`M2MapLayer` 没有完整地图保存入口；对任意已加载地图做“反序列化后全量覆盖”会丢未知块、Joint 连接或编辑器排序，因此第一版刻意不伪装成无损保存器。

## 导出完整地图 PNG

```csharp
using Polaris.Map;

MapPngExport export = MapAPI.ExportMapPng(
    mapId: "forest_secretlake_cave",
    outputPath: @"D:\captures\forest_secretlake_cave.png",
    options: new MapPngExportOptions
    {
        IncludeEntities = false,
        IncludeDarkOverlay = false,
        ExcludedEffectLayers = new[] { "SomeOptionalEffectLayer" },
        EnterMapIfNeeded = true,
        Overwrite = true,
    });

export.Finished += result =>
{
    if (result.Status == MapPngExportStatus.Completed)
        UnityEngine.Debug.Log($"Saved {result.Width}x{result.Height}: {result.OutputPath}");
    else
        UnityEngine.Debug.LogException(result.Error);
};
```

调用必须发生在 Unity 主线程且已经进入游戏世界。目标 ID 不是当前地图时，默认通过游戏原生
`initMapMaterialASync` 进入目标地图，导出后留在该地图；可用 `EnterMapIfNeeded = false` 禁止切图。
捕获不是玩家视口截图，也不分块拼接：PolarisMap 在一帧内把当前 `M2Camera` 完整合成链路的
MapChip、PIC、子地图、光照、动态绘制器及相关中间 RenderTexture 一起临时扩展到地图像素尺寸，
以地图中心做一次渲染，编码 PNG 后立即恢复原摄像机和纹理引用。地图尺寸超过显卡单张纹理上限时
会明确失败，不会退回到会产生接缝或漏层的分块方案。

`IncludeDarkOverlay = false` 会在捕获期间移除 `M2DarkRenderer`，随后按原顺序恢复；
不会修改 `LightCamX` 的清屏颜色，也不会把游戏的光照缓冲强制改成白色。
`ExcludedEffectLayers` 可按 Unity 层名排除其他独立特效层；层名不存在时导出会明确失败。

F11 页面顶部提供相同功能；默认输出到游戏根目录的 `PolarisCaptures/<mapId>.png`。

## 创建、写盘并切换

```csharp
using Polaris.Map;

var draft = new MapDraft("polaris_demo_room", width: 64, height: 36)
{
    Background = new MapColor(20, 24, 32),
    Comment = ""
};

MapLayerDraft layer = draft.AddLayer("Layer", isKeyLayer: true);
layer.AddChip("interior_bar/dish_cheese.png", x: 10, y: 8, id: "anchor")
    .AddPicture("interior_bar/dish_cheese.png", x: 18.5f, y: 10f, rotationDegrees: 12);
layer.AddLabelPoint("start", x: 12, y: 8, width: 2, height: 2);
layer.AddGradation(new MapGradationDraft("fog", 0, 0, 64, 8)
{
    Order = MapGradationOrder.Ground,
    Direction = MapGradationDirection.Top,
    StartColor = new MapColor(128, 144, 160, 204),
    EndColor = new MapColor(128, 144, 160, 0),
});
layer.AddSubMap(new MapSubMapDraft("_city_back_fence") { Order = MapSubMapOrder.Back });
layer.AddJoint(new MapJointDraft(12, 8).AddPoint(0, 0, "anchor").AddPoint(4, 0));

MapTransition transition = MapAPI.CreateAndEnter(draft);
transition.Finished += result =>
{
    if (result.Status == MapTransitionStatus.Completed)
    {
        // 此时 MapAPI.Current 已是新地图。
    }
    else
    {
        Exception error = result.Error;
    }
};
```

这次调用会：

1. 校验 key、尺寸、图层、图像路径和 TMAP 数值范围。
2. 生成大端 TMAP v4；CP/PIC、LP、GRD、SM、JOINT 及相关头部块会一同写入文件。
3. 新建 `StreamingAssets/m2d/<key>.tmap`。
4. 原子更新 `StreamingAssets/m2d/__m2d_list.dat`，并留下带时间戳的 `.bak`。
5. 把地图登记进当前 `M2DBase`，调用原版 `initMapMaterialASync`；完成后把玩家放到 `start` 标签点，缺失时放到地图中心。

切图是逐帧异步完成的，`CreateAndEnter` 返回时文件已经落盘，但 `MapTransition.Status` 通常仍为 `Pending`。调用必须发生在 Unity 主线程且已经进入游戏世界。

## 边界与格式规则

- TMAP v4 的当前块均可生成；原版 TMAP 可转换成独立 PMAP 用于对照和编辑，但所有权保护仍禁止把结果直接覆盖到原版 key。
- 新 key 必须由 ASCII 字母、数字、下划线、点或连字符组成，且不能以点开头。
- `CreateAndEnter` 始终拒绝覆盖已有 key；`.pmap` 只允许替换带匹配 PolarisMap 所有权 sidecar 的 key。
- 地图宽高以格为单位；ver029 每格 28 像素，并受 TMAP `u16` 像素尺寸限制。
- Chip 坐标以格表示，PMAP/MapDraft 支持 1 像素（1/28 格）偏移；Picture 坐标是图片中心的地图坐标。
- 运行时追加的图元不会自动回写。需要持久初始内容时，应把它放进 `MapDraft`。
