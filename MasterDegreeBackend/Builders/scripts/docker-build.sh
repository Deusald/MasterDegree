#!/usr/bin/env bash
set -eo pipefail

# Components (new component should be add here and in create-minikube.sh
__allComponents=(game-server game-servers-controller)

function usage() {
  # Save printed components to variable
  printf -v __allComponentsString ' | %s' "${__allComponents[@]}"
  __allComponentsString=${__allComponentsString:3}
  
  echo "usage $0 [options] <context> <env> <component>"
  echo "  context may be:     docker | minikube"
  echo ""
  echo "  env may be:         local | prod"
  echo ""
  echo "  component may be:   ${__allComponentsString} | all"
  echo ""
  echo "Warning - this script must be executed from it's location"
  echo "Options:"
  echo "  -c, --cluster       DigitalOcean/Minikube cluster name to use as docker environment"
  echo "  -p, --push          Pushes the built image to digital ocean repository"
  echo "  -r, --redeploy      Redeploys containers by deleting current pods"
}

# We need to check if we execute this script from the correct folder because some parts of it depends on it
__exeFolder=$(basename "$PWD")

if [ "$__exeFolder" != "scripts" ]; then
  echo "Script must be executed from it's location."
  exit 1;
fi

# Collect all arguments
__positionalArgs=()

while [ $# -gt 0 ]; do
    __key="$1"
    
    case $__key in
      -c | --cluster)   __cluster="$2"; __clusterArg="-p $2"; shift ;;
      -p | --push)      __pushToDO="true" ;;
      -r | --redeploy)  __redeploy="true" ;;
      -? | --help)      usage; exit 0 ;;
      *)                __positionalArgs+=("$1") ;;
    esac
    
    shift 
done

# Check context
case "${__positionalArgs[0]}" in
    minikube)   eval "$(minikube docker-env "$__clusterArg" --shell bash)" ;;
    docker)     eval "$(minikube docker-env --unset)" ;;
    *)          echo "Unsupported context: ${__positionalArgs[0]}"; usage; exit 1; ;;
esac

# Check environment
__kubeCtx=""

case "${__positionalArgs[1]}" in
    local)
        __kubeCtx="${__cluster:-"masterdegree"}"
        ;;  
    prod)
        __kubeCtx="${__cluster:-"do-masterdegree"}"
        ;;
    *)
        echo "Unsupported environment: ${__positionalArgs[1]}"
        usage
        exit 1
        ;;
esac

echo "Kube context: $__kubeCtx"

__components=()

# Check component to build
case "${__positionalArgs[2]}" in
      all)
          __components+=("${__allComponents[@]}")
          ;;
      *)
          if [[ "${__allComponents[*]}" =~ ${__positionalArgs[2]} ]]; then
              __components+=("${__positionalArgs[2]}")
          else
              echo "Unsupported component: ${__positionalArgs[2]}"
              usage
              exit 1
          fi
          ;;
esac

# Move two folders up to main folder of project - for proper docker build
cd ..
cd ..

for comp in "${__components[@]}"; do
  
    echo ""
    echo "==== Building $comp ===="
    
    # Build docker image
    (set -x; docker build -t "$comp" -f Builders/dockerfiles/"$comp".Dockerfile ./)
    
    # Push image to Digital Ocean
    if [[ -n "$__pushToDO" ]]; then
        echo "Pushing to Digital Ocean"
        docker tag "$comp" registry.digitalocean.com/deusald-container/"$comp"
        docker push registry.digitalocean.com/deusald-container/"$comp"
    fi
    
    # Remove all containers of this type to spawn new version
    if [[ -n "$__redeploy" ]]; then
        kubectl delete pod --context="$__kubeCtx" -l name="$comp"
    fi
done