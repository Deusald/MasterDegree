#!/usr/bin/env bash
set -eo pipefail

__allComponents=(master-degree loki prometheus-stack agones)

function usage() {
    # Save printed components to variable
    printf -v __allComponentsString ' | %s' "${__allComponents[@]}"
    __allComponentsString=${__allComponentsString:3}
    
    echo "usage: $0 [options] <component>"
    echo ""
    echo "  component may be:   ${__allComponentsString}"
    echo ""
    echo "Warning - this script must be executed from it's location"
    echo ""
    echo "Options:"
    echo "  -d, --delete     Delete cluster"
    echo "  -c, --context    Specifies kubectl context to use. Default: masterdegree"
    echo "  -e, --env        Specifies environment [Default: local]"
    echo "  -i, --install    Use install instead of upgrade of helm chart"
}

# We need to check if we execute this script from the correct folder because some parts of it depends on it
__exeFolder=$(basename "$PWD")

if [ "$__exeFolder" != "scripts" ]; then
  echo "Script must be executed from it's location."
  exit 1;
fi

# Collect all arguments
__positionalArgs=()
__delete=""
__install=""

while [[ $# -gt 0 ]]; do
    __key="$1"

    case $__key in
        -d | --delete)    __delete="true" ;;
        -c | --context)   __context=$2; shift ;;
        -e | --env)       __env=$2; shift ;;
        -i | --install)   __install="true" ;;
        -? | --help)      usage; exit 0 ;;
        *)                __positionalArgs+=("$1") ;;
    esac

    shift
done

# Set defaults
__env=${__env:-"local"}
__kustomizationPath="../k8s/master-degree/$__env/"

# Switch kubectl context
__currentKubeContext=$(kubectl config current-context)

case "$__env" in
    local)
        kubectl config use-context "${__context:-"masterdegree"}"
        ;;
    prod)
        kubectl config use-context "${__context:-"do-bomberman"}"
        ;;
    *)
        echo "Unsupported environment: $__env"
        exit 1
        ;;
esac

# Check component to apply
if [[ "${__allComponents[*]}" =~ ${__positionalArgs[0]} ]]; then
    __components+=("${__positionalArgs[0]}")
else
    echo "Unsupported component: ${__positionalArgs[0]}"
    usage
    exit 1
fi

# Restore kubectl context
trap 'kubectl config use-context $__currentKubeContext' EXIT

if [[ -z "${__positionalArgs[0]}" ]]; then
    echo "Unsupported empty component."
    usage
    exit 1
fi

if [[ "${__positionalArgs[0]}" == "master-degree" ]]; then
    if [[ -z "$__delete" ]]; then
        echo "==== Applying kustomization ===="
        kubectl apply -k "$__kustomizationPath"
    else
        echo "==== Removing kustomization ===="
        kubectl delete -k "$__kustomizationPath"
    fi 
    exit 0
fi

if [[ "${__positionalArgs[0]}" == "loki" ]]; then
    
    helm repo add grafana https://grafana.github.io/helm-charts
    helm repo update
    
    if [[ -z "$__delete" ]]; then
      
        if [[ -z "$__install" ]]; then
            echo "==== Upgrading loki helm chart ===="
            helm upgrade loki grafana/loki-stack
        else
            echo "==== Installing loki helm chart ===="
            helm install loki grafana/loki-stack
        fi
    else
        echo "==== Removing loki helm chart ===="
        helm uninstall loki
    fi 
    
    exit 0
fi

if [[ "${__positionalArgs[0]}" == "prometheus-stack" ]]; then
    
    helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
    helm repo update
    
    if [[ -z "$__delete" ]]; then
      
        if [[ -z "$__install" ]]; then
            echo "==== Upgrading prometheus-stack helm chart ===="
            helm upgrade prometheus prometheus-community/kube-prometheus-stack --values ../k8s/prometheus-stack/values.yml
        else
            echo "==== Installing prometheus-stack helm chart ===="
            helm install prometheus prometheus-community/kube-prometheus-stack --values ../k8s/prometheus-stack/values.yml
        fi
    else
        echo "==== Removing prometheus-stack helm chart ===="
        helm uninstall prometheus
    fi
    
    exit 0
fi

if [[ "${__positionalArgs[0]}" == "agones" ]]; then
    
    helm repo add agones https://agones.dev/chart/stable
    helm repo update
    
    if [[ -z "$__delete" ]]; then
      
        if [[ -z "$__install" ]]; then
            echo "==== Upgrading agones helm chart ===="
            helm upgrade agones agones/agones --values ../k8s/agones/values.yml
        else
            echo "==== Installing agones helm chart ===="
            helm install agones --namespace agones-system --create-namespace agones/agones --values ../k8s/agones/values.yml
        fi
    else
        echo "==== Removing agones helm chart ===="
        helm uninstall agones
    fi
    
    exit 0
fi



