$bitcoind_container_id=$(docker ps -q --filter label=com.docker.compose.project=btcpayservertests --filter label=com.docker.compose.service=bitcoind)
$address=$(docker exec -ti $bitcoind_container_id bitcoin-cli -datadir="/data" getnewaddress)
docker exec -ti $bitcoind_container_id bitcoin-cli -datadir="/data" generatetoaddress $args $address
:bc1q4k4zlga72f0t0jrsyh93dzv2k7upry6an304jp