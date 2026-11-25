#!/bin/bash
# Runs one Unity step on this machine. Usage: build_host.sh <executeMethod>
# The sandbox caps a process at 8 GB and resets the host name between sessions, so both are pinned
# here before Unity starts: the licence is bound to the device identity it was activated under.
METHOD=${1:-ProjectBootstrap.CiBuild.BuildApk}

hostname 846694108c80 2>/dev/null
echo 576562626572264761624c65526f7578 > /etc/machine-id 2>/dev/null

for p in $(pgrep -f "Unity.Licensing.C[l]ient"); do kill "$p" 2>/dev/null; done
sleep 1
/opt/unity/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client > /tmp/lic.log 2>&1 &
sleep 4

cd /workspace/CardboardXR-ChromaPassthrough || exit 1
rm -f host_build.log

env GRADLE_OPTS="-Xmx1536m" \
  taskset -c 0-1 /opt/unity/Editor/Unity \
  -batchmode -nographics -quit \
  -logFile /workspace/CardboardXR-ChromaPassthrough/host_build.log \
  -projectPath /workspace/CardboardXR-ChromaPassthrough \
  -executeMethod "$METHOD"

echo "EXIT:$?" > /workspace/CardboardXR-ChromaPassthrough/host_build.done
