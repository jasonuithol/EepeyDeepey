dotnet build -c Release
ffmpeg -y -i lib/lullaby.mp3 -c:a libvorbis -q:a 4 lib/lullaby.ogg
