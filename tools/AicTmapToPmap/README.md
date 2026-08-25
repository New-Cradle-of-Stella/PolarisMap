# AicTmapToPmap

将 AliceInCradle 原版 TMAP v4 转成 PolarisMap 可编辑的 `.pmap`。命令行习惯与
`E:\Projects\AicCmdToPevt` 一致，支持单文件、目录批量和递归转换。

```powershell
dotnet run --project E:\Projects\AicTmapToPmap -- `
  "D:\AliceInCradle\AliceInCradle_Data\StreamingAssets\m2d\forest_01.tmap" `
  -o "E:\Temp\forest_01.pmap"
```

若要把结果编译并进入游戏测试，可添加 `--key polaris_test_forest_01`，避免与原版 key 冲突；
仅做编辑器与原版视觉对照时则可保留默认 key。

批量转换：

```powershell
dotnet run --project E:\Projects\AicTmapToPmap -- `
  "D:\AliceInCradle\AliceInCradle_Data\StreamingAssets\m2d" `
  -o "E:\Temp\pmap" --recursive
```

程序默认从 `E:\Projects\Polaris\aic_path.txt` 定位游戏，并读取
`AliceInCradle_Data/StreamingAssets/m2d/__m2d_chips.dat`，以还原 TMAP 中的 imageId、
PixelLiner 源路径和图像几何。也可使用 `--game-root` 或 `--chips` 显式指定。

- 输入 TMAP 始终只读；已有输出默认拒绝覆盖，需明确添加 `--force`。
- 目录批量模式忽略 `*.old.tmap` 备份；显式传入该文件时仍会解析并报告其中的结构错误。
- 生成的 PMAP 不含原版 PNG/PXLS 像素数据，只含资源路径引用和地图结构。
- CP/PIC、PAT_CHANGE、LP、GRD（含 SLICER）、SM、JOINT、CSP、编辑器附加字段和网格矩形都会转换。
- 每个输出都会经过 PolarisMap 正式 PMAP 校验器，结构错误时不写文件。
