FROM alpine:3.8

RUN apk add --no-cache openssh sudo bash
RUN ssh-keygen -f /root/.ssh/id_rsa -t rsa -q -P "" -m PEM
RUN echo 'root:opD3i2282D' | chpasswd
RUN sed -i 's/#PermitRootLogin prohibit-password/PermitRootLogin yes/' /etc/ssh/sshd_config
RUN ssh-keygen -f /etc/ssh/ssh_host_rsa_key -N '' -t rsa && \
    ssh-keygen -f /etc/ssh/ssh_host_dsa_key -N '' -t dsa && \
    ssh-keygen -f /etc/ssh/ssh_host_ecdsa_key -N '' -t ecdsa && \
    ssh-keygen -f /etc/ssh/ssh_host_ed25519_key -N '' -t ed25519

CMD ["/usr/sbin/sshd", "-D"]
