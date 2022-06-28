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
ORIGINAL_DIR="$(pwd)"
SCRIPT_DIR=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
ROOT_DIR="$SCRIPT_DIR"
cd "$ROOT_DIR" || exit 1

build_usage() {
  usage
  echo ""
  echo "$0 build [OPTIONS...]:"
  echo "OPTIONS:"
  echo " -a, --arch <arch>      Sets the architecture when publishing Is one of x86, x64, arm, arm64. (default = x64)"
  echo " -c, --config <config>  Sets the configuration. Is one of debug, release, or lint. (default = debug)"
  echo " -q, --quiet            Verbosity is set to quiet."
  echo " --publish              Publishes build."
  echo " --os OS                Sets the os to compile to. linux or win. (default = win)"
  echo " --single-file          Publishes a single file."
}

build_cmd() {
    CONFIG="Debug"
    VERBOSITY=()
    PUBLISH="0"
    OS="win"
    ARCH="x64"
    
    SHARED_COMPILATION=""

    while [[ $1 != "" ]]; do
        case "$1" in
            --arch|-a)      shift
                            case "${1,,}" in
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
                            case "${1,,}" in
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
                            case "${1,,}" in
                                win|windows)    OS="win";;
                                linux)          OS="linux";;
                                osx|mac)        OS="osx";;
                                *)              echo "Invalid os $1."
                                                build_usage
                                                exit 1
                                                ;;
                            esac
                            ;;
            --ci)           SHARED_COMPILATION="/p:UseSharedCompilation=false"
                            ;;
            *)              build_usage
                            exit 1
                            ;;
        esac
        shift
    done
    
    if [[ "$PUBLISH" == "1" ]]; then
        PUBLISH_ARGS=("--self-contained=false" "-p:PublishSingleFile=true")
        
        if [[ "$OS" != "osx" ]]; then
            # OSX already gets packaged up.
            PUBLISH_ARGS+=("-p:IncludeNativeLibrariesForSelfExtract=true")
        fi
        
        dotnet publish -c "$CONFIG" "${VERBOSITY[@]}" "$SHARED_COMPILATION" --arch "$ARCH" --os "$OS" "${PUBLISH_ARGS[@]}"
        
        if [[ "$OS" == "osx" ]]; then
            PUBLISH_DIR="$ROOT_DIR/bin/$CONFIG/net6.0/$OS-$ARCH"
            APP_DIR="$ROOT_DIR/bin/$CONFIG/net6.0/$OS-$ARCH/rgit.app"
            rm -rf "$APP_DIR"
            mkdir -p "$APP_DIR" || exit 1
            cp -r "$ROOT_DIR"/Assets/osx/* "$APP_DIR" || exit 1
            cp "$PUBLISH_DIR"/Publish/* "$APP_DIR/Contents/MacOS"
            rm "$APP_DIR/Contents/MacOS/README"
            
        fi
    else
        dotnet build -c "$CONFIG" "${VERBOSITY[@]}" "$SHARED_COMPILATION"
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
