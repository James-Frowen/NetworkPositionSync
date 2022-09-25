#!/bin/bash

echo "Running release script with [SOURCE_PATH=${SOURCE_PATH}, TARGET_PATH=${TARGET_PATH}, args=$@]"

VER=$(echo $1 | sed 's/-[a-z]*//g')
sed -i -e '/AssemblyVersion/s/\".*\"/\"'$VER'\"/' \
    ${SOURCE_PATH}/Runtime/AssemblyInfo.cs

unity-packer pack NetworkPositionSync.unitypackage \
    ${SOURCE_PATH}/Runtime ${TARGET_PATH}/Runtime
    ${SOURCE_PATH}/CHANGELOG.md ${TARGET_PATH}/CHANGELOG.md
    ${SOURCE_PATH}/LICENSE ${TARGET_PATH}/LICENSE
    ${SOURCE_PATH}/package.json ${TARGET_PATH}/package.json
    ${SOURCE_PATH}/Readme.txt ${TARGET_PATH}/Readme.txt