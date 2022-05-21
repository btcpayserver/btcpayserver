#!/bin/bash


docker build --build-arg CONFIGURATION_NAME=Altcoins-Release -t driquelme/btcpayserver:latest-altcoins-amd64 -f amd64.Dockerfile .

docker push driquelme/btcpayserver:latest-altcoins-amd64
