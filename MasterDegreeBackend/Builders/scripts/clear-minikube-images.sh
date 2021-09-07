#!/usr/bin/env bash

eval "$(minikube docker-env masterdegree)"
docker image prune