# CardboardXR：视频模块加彩透 + 自动扣绿幕

在 `brenpoly/CardboardXR` v1.0.0 源码基础上改的。工程本身可直接用 Unity 6000.6.8f1 打开构建，也带了一条 GitHub Actions 出 APK 的流水线。

## 一、为什么原来看视频不能彩透

不是没做彩透，是这个模块**主动关掉了**。`Assets/_Project/Demos/Video/Editor/VideoSetup.cs` 里原本写着：

```csharp
Requirements = new DemoRequirements { SixDof = Need.Off, Passthrough = Need.Off, Hands = Need.Off }
```

`DemoRequirements` 会在进入 demo 时切掉 ARCore 会话（6DoF）和透视——原作者这么写是为了省电省热，因为视频解码已经很吃性能。透视没了，自然看不到真实环境。

另外视频画面是贴在一个**半径 15 米、完全不透明**的球体内壁上的，整屏都被视频包住，就算硬开透视也看不见房间。

## 二、改了什么

| 文件 | 改动 |
|---|---|
| `Assets/_Project/Demos/Video/Shaders/ChromaKey.shader` | **新增**。绿幕抠像 shader：按色度判断绿色，输出透明 alpha，去掉绿边溢色 |
| `Assets/_Project/Demos/Video/Scripts/VideoSphere.cs` | 球体持两套材质（普通 / 绿幕），新增 `SetChromaKey()` 切换 |
| `Assets/_Project/Demos/Video/Scripts/VideoDemo.cs` | 控制面板新增「See-through」「Green screen」「Stereo eyes」三个按钮 |
| `Assets/_Project/Demos/Video/Editor/VideoSetup.cs` | 需求改为 `SixDof = On, Passthrough = On`；构建场景时同时生成绿幕材质 |
| `Assets/_Project/Scripts/Stereo/PassthroughController.cs` | 新增 `StereoEyes`：透视时保留双眼视差，3D 视频不丢立体 |
| `.github/workflows/android-apk.yml` | **新增**。GitHub Actions 自动构建 APK |

### 1. 彩透怎么接上的

`VideoSetup` 把需求改成 6DoF + 透视开启后，`DemoManager` 会自动让 `PassthroughController` 工作：ARCore 的相机画面被画到眼睛前 2 米的一块四边形上，房间就出现在视频后面了。

控制面板上的 **See-through** 按钮可以随时开关。关掉能明显降温、省电，因为 ARCore 相机和房间扫描都停了。

### 1.1 双眼透视（原来为什么会丢立体）

`PassthroughController` 原本在开启透视时把 `StereoRig.MonoEyes` 设为 `true`——把两只眼的相机并到中点，瞳距归零。整幅画面成了"一块平面窗口"，深度自洽，但 **SBS/TB 3D 视频只剩左眼画面，立体全丢**。

现在加了 `StereoEyes` 属性，开启时改为：

```csharp
m_Rig.MonoEyes = on && !m_StereoEyes;
```

`MonoEyes = false` 时，两只眼相机各自回到 `m_Lens.GetEyeOffset()` 算出的镜头位置（约 ±3.1 cm），并且**每只眼仍保留自己的镜头视锥**。透视画面因此被渲染两次、带真实视差，3D 视频的立体回来了。

代价要说清楚：ARCore 只有一个摄像头，透视画面仍然是**一张 2 米处的平面图**。双眼看它有视差，但真实房间没有纵深——看起来像"2 米外的一块屏幕"，图像里的近物（桌上的杯子）也会被画在 2 米处。要和真实几何完全对齐，需要 ARCore Depth API 把单目图像按深度重投影，那是另一个量级的工程，本次没做。

面板上的 **Stereo eyes** 按钮可随时切换双眼/单眼，默认双眼。

### 2. 自动扣绿幕怎么做的

`ChromaKey.shader` 不用你指定"哪种绿"，而是把像素转成**色度**（RGB 去掉亮度），看绿色通道是不是明显压过红蓝：

```hlsl
float3 chroma = rgb / (rgb.r + rgb.g + rgb.b);
float dominance = chroma.g - max(chroma.r, chroma.b);
float green = smoothstep(_Threshold, _Threshold + _Softness, dominance);
```

所以深绿、荧光绿、打了光的绿、阴影里的绿都能抠，不用手动取色。绿色像素 alpha 变 0，透视画面从洞里透出来，人物就"站"在房间里。

绿边溢色（人物边缘被绿光染到）也做了压制，不会在人身边留一圈绿边。

### 3. 深度是怎么处理的（这段是坑）

