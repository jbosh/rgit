$VERSION = git rev-parse --abbrev-ref HEAD
$VERSION = $version.replace('releases/', '')
dotnet publish -c Release --arch x64 --os win --self-contained=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$VERSION