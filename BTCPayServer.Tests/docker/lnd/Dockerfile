FROM golang:1.10

MAINTAINER BtcPayServer <btcpayserver@github.com>

# Force Go to use the cgo based DNS resolver. This is required to ensure DNS
# queries required to connect to linked containers succeed.
ENV GODEBUG netdns=cgo

# Install dep to manage vendor.
RUN go get -u github.com/golang/dep/cmd/dep

# Grab and install the latest version of lnd and all related dependencies.
RUN git clone https://github.com/lightningnetwork/lnd $GOPATH/src/github.com/lightningnetwork/lnd

# Make lnd folder default.
WORKDIR $GOPATH/src/github.com/lightningnetwork/lnd

# Install dependencies and install/build lnd.
RUN dep ensure
RUN go install . ./cmd/...

COPY "start-lnd.sh" .
RUN chmod +x start-lnd.sh

ENTRYPOINT ["./start-lnd.sh"]
