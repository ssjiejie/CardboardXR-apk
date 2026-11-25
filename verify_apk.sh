#!/usr/bin/env bash
# APK 交付前自检：把"能不能装上、能不能起来"的每一项前置条件都验一遍。
# 用法: ./verify_apk.sh <apk路径>
set -u
APK="${1:?用法: verify_apk.sh <apk>}"
SDK=/opt/unity/Editor/Data/PlaybackEngines/AndroidPlayer/SDK
BT=$SDK/build-tools/36.0.0
export JAVA_HOME=/opt/unity/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK
export PATH=$JAVA_HOME/bin:$PATH

PASS=0; FAIL=0; WARN=0
ok()   { echo "  [PASS] $1"; PASS=$((PASS+1)); }
bad()  { echo "  [FAIL] $1"; FAIL=$((FAIL+1)); }
warn() { echo "  [WARN] $1"; WARN=$((WARN+1)); }
sec()  { echo; echo "── $1 ──"; }

[ -f "$APK" ] || { echo "找不到文件 $APK"; exit 2; }

sec "1. 包体完整性"
if unzip -tqq "$APK" >/dev/null 2>&1; then ok "zip 结构完整"; else bad "zip 损坏"; fi
SIZE=$(stat -c%s "$APK"); echo "  大小: $((SIZE/1024/1024)) MB"

sec "2. 签名"
if $BT/apksigner verify "$APK" >/dev/null 2>&1; then
  ok "签名校验通过"
  $BT/apksigner verify --print-certs "$APK" 2>/dev/null | grep -E "Signer #1 certificate DN|SHA-256" | head -2 | sed 's/^/    /'
  V=$($BT/apksigner verify -v "$APK" 2>/dev/null | grep -E "^Verified using" | tr -d ' ')
  echo "    ${V:-方案未知}"
else
  bad "签名无效（装上会提示解析包失败）"
fi

sec "3. 对齐（影响安装与内存映射）"
if $BT/zipalign -c -p 4 "$APK" >/dev/null 2>&1; then ok "4 字节对齐"; else warn "未对齐（部分机型安装失败）"; fi

sec "4. 清单"
BADGING=$($BT/aapt2 dump badging "$APK" 2>/dev/null)
echo "$BADGING" | grep -E "^package:" | sed 's/^/    /'
echo "$BADGING" | grep -E "sdkVersion|targetSdkVersion|native-code" | sed 's/^/    /'
PKG=$(echo "$BADGING" | sed -n "s/^package: name='\([^']*\)'.*/\1/p")
MIN=$(echo "$BADGING" | sed -n "s/^minSdkVersion:'\([0-9]*\)'.*/\1/p")
TGT=$(echo "$BADGING" | sed -n "s/.*targetSdkVersion:'\([0-9]*\)'.*/\1/p")
if echo "$BADGING" | grep -q "^launchable-activity:"; then
  ok "有启动入口: $(echo "$BADGING" | sed -n "s/^launchable-activity: name='\([^']*\)'.*/\1/p")"
else
  bad "没有 launchable-activity（图标点了没反应）"
fi
[ "${MIN:-0}" -lt 29 ] && warn "minSdk=$MIN（低于 Android 10，ARCore 要求 29+）" || ok "minSdk=$MIN 满足 ARCore"

sec "5. CPU 架构"
ARCHS=$(unzip -l "$APK" | grep -oE "lib/[^/]+/" | cut -d/ -f2 | sort -u | tr '\n' ' ')
echo "    含: ${ARCHS:-无}"
case "$ARCHS" in
  *arm64-v8a*) ok "含 arm64-v8a（64 位，ARCore 必需）" ;;
  *) bad "缺 arm64-v8a，ARCore 无法运行" ;;
esac
case "$ARCHS" in
  *armeabi-v7a*) echo "    另含 32 位（兼容老机型）" ;;
esac

