TARGET="${HOME}/.steam/steam/steamapps/common/Valheim"
cp bin/Release/netstandard2.1/*.dll "${TARGET}"/BepInEx/plugins/
cp *.cfg "${TARGET}"/BepInEx/config/
echo Files deployed to Valheim Client BepInEx plugin and config folders.


