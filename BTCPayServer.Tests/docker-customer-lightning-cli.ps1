$customer_lightning_container_id=$(docker ps -q --filter label=com.docker.compose.project=btcpayservertests --filter label=com.docker.compose.service=customer_lightningd)
docker exec -ti $customer_lightning_container_id lightning-cli --rpc-file=/root/.lightning/lightning-rpc $args
:bc1q4k4zlga72f0t0jrsyh93dzv2k7upry6an304jp