sec "6. 原生库依赖闭包（启动闪退最常见原因）"
TMP=$(mktemp -d)
unzip -qq -o "$APK" "lib/*" -d "$TMP" 2>/dev/null
# 系统库（设备 /system/lib 自带，不需要打进包）。libmediandk / libcamera2ndk 属 NDK 公开库，
# 从 API 21/24 起由系统提供，打包缺失不代表闪退。
SYSLIB="libc.so libm.so libdl.so liblog.so libandroid.so libEGL.so libGLESv3.so libGLESv2.so libz.so libstdc++.so libjnigraphics.so libnativewindow.so libnativehelper.so libaaudio.so libmediandk.so libcamera2ndk.so libvulkan.so libsync.so libui.so libutils.so libbinder.so libcutils.so libhardware.so libhidlbase.so"
MISSING=0
for so in $(find "$TMP/lib" -name "*.so" 2>/dev/null); do
  REL=${so#$TMP/lib/}
  for need in $(readelf -d "$so" 2>/dev/null | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p'); do
    skip=0
    for s in $SYSLIB; do [ "$need" = "$s" ] && skip=1 && break; done
    [ $skip -eq 1 ] && continue
    if ! find "$TMP/lib" -name "$need" 2>/dev/null | grep -q .; then
      echo "    缺依赖: $REL 需要 $need"
      MISSING=$((MISSING+1))
    fi
  done
done
[ $MISSING -eq 0 ] && ok "所有非系统库依赖在包内闭合" || bad "$MISSING 处依赖缺失（启动即闪退）"
echo "    so 数量: $(find "$TMP/lib" -name '*.so' | wc -l)"

sec "7. IL2CPP 运行时完整性"
if [ -n "$(find "$TMP/lib" -name "libil2cpp.so" 2>/dev/null)" ]; then ok "libil2cpp.so 存在"; else bad "缺 libil2cpp.so"; fi
if unzip -l "$APK" | grep -q "assets/bin/Data/Managed/Metadata/global-metadata.dat"; then
  ok "global-metadata.dat 存在"
else
  bad "缺 global-metadata.dat（IL2CPP 无法启动）"
fi
for f in globalgamemanagers boot.config; do
  unzip -l "$APK" | grep -q "assets/bin/Data/$f" && ok "$f 存在" || warn "缺 $f"
done

sec "8. ARCore 声明"
XML=$($BT/aapt2 dump xmltree --file AndroidManifest.xml "$APK" 2>/dev/null)
echo "$XML" | grep -q "com.google.ar.core" && ok "含 com.google.ar.core 元数据" || bad "缺 ARCore 元数据"
if echo "$BADGING" | grep -qE "^ *uses-feature: name='android.hardware.camera.ar'"; then
  ok "camera.ar 为 required（无 ARCore 的设备装不上，不会白装）"
else
  warn "camera.ar 未标记 required"
fi

sec "9. 权限"
for p in CAMERA INTERNET; do
  echo "$XML" | grep -q "android.permission.$p" && ok "权限 $p" || warn "缺权限 $p"
done

sec "10. 彩透/绿幕功能是否真的在包里"
if unzip -l "$APK" | grep -q "Managed/CardboardXR"; then
  python3 - "$APK" <<'PY'
import sys, zipfile
z = zipfile.ZipFile(sys.argv[1])
hit = {}
for n in z.namelist():
    if n.endswith('.dll'):
        d = z.read(n)
        for kw in (b'ChromaKey', b'StereoEyes', b'SetChromaKey'):
            if kw in d:
                hit.setdefault(kw.decode(), []).append(n.split('/')[-1])
for k in ('ChromaKey', 'StereoEyes', 'SetChromaKey'):
    print(f"    {k}: {'在 ' + ','.join(hit[k]) if k in hit else '缺失'}")
PY
else
  warn "托管程序集已合并，改用全局扫描"
fi
if unzip -p "$APK" assets/bin/Data/globalgamemanagers 2>/dev/null | grep -aq "ChromaKey"; then
  ok "场景资源内含 ChromaKey 材质"
else
  echo "    （ChromaKey 材质名未在资源里命中，可能由脚本运行时创建）"
fi

sec "11. ARCore 相机背景着色器（摄像头黑屏的根因）"
# ARCore 的相机子系统在运行时用 Shader.Find 找这两个 shader，构建时没有任何资产引用它们，
# 默认会被剥掉。剥掉后 cameraMaterial 为 null，彩透、手势、AR 预览全部黑屏。
if /usr/bin/python3 -c "import UnityPy" 2>/dev/null; then
  AROUT=$(/usr/bin/python3 - "$APK" <<'PY'
import sys
import UnityPy
env = UnityPy.load(sys.argv[1])
names = []
for o in env.objects:
    if o.type.name == "Shader":
        try:
            names.append(o.read().m_ParsedForm.m_Name)
        except Exception:
            pass
for want in ("Unlit/ARCoreBackground", "Unlit/ARCoreBackground/AfterOpaques"):
    print(f"  [{'PASS' if want in names else 'FAIL'}] shader {want}")
print(f"    shader 总数: {len(names)}")
PY
)
  echo "$AROUT"
  if echo "$AROUT" | grep -q "\[FAIL\]"; then
    bad "AR 背景 shader 缺失，装上后所有摄像头画面都是黑的"
  else
    ok "两个 AR 背景 shader 都在包内"
  fi
else
  warn "无 UnityPy，无法精确检查 AR 背景 shader"
  if grep -rqa "Unlit/ARCoreBackground" "$TMP" 2>/dev/null; then
    echo "    （粗查命中字符串，但可能只是常量名，不足以证明 shader 在）"
  fi
fi

rm -rf "$TMP"
sec "结论"
echo "  通过 $PASS 项，失败 $FAIL 项，警告 $WARN 项"
[ $FAIL -eq 0 ] && echo "  → 静态自检全通过，可以交付安装" || echo "  → 存在失败项，不可交付"
exit $([ $FAIL -eq 0 ] && echo 0 || echo 1)
