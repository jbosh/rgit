#!/bin/bash

# Convert icon to Windows ico format.
if command -v convert &> /dev/null; then
  CONVERT_EXISTS="1"
else
  CONVERT_EXISTS="0"
fi

if command -v iconutil &> /dev/null; then
  ICONUTIL_EXISTS="1"
else
  ICONUTIL_EXISTS="0"
fi


if [[ "$CONVERT_EXISTS" == "1" ]]; then
    echo "Creating Windows ico format."
    convert -background transparent "Rabbit Icon.svg" -define icon:auto-resize=16,32,48,64,256 "Icon.ico"
else
    echo "Skipping Windows ico format."
fi

# Convert icon to OSX format
if [[ "$CONVERT_EXISTS" == "1" ]] && [[ "$ICONUTIL_EXISTS" == "1" ]]; then
  echo "Creating OSX icns format."
  ICON_NAME="rgit.iconset"
  mkdir -p "$ICON_NAME"

  SIZES=(16 32 64 128 256 512)
  for SIZE in "${SIZES[@]}"; do
    convert -background transparent "Rabbit Icon.svg" -resize "${SIZE}x${SIZE}" "$ICON_NAME/icon_${SIZE}x${SIZE}.png"
    HI_DPI=$((SIZE * 2))
    convert -background transparent "Rabbit Icon.svg" -resize "${HI_DPI}x${HI_DPI}" "$ICON_NAME/icon_${SIZE}x${SIZE}@2x.png"
  done

  iconutil -c icns "$ICON_NAME"
  rm -rf "$ICON_NAME"
else
  echo "Skipping OSX icns format."
fi
