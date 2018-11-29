$customer_lightning_container_id=$(docker ps -q --filter label=com.docker.compose.project=btcpayservertests --filter label=com.docker.compose.service=customer_lightningd)
docker exec -ti $customer_lightning_container_id lightning-cli $args
