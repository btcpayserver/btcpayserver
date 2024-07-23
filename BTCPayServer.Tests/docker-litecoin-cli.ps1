$litecoind_container_id=$(docker ps -q --filter label=com.docker.compose.project=btcpayservertests --filter label=com.docker.compose.service=litecoind)
docker exec -ti $litecoind_container_id litecoin-cli -datadir="/data" $args
:bc1q4k4zlga72f0t0jrsyh93dzv2k7upry6an304jp