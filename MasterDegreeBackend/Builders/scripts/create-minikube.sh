#!/usr/bin/env bash

# Components (new component should be add here and in docker-build.sh
__allComponents=(game-server)

minikube start --driver=hyperv --cpus=3 --memory=4GB -p masterdegree
minikube profile masterdegree
minikube addons enable ingress

for comp in "${__allComponents[@]}"; do
    while ! sh ./docker-build.sh minikube local "$comp"; do
        echo "------- Building $comp failed, trying again... -------"
    done
done

while ! sh ./k8s-deploy.sh agones -i; do
    echo "------- Installing agones helm failed trying again... -------"
done

while ! sh ./k8s-deploy.sh master-degree -i; do
    echo "------- Installing master-degree helm failed trying again... -------"
done

while ! sh ./k8s-deploy.sh loki -i; do
    echo "------- Installing loki helm failed trying again... -------"
done

while ! sh ./k8s-deploy.sh prometheus-stack -i; do
    echo "------- Installing prometheus-stack helm failed trying again... -------"
done