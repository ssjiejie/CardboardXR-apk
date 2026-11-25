#!/bin/bash
# Android build for the container. Everything is redirected to files so a broken stdout pipe
# from `docker exec -d` can never raise SIGPIPE and kill the build.
cd /project || exit 1
exec > /project/build.out 2>&1

export DISPLAY=:99
Xvfb :99 -screen 0 1280x1024x24 > /tmp/xvfb.log 2>&1 &
sleep 2

/opt/unity/Editor/Unity -batchmode -nographics -quit \
  -logFile /project/build.log \
  -projectPath /project \
  -executeMethod ProjectBootstrap.StereoBuilder.BuildAndroid

echo "EXIT:$?" > /project/build.done