透视画面画在 **2 米**处，视频球在 **15 米**外。正常深度测试下，15 米的球每一帧都会被 2 米的画面挡死，视频根本出不来。所以球用了 `ZTest Always`，永远画在最上层，只在不透明的地方上色；透明的地方自然露出下面已画好的透视画面。控制面板是更近的 Canvas、同一透明队列、后画，因此仍然盖在视频上面。

## 三、怎么出 APK

### 路线 A：GitHub Actions（推荐，不用装 Unity）

1. 把整个 `CardboardXR-ChromaPassthrough` 目录推到你自己的 GitHub 仓库。
2. 仓库 **Settings → Secrets and variables → Actions** 添加三个 secret（获取方式见 https://game.ci/docs/github/activation ）：
   - `UNITY_LICENSE`：Unity 个人版 `.ulf` 许可证文件的内容
   - `UNITY_EMAIL`
   - `UNITY_PASSWORD`
3. 两种触发方式：
   - 打 tag：`git tag v1.0.1 && git push origin v1.0.1`
   - 或仓库 **Actions → Android APK → Run workflow** 手动跑
4. 跑完在 **Actions 运行页 → Artifacts** 下载 `CardboardXR-apk`；打 tag 的话也会自动挂到 Release 上。

产物是 **debug 签名**的 APK，可以直接装。注意：同一个包名的后续更新必须用同一个签名，所以别换构建方式。

> 这条流水线是照项目自带的 `ios-ipa.yml` 写的，同样是 GameCI + `buildMethod` 调 `ProjectBootstrap.StereoBuilder.BuildAndroid`，场景生成、ARCore 依赖都由项目自己的构建方法处理。

### 路线 B：本地 Unity

1. Unity Hub 安装 **6000.6.8f1**，勾选 **Android Build Support**（含 SDK/NDK/JDK）。
2. 打开工程，等待首次导入完成。
3. 构建（与作者发版脚本同一入口）：

```bash
Unity -batchmode -projectPath . -buildTarget Android \
  -executeMethod ProjectBootstrap.StereoBuilder.BuildAndroid \
  -logFile build.log
```

产物：`Builds/CardboardXR.apk`。

也可以在编辑器里跑一次 `ProjectBootstrap` 的菜单生成场景，再用 Build Settings 出包。

## 四、效果调不准时改哪里

绿幕参数在生成的材质 `Assets/_Project/Demos/Video/Video Sphere Chroma.mat` 上（重新生成场景后会重建）：

| 属性 | 默认 | 作用 |
|---|---|---|
| `_Threshold` | 0.12 | 多绿才算绿幕。抠不干净就调低；误抠人物/衣服就调高 |
| `_Softness` | 0.08 | 边缘羽化宽度。边缘有锯齿就加大 |
| `_Spill` | 0.6 | 去绿边强度。人物边缘发绿就加大 |

也可以在 `VideoSphere.SetChromaKeyThreshold(threshold, softness)` 里运行时调，接个滑杆就能做成可调。

## 五、已知限制（重要）

1. **双眼透视是"平面窗口"式的**：视差回来了，3D 视频有立体，但真实房间仍是单目图像贴在 2 米平面上，没有真实纵深，近处物体有"贴纸感"。要真实纵深得用 ARCore Depth API 重投影（未实现）。默认开；觉得晕就按 **Stereo eyes** 关掉，退回单眼平面窗口。
2. **发热和耗电**：ARCore + 视频硬解同时跑是这台手机最重的组合。原作者关透视就是为了这个。长时间看建议关掉 See-through。
3. **画面四周有黑边**：ARCore 相机视场角比 Cardboard 镜片小，透视画面是一块"窗口"，这是项目原有行为，本次没动。
4. **纯绿色物体也会被抠掉**：色度抠像的固有问题，演员别穿绿衣服。
5. **必须 ARCore 手机**：Android 10+、64 位、支持 Vulkan。
6. **APK 我这边构建不了**：这台机器没有 Unity 许可证，`-batchmode` 会在激活环节失败。功能代码必须编译进 `libil2cpp.so`，没法在 APK 层面打补丁。所以交付的是完整可构建工程 + CI 流水线。

## 六、验证时看日志

- `[Video] Green screen removal on/off`
- `[Video] Passthrough on/off`
- `[Passthrough] On` / `Camera image 1280x720 ...`
- `[Build] Build Succeeded: Builds/CardboardXR.apk`

如果视频是黑的但日志正常，先看 `[Passthrough] Texture centre reads ...`，那是项目自带的透视链路自检。
