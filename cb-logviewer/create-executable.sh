#!/bin/sh

# Mac-specific instructions
export AS="as -arch i386"
export CC="cc -framework CoreFoundation -lobjc -liconv -arch i386"
export MONO_PATH=/Library/Frameworks/Mono.framework/Versions/Current/
export PKG_CONFIG_PATH=$PKG_CONFIG_PATH:/usr/lib/pkgconfig:$MONO_PATH/lib/pkgconfig
export LD_LIBRARY_PATH=$MONO_PATH/lib/

set -e

xbuild /p:Configuration=Release

cwd=`pwd`
cd bin/Release

if [[ -f "cb-logcat" ]]; then
    rm cb-logcat
fi

if [[ -d "cb-logcat.app" ]]; then
    rm -Rf cb-logcat.app
fi

mkbundle  --deps --static -o ../../cb-logcat cb-logviewer.exe *.dll

cd $cwd    

cp cb-logcat ~/Utilities

#macpack -m:console \
#   -a:cb-logviewer.exe \
#   -a:Couchbase.Lite.dll \
#   -a:ICSharpCode.SharpZipLib.Portable.dll \
#   -a:Mono.Options.dll \
#   -a:Mono.Security.dll \
#   -a:Mono.Terminal.dll \
#   -a:Newtonsoft.Json.dll \
#   -a:SQLitePCL.raw.dll \
#   -a:SQLitePCL.ugly.dll \
#   -a:Stateless.dll \
#   -a:cbforest-sharp.dll \
#   -a:mono-curses.dll \
#   -r:/Library/Frameworks/Mono.framework/Versions/Current/lib/ \
#   -n:cb-logcat \
