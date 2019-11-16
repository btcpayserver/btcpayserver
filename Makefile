.PHONY: setup

# setup project
setup: up build run

#up the whole container
up:
	docker-compose up -d

# stop containers
stop:
	docker-compose stop

# loggs container
log:
	docker logs -f btcpayserver-app

# build project
restore:
	docker exec -it btcpayserver-app dotnet restore

# run project
bash:
	docker exec -it btcpayserver-app bash

down:
	docker-compose down
