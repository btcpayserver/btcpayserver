#!/bin/sh

curl -o dotnet-install.sh https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh -version 3.1.416
export PATH=/root/.dotnet:$PATH

# mkdir -p /Android/sdk
# export ANDROID_SDK_ROOT=/Android/sdk
# pacman -Sy --noconfirm unzip jdk8-openjdk git

# curl -o sdk-tools.zip https://dl.google.com/android/repository/commandlinetools-linux-7583922_latest.zip
# unzip sdk-tools.zip
# (cd cmdline-tools/bin &&
# yes | ./sdkmanager --sdk_root=$ANDROID_SDK_ROOT --licenses &&
# yes | ./sdkmanager --sdk_root=$ANDROID_SDK_ROOT "platform-tools" "platforms;android-30")
# rm sdk-tools.zip

# curl -o ndk.zip https://dl.google.com/android/repository/android-ndk-r21e-linux-x86_64.zip
# (cd Android && unzip ../ndk.zip)
# rm ndk.zip

# pacman -Sy --noconfirm rustup
# rustup install stable
# rustup target add aarch64-linux-android armv7-linux-androideabi i686-linux-android x86_64-linux-android
# cargo install --force cargo-make

# curl -o flutter.tar.xz https://storage.googleapis.com/flutter_infra_release/releases/stable/linux/flutter_linux_2.5.1-stable.tar.xz
# tar xvf flutter.tar.xz
# rm -f flutter.tar.xz

# curl -sSL https://git.io/get-mo -o /usr/bin/mo
# chmod +x /usr/bin/mo

# mkdir /root/.zcash-params
# curl https://download.z.cash/downloads/sapling-output.params -o /root/.zcash-params/sapling-output.params
# curl https://download.z.cash/downloads/sapling-spend.params -o /root/.zcash-params/sapling-spend.params

# export ANDROID_NDK_HOME=$HOME/Android/android-ndk-r21e
# export PATH=$PATH:/flutter/bin
