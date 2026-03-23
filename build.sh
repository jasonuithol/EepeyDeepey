dotnet build -c Release
ffmpeg -y -i ikoliks_aj-lullaby-baby-sleep-music-331777.mp3 -c:a libvorbis -q:a 4 lullaby.ogg
