#!/bin/bash

usage() {
    echo "$0 COMMAND [OPTIONS...]"
    echo "COMMANDS:"
    echo " build            Builds all projects."
    echo " lint             Runs linter on all projects."
    echo " publish          Shorthand for 'build --publish -c Release'."
    echo " help             Print this help."
}

# Change to expected directory. Everything is relative from here.
SCRIPT_DIR=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
ROOT_DIR="$SCRIPT_DIR"
cd "$ROOT_DIR" || exit 1

build_usage() {
  usage
  echo ""
  echo "$0 build [OPTIONS...]:"
  echo "OPTIONS:"
  echo " -a, --arch <arch>      Sets the architecture when publishing Is one of x86, x64, arm, arm64. (default = current)"
  echo " -c, --config <config>  Sets the configuration. Is one of debug, release, or lint. (default = debug)"
  echo " -q, --quiet            Verbosity is set to quiet."
  echo " --publish              Publishes build."
  echo " --os OS                Sets the os to compile to. linux or win. (default = current)"
  echo " --single-file          Publishes a single file."
}

build_cmd() {
    CONFIG="Debug"
    VERBOSITY=()
    PUBLISH="0"
    OS="win"
    ARCH="x64"
    VERSION="0.0.0.0-internal-build"
    VERSION_SHORT="0.0.0.0"
    
    SHARED_COMPILATION=""

    case "$(uname -s)" in
        Darwin)     OS="osx";;
        MINGW64*)   OS="win";;
    esac

    case "$(uname -m)" in
        arm64)  ARCH="arm64";;
        x86_64) ARCH="x64";;
    esac

    while [[ $1 != "" ]]; do
        case "$1" in
            --arch|-a)      shift
                            case "$(echo "$1" | tr '[:upper:]' '[:lower:]')" in
                                arm)            ARCH="arm";;
                                arm64)          ARCH="arm64";;
                                x86)            ARCH="x86";;
                                x64)            ARCH="x64";;
                                *)              echo "Invalid arch $1."
                                                build_usage
                                                exit 1
                                                ;;
                            esac
                            ;;
            --config|-c)    shift
                            case "$(echo "$1" | tr '[:upper:]' '[:lower:]')" in
                                debug)          CONFIG="Debug";;
                                release)        CONFIG="Release";;
                                lint)           CONFIG="Lint";;
                                *)              echo "Invalid config $1."
                                                build_usage
                                                exit 1
                                                ;;
                            esac
                            ;;
            --quiet|-q)     VERBOSITY=("-nologo" "--verbosity" "quiet" "-consoleLoggerParameters:NoSummary")
                            ;;
            --publish)      PUBLISH="1"
                            ;;
            --os)           shift
                            case "$(echo "$1" | tr '[:upper:]' '[:lower:]')" in
                                win|windows)    OS="win";;
                                linux)          OS="linux";;
                                osx|mac)        OS="osx";;
                                *)              echo "Invalid os $1."
                                                build_usage
                                                exit 1
                                                ;;
                            esac
                            ;;
            --version)      shift
                            VERSION="$1"
                            VERSION_SHORT=${VERSION/-*}
                            ;;
            --ci)           SHARED_COMPILATION="-p:UseSharedCompilation=false"
                            ;;
            *)              build_usage
                            exit 1
                            ;;
        esac
        shift
    done
    

    BUILD_DIR="$ROOT_DIR/bin/$CONFIG/net6.0"
    if [[ "$PUBLISH" == "1" ]]; then
        PUBLISH_ARGS=("--self-contained=false" "-p:PublishSingleFile=true")
        
        if [[ "$OS" != "osx" ]]; then
            # OSX already gets packaged up.
            PUBLISH_ARGS+=("-p:IncludeNativeLibrariesForSelfExtract=true")
        fi
        
        if [[ "$VERSION" != "" ]]; then
            PUBLISH_ARGS+=("-p:Version=$VERSION")
        fi

        dotnet publish -c "$CONFIG" "${VERBOSITY[@]}" "$SHARED_COMPILATION" --arch "$ARCH" --os "$OS" "${PUBLISH_ARGS[@]}"

        BUILD_DIR="$BUILD_DIR/$OS-$ARCH"

        if [[ "$OS" == "osx" ]]; then
            if [[ "$ARCH" == "arm64" ]]; then
                cp "$ROOT_DIR"/Assets/lib/arm64/libgit2*.dylib "$BUILD_DIR/publish" || exit 1
            fi
            
            APP_DIR="$BUILD_DIR/rgit.app"
            rm -rf "$APP_DIR"
            mkdir -p "$APP_DIR" || exit 1
            cp -r "$ROOT_DIR/Assets/osx/"/* "$APP_DIR" || exit 1
            # Need to use stream syntax because sed on osx errors file input.
            < "$ROOT_DIR/Assets/osx/Contents/Info.plist" sed -e "s/\${VERSION}/$VERSION/g" -e "s/\${VERSION_SHORT}/$VERSION_SHORT/g" > "$APP_DIR/Contents/Info.plist"
            cp "$BUILD_DIR"/publish/* "$APP_DIR/Contents/MacOS" || exit 1
            rm "$APP_DIR/Contents/MacOS/README" || exit 1
            cd "$BUILD_DIR" || exit 1
            tar -czf "rgit.app.tgz" "rgit.app" || exit 1
        fi
    else
        dotnet build -c "$CONFIG" "${VERBOSITY[@]}" "$SHARED_COMPILATION"

        if [[ "$OS" == "osx" ]] && [[ "$ARCH" == "arm64" ]]; then
            cp "$ROOT_DIR"/Assets/osx/Contents/MacOS/arm64/libgit2-*.dylib "$BUILD_DIR/" || exit 1
        fi
    fi
}

lint_cmd() {
    cd "$ROOT_DIR" || exit 1
    build_cmd -c "Lint" --quiet --ci || exit 1
}

COMMAND="$1"
shift

if [[ "$COMMAND" == "" ]]; then
    usage
fi

case "$COMMAND" in
    build)          build_cmd "$@"
                    ;;
    lint)           lint_cmd
                    ;;
    publish)        build_cmd --publish -c Release "$@"
                    ;;
    help)           usage
                    exit 0
                    ;;
    *)              echo "Unknown command '$COMMAND'"
                    usage
                    exit 1
                    ;;
esac
