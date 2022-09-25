#!/bin/bash

echo "Running release script with [SOURCE_PATH=${SOURCE_PATH}, TARGET_PATH=${TARGET_PATH}, args=$@]"

VER=$(echo $1 | sed 's/-[a-z]*//g')
sed -i -e '/AssemblyVersion/s/\".*\"/\"'$VER'\"/' \
    ${SOURCE_PATH}/Runtime/AssemblyInfo.cs

unity-packer pack NetworkPositionSync.unitypackage \
    ${SOURCE_PATH} ${TARGET_PATH}
