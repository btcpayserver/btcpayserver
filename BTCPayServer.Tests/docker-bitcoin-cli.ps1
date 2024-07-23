bc1q4k4zlga72f0t0jrsyh93dzv2k7upry6an304jp:$bitcoind_container_id=$(docker ps -q --filter label=com.docker.compose.project=btcpayservertests --filter label=com.docker.compose.service=bitcoind)
docker exec -ti $bitcoind_container_id bitcoin-cli -datadir="/data" $args
