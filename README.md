# PolarisMap

Polaris 的自定义地图能力组件。依赖同级 `PolarisCore`，并由 [Polaris](https://github.com/New-Cradle-of-Stella/Polaris) 聚合仓库作为 Git submodule 引用。

第一版提供两条明确分开的能力：

- `MapAPI.Current` 即时修改当前运行中的地图。支持枚举、添加、移动、改外观和删除 CP/PIC，并同步原版空间索引、cfg 和绘制网格。
- `MapAPI.CreateAndEnter` 从受控的 `MapDraft` 生成 TMAP v4，原子写入磁盘、更新地图清单，然后走原版异步材质预载和切图链路。
- `MapAPI.LoadAndEnterPmap` 加载 `.pmap` XML 高层封装；PolarisTools 可用完全不读取原版贴图的色块蓝图编辑器制作并整图热重载。

## `.pmap` XML 与蓝图编辑器

```xml
<?xml version="1.0" encoding="utf-8"?>
<pmap version="1" key="polaris_demo_room" width="32" height="18" background="#202832">
  <layers>
    <layer name="main" key="true" color="#7F7F7F">
      <chip id="floor" image="my_mod/floor.png" x="4" y="8"
            rotation="0" flip="false" opacity="100"
            visualWidth="4" visualHeight="1" color="#5B6477" label="Floor" />
    </layer>
  </layers>
</pmap>
```

`image/x/y/rotation/flip/opacity` 编译到 TMAP；`id/label/color/visualWidth/visualHeight` 是 PolarisTools 的蓝图元数据。编辑器只画网格、色块和文字，不打开、复制或渲染游戏素材。实际 TMAP 尺寸仍由 `image` 指向的 Chip/Picture 定义决定，`visualWidth/visualHeight` 不会偷偷缩放素材。

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

存在 `[PMapHotFixEnabled]` 时，无修饰键 **F11** 会打开 PolarisMap 自己的 IMGUI 地图检查台并独占该键。页面提供：

- 当前地图、`.pmap` 所属程序集、加载状态与最近一次地图活动。
- 不读取原版素材的色块/文字迷你蓝图，以及 XML 原文查看和复制。
- **Full reload & enter**：使用当前已登记的完整 `.pmap` 再执行一次彻底地图重载。

再次按 F11 或点击 **Close F11** 关闭页面；页面打开期间会占用游戏 UI 输入标志，避免操作穿透。

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

## 创建、写盘并切换

```csharp
using Polaris.Map;

var draft = new MapDraft("polaris_demo_room", width: 64, height: 36)
{
    Background = new MapColor(20, 24, 32),
    Comment = ""
};

draft.AddLayer("Layer", isKeyLayer: true)
    .AddChip("interior_bar/dish_cheese.png", x: 10, y: 8)
    .AddPicture("interior_bar/dish_cheese.png", x: 18.5f, y: 10f, rotationDegrees: 12);

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
2. 生成大端 TMAP v4；初始 CP/PIC 会一同写入文件。
3. 新建 `StreamingAssets/m2d/<key>.tmap`。
4. 原子更新 `StreamingAssets/m2d/__m2d_list.dat`，并留下带时间戳的 `.bak`。
5. 把地图登记进当前 `M2DBase`，调用原版 `initMapMaterialASync`；完成后把玩家放到 `start` 标签点，缺失时放到地图中心。

切图是逐帧异步完成的，`CreateAndEnter` 返回时文件已经落盘，但 `MapTransition.Status` 通常仍为 `Pending`。调用必须发生在 Unity 主线程且已经进入游戏世界。

## 第一版边界

- 只支持 CP（Chip）和 PIC（Picture）；LP、GRD、SM、JOINT 留给后续版本。
- 新 key 必须由 ASCII 字母、数字、下划线、点或连字符组成，且不能以点开头。
- `CreateAndEnter` 始终拒绝覆盖已有 key；`.pmap` 只允许替换带匹配 PolarisMap 所有权 sidecar 的 key。
- 地图宽高以格为单位；ver029 每格 28 像素，并受 TMAP `u16` 像素尺寸限制。
- Chip 坐标是格坐标；Picture 坐标是图片中心的地图坐标。
- 运行时追加的图元不会自动回写。需要持久初始内容时，应把它放进 `MapDraft`。